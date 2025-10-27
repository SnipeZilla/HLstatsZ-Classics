using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Plugin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using GameTimer = CounterStrikeSharp.API.Modules.Timers.Timer;
using System.Net.Sockets;
using System.Net;
using System.Text;

namespace HLstatsZ;

public class SourceBansConfig
{
    [JsonPropertyName("Host")] public string Host { get; set; } = "127.0.0.1";
    [JsonPropertyName("Port")] public int Port { get; set; } = 3306;
    [JsonPropertyName("Database")] public string Database { get; set; } = "";
    [JsonPropertyName("Prefix")] public string Prefix { get; set; } = "sb";
    [JsonPropertyName("User")] public string User { get; set; } = "";
    [JsonPropertyName("Password")] public string Password { get; set; } = "";
    [JsonPropertyName("VoteBan")] public string VoteBan { get; set; } = "public";
    [JsonPropertyName("VoteMap")] public string VoteMap { get; set; } = "public";
    [JsonPropertyName("MapCycle")] public MapCycleSection MapCycle { get; set; } = new();
}

public class MapCycleSection
{
    [JsonPropertyName("Admin")] public MapCycleConfig Admin { get; set; } = new();
    [JsonPropertyName("Public")] public MapCycleConfig Public { get; set; } = new();
}

public class MapCycleConfig
{
    [JsonPropertyName("Maps")] public List<string> Maps { get; set; } = new();
    [JsonPropertyName("WorkShop")] public Dictionary<string, string> WorkShop { get; set; } = new();
}

public class HLstatsZMainConfig : IBasePluginConfig
{
    [JsonPropertyName("hlstats")] public string hlstats { get; set; } = "yes";
    [JsonPropertyName("sb")] public string sb { get; set; } = "no";
    [JsonPropertyName("Log_Address")] public string Log_Address { get; set; } = "127.0.0.1";
    [JsonPropertyName("Log_Port")] public int Log_Port { get; set; } = 27500;
    [JsonPropertyName("BroadcastAll")] public int BroadcastAll { get; set; } = 0;
    [JsonPropertyName("ServerAddr")] public string ServerAddr { get; set; } = "";
    public int Version { get; set; } = 2;

    [JsonPropertyName("SourceBans")] public SourceBansConfig SourceBans { get; set; } = new();
}

public class HLstatsZ : BasePlugin, IPluginConfig<HLstatsZMainConfig>
{
    public static HLstatsZ? Instance;
    private static readonly HttpClient httpClient = new();
    public HLstatsZMainConfig Config { get; set; } = new();

    public static bool _enabled = false;
    public string Trunc(string s, int max=20)
        => s.Length > max ? s.Substring(0, Math.Max(0, max - 3)) + "..." : s;

    private string? _lastPsayHash;
    public override string ModuleName => "HLstatsZ Classics";
    public override string ModuleVersion => "2.0.0";
    public override string ModuleAuthor => "SnipeZilla";

    public void OnConfigParsed(HLstatsZMainConfig config)
    {
        Config = config;

        if (string.IsNullOrWhiteSpace(Config.Log_Address) || Config.hlstats == "no")
        {
            _enabled = false;
            Instance?.Logger.LogInformation("[HLstatsZ] HLstats disabled: missing config (Log_Address).");
            return;
        }
        _enabled = true;
    }

    public HLZMenuManager _menuManager = null!;

    public override void Load(bool hotReload)
    {
        Instance = this;

        RegisterListener<Listeners.OnTick>(OnTick);

        RegisterEventHandler<EventRoundMvp>(OnRoundMvp);
        RegisterEventHandler<EventBombDefused>(OnBombDefused);
        RegisterEventHandler<EventPlayerConnect>(OnPlayerConnect);
        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);

        AddCommandListener(null, ComamndListenerHandler, HookMode.Pre);

        _menuManager = new HLZMenuManager(this);

        var serverAddr = Config.ServerAddr;
        if (string.IsNullOrWhiteSpace(serverAddr))
        {
            var hostPort = ConVar.Find("hostport")?.GetPrimitiveValue<int>() ?? 27015;
            var serverIP = GetLocalIPAddress();
            serverAddr = $"{serverIP}:{hostPort}";
            Config.ServerAddr=serverAddr;
        }
        SourceBans.Init(Config, Logger);
        SourceBans.serverAddr = serverAddr;
        _ = SourceBans.GetSid();

        if (hotReload)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player?.IsValid == true && player?.IsBot == false)
                {
                    _ = SourceBans.isAdmin(player);
                }
            }
        }

        SourceBans._cleanupTimer?.Kill();
        if (SourceBans._enabled)
            SourceBans._cleanupTimer = new GameTimer(60.0f,
                                            () => SourceBans.CleanupExpiredUsers(),
                                            TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE
            );
    }

    public override void Unload(bool hotReload)
    {
        RemoveListener<Listeners.OnTick>(OnTick);

        DeregisterEventHandler<EventRoundMvp>(OnRoundMvp);
        DeregisterEventHandler<EventBombDefused>(OnBombDefused);
        DeregisterEventHandler<EventPlayerConnect>(OnPlayerConnect);
        DeregisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        DeregisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        DeregisterEventHandler<EventPlayerDeath>(OnPlayerDeath);

        RemoveCommandListener(null!, ComamndListenerHandler, HookMode.Pre);

        SourceBans._cleanupTimer?.Kill();
        SourceBans._cleanupTimer = null;
        SourceBans._voteTimer?.Kill();
        SourceBans._voteTimer = null;
        SourceBans._DelayedCommand?.Kill();
        SourceBans._DelayedCommand = null;

    }

    // ------------------ Core Logic ------------------
    public static string GetLocalIPAddress()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
        socket.Connect("8.8.8.8", 65530); // Google's DNS
        var endPoint = socket.LocalEndPoint as IPEndPoint;
        return endPoint?.Address.ToString() ?? "127.0.0.1";
    }

    public enum TeamNum : byte
    {
        Spectator = 1,
        Terrorist = 2,
        CounterTerrorist = 3
    }

    public async Task SendLog(CCSPlayerController player, string message, string verb)
    {
        if (!player.IsValid) return;
        var name    = player.PlayerName;
        var userid  = player.UserId;
        var steamid = (uint)(player.SteamID - 76561197960265728);
        var team    = player.TeamNum switch {2 => "TERRORIST", 3 => "CT", _ => "UNASSIGNED"};

        var serverAddr = Config.ServerAddr;

        var logLine = $"L {DateTime.Now:MM/dd/yyyy - HH:mm:ss}: \"{name}<{userid}><[U:1:{steamid}]><{team}>\" {verb} \"{message}\"";

        try
        {
            if (!httpClient.DefaultRequestHeaders.Contains("X-Server-Addr"))
                httpClient.DefaultRequestHeaders.Add("X-Server-Addr", serverAddr);

            var content = new StringContent(logLine, Encoding.UTF8, "text/plain");
            var response = await httpClient.PostAsync($"http://{Config.Log_Address}:{Config.Log_Port}/log", content);

            if (!response.IsSuccessStatusCode)
            {
                Instance?.Logger.LogInformation($"[HLstatsZ] HTTP log send failed: {response.StatusCode} - {response.ReasonPhrase}");
            }
        }
        catch (Exception ex)
        {
            Instance?.Logger.LogInformation($"[HLstatsZ] HTTP log send exception: {ex.Message}");
        }
    }

    public static void DispatchHLXEvent(string type, CCSPlayerController? player, string message)
    {
        if (Instance == null) return;

        switch (type)
        {
            case "psay":
                if (player != null) SendPrivateChat(player, message);
                else Instance?.Logger.LogInformation($"Player null from message: {message}");
                break;
            case "csay":
                Instance.BroadcastCenterMessage(message);
                break;
            case "msay":
                if (player == null || Instance?._menuManager == null) return;
                var lines = message.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\\n", "\n")
                                     .Split('\n')
                                     .Select(l => l.Trim())
                                     .Where(l => !string.IsNullOrWhiteSpace(l))
                                     .ToArray();
                var output = new List<string>();
                int page = 1;
                int lineCount = 0;
                string Heading = "Stats";
                foreach (var line in lines)
                {
                    if (line.StartsWith("->") && line.Length > 3 && char.IsDigit(line[2]))
                    {
                        output.Add(line);
                        lineCount = 0;
                    int dashIndex = line.IndexOf('-', 3);
                    if (dashIndex >= 0 && dashIndex + 1 < line.Length)
                         Heading = line.Substring(dashIndex + 1).Trim();
                        continue;
                    }
                    if (lineCount == 0 && Heading == "Stats")
                        output.Add($"->{page} - {Heading}");
                    if (lineCount >= 6)
                    {
                        page++;
                        output.Add($"->{page} - {Heading}");
                        lineCount = 0;
                    }
                    output.Add(line);
                    lineCount++;
                }
                message = string.Join("\n", output);
                Instance._menuManager.Open(player, string.Join("\n", output));
                break;
            case "say":
                SendChatToAll(message);
                break;
            case "hint":
                if (player != null) ShowHintMessage(player, message);
                break;
            default:
                if (player != null) player.PrintToChat($"Unknown HLX type: {type}");
                else Instance?.Logger.LogInformation($"Unknown HLX type: {type}"); 
                break;
        }
    }

    private static string NormalizeName(string name)
    {
        var normalized = name.ToLowerInvariant();
        normalized = new string(normalized
            .Where(c => !char.IsControl(c) && c != '\u200B' && c != '\u200C' && c != '\u200D')
            .ToArray());
        normalized = new string(normalized.Where(c => c <= 127).ToArray());
        return normalized.Trim();
    }

    public static CCSPlayerController? FindTarget(object pl)
    {
        string? token = pl as string ?? pl?.ToString();
        if (string.IsNullOrWhiteSpace(token)) return null;

        // #userid
        if (token[0] == '#' && int.TryParse(token.AsSpan(1), out var uid))
            return Utilities.GetPlayers().FirstOrDefault(p => p?.IsValid == true && p.UserId == uid);

        // userid
        if (int.TryParse(token, out var uid2))
            return Utilities.GetPlayers().FirstOrDefault(p => p?.IsValid == true && p.UserId == uid2);

        // SteamID64
        if (ulong.TryParse(token, out var sid64) && token.Length >= 17)
            return Utilities.GetPlayers().FirstOrDefault(p => p?.IsValid == true && p.SteamID == sid64);

        // Name
        var name = NormalizeName(token);

        var exactMatches = Utilities.GetPlayers()
            .Where(p => p?.IsValid == true &&
                        NormalizeName(p.PlayerName) == name)
            .ToList();

        if (exactMatches.Count == 1)
            return exactMatches[0];

        if (exactMatches.Count > 1)
            return null;

        return null;
    }

    public static void SendPrivateChat(CCSPlayerController player, string message)
    {
        player.PrintToChat($"{message}");
    }

    public static void SendChatToAll(string message)
    {
        Server.PrintToChatAll($"{message}");
    }

    public void BroadcastCenterMessage(string message, float durationInSeconds = 5.0f)
    {
        string messageHTML = message.Replace("HLstatsZ","<font color='#FFFFFF'>HLstats</font><font color='#FF2A2A'>Z</font>");
        string messageCHAT = message.Replace("HLstatsZ", "HLstats{ChatColors.Red}Z{ChatColors.Default}");

        string htmlContent = $"<font color='#FFFFFF'>{messageHTML}</font>";

        var menu = new CenterHtmlMenu(htmlContent, this)
        {
            ExitButton = false
        };

        foreach (var p in Utilities.GetPlayers())
        {
            if (p?.IsValid == true && p?.IsBot == false)
            {
                if (!_menuManager._activeMenus.ContainsKey(p.SteamID))
                {
                    menu.Open(p);
                } else {
                    p.PrintToChat($"{messageCHAT}");
                }
            }
        }

        _ = new GameTimer(durationInSeconds, () =>
        {
            foreach (var p in Utilities.GetPlayers())
            {
                if (p?.IsValid == true && p?.IsBot == false && !_menuManager._activeMenus.ContainsKey(p.SteamID))
                    MenuManager.CloseActiveMenu(p);
            }
        });
    }

    public static void ShowHintMessage(CCSPlayerController player, string message)
    {
        player.PrintToCenter($"{message}");
    }

    private HookResult ComamndListenerHandler(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !SourceBans._userCache.TryGetValue(player.SteamID, out var userData))
            return HookResult.Continue;

        var raw = info.ArgCount > 1 ? (info.GetArg(1) ?? string.Empty) : string.Empty;
        raw = raw.Trim();

        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
            raw = raw.Substring(1, raw.Length - 2);

        bool silenced = raw.StartsWith("/");
        bool prefixed = raw.StartsWith("!") || silenced;
        string text = prefixed ? raw.Substring(1) : raw;

        var parts = text.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return HookResult.Continue;

        var cmd = parts[0].ToLowerInvariant();
        var args  = parts.Length > 1 ? parts[1].Trim() : "";

        bool voteban = (SourceBans.VoteBan == "public" || (SourceBans.VoteBan == "admin" && userData.IsAdmin));
        bool votemap = (SourceBans.VoteMap == "public" || (SourceBans.VoteMap == "admin" && userData.IsAdmin));

        // ---- Handle Public Command ----
        if ((cmd == "menu" || cmd == "hlx_menu") && parts.Length == 1)
        {
            if (Instance!._menuManager._activeMenus.TryGetValue(player.SteamID, out var menu))
            {
                Instance!._menuManager.DestroyMenu(player);
            }
            var builder = new HLZMenuBuilder("Main Menu");
            if (HLstatsZ._enabled)
            {
                builder.Add("Rank", p => _ = SendLog(p, "rank", "say"));
                builder.Add("Next Rank", p => _ = SendLog(p, "next", "say"));
                builder.Add("TOP 10", p => _ = SendLog(p, "top10", "say"));
            }
            if (SourceBans._enabled && (voteban || votemap))
                builder.Add("Vote", p => VoteMenu(p));

            if (userData.IsAdmin)
                builder.Add("Admin", p => AdminMenu(p));

            builder.Open(player, Instance!._menuManager);

            return HookResult.Handled;
        }

        // ---- HLstatsZ -> Daemon ----
        var Cmds = new[] {"top10","rank","session","weaponstats","accuracy","next","clans"};

        if (silenced && parts.Length == 1 && (Cmds.Contains(cmd, StringComparer.OrdinalIgnoreCase) || Regex.IsMatch(cmd, @"^top\d{1,2}$", RegexOptions.CultureInvariant)))
        {
            _ = SendLog(player, cmd, "say");
            return HookResult.Handled;
        }

        // ---- SourceBans ----
        if (!SourceBans._enabled)
            return HookResult.Continue;

        // ---- Handle Gag ----
        if ((userData.Ban & BanType.Gag)>0) return HookResult.Handled;

        // ----SourceBans Commands----
        if (cmd == "votemap" && prefixed)
        {
            if (!votemap)
            {
                SendPrivateChat(player, $"[HLstats{ChatColors.Red}Z{ChatColors.Default}] You don't have enough permission");
                return HookResult.Continue;
            }
            if (parts.Length == 1)
            {
                VoteMenu(player);
                return HookResult.Handled;
            }
            var map = text.Substring(7).Trim();
            AdminAction(player,cmd,player,$"{map}",0);
            return HookResult.Handled;
        }

        if (cmd == "votekick" && prefixed)
        {
            if (!voteban)
            {
                SendPrivateChat(player, $"[HLstats{ChatColors.Red}Z{ChatColors.Default}] You don't have enough permission");
                return HookResult.Continue;
            }
            if (parts.Length == 1)
            {
                VoteMenu(player);
                return HookResult.Handled;
            }
            if (parts.Length < 2) 
            {
                SendPrivateChat(player, $"[HLstats{ChatColors.Red}Z{ChatColors.Default}] Usage: !votekick <#userid|name>, or type !menu");
                return HookResult.Handled;
            }
            var who    = args;
            var reason = parts.Length >= 3 ? (" (" + parts[2] + ")") : "";
            var target = FindTarget(who);
            if (target != null)
            {
                AdminAction(player,"votekick",target,reason);
            }
            else
            {
                SendPrivateChat(player, $"[HLstats{ChatColors.Red}Z{ChatColors.Default}] Target '{who}' not found. Use !menu");
            }
            return HookResult.Handled;
        }

        // ---- Handle Admin command ----
        if (!userData.IsAdmin || !prefixed) return HookResult.Continue;
        var adminFlags = userData.Flags.ToFlags();

        if (cmd == "say" && !string.IsNullOrWhiteSpace(args))
        {
            if (!string.IsNullOrWhiteSpace(args))
                SendChatToAll($"[HLstats{ChatColors.Red}Z{ChatColors.Default}] {args}");
            return HookResult.Handled;
        }

        if (cmd == "admin" && string.IsNullOrWhiteSpace(args))
        {
            AdminMenu(player);
            return HookResult.Handled;
        }

        if (cmd == "kick")
        {

            if (!(adminFlags.Has(AdminFlags.Root) && !adminFlags.Has(AdminFlags.Kick)))
            {
                SendPrivateChat(player, $"[HLstats{ChatColors.Red}Z{ChatColors.Default}] You don't have enough permission");
                return HookResult.Handled;
            }

            if (parts.Length == 1)
            {
                AdminPlayer(player);
                return HookResult.Handled;
            }
            if (parts.Length < 3) 
            {
                SendPrivateChat(player, $"[HLstats{ChatColors.Red}Z{ChatColors.Default}] Usage: !kick <#userid|name> <reason>, or type !menu");
                return HookResult.Handled;
            }
            var who    = args;
            var reason = parts.Length >= 3 ? (" (" + parts[2] + ")") : "";
            var target = FindTarget(who);
            if (target != null)
            {
                AdminAction(player,"kick",target,reason);
            }
            else
            {
                SendPrivateChat(player, $"[HLstats{ChatColors.Red}Z{ChatColors.Default}] Target '{who}' not found. Use !menu");
            }
            return HookResult.Handled;
        }

        if (cmd == "map")
        {
            if (!(adminFlags.Has(AdminFlags.Root) && !adminFlags.Has(AdminFlags.Map)))
            {
                SendPrivateChat(player, $"[HLstats{ChatColors.Red}Z{ChatColors.Default}] You don't have enough permission");
                return HookResult.Handled;
            }

            if (parts.Length == 1)
            {
                MapMenu(player);
                return HookResult.Handled;
            }
            var map = text.Substring(3).Trim();
            AdminAction(player,cmd,player,$"{map}",0);
            return HookResult.Handled;
        }

        if ((cmd == "ban" || cmd == "gag" || cmd == "mute" || cmd == "silence") ||
            (cmd == "unban" || cmd == "ungag" || cmd == "unmute" || cmd == "unsilence"))
        {
            if ((cmd == "ban" && !(adminFlags.Has(AdminFlags.Root) && !adminFlags.Has(AdminFlags.Ban))) ||
           (cmd == "unban" && !(adminFlags.Has(AdminFlags.Root) && !adminFlags.Has(AdminFlags.Unban))))
            {
                SendPrivateChat(player, $"[HLstats{ChatColors.Red}Z{ChatColors.Default}] You don't have enough permission");
                return HookResult.Handled;
            }

            var reason = parts.Length >= 4 ? (" (" + parts[3] + ")") : "";
            var length = parts.Length >= 2 ?  parts[2] : "";
            if (parts.Length < 3 || !(int.TryParse(length, out int min) && min >= 0)) 
            {
                SendPrivateChat(player, $"[HLstats{ChatColors.Red}Z{ChatColors.Default}] Usage: !{cmd} <#userid|name> <minutes|0> <reason>, or type !menu");
                return HookResult.Handled;
            }
            var who    = parts[1];
            var target = FindTarget(who);
            if (target != null && target.IsValid)
            {
                AdminAction(player,cmd,target,reason,min);
            }
            else
            {
                SendPrivateChat(player, $"[HLstats{ChatColors.Red}Z{ChatColors.Default}] Target '{who}' not found. Use !menu");
            }
            return HookResult.Handled;
        }

        if (cmd == "slay")
        {
            if (!(adminFlags.Has(AdminFlags.Root) && !adminFlags.Has(AdminFlags.Slay)))
            {
                SendPrivateChat(player, $"[HLstats{ChatColors.Red}Z{ChatColors.Default}] You don't have enough permission");
                return HookResult.Handled;
            }

            if (parts.Length == 1)
            {
                SendPrivateChat(player, $"[HLstats{ChatColors.Red}Z{ChatColors.Default}] Usage: !{cmd} <#userid|name>, or type !menu");
                return HookResult.Handled;
            }
            var who    = args;
            var target = FindTarget(who);
            if (target != null && target.IsValid)
            {
                AdminAction(player,cmd,target,null);
            }
            else
            {
                SendPrivateChat(player, $"[HLstats{ChatColors.Red}Z{ChatColors.Default}] Target '{who}' not found. Use !menu");
            }
            return HookResult.Handled;
        }

         if (cmd == "team")
         {
             if (parts.Length < 3)
             {
                 SendPrivateChat(player, $"[HLstats{ChatColors.Red}Z{ChatColors.Default}] Usage: !{cmd} <#userid|name> <t|ct|spec>, or type !menu");
                 return HookResult.Handled;
             }
             var tname = parts[2].ToLowerInvariant();
             var team  = 0;
             switch (tname)
             {
                case "1":
                case "s":
                case "spec":
                case "spectator":
                    team = 1;
                    tname = "Spectator";
                break;
                case "2":
                case "t":
                case "tt":
                case "terrorist":
                    team = 2;
                    tname = "Terrorist";
                break;
                case "3":
                case "ct":
                case "counter-terrorist":
                case "counterterrorist":
                    team = 3;
                    tname = "Counter-Terrorist";
                break;
                default:
                    SendPrivateChat(player, $"[HLstats{ChatColors.Red}Z{ChatColors.Default}] Team {tname} doesn't exist, use !menu");
                return HookResult.Handled;
            }
            var who    = args;
            var target = FindTarget(who);
            if (target != null && target.IsValid)
            {
                AdminAction(player,cmd,target,tname,team);
            } else {
                 SendPrivateChat(player, $"[HLstats{ChatColors.Red}Z{ChatColors.Default}] Target '{who}' not found. Use !menu");
            }
            return HookResult.Handled;
        }
        if (cmd == "camera")
        {
            int d = args == "-d" ? 1 : 0;
            SourceBans.CameraCommand(player, d);
            return HookResult.Handled;
        }
        return HookResult.Continue;
    }

    [ConsoleCommand("hlx_sm_psay")]
    public void OnHlxSmPsayCommand(CCSPlayerController? _, CommandInfo command)
    {
        if (command.ArgCount < 2) return; // hlx_sm_psay "1" 1 "message"

        var arg = command.ArgByIndex(1);
        var message = command.ArgByIndex(command.ArgCount - 1);

        string[] privateOnlyPatterns =
        {
            "kills to get regular points",
            "You have been banned"
        };
        bool isPrivateOnly = privateOnlyPatterns.Any(p => message?.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
        // Broadcast to all
        if (Config.BroadcastAll == 1 && !isPrivateOnly)
        {
            var hash = $"ALL:{message}";
            if (_lastPsayHash == hash) return;

            _lastPsayHash = hash;
            DispatchHLXEvent("say", null, message);
            return;
        }

        // users?
        var userIds = new List<int>();
        foreach (var idStr in arg.Split(','))
        {
            if (int.TryParse(idStr, out var id)) userIds.Add(id);
        }

        // Broadcast to user
        foreach (var userid in userIds)
        {
            var target = FindTarget(userid);
            if (target == null || !target.IsValid) continue;

            var hash = $"{userid}:{message}";
            if (_lastPsayHash == hash) continue;

            _lastPsayHash = hash;
            DispatchHLXEvent("psay", target, message);
        }
    }

    [ConsoleCommand("hlx_sm_csay")]
    public void OnHlxSmCsayCommand(CCSPlayerController? _, CommandInfo command)
    {
        var message = command.ArgByIndex(1);
        DispatchHLXEvent("csay", null, message);
    }

    [ConsoleCommand("hlx_sm_hint")]
    public void OnHlxSmHintCommand(CCSPlayerController? _, CommandInfo command)
    {
        if (!int.TryParse(command.ArgByIndex(1), out var userid)) return;

        var message = command.ArgByIndex(command.ArgCount - 1);
        var target  = FindTarget(userid);
        if (target == null || !target.IsValid) return;
        DispatchHLXEvent("hint", target, message);
    }

    [ConsoleCommand("hlx_sm_msay")]
    public void OnHlxSmMsayCommand(CCSPlayerController? _, CommandInfo command)
    {
        if (!int.TryParse(command.ArgByIndex(2), out var userid)) return;

        var message = command.ArgByIndex(command.ArgCount - 1);
        var target  = FindTarget(userid);
        if (target == null || !target.IsValid) return;
        DispatchHLXEvent("msay", target, message);
    }

    // --------------------- Menu ---------------------
    private const int PollInterval = 6; // 4~80ms 6~120ms
    private int _tickCounter = 0;

    private void OnTick()
   {
        if (_menuManager._activeMenus.Count == 0) return;
        if (++_tickCounter % PollInterval != 0) return;
        _tickCounter=0;

        foreach (var kvp in _menuManager._activeMenus.ToList())
        {
            var steamId = kvp.Key;
            var (player, menu) = kvp.Value;

            if (player == null || !player.IsValid)
            {
                _menuManager.DestroyMenu(player!);
                continue;
            }
            var current = player.Buttons;
            var last = _menuManager._lastButtons.TryGetValue(steamId, out var prev) ? prev : PlayerButtons.Cancel;

            if (current.HasFlag(PlayerButtons.Forward) && !last.HasFlag(PlayerButtons.Forward))
                _menuManager.HandleWasdPress(player, "W");

            if (current.HasFlag(PlayerButtons.Back) && !last.HasFlag(PlayerButtons.Back))
                _menuManager.HandleWasdPress(player, "S");

            if (current.HasFlag(PlayerButtons.Moveleft) && !last.HasFlag(PlayerButtons.Moveleft))
                _menuManager.HandleWasdPress(player, "A");

            if (current.HasFlag(PlayerButtons.Moveright) && !last.HasFlag(PlayerButtons.Moveright))
                _menuManager.HandleWasdPress(player, "D");

            if (current.HasFlag(PlayerButtons.Use) && !last.HasFlag(PlayerButtons.Use))
                _menuManager.HandleWasdPress(player, "E");

            _menuManager._lastButtons[steamId] = current;

        }
    }

    public static void AdminMenu(CCSPlayerController player)
    {
        if (!SourceBans._enabled || player == null || !player.IsValid)
            return;

        SourceBans._userCache.TryGetValue(player.SteamID, out var userData);
        bool votemap = (SourceBans.VoteMap == "public" || (SourceBans.VoteMap == "admin" && userData.IsAdmin));

        var builder = new HLZMenuBuilder("Admin Menu")
                     .Add("Players", _ => AdminPlayer(player))
                     .Add("Remove Ban (session)", _ => AdminPlayer(player,1));
        if (votemap)
            builder.Add("Map Change", _ => MapMenu(player));
        builder.Open(player, Instance!._menuManager);

    }

    public static void VoteMenu(CCSPlayerController player)
    {
        if (!SourceBans._enabled || player == null || !player.IsValid) return;

        SourceBans._userCache.TryGetValue(player.SteamID, out var userData);
        bool voteban = (SourceBans.VoteBan == "public" || (SourceBans.VoteBan == "admin" && userData.IsAdmin));
        bool votemap = (SourceBans.VoteMap == "public" || (SourceBans.VoteMap == "admin" && userData.IsAdmin));

        var builder = new HLZMenuBuilder("Vote Menu");
        if (!SourceBans._vote.ContainsKey("kick") && !SourceBans._vote.ContainsKey("map") && voteban)
        {
            builder.Add("Kick player", _ => VotePlayer(player));
        } else {
            builder.AddNoOp("Kick player");
        }
        if (!SourceBans._vote.ContainsKey("kick") && !SourceBans._vote.ContainsKey("map") && votemap)
        {
            builder.Add("Change Map", _ => VoteMap(player));
        } else {
            if (votemap)
                builder.AddNoOp("Change Map");
        }
        if (!SourceBans._vote.ContainsKey("kick") && !SourceBans._vote.ContainsKey("map") && votemap)
        {
            builder.Add("Rock The Vote", _ => AdminConfirm(player,player,"rtv"));
        } else {
            if (votemap)
                builder.AddNoOp("Rock The Vote");
        }
        builder.Add("Active Vote", _ => VoteActive("check",player,null,null));

        builder.Open(player, Instance!._menuManager);
    }

    public static void VotePlayer(CCSPlayerController player)
    {
        if (!SourceBans._enabled || player == null || !player.IsValid) return;

        var name = "";
        var builder = new HLZMenuBuilder("Vote Kick");
        foreach (var target in Utilities.GetPlayers())
        {
            name = Instance?.Trunc(target.PlayerName, 20);
            builder.Add($"{name}", _ => AdminConfirm(player, target, "votekick"));
        }

        builder.Open(player, Instance!._menuManager);
    }

    public static void VoteMap(CCSPlayerController player)
    {
        if (!SourceBans._enabled || player == null || !player.IsValid)
            return;
        var maps = SourceBans.GetAvailableMaps(Instance!.Config, false);

        var builder = new HLZMenuBuilder("Vote Map");
        foreach (var entry in maps)
        {
            string label = entry.IsSteamWorkshop ? $"[WS] {entry.DisplayName}" : entry.DisplayName;
            builder.Add(label, _ => AdminConfirm(player, player, $"votemap 1 {entry.DisplayName}"));
        }

        builder.Open(player, Instance!._menuManager);
    }

    public static void AddVote(string type, CCSPlayerController player, bool yes, string? mapName = null, int? choiceIndex = null)
    {
        if (!SourceBans._vote.ContainsKey(type))
        {
            Instance!._menuManager.DestroyMenu(player);
            return;
        }

        if (!SourceBans._userVote.ContainsKey(type))
            SourceBans._userVote[type] = new();

        var vote = SourceBans._vote[type];
        var userVotes = SourceBans._userVote[type];
        ulong sid = player.SteamID;
        bool isRtv = SourceBans._rtv.Count > 0;

        if (isRtv)
        {
            if (choiceIndex == null || choiceIndex < 0 || choiceIndex >= SourceBans._rtv.Count)
            {
                player.PrintToChat("[HLstatsZ] Invalid map choice.");
                return;
            }
            userVotes[sid] = choiceIndex.Value;
            vote.MapName = SourceBans._rtv[choiceIndex.Value].MapName;
        }
        else
        {
            if (userVotes.TryGetValue(sid, out int previous))
            {
                if (previous == 1) vote.YES--;
                else vote.NO--;
            }
    
            userVotes[sid] = yes ? 1 : 0;
            if (yes) vote.YES++;
            else vote.NO++;
        }

        SourceBans._vote[type] = vote;
        Instance!._menuManager.DestroyMenu(player);
    }

    public static void VoteActive(string type, CCSPlayerController player, CCSPlayerController? target, string? mapname)
    {
        if (!SourceBans._enabled || player == null || !player.IsValid)
            return;
        string title = type == "check" ? "Vote Menu" : "Cast Vote";
        var builder = new HLZMenuBuilder(title).NoNumber();

        if (type == "kick" || (SourceBans._vote.ContainsKey("kick") && type == "check"))
            BuildVoteKickMenu(builder, player, target);

        if (type == "map" || (SourceBans._vote.ContainsKey("map") && type == "check" && SourceBans._rtv.Count == 0))
            BuildVoteMapMenu(builder, player, mapname);

        if (type == "rtv" || (SourceBans._vote.ContainsKey("map") && type == "check" && SourceBans._rtv.Count > 0))
            BuildVoteRtvMenu(builder, player);

        if (!SourceBans._vote.ContainsKey("kick") && !SourceBans._vote.ContainsKey("map"))
            builder.AddNoOp("There is no active vote");

        if (type != "check")
             Instance!._menuManager.DestroyMenu(player);

        builder.Open(player, Instance!._menuManager);
    }

    private static void BuildVoteKickMenu(HLZMenuBuilder builder, CCSPlayerController player, CCSPlayerController? target)
    {
        string playerName = SourceBans._vote.TryGetValue("kick", out var vote) ? vote.target?.PlayerName ?? "?" : target?.PlayerName ?? "?";

        builder.AddNoOp($"Kick {playerName}?");
        builder.Add("NO", _ => AddVote("kick", player, false));
        builder.Add("YES", _ => AddVote("kick", player, true));

        if (SourceBans._vote.TryGetValue("kick", out var result))
            builder.AddNoOp($"YES: {result.YES}. NO: {result.NO}");
    }

    private static void BuildVoteMapMenu(HLZMenuBuilder builder, CCSPlayerController player, string? mapname)
    {
        string displayMap = SourceBans._vote.TryGetValue("map", out var vote) ? vote.MapName ?? mapname ?? "?" : mapname ?? "?";

        builder.AddNoOp($"Change Map to {displayMap}?");
        builder.Add("NO", _ => AddVote("map", player, false));
        builder.Add("YES", _ => AddVote("map", player, true));

        if (SourceBans._vote.TryGetValue("map", out var result))
            builder.AddNoOp($"YES: {result.YES}. NO: {result.NO}");
    }

    private static void BuildVoteRtvMenu(HLZMenuBuilder builder, CCSPlayerController player)
    {
        if (!SourceBans._userVote.TryGetValue("map", out var userVotes))
        {
            SourceBans._userVote["map"] = new();
            userVotes = SourceBans._userVote["map"];
        }
        var tally = userVotes.Values
                             .GroupBy(c => c)
                             .ToDictionary(g => g.Key, g => g.Count());
         for (int i = 0; i < SourceBans._rtv.Count; i++)
        {
            int choiceIndex = i;
            var mapEntry = SourceBans._rtv[choiceIndex];

            int count = tally.TryGetValue(i, out var c) ? c : 0;
            string label = mapEntry.IsSteamWorkshop ? $"[WS] {mapEntry.DisplayName} → {count}" : $"{mapEntry.DisplayName} → {count}";
            builder.Add(label, _ => AddVote("map", player, true, mapEntry.DisplayName, choiceIndex));
        }
    }

    public static void MapMenu(CCSPlayerController player)
    {
        if (!SourceBans._enabled || player == null || !player.IsValid)
            return;
        SourceBans._userCache.TryGetValue(player.SteamID, out var userData);

        var maps = SourceBans.GetAvailableMaps(Instance!.Config, userData.IsAdmin);
        string currentMap = Server.MapName;
        var builder = new HLZMenuBuilder("Map Menu");

        foreach (var entry in maps)
       {
            string label = entry.IsSteamWorkshop ? $"[WS] {entry.DisplayName}" : entry.DisplayName;
            if (string.Equals(entry.DisplayName, currentMap, StringComparison.OrdinalIgnoreCase))
            {
                builder.AddNoOp(label);
            } else {
                builder.Add(label, _ => AdminConfirm(player, player, $"map 1 {entry.DisplayName}"));
            }
        }

        builder.Open(player, Instance!._menuManager);
    }

    public static void AdminPlayer(CCSPlayerController player, int type = 0)
    {
        if (!SourceBans._enabled || player == null || !player.IsValid)
            return;

        var title = type == 0 ? "Admin Players" : "Admin Unban";
        var builder = new HLZMenuBuilder($"{title}").NoNumber();
        var count =0;
        var name = "";
        var label = "";
        foreach (var target in Utilities.GetPlayers())
        {
            if (target == null || target?.IsValid != true) continue;
            SourceBans._userCache.TryGetValue(target.SteamID, out var userData);

            switch (type)
            {
                case 0:
                    if ((userData.Ban & (BanType.Kick | BanType.Ban))>0) continue;
                    name = Instance?.Trunc(target.PlayerName, 20);
                    label = $"#{target.Slot} - {name}";
                    //if (player != target)
                        builder.Add(label, _ => AdminCMD(player, target));
                    //else
                       // builder.AddNoOp(label);
                break;
                case 1:
                    if ((userData.Ban ^ BanType.None)==0) continue;
                    count++;
                    name = Instance?.Trunc(target.PlayerName, 20);
                    label = $"{target.Slot} - {name}";
                    builder.Add(label, _ => AdminCMD2(player, target));
                break;
                default: break;

            }
        }
        if (count == 0 && type == 1)
            builder.AddNoOp("No players have a ban");
        builder.Open(player, Instance!._menuManager);

    }

    public static void AdminCMD(CCSPlayerController player,CCSPlayerController target)
    {
        if (!SourceBans._enabled || player == null || !player.IsValid)
            return;

        var name = Instance?.Trunc(target.PlayerName,10);
        var builder = new HLZMenuBuilder($"{name}");
        SourceBans._userCache.TryGetValue(player.SteamID, out var userData);
        var adminFlags = userData.Flags.ToFlags();

        if (!target.IsBot) {
            if (adminFlags.Has(AdminFlags.Root | AdminFlags.Kick | AdminFlags.Ban))
                builder.Add("Kick/Ban", _ => AdminCMD1(player,target,"Ban"));
            builder.Add("Gag (Disable Chat)", _ => AdminCMD1(player,target,"Gag"));
            builder.Add("Mute (Disable Voice)", _ => AdminCMD1(player,target,"Mute"));
            builder.Add("Silence (Disable Voice/Chat", _ => AdminCMD1(player,target,"Silence"));
            if (adminFlags.Has(AdminFlags.Root | AdminFlags.Slay))
                builder.Add("Slay", _ => AdminConfirm(player,target,"Slay"));
            if (target.TeamNum > 0)
                builder.Add("Change Team", _ => AdminCMD1(player,target,"team"));
        } else {
            if (adminFlags.Has(AdminFlags.Root | AdminFlags.Kick | AdminFlags.Ban))
                builder.Add("Kick", _ => AdminConfirm(player,target,"kick"));
        }
        builder.Open(player, Instance!._menuManager);

    }

    public static void AdminCMD1(CCSPlayerController player,CCSPlayerController? target, string cmd)
    {
        if (!SourceBans._enabled || player == null || !player.IsValid || target == null)
            return;

        var name = Instance?.Trunc(target.PlayerName,10);
        var builder = new HLZMenuBuilder($"{name}");
        SourceBans._userCache.TryGetValue(player.SteamID, out var userData);
        var adminFlags = userData.Flags.ToFlags();

        if (cmd == "team")
        {
            if (target.TeamNum != 2)
                builder.Add($"Terrorist", _ => AdminConfirm(player,target,$"team 2"));
            if (target.TeamNum != 3)
                builder.Add($"Counter-Terrorist", _ => AdminConfirm(player,target,$"team 3"));
            if (target.TeamNum != 1)
                builder.Add($"Spectator", _ => AdminConfirm(player,target,$"team 1"));

        } else {
            if (adminFlags.Has(AdminFlags.Root | AdminFlags.Kick))
                builder.Add("Kick", _ => AdminConfirm(player,target,"kick"));
            if (adminFlags.Has(AdminFlags.Root | AdminFlags.Ban))
            {
                builder.Add($"{cmd} 15 minutes", _ => AdminConfirm(player,target,$"{cmd} 900"));
                builder.Add($"{cmd} 30 minutes", _ => AdminConfirm(player,target,$"{cmd} 1800"));
                builder.Add($"{cmd} 60 minutes", _ => AdminConfirm(player,target,$"{cmd} 3600"));
                builder.Add($"{cmd} 24 hours", _ => AdminConfirm(player,target,$"{cmd} 1440"));
                builder.Add($"{cmd} permanently", _ => AdminConfirm(player,target,$"{cmd} 0"));
            }
        }
        builder.Open(player, Instance!._menuManager);
    }

    public static void AdminCMD2(CCSPlayerController player,CCSPlayerController target)
    {
        if (!SourceBans._enabled || player == null || !player.IsValid)
            return;

        var name = Instance?.Trunc(target.PlayerName,10);
        var builder = new HLZMenuBuilder($"{name}");
        SourceBans._userCache.TryGetValue(target.SteamID, out var targetData);
        SourceBans._userCache.TryGetValue(player.SteamID, out var userData);
        var adminFlags = userData.Flags.ToFlags();
        DateTime now = DateTime.UtcNow; //DateTime.MaxValue

        if ((targetData.Ban & BanType.Kick)>0) {
            var remaining = targetData.ExpiryBan == DateTime.MaxValue ? "permanently" : SourceBans.FormatTimeLeft(targetData.ExpiryBan-now);
            var label = $"Remove Kick ({remaining})";
            if (adminFlags.Has(AdminFlags.Root | AdminFlags.Unban))
            {
                builder.Add(label, _ => AdminConfirm(player,target,"unkick"));
            } else {
                builder.AddNoOp(label);
            }
        }
        if ((targetData.Ban & BanType.Ban)>0) {
            var remaining = targetData.ExpiryBan == DateTime.MaxValue ? "permanently" : SourceBans.FormatTimeLeft(targetData.ExpiryBan-now);
            var label = $"Remove Ban ({remaining})";
            if (adminFlags.Has(AdminFlags.Root | AdminFlags.Unban))
            {
                builder.Add(label, _ => AdminConfirm(player,target,"unban"));
            } else {
                builder.AddNoOp(label);
            }
        }
        if ((targetData.Ban & BanType.Gag)>0) {
            var remaining = targetData.ExpiryBan == DateTime.MaxValue ? "permanently" : SourceBans.FormatTimeLeft(targetData.ExpiryComm - now);
            builder.Add($"Remove Gag ({remaining})", _ => AdminConfirm(player,target,"ungag"));
        }
        if ((targetData.Ban & BanType.Mute)>0) {
            var remaining = targetData.ExpiryBan == DateTime.MaxValue ? "permanently" : SourceBans.FormatTimeLeft(targetData.ExpiryComm-now);
            builder.Add($"Remove Mute ({remaining})", _ => AdminConfirm(player,target,"unmute"));
        }
        if ((targetData.Ban & BanType.Gag)>0 && (targetData.Ban & BanType.Mute)>0) {
            var remaining = targetData.ExpiryBan == DateTime.MaxValue ? "permanently" : SourceBans.FormatTimeLeft(targetData.ExpiryComm-now);
            builder.Add($"Remove Silence ({remaining})", _ => AdminConfirm(player,target,"unsilence"));
        }
 
        builder.Open(player, Instance!._menuManager);

    }

    public static void AdminConfirm(CCSPlayerController player,CCSPlayerController target, string cmd)
    {
        if (!SourceBans._enabled || player == null || !player.IsValid)
            return;

        var name = Instance?.Trunc(target.PlayerName,10);
        var builder = new HLZMenuBuilder($"{name}");
        var parts  = cmd.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        int length = parts.Length > 1 ?  int.Parse(parts[1]) : 0;
        string? level = parts.Length > 2 ? parts[2] : null;

        builder.Add("Confirm", _ => AdminAction(player,parts[0],target,level,length));

        builder.Open(player, Instance!._menuManager);

    }

    public static char TeamColor(int teamNum)
    {
        return teamNum switch {
            2 => ChatColors.Gold, // T
            3 => ChatColors.Blue, // CT
            _ => ChatColors.Grey  // Spectator/None
        };
    }

    public static string ActionMessage(CCSPlayerController? admin, string cmd, CCSPlayerController? target, string reason)
    {
        var (verb, color) = FormatVerb(cmd);
        var adminText  = admin  != null ? $"{TeamColor(admin.TeamNum)}{admin.PlayerName}{ChatColors.Default}" : "";
        var targetText = target != null ? $"{TeamColor(target.TeamNum)}{target.PlayerName}{ChatColors.Default}" : "";
        var reasonText = !string.IsNullOrEmpty(reason) ? $" ({ChatColors.White}{reason}{ChatColors.Default})" : "";

        return $"[HLstats{ChatColors.Red}Z{ChatColors.Default}] {adminText} {verb} {targetText}{reasonText}";
    }

    public static (string verb, char color) FormatVerb(string cmd)
    {
        return cmd switch {
            "kick"      => ("kicked", ChatColors.White),
            "ban"       => ("banned", ChatColors.White),
            "mute"      => ("muted", ChatColors.White),
            "silence"   => ("silenced", ChatColors.White),
            "gag"       => ("gagged", ChatColors.White),
            "unban"     => ("unbanned", ChatColors.White),
            "unmute"    => ("unmuted", ChatColors.White),
            "unsilence" => ("unsilenced", ChatColors.White),
            "ungag"     => ("ungagged", ChatColors.White),
            "slay"      => ("slayed", ChatColors.White),
            "team"      => ("changed Team for", ChatColors.White),
            _ => ("", ChatColors.White)
        };
    }

    public static bool AdminAction(CCSPlayerController admin,string action, CCSPlayerController? target, string? args, int number = 0)
    {
        if (admin == null || !admin.IsValid) return false;
        var cmd     = action.Trim().ToLowerInvariant();
        var reason  = (args ?? "").Trim();
        var message = "";
        var type    = BanType.None;
        var page    = 0;
        int min     = 1;
        if (string.IsNullOrEmpty(reason))
            reason = "Admin Action";

        switch (cmd)
        {
            case "kick":
                if (target == null || !target.IsValid) return false;
                SourceBans.DelayedCommand($"kickid {target.UserId} \"Kicked {target.PlayerName} ({reason})\"",3.0f);
                SourceBans.UpdateBanUser(target, BanType.Kick, DateTime.UtcNow.AddMinutes(2));
                message = ActionMessage(admin, cmd, target,reason);
                SendChatToAll(message);
            break;
            case "votekick":
                if (target == null || !target.IsValid) return false;
                if (SourceBans._vote.ContainsKey("kick"))
                {
                    SendPrivateChat(admin, $"[HLstats{ChatColors.Red}Z{ChatColors.Default}] {ChatColors.Green}!menu{ChatColors.Default} to access the current vote");
                    return false;
                }
                min = Utilities.GetPlayers().Count(p => p != null && p.IsValid && !p.IsBot);
                SourceBans._vote["kick"] = (DateTime.UtcNow, target, target.PlayerName, null, 0, 0, Math.Max(1,min/2));
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p?.IsValid == true && p?.IsBot == false)
                        VoteActive("kick", p, target, null);
                }
                SourceBans.StartVoteTimer();
            return false;
            case "ban":
            case "gag":
            case "mute":
            case "silence":
                if (target == null || !target.IsValid) return false;
                type = cmd == "ban" ? BanType.Ban :
                       cmd == "gag" ? BanType.Gag :
                       cmd == "mute" ? BanType.Mute : BanType.Silence;
                if (cmd == "mute" || cmd == "silence")
                    target.VoiceFlags = VoiceFlags.Muted;
                _ = SourceBans.WriteBan(target, admin, type, number, reason);
                SourceBans.UpdateBanUser(target, type, number == 0 ? DateTime.MaxValue : DateTime.UtcNow.AddSeconds(number));
                message = ActionMessage(admin, cmd, target, reason);
                SendChatToAll(message);
                if (cmd == "ban")
                    Server.ExecuteCommand($"kickid {target.UserId} \"Banned {target.PlayerName} ({reason})\"");
            break;
            case "ungag":
            case "unmute":
            case "unsilence":
            case "unban":
                page=1;
                if (target == null || !target.IsValid) return false;
                type = cmd == "ungag" ? BanType.Gag :
                       cmd == "unmute" ? BanType.Mute :
                       cmd == "unsilence" ?  BanType.Silence : BanType.Ban;
                if (cmd == "unmute" || cmd == "unsilence")
                    target.VoiceFlags = VoiceFlags.Normal;
                _ = SourceBans.WriteUnBan(target, admin, type, reason);
                SourceBans.UpdateBanUser(target, type, DateTime.UtcNow,true);
                message = ActionMessage(admin, cmd, target, reason);
                SendChatToAll(message);
            break;
            case "map": 
            case "votemap":
                page = 2;
                if (args == null) return false;
                if (cmd == "votemap" && SourceBans._vote.ContainsKey("map"))
                {
                    SendPrivateChat(admin, $"[HLstats{ChatColors.Red}Z{ChatColors.Default}] {ChatColors.Green}!menu{ChatColors.Default} to access the current vote");
                    return false;
                }
                var availableMaps = SourceBans.GetAvailableMaps(Instance!.Config, true);
                var match = availableMaps.FirstOrDefault(m =>
                    string.Equals(m.DisplayName, args, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(m.MapName, args, StringComparison.OrdinalIgnoreCase) ||
                    (m.WorkshopId != null && string.Equals(m.WorkshopId, args, StringComparison.OrdinalIgnoreCase)));
                if (match == null)
                {
                    SendPrivateChat(admin, $"[HLstats{ChatColors.Red}Z{ChatColors.Default}] Map '{ChatColors.Green}{args}{ChatColors.Default}' not found. Use {ChatColors.Green}!menu{ChatColors.Default} for map list");
                    return false;
                }
                if (cmd=="votemap") {
                    min = Utilities.GetPlayers().Count(p => p != null && p.IsValid && !p.IsBot);
                    SourceBans._vote["map"] = (DateTime.UtcNow, null, null, args, 0, 0, Math.Max(1,min/2));
                    foreach (var p in Utilities.GetPlayers())
                    {
                        if (p?.IsValid == true && p?.IsBot == false)
                            VoteActive("map",p,null,args);
                    }
                    SourceBans.StartVoteTimer();
                    return false;
                }
                message = $"[HLstats{ChatColors.Red}Z{ChatColors.Default}] {TeamColor(admin.TeamNum)}{admin.PlayerName}{ChatColors.Default} changed map to {ChatColors.Green}match.DisplayName{ChatColors.Default}";
                SendChatToAll(message);
                var command = match.IsSteamWorkshop ? $"host_workshop_map {match.WorkshopId}" :
                                                      $"changelevel {match.MapName}";
                SourceBans.DelayedCommand(command,5.0f);
            break;
            case "slay":
                if (target == null || !target.IsValid) return false;
                target.CommitSuicide(false, true);
                message = ActionMessage(admin, cmd, target,reason);
                SendChatToAll(message);
            break;
            case "team":
                if (target == null || !target.IsValid) return false;
                SendPrivateChat(admin,"{number}");
                CsTeam csTeam;
                switch ((TeamNum)number)
                {
                    case TeamNum.Spectator:
                        csTeam = CsTeam.Spectator;
                        break;
                    case TeamNum.Terrorist:
                        csTeam = CsTeam.Terrorist;
                        break;
                    case TeamNum.CounterTerrorist:
                        csTeam = CsTeam.CounterTerrorist;
                        break;
                    default:
                        SendPrivateChat(admin, $"[HLstats{ChatColors.Red}Z{ChatColors.Default}] Invalid team ID {number}");
                        return false;
                }
                target.ChangeTeam(csTeam);
                message = ActionMessage(admin, cmd, target, "");
                SendChatToAll(message);
            break;
            case "rtv":
                var allMaps = SourceBans.GetAvailableMaps(Instance!.Config, true);
                string currentMap = Server.MapName;
                var candidates = allMaps
                    .Where(m => !string.Equals(m.MapName, currentMap, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (candidates.Count == 0) {
                    admin.PrintToChat($"[HLstats{ChatColors.Red}Z{ChatColors.Default}] No alternative maps available.");
                    return false;
                }
                var rng = new Random();
                SourceBans._rtv = candidates.OrderBy(_ => rng.Next()).Take(5).ToList();
                min = Utilities.GetPlayers().Count(p => p != null && p.IsValid && !p.IsBot);
                SourceBans._vote["map"] = (DateTime.UtcNow, null, admin.PlayerName, null, 0, 0, Math.Max(1,min/2));
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p?.IsValid == true && p?.IsBot == false)
                        VoteActive("rtv",p,null,args);
                }
                SourceBans.StartVoteTimer();
            return false;
            default:
            break;
        }

        if (Instance!._menuManager._activeMenus.TryGetValue(admin.SteamID, out var menu))
        {

            if (page == 0)
            {
                Instance!._menuManager.RewindToLabel(admin,"Admin Players");
                AdminPlayer(admin,page);
            }

            if (page == 1)
            {
                Instance!._menuManager.RewindToLabel(admin,"Admin Unban");
                AdminPlayer(admin,page);
            }

           if ( page == 2)
           {
               Instance!._menuManager.DestroyMenu(admin);
           }

        }
        return true;
    }

    // ------------------ Event Handler ------------------
    public HookResult OnRoundMvp(EventRoundMvp @event, GameEventInfo info)
    {
        var reason = @event.Reason;
        var player = @event.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;

        string reasonText = reason switch
        {
            1  => "with most eliminations",
            2  => "with bomb planted",
            3  => "with bomb defused",
            4  => "with hostage rescued",
            11 => "with HE grenade",
            14 => "with a clutch defuse",
            15 => "with most kills",
            _  => $"with best overall"
        };

        _ = SendLog(player, $"round_mvp {reasonText}", "triggered");
        return HookResult.Continue;
    }

    public HookResult OnBombDefused(EventBombDefused @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;

        _ = SendLog(player, "Defused_The_Bomb", "triggered");
        return HookResult.Continue;
    }

    public HookResult OnPlayerConnect(EventPlayerConnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null) return HookResult.Continue;

        _ = SourceBans.isAdmin(player);

        return HookResult.Continue;
    }

    public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null) return HookResult.Continue;

        _ = SourceBans.isAdmin(player);

        if (SourceBans._userCache.TryGetValue(player.SteamID, out var userData))
        {
            if ((userData.Ban & (BanType.Ban | BanType.Kick))>0)
            {
                DateTime now = DateTime.UtcNow;
                if (userData.ExpiryBan > now)
                {
                    var timeleft = SourceBans.FormatTimeLeft(userData.ExpiryBan - DateTime.UtcNow);
                    var remain = DateTime.MaxValue > userData.ExpiryBan ? $"({timeleft} remaining)" : "(permanently)";
                    Server.ExecuteCommand($"kickid {player.UserId} \"You are banned from this server {remain}\"");
                    Server.PrintToChatAll($"[HLstats{ChatColors.Red}Z{ChatColors.Default}] {player.PlayerName} tried to join while banned {remain}");
                    return HookResult.Continue;
                }
            }
            if ((userData.Ban & (BanType.Mute | BanType.Silence))>0)
            {
                var timeleft = SourceBans.FormatTimeLeft(userData.ExpiryBan - DateTime.UtcNow);
                var remain = DateTime.MaxValue > userData.ExpiryBan ? $"({timeleft} remaining)" : "(permanently)";
                player.VoiceFlags = VoiceFlags.Muted;
                player.PrintToChat($"[HLstats{ChatColors.Red}Z{ChatColors.Default}] {player.PlayerName}, you are muted {remain}");
            }
        }
        return HookResult.Continue;
    }

    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.IsValid)
            _menuManager.DestroyMenu(player);
        if (player != null && !SourceBans._enabled && SourceBans._userCache.TryGetValue(player.SteamID, out var userData))
           SourceBans._userCache.Remove(player.SteamID);
        return HookResult.Continue;
    }

    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.IsValid)
            _menuManager.DestroyMenu(player);
        return HookResult.Continue;
    }

}
