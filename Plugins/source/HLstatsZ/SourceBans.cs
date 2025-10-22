using MySqlConnector;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
//using System.Threading;
using CounterStrikeSharp.API.Modules.Timers;
using GameTimer = CounterStrikeSharp.API.Modules.Timers.Timer;
using System;
using System.Text.RegularExpressions;
using System.Text;

namespace HLstatsZ;

public class SourceBans
{
    private static string? _cachedDBH;
    private static string? _prefix;
    public static bool _enabled = false;
    public static GameTimer? _cleanupTimer;
    public static GameTimer? _voteTimer;
    public static GameTimer? _DelayedCommand;
    private static ILogger? _logger;
    public static readonly Dictionary<ulong, (bool IsAdmin, int Aid, string? IP, DateTime Updated, BanType Ban, DateTime ExpiryBan, DateTime ExpiryComm)> _userCache = new();
    private static readonly Regex Ipv4WithPort = new(@"^(?<ip>\d{1,3}(?:\.\d{1,3}){3})(?::\d+)?$", RegexOptions.Compiled);
    public static Dictionary<string, (DateTime Created, CCSPlayerController? target, string? Name, string? MapName, int YES, int NO, int Need)> _vote = new();
    public static Dictionary<string, Dictionary<ulong, int>> _userVote = new();
    public static string serverAddr = "";
    public static int serverID = 0;

    public enum BanType
    {
        None    = 0,
        Mute    = 1,
        Gag     = 2,
        Silence = Mute | Gag,
        Kick    = 4,
        Ban     = 8,
    }

    public static void Init(HLstatsZMainConfig cfg, ILogger logger)
    {
        _logger = logger;

        var sb = cfg.SourceBans;
        if (string.IsNullOrWhiteSpace(sb.Database) || string.IsNullOrWhiteSpace(sb.Host) ||
            string.IsNullOrWhiteSpace(sb.User) || string.IsNullOrWhiteSpace(sb.Prefix) || cfg.sb == "no")
        {
            _enabled = false;
            _logger?.LogInformation("[HLstatsZ] SourceBans disabled: missing config (sb/host/db/user/prefix).");
            return;
        }
        var builder = new MySqlConnectionStringBuilder
        {
            Server = sb.Host,
            Port = (uint)sb.Port,
            Database = sb.Database,
            UserID = sb.User,
            Password = sb.Password,
            Pooling = true,
            ConnectionReset = true,
            DefaultCommandTimeout = 5
        };
        _cachedDBH = builder.ConnectionString;

        _prefix = sb.Prefix;
        _enabled = true;

    }

    private static string? DBH => _cachedDBH;

    public static async Task<bool> isAdmin(CCSPlayerController player, bool? entergame = false)
    {

        if (player == null || !player.IsValid)
            return false;

        var sid64 = player.SteamID;
        if (sid64 == 0) return false;

        DateTime now = DateTime.UtcNow;

        // Cache
        if (_userCache.TryGetValue(sid64, out var cached) && _enabled)
        {
            if ((cached.Ban & (BanType.Ban | BanType.Kick)) != 0 && cached.ExpiryBan > now)
            {
                if (now.AddMinutes(-2) < cached.Updated)
                {
                    var time = cached.ExpiryBan - now;
                    var timeLeft = FormatTimeLeft(time);
                    string reason = (cached.Ban & BanType.Ban)>0 ? "Banned" : "Kicked";
                    Server.ExecuteCommand($"kickid {player.UserId} \"{reason} from server ({timeLeft} remaining)\"");
                }
            } else {
                return cached.IsAdmin;
            }
        }

        var steam2_v0 = ToSteam2(sid64); // STEAM_0:X:Y
        var steam2_v1 = steam2_v0.Replace("STEAM_0:", "STEAM_1:"); // STEAM_1:X:Y
        var sid64_str = sid64.ToString(); // 64-bit

        var table = $"`{_prefix}_admins`";

        bool isAdmin = false;
        int aid = 0;
        string? ip_addr = GetClientIp(player);
        _userCache[sid64] = (isAdmin, aid, ip_addr, DateTime.UtcNow, BanType.None, DateTime.UtcNow,DateTime.UtcNow);

        if (!_enabled) return false;

        try
        {
            using var dbh = new MySqlConnection(DBH);
            await dbh.OpenAsync();

            // Admin check
            using var cmd = new MySqlCommand($@"
                SELECT aid
                FROM {table}
                WHERE authid IN (@s0, @s1, @s64)
                ORDER BY FIELD(authid, @s0, @s1, @s64)
                LIMIT 1;", dbh);

            cmd.Parameters.AddWithValue("@s0",  steam2_v0);
            cmd.Parameters.AddWithValue("@s1",  steam2_v1);
            cmd.Parameters.AddWithValue("@s64", sid64_str);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                aid     = reader.GetInt32("aid");
                isAdmin = aid > 0;
            }
            reader.Close();

            _userCache[sid64] = (isAdmin, aid, ip_addr, DateTime.UtcNow, BanType.None, DateTime.UtcNow,DateTime.UtcNow);

            // Ban check
            using (var banCmd = new MySqlCommand($@"
                SELECT ends, RemovedOn
                FROM `{_prefix}_bans`
                WHERE (authid IN (@s0, @s1, @s64) OR ip = @ip)
                  AND RemovedOn = 0
                  AND (created = ends OR ends > UNIX_TIMESTAMP())
                ORDER BY ends DESC
                LIMIT 1;", dbh))
            {
                banCmd.Parameters.AddWithValue("@s0", steam2_v0);
                banCmd.Parameters.AddWithValue("@s1", steam2_v1);
                banCmd.Parameters.AddWithValue("@s64", sid64_str);
                banCmd.Parameters.AddWithValue("@ip", (object?)ip_addr ?? DBNull.Value);

                using var banReader = await banCmd.ExecuteReaderAsync();
                while (await banReader.ReadAsync())
                {
                    int ends = banReader.IsDBNull(banReader.GetOrdinal("ends")) ? 0 : banReader.GetInt32("ends");
                    var expiry = ends > 0 ? DateTimeOffset.FromUnixTimeSeconds(ends).UtcDateTime : DateTime.MaxValue;
                    UpdateBanUser(player, BanType.Ban, expiry);
                    banReader.Close();
                    return false;
                }
                banReader.Close();

            }

            // comms check
            using (var commsCmd = new MySqlCommand($@"
                SELECT ends, RemovedOn, type
                FROM `{_prefix}_comms`
                WHERE authid IN (@s0, @s1, @s64)
                  AND RemovedOn = 0
                  AND (created = ends OR ends > UNIX_TIMESTAMP())
                ORDER BY ends DESC
                LIMIT 1;", dbh))
            {
                commsCmd.Parameters.AddWithValue("@s0", steam2_v0);
                commsCmd.Parameters.AddWithValue("@s1", steam2_v1);
                commsCmd.Parameters.AddWithValue("@s64", sid64_str);

                using var commReader = await commsCmd.ExecuteReaderAsync();
                while (await commReader.ReadAsync())
                {
                    int ends = commReader.IsDBNull(commReader.GetOrdinal("ends")) ? 0 : commReader.GetInt32("ends");
                    int type = commReader.IsDBNull(commReader.GetOrdinal("type")) ? 0 : commReader.GetByte(commReader.GetOrdinal("type"));
                    var commsType = (BanType)type;
                    var expiry = ends > 0 ? DateTimeOffset.FromUnixTimeSeconds(ends).UtcDateTime : DateTime.MaxValue;
                    UpdateBanUser(player, commsType, expiry);
                }
                commReader.Close();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[HLstatsZ] SourceBans admin check failed for {Sid64}", sid64);
            return false;
        }

        return isAdmin;
    }

    public class MapEntry
    {
        public string DisplayName { get; set; } = "";
        public string MapName { get; set; } = "";
        public string? WorkshopId { get; set; } = null;
        public bool IsSteamWorkshop => WorkshopId != null;
    }

    public static List<MapEntry> GetAvailableMaps(HLstatsZMainConfig config, bool isAdmin)
    {
        var results = new List<MapEntry>();

        // Public maps
        results.AddRange(config.SourceBans.MapCycle.Public.Maps.Select(m => new MapEntry
        {
            DisplayName = m,
            MapName = m
        }));

        // Public WorkShop
        results.AddRange(config.SourceBans.MapCycle.Public.WorkShop.Select(kv => new MapEntry
        {
            DisplayName = kv.Key,
            MapName = kv.Key,
            WorkshopId = kv.Value
        }));

        if (isAdmin)
        {
            // Admin maps
            results.AddRange(config.SourceBans.MapCycle.Admin.Maps.Select(m => new MapEntry
            {
                DisplayName = m,
                MapName = m
            }));

            // Admin WorkShop
            results.AddRange(config.SourceBans.MapCycle.Admin.WorkShop.Select(kv => new MapEntry
            {
                DisplayName = kv.Key,
                MapName = kv.Key,
                WorkshopId = kv.Value
            }));
        }

        return results
            .GroupBy(e => e.DisplayName.ToLowerInvariant())
            .Select(g => g.First())
            .ToList();
    }

    public static async Task<bool> WriteBan(CCSPlayerController target, CCSPlayerController admin, BanType type, int durationSeconds, string reason)
    {

        if (!_userCache.TryGetValue(admin.SteamID, out var adminData) || adminData.Aid <= 0)
        {
            _logger?.LogWarning("[HLstatsZ] WriteBan: admin not found in cache");
            return false;
        }

        if (!_userCache.TryGetValue(target.SteamID, out var targetData))
        {
            _logger?.LogWarning("[HLstatsZ] WriteBan: target not found in cache");
            return false;
        }

        string authid   = ToSteam2(target.SteamID);
        string name     = target.PlayerName;
        int created     = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int ends        = durationSeconds == 0 ? created : created + durationSeconds;
        int aid         = adminData.Aid;
        string adminIp  = adminData.IP ?? "";
        string targetIp = targetData.IP ?? "";
        string ureason  = "";
        string table    = "";
        string sql      = "";
        int banType     = (type ^ BanType.Gag)     == 0 ? 1 :
                          (type ^ BanType.Mute)    == 0 ? 2 :
                          (type ^ BanType.Silence) == 0 ? 3 :
                          (type ^ BanType.Ban)     == 0 ? 0 : 0;

        await using var dbh = new MySqlConnection(DBH);
        await dbh.OpenAsync();
        if ((type & BanType.Ban)>0)
        {
             table = $"`{_prefix}_bans`";
             sql = $@"
                 INSERT INTO {table}
                     (ip,authid, name, created, ends, length, reason, aid, adminIp, sid, RemovedOn, ureason, type)
                 VALUES
                     (@ip,@authid, @name, @created, @ends, @length, @reason, @aid, @adminip, @sid, @removedon, @ureason, @type)";
        } else {
             table = $"`{_prefix}_comms`";
             sql = $@"
                 INSERT INTO {table}
                     (authid, name, created, ends, length, reason, aid, adminIp, sid, RemovedOn, ureason, type)
                 VALUES
                     (@authid, @name, @created, @ends, @length, @reason, @aid, @adminip, @sid, @removedon, @ureason, @type)";
        }
        using var cmd = new MySqlCommand(sql, dbh);

        if ((type & BanType.Ban)>0)
            cmd.Parameters.AddWithValue("@ip", targetIp);
        cmd.Parameters.AddWithValue("@authid", authid);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@created", created);
        cmd.Parameters.AddWithValue("@ends", ends);
        cmd.Parameters.AddWithValue("@length", durationSeconds);
        cmd.Parameters.AddWithValue("@reason", reason ?? "");
        cmd.Parameters.AddWithValue("@aid", aid);
        cmd.Parameters.AddWithValue("@adminip", adminIp);
        cmd.Parameters.AddWithValue("@sid", serverID);
        cmd.Parameters.AddWithValue("@removedon", 0);
        cmd.Parameters.AddWithValue("@ureason", ureason);
        cmd.Parameters.AddWithValue("@type", banType);

        try
        {
            await cmd.ExecuteNonQueryAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[HLstatsZ] WriteBan exception for {Target}", target?.PlayerName);
            return false;
        }
    }

    public static async Task<bool> WriteUnBan(CCSPlayerController target, CCSPlayerController? admin, BanType typeToRemove, string ureason)
    {
        _userCache.TryGetValue(target.SteamID, out var targetData);
        string removeType = "E";
        int removedBy = 0;
        if (admin != null && _userCache.TryGetValue(admin.SteamID, out var adminData))
        {
            removeType = "U";
            removedBy = adminData.Aid;
        }

        string table = (typeToRemove & BanType.Silence) > 0
            ? $"`{_prefix}_comms`"
            : $"`{_prefix}_bans`";

        int removedOn = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await using var dbh = new MySqlConnection(DBH);
        await dbh.OpenAsync();
    
        using var updateCmd = new MySqlCommand($@"
            UPDATE {table}
            SET RemovedBy = @removedBy,
                RemoveType = @removeType,
                RemovedOn = @removedOn,
                ureason = @reason
            WHERE authid = @authid AND (RemovedOn IS NULL OR RemovedOn = 0)
            ORDER BY ends DESC
            LIMIT 1;", dbh);

        updateCmd.Parameters.AddWithValue("@authid", ToSteam2(target.SteamID));
        updateCmd.Parameters.AddWithValue("@removedBy", removedBy);
        updateCmd.Parameters.AddWithValue("@removeType", removeType);
        updateCmd.Parameters.AddWithValue("@removedOn", removedOn);
        updateCmd.Parameters.AddWithValue("@reason", ureason);

        try
        {
            await updateCmd.ExecuteNonQueryAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[HLstatsZ] WriteUnBan exception for {Target}", target?.PlayerName);
            return false;
        }
    }

    public static bool IsPlayerConnected(ulong steamId)
    {
        return Utilities.GetPlayers().Any(p => p?.IsValid == true && p.SteamID == steamId);
    }

    public static void CleanupExpiredUsers()
    {
        DateTime now = DateTime.UtcNow;
        DateTime forever = DateTime.MaxValue;
        var keysToRemove = new List<ulong>();

        foreach (var kvp in _userCache)
        {
            var steamId = kvp.Key;
            var data = kvp.Value;
            bool isConnected = IsPlayerConnected(steamId);
            bool isDisconnected = !isConnected;
            bool isOlderThan24Hour = data.Updated < now.AddHours(-24);
            bool isOlderThan2Min = data.Updated < now.AddMinutes(-2);
            bool isExpiredBan = data.ExpiryBan < now && data.ExpiryComm < now;
            bool isPermanentBan = data.ExpiryBan == forever;

            // unban
            if (isConnected && ((data.Ban & (BanType.Mute | BanType.Silence))>0))
            {
                var player = Utilities.GetPlayers().FirstOrDefault(p => p?.IsValid == true && p.SteamID == steamId);
 
                if (player != null)
                {
                   if (data.ExpiryComm < now && ((data.Ban & BanType.Mute)>0))
                    {
                        player.VoiceFlags = VoiceFlags.Normal;
                        //_userCache[steamId] = (data.IsAdmin, data.Aid, data.IP, data.Updated,
                                               //data.Ban & ~BanType.Mute, data.ExpiryBan, data.ExpiryComm);
                        player.PrintToChat($"[HLstats{ChatColors.Red}Z{ChatColors.Default}] {HLstatsZ.TeamColor(player.TeamNum)}{player.PlayerName}{ChatColors.Default}, you are free to speak");
                    }
                    if (data.ExpiryComm < now && ((data.Ban & BanType.Gag)>0))
                    {
                        //_userCache[steamId] = (data.IsAdmin, data.Aid, data.IP, data.Updated,
                                              //data.Ban & ~BanType.Gag, data.ExpiryBan, data.ExpiryComm);
                        player.PrintToChat($"[HLstats{ChatColors.Red}Z{ChatColors.Default}] {HLstatsZ.TeamColor(player.TeamNum)}{player.PlayerName}{ChatColors.Default}, you are free to chat");
                    }
                }
            }

            // remove offline
            if (isDisconnected && ((isOlderThan2Min && isExpiredBan) || isOlderThan24Hour))
            {
                keysToRemove.Add(steamId);
            }
        }

        foreach (var key in keysToRemove)
        {
            _userCache.Remove(key);
        }
    }

    public static void UpdateBanUser(CCSPlayerController target, BanType type, DateTime? until, bool unban = false)
    {
        var sid64 = target.SteamID;
        if (!_userCache.TryGetValue(sid64, out var userData))
            return;

        var newBan = unban ? userData.Ban & ~type : userData.Ban | type;

        DateTime banTime = userData.ExpiryBan;
        DateTime commTime = userData.ExpiryComm;

        if ((type & (BanType.Ban | BanType.Kick)) != 0)
            banTime = until ?? DateTime.MaxValue;

        if ((type & (BanType.Mute | BanType.Gag | BanType.Silence)) != 0)
            commTime = until ?? DateTime.MaxValue;

        _userCache[sid64] = (
            userData.IsAdmin,
            userData.Aid,
            userData.IP,
            DateTime.UtcNow,
            newBan,
            banTime,
            commTime
        );
    }

    public static async Task GetSid()
    {
        if (!_enabled || string.IsNullOrEmpty(_cachedDBH)) return;

        try
        {
            await using var dbh = new MySqlConnection(DBH);
            await dbh.OpenAsync();
    
            var addr = serverAddr.Split(":", 2);
            if (addr.Length < 2) return;

            using var cmd = new MySqlCommand($@"
                SELECT sid
                FROM `{_prefix}_servers`
                WHERE ip = @ip AND port = @port
                LIMIT 1;", dbh);

            cmd.Parameters.AddWithValue("@ip", addr[0]);
            cmd.Parameters.AddWithValue("@port", addr[1]);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                serverID = reader.GetInt32("sid");
            }
            reader.Close();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[HLstatsZ] SourceBans Get Server ID failed (non-fatal).");
        }
    }


    public static void PrimeConnectedAdmins()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (player?.IsValid == true)
            {
                _ = isAdmin(player);
            }
        }
    }

    public static string FormatTimeLeft(TimeSpan timeLeft)
    {
        if (timeLeft.TotalSeconds < 1)
            return "expired";

        if (timeLeft.TotalHours >= 1)
            return $"{(int)timeLeft.TotalHours}h";

        if (timeLeft.Minutes > 0)
            return $"{timeLeft.Minutes}m";

        return $"{timeLeft.Seconds}s";
    }

    // Converts a Steam64 ID (ulong) to Steam2 format (STEAM_0:X:Y).
    public static string ToSteam2(ulong steamId64)
    {
        const ulong universeOffset = 76561197960265728UL;

        if (steamId64 <= universeOffset) return steamId64.ToString();

        var accountId = steamId64 - universeOffset;
        var authServer = accountId % 2;
        var authId = accountId / 2;

        return $"STEAM_0:{authServer}:{authId}";
    }

    private static string? GetClientIp(CCSPlayerController? player)
    {
        var s = player?.IpAddress;
        if (string.IsNullOrWhiteSpace(s))
            return null;
    
        var m4 = Ipv4WithPort.Match(s);
        if (m4.Success) return m4.Groups["ip"].Value;

        return null;
    }

    public static void DelayedCommand(string command, float delaySeconds)
    {
        _DelayedCommand?.Kill();
        _DelayedCommand = new GameTimer(
            MathF.Max(0.01f, delaySeconds),
            () => Server.ExecuteCommand(command)
        );
    }

    public static bool MakeVote(bool isAdmin,string vote)
    {
        return (vote=="public" || (vote=="admin" && isAdmin));
    }

    public static void StartVoteTimer()
    {
        _voteTimer?.Kill();

        void Tick()
        {
            bool stillActive = CheckVotes();

            if (stillActive) {
                _voteTimer = new GameTimer(1.0f, Tick);
            } else { _voteTimer = null; }
        }

        _voteTimer = new GameTimer(1.0f, Tick);
    }
    
    public static void StopVoteTimer()
    {
        _voteTimer?.Kill();
        _voteTimer = null;
    }
    
    
    public static bool CheckVotes()
    {
        bool active = false;

        if (_vote.TryGetValue("kick", out var vk))
            active |= HandleKickVote(vk);

        if (_vote.TryGetValue("map", out var vm))
            active |= HandleMapVote(vm);

        return active;
    }

    private static bool HandleKickVote((DateTime Created, CCSPlayerController? target, string? Name, string? MapName, int YES, int NO, int Need) vote)
    {
        // Target left
        if (vote.target?.SteamID == null)
        {
            Server.PrintToChatAll($"[HLstats{ChatColors.Red}Z{ChatColors.Default}] Vote {ChatColors.Green}canceled{ChatColors.Default}: {vote.Name} left the game");
            _vote.Remove("kick");
            _userVote.Remove("kick");
            return false;
        }

        // Passed
        if (vote.YES >= vote.Need)
        {
            Server.PrintToChatAll($"[HLstats{ChatColors.Red}Z{ChatColors.Default}] Vote passed: {ChatColors.Green}Kicking{ChatColors.Default} → {vote.Name} ({vote.YES} YES)");
            _vote.Remove("kick");
            _userVote.Remove("kick");
            DelayedCommand($"kickid {vote.target.UserId} \"Kicked {vote.Name} (Vote)\"", 3.0f);
            return false; // vote is done
        }

        // Expired
        if ((DateTime.UtcNow - vote.Created).TotalSeconds > 30)
        {
            Server.PrintToChatAll($"[HLstats{ChatColors.Red}Z{ChatColors.Default}] Vote {ChatColors.Green}expired{ChatColors.Default}: Kick → {vote.Name}");
            _vote.Remove("kick");
            _userVote.Remove("kick");
            return false;
        }

        return true;
    }

    private static bool HandleMapVote((DateTime Created, CCSPlayerController? target, string? Name, string? MapName, int YES, int NO, int Need) vote)
    {
        // Passed
        if (vote.YES >= vote.Need && !_vote.ContainsKey("kick"))
        {
            Server.PrintToChatAll($"[HLstats{ChatColors.Red}Z{ChatColors.Default}] Vote passed: Map → {ChatColors.Green}{vote.MapName}{ChatColors.Default} ({vote.YES} YES)");
            _vote.Remove("map");
            _userVote.Remove("map");

            var availableMaps = SourceBans.GetAvailableMaps(HLstatsZ.Instance!.Config, true);
            var match = availableMaps.FirstOrDefault(m =>
                string.Equals(m.DisplayName, vote.MapName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m.MapName, vote.MapName, StringComparison.OrdinalIgnoreCase) ||
                (m.WorkshopId != null && string.Equals(m.WorkshopId, vote.MapName, StringComparison.OrdinalIgnoreCase)));

            if (match != null)
            {
                var command = match.IsSteamWorkshop ? $"host_workshop_map {match.WorkshopId}" : $"changelevel {match.MapName}";
                DelayedCommand(command, 3.0f);
            }
            return false;
        }

        // Expired
        if ((DateTime.UtcNow - vote.Created).TotalSeconds > 30)
        {
            Server.PrintToChatAll($"[HLstats{ChatColors.Red}Z{ChatColors.Default}] Vote {ChatColors.Green}expired{ChatColors.Default}: Map → {vote.MapName}");
            _vote.Remove("map");
            _userVote.Remove("map");
            return false;
        }

        return true;
    }


}
