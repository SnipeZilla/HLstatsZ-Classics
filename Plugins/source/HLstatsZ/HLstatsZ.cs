using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
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
using System.Globalization;
using System.Net.Sockets;
using System.Net;
using System.Text;

namespace HLstatsZ;

public class HLstatsZMainConfig : IBasePluginConfig
{
    [JsonPropertyName("Enable_HLstats")] public bool Enable_HLstats { get; set; } = false;
    [JsonPropertyName("Enable_Sourcebans")] public bool Enable_Sourcebans { get; set; } = true;
    [JsonPropertyName("Enable_Discord")] public bool Enable_Discord { get; set; } = false;
    [JsonPropertyName("Log_Address")] public string Log_Address { get; set; } = "127.0.0.1";
    [JsonPropertyName("Log_Port")] public int Log_Port { get; set; } = 27500;
    [JsonPropertyName("BroadcastAll")] public int BroadcastAll { get; set; } = 0;
    [JsonPropertyName("ServerAddr")] public string ServerAddr { get; set; } = "";
    public int Version { get; set; } = 2;

    [JsonPropertyName("SourceBans")] public SourceBansConfig SourceBans { get; set; } = new();
    [JsonPropertyName("Discord")] public DiscordConfig Discord { get; set; } = new();
}

public class SourceBansConfig
{
    [JsonPropertyName("Host")] public string Host { get; set; } = "127.0.0.1";
    [JsonPropertyName("Port")] public int Port { get; set; } = 3306;
    [JsonPropertyName("Database")] public string Database { get; set; } = "";
    [JsonPropertyName("Prefix")] public string Prefix { get; set; } = "sb";
    [JsonPropertyName("User")] public string User { get; set; } = "";
    [JsonPropertyName("Password")] public string Password { get; set; } = "";
    [JsonPropertyName("Website")] public string Website { get; set; } = "";
    [JsonPropertyName("VoteKick")] public string VoteKick { get; set; } = "public";
    [JsonPropertyName("VoteMap")] public string VoteMap { get; set; } = "private";
    [JsonPropertyName("Nominate")] public bool Nominate { get; set; } = false;
    [JsonPropertyName("Chat_Ban_Duration_Max")] public int Chat_Ban_Duration_Max { get; set; } = 10080;
    [JsonPropertyName("Menu_Ban1_Duration")] public int Menu_Ban1_Duration { get; set; } = 15;
    [JsonPropertyName("Menu_Ban2_Duration")] public int Menu_Ban2_Duration { get; set; } = 60;
    [JsonPropertyName("Menu_Ban3_Duration")] public int Menu_Ban3_Duration { get; set; } = 1440;
    [JsonPropertyName("Menu_Ban4_Duration")] public int Menu_Ban4_Duration { get; set; } = 10080;
    [JsonPropertyName("Menu_Ban5_Duration")] public int Menu_Ban5_Duration { get; set; } = 0;

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

public sealed class DiscordConfig
{
    public string WebhookUrl          { get; set; } = "";
    public string Username            { get; set; } = "HLstatsZ-Bot";
    public string Description         { get; set; } = "Contest or Appeal? visit →";
    public string TitleKick           { get; set; } = "👢 Kicked";
    public string TitleBan            { get; set; } = "🚫 Banned";
    public string TitleGag            { get; set; } = "🤐 Gagged";
    public string TitleMute           { get; set; } = "🔇 Muted";
    public string TitleSilence        { get; set; } = "🤫 Silenced";
    public string TitleUnban          { get; set; } = "✅ Unbanned";
    public string TitleUngag          { get; set; } = "😁 Ungagged";
    public string TitleUnmute         { get; set; } = "🔊 Unmuted";
    public string TitleUnsilence      { get; set; } = "🥳 Unsilenced";
    public string ColorPermanent      { get; set; } = "#FF0000";
    public string ColorWithExpiration { get; set; } = "#FF9900";
    public string ColorUnban          { get; set; } = "#00FF00";
    public bool   ShowAdmin           { get; set; } = true;
}

public class HLstatsZ : BasePlugin, IPluginConfig<HLstatsZMainConfig>
{
    public static HLstatsZ? Instance;
    private static readonly HttpClient httpClient = new();
    public static HttpClient Http => httpClient;
    public HLstatsZMainConfig Config { get; set; } = new();
    public HLZMenuManager _menuManager = null!;

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

        if (string.IsNullOrWhiteSpace(Config.Log_Address) || !Config.Enable_HLstats)
        {
            _enabled = false;
            Instance?.Logger.LogInformation("[HLstatsZ] HLstats disabled: missing config (Enable_HLstats/Log_Address).");
            return;
        }
        _enabled = true;

       SourceBans.Durations[0] = Config.SourceBans.Chat_Ban_Duration_Max *60;
       SourceBans.Durations[1] = Config.SourceBans.Menu_Ban1_Duration    *60;
       SourceBans.Durations[2] = Config.SourceBans.Menu_Ban2_Duration    *60;
       SourceBans.Durations[3] = Config.SourceBans.Menu_Ban3_Duration    *60;
       SourceBans.Durations[4] = Config.SourceBans.Menu_Ban4_Duration    *60;
       SourceBans.Durations[5] = Config.SourceBans.Menu_Ban5_Duration    *60;

    }

    public override void Load(bool hotReload)
    {
        Instance = this;

        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnClientAuthorized>(OnClientAuthorized);

        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        RegisterEventHandler<EventRoundMvp>(OnRoundMvp);
        RegisterEventHandler<EventBombAbortdefuse>(OnBombAbortdefuse);
        RegisterEventHandler<EventBombDefused>(OnBombDefused);
        RegisterEventHandler<EventPlayerConnect>(OnPlayerConnect);
        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        RegisterEventHandler<EventCsWinPanelMatch>(OnCsWinPanelMatch);

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
        SourceBans.hostName = ConVar.Find("hostname")?.StringValue ?? "Counter-Strike 2";
        _ = SourceBans.GetSid();

        if (hotReload)
        {
            _ = SourceBans.Refresh();
            SourceBans._canVote = true;
        }

        SourceBans._cleanupTimer?.Kill();
        if (SourceBans._enabled)
            SourceBans._cleanupTimer = new GameTimer(60.0f,
                                            () => SourceBans.CleanupExpiredUsers(),
                                            TimerFlags.REPEAT
            );
    }

    public override void Unload(bool hotReload)
    {
        RemoveListener<Listeners.OnTick>(OnTick);
        RemoveListener<Listeners.OnMapStart>(OnMapStart);
        RemoveListener<Listeners.OnClientAuthorized>(OnClientAuthorized);

        DeregisterEventHandler<EventRoundStart>(OnRoundStart);
        DeregisterEventHandler<EventRoundEnd>(OnRoundEnd);
        DeregisterEventHandler<EventRoundMvp>(OnRoundMvp);
        DeregisterEventHandler<EventBombAbortdefuse>(OnBombAbortdefuse);
        DeregisterEventHandler<EventBombDefused>(OnBombDefused);
        DeregisterEventHandler<EventPlayerConnect>(OnPlayerConnect);
        DeregisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        DeregisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        DeregisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        DeregisterEventHandler<EventCsWinPanelMatch>(OnCsWinPanelMatch);

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

    private static readonly Dictionary<string, char> ColorCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["default"]     = '\x01',
        ["white"]       = '\x01',
        ["darkred"]     = '\x02',
        ["green"]       = '\x04',
        ["lightyellow"] = '\x09',
        ["yellow"]      = '\x09',
        ["lightblue"]   = '\x0B',
        ["blue"]        = '\x0B',
        ["darkblue"]    = '\x0C',
        ["olive"]       = '\x05',
        ["lime"]        = '\x06',
        ["red"]         = '\x07',
        ["lightpurple"] = '\x03',
        ["purple"]      = '\x0E',
        ["magenta"]     = '\x0E',
        ["grey"]        = '\x08',
        ["gold"]        = '\x10',
        ["orange"]      = '\x10',
        ["silver"]      = '\x0A',
        ["bluegrey"]    = '\x0A',
        ["lightred"]    = '\x0F',
    };

    private static readonly Regex ColorRegex = new(@"\{(\w+)\}", RegexOptions.Compiled);

    public string T(string key, params object[] args)
        => Colors(Localizer[key, args]);
    
    public static string T(CCSPlayerController? p, string key, params object[] args)
    {
        if (p == null) return Instance!.T(key, args);
        return Colors(Instance!.Localizer.ForPlayer(p, key, args));
    }

    public static void privateChat(CCSPlayerController player, string key, params object[] args)
    {
        player.PrintToChat(T(player, "sz_chat.prefix") + " " + T(player, key, args));
    }

    public static void publicChat(string key, params object[] args)
    {
        var players = Utilities.GetPlayers();
        foreach (var player in players)
        {
            if (player.IsValid == true && player.IsBot == false)
            {
                player.PrintToChat(T(player, "sz_chat.prefix") + " " + T(player, key, args));

            }
        }
    }

    public static string Colors(string input)
    {
        return ColorRegex.Replace(input, m =>
        {
            var key = m.Groups[1].Value;
            return ColorCodes.TryGetValue(key, out var code) ? code.ToString() : m.Value;
        });
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
        if (Instance == null || player == null) return;

        switch (type)
        {
            case "psay":
                SendPrivateChat(player, message);
                break;
            case "csay":
                Instance.BroadcastCenterMessage(message);
                break;
            case "msay":
                Instance._menuManager.Open(player,message);
                break;
            case "say":
                SendChatToAll(message);
                break;
            case "hint":
                ShowHintMessage(player, message);
                break;
            default:
                player.PrintToChat($"Unknown HLX type: {type}");
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

        var players = Utilities.GetPlayers().Where(p => p?.IsValid == true).ToList();

        // 1. Exact match
        var exactMatches = players.Where(p => NormalizeName(p.PlayerName) == name).ToList();
        if (exactMatches.Count == 1) return exactMatches[0];
        if (exactMatches.Count > 1) return null;

        // 2. Unique "starts with"
        var prefixMatches = players.Where(p => NormalizeName(p.PlayerName).StartsWith(name)).ToList();
        if (prefixMatches.Count == 1) return prefixMatches[0];
        if (prefixMatches.Count > 1) return null;

        // 3. Unique "contains"
        var containsMatches = players.Where(p => NormalizeName(p.PlayerName).Contains(name)).ToList();
        if (containsMatches.Count == 1) return containsMatches[0];
        if (containsMatches.Count > 1) return null;

    return null;
    }

    public static void SendPrivateChat(CCSPlayerController player, string message)
    {
        player.PrintToChat($"{message}");
    }

    public static void SendChatToAll(string message)
    {
        var players = Utilities.GetPlayers();
        foreach (var player in players)
        {
            if (player?.IsValid == true && player?.IsBot == false)
            {
                player.PrintToChat($"{message}");
            }
        }
    }

    public void BroadcastCenterMessage(string message, float durationInSeconds = 5.0f)
    {
        string messageHTML = message.Replace("HLstatsZ","<font color='#FFFFFF'>HLstats</font><font color='#FF2A2A'>Z</font>");
        string messageCHAT = message.Replace("HLstatsZ", "HLstats\x07Z\x01");

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
        if (player == null || !player.IsValid)
            return HookResult.Continue;

        if (!SourceBans._userCache.TryGetValue(player.SteamID, out var userData))
            return HookResult.Continue;

        bool MenuIsOpen = _menuManager._activeMenus.ContainsKey(player.SteamID);
        var command = info.ArgByIndex(0).ToLower();

        if (MenuIsOpen && command == "single_player_pause")
            Instance!._menuManager.DestroyMenu(player);

        if (!command.StartsWith("say"))
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

        var cmd       = parts[0].ToLowerInvariant();
        bool votekick = (SourceBans.VoteKick == "public" || (SourceBans.VoteKick == "admin" && userData.IsAdmin));
        bool votemap  = (SourceBans.VoteMap == "public"  || (SourceBans.VoteMap == "admin" && userData.IsAdmin));

        // ----- HLstatsZ -> Daemon -----
        if (silenced && parts.Length == 1)
        {
            switch (cmd)
            {
                case "rank":
                case "next":
                case "top10":
                case "top20":
                case "top30":
                case "session":
                    _ = SendLog(player, cmd, "say");
                return HookResult.Handled;
                default:
                break;
            }
        }

        // ----- Handle Gag -----
        if ((userData.Ban & BanType.Gag)!=0)
            return HookResult.Handled;

        // ---- Handle Menu Command ----
        if ((cmd == "menu" || cmd == "hlx_menu") && parts.Length == 1)
        {
            if (MenuIsOpen)
                Instance!._menuManager.DestroyMenu(player);

            var builder = new HLZMenuBuilder("Main Menu");
            if (HLstatsZ._enabled)
            {
                builder.Add("Rank", p => _ = SendLog(p, "rank", "say"));
                builder.Add("Next Rank", p => _ = SendLog(p, "next", "say"));
                builder.Add("TOP 10", p => _ = SendLog(p, "top10", "say"));
            }
            if (SourceBans._enabled && (votekick || votemap))
                builder.Add(T(player,"sz_menu.vote"), p => VoteMenu(p));

            if (userData.IsAdmin)
                builder.Add(T(player,"sz_menu.admin"), p => AdminMenu(p));

            builder.Open(player, Instance!._menuManager);

            return HookResult.Handled;
        }

        // ----- SourceBans -----
        if (!SourceBans._enabled)
            return HookResult.Continue;


        // ----- SourceBans Public Commands -----
        if (!prefixed) return HookResult.Continue;

        switch (cmd)
        {
            case "rtv":
            case "votemap":
                if (!votemap)
                {
                    privateChat(player, "sz_chat.permission");
                    return HookResult.Continue;
                }
                if (SourceBans._vote.ContainsKey("map") || SourceBans._vote.ContainsKey("kick"))
                {
                    privateChat(player,"sz_chat.vote_inprogress");
                    return HookResult.Handled;
                }
                if (SourceBans._mVote > 1)
                {
                    privateChat(player, "sz_chat.vote_map_enough");
                    return HookResult.Handled;
                }
                if (!SourceBans._canVote)
                {
                    privateChat(player, "sz_chat.vote_map_early");
                    return HookResult.Continue;
                }
                if (cmd == "rtv")
                {
                    AdminAction(player,cmd,player,null);
                    return HookResult.Handled;
                }
                if (parts.Length == 1)
                {
                    VoteMap(player,"vote");
                    return HookResult.Handled;
                }
                var map = string.Join(' ', parts.Skip(1)).Trim();
                AdminAction(player,cmd,player,map);
            return HookResult.Handled;
            case "votekick":
                if (!votekick)
                {
                    privateChat(player, "sz_chat.permission");
                    return HookResult.Continue;
                }
                if (SourceBans._vote.ContainsKey("map") || SourceBans._vote.ContainsKey("kick"))
                {
                    privateChat(player,"sz_chat.vote_inprogress");
                    return HookResult.Handled;
                }
                if (parts.Length == 1)
                {
                    VotePlayer(player);
                    return HookResult.Handled;
                }
                var who    = parts.Length > 1 ? parts[1].Trim() : "";
                var reason = parts.Length > 2 ? string.Join(' ', parts.Skip(2)).Trim() : "";

                if (string.IsNullOrWhiteSpace(who) || string.IsNullOrWhiteSpace(reason)) 
                {
                    privateChat(player, "sz_chat.generic_usage",cmd);
                    return HookResult.Handled;
                }
                if (reason.Length < 3)
                {
                    privateChat(player, "sz_chat.short_reason");
                    return HookResult.Handled;
                }
                var target = FindTarget(who);
                if (target != null)
                {
                    AdminAction(player,"votekick",target,reason);
                }
                else
                {
                    privateChat(player, "sz_chat.target_notfound",who);
                }
            return HookResult.Handled;
            case "nominate":
                if (!SourceBans.Nominate)
                {
                    privateChat(player, "sz_chat.permission");
                    return HookResult.Continue;
                }
                if (!SourceBans._canVote)
                {
                    privateChat(player, "sz_chat.vote_map_early");
                    return HookResult.Continue;
                }
                if (SourceBans._userNominate.ContainsKey(player.SteamID))
                {
                    privateChat(player, "sz_chat.nominate_already");
                    return HookResult.Handled;
                }
                if (parts.Length == 1)
                {
                    VoteMap(player, "nominate");
                    return HookResult.Handled;
                }
                var mapNom = string.Join(' ', parts.Skip(1)).Trim();
                AdminAction(player,cmd,player,mapNom);
            return HookResult.Handled;
        }

        // ---- Handle Admin command ----
        if (!userData.IsAdmin) return HookResult.Continue;

        var adminFlags = userData.Flags.ToFlags();

        switch (cmd)
        {
            case "say":
                var reason = parts.Length > 1 ? string.Join(' ', parts.Skip(1)).Trim() : "";
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    SendChatToAll(Colors($"{reason}"));
                    return HookResult.Handled;
                }
            return HookResult.Continue;

            case "hsay":
            case "psay":
                if (parts.Length < 2) 
                {
                    privateChat(player, "sz_chat.generic_usage",cmd);
                    return HookResult.Handled;
                }
                var who = parts.Length > 1 ? parts[1].Trim() : "";
                reason  = parts.Length > 2 ? string.Join(' ', parts.Skip(2)).Trim() : "";
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    privateChat(player, "sz_chat.generic_usage", cmd);
                    return HookResult.Handled;
                }
                var target = FindTarget(who);
                if (target != null)
                {
                   if (cmd == "psay")
                       SendPrivateChat(target, Colors($"{reason}"));
                   else ShowHintMessage(player, Colors($"{reason}"));
                }
                else
                {
                    privateChat(player, "sz_chat.target_notfound", who);
                }
            return HookResult.Handled;

            case "admin":
                if (parts.Length == 1)
                {
                    AdminMenu(player);
                    return HookResult.Handled;
                }
            return HookResult.Continue;

            case "kick":
                if (!adminFlags.Has(AdminFlags.Root | AdminFlags.Kick))
                {
                    privateChat(player, "sz_chat.permission");
                    return HookResult.Handled;
                }
                if (parts.Length == 1)
                {
                    AdminPlayer(player);
                    return HookResult.Handled;
                }
                if (parts.Length < 3) 
                {
                    privateChat(player, "sz_chat.generic_usage", cmd);
                    return HookResult.Handled;
                }
                who    = parts.Length > 1 ? parts[1].Trim() : "";
                reason = parts.Length > 2 ? string.Join(' ', parts.Skip(2)).Trim() : "";
                if (reason.Length < 3)
                {
                    privateChat(player, "sz_chat.short_reason");
                    return HookResult.Handled;
                }
                target = FindTarget(who);
                if (target != null)
                {
                    AdminAction(player, "kick", target, reason);
                }
                else
                {
                    privateChat(player, "sz_chat.target_notfound", who);
                }
            return HookResult.Handled;

            case "map":
                if (!adminFlags.Has(AdminFlags.Root | AdminFlags.Map))
                {
                    privateChat(player, "sz_chat.permission");
                    return HookResult.Handled;
                }
                if (parts.Length == 1)
                {
                    MapMenu(player);
                    return HookResult.Handled;
                }
                var map = string.Join(' ', parts.Skip(1)).Trim();
                AdminAction(player, cmd, player, map);
            return HookResult.Handled;

            case "ban":
            case "banip":
            case "gag":
            case "mute":
            case "silence":
            case "slay":
            if (((cmd == "ban" || cmd == "banip") && !adminFlags.Has(AdminFlags.Root | AdminFlags.Ban)) ||
                (cmd == "slay" && !adminFlags.Has(AdminFlags.Root | AdminFlags.Slay)))
            {
                privateChat(player, "sz_chat.permission");
                return HookResult.Handled;
            }
            var length = parts.Length > 2 ? parts[2] : "1";
            reason  = parts.Length > 3 ? string.Join(' ', parts.Skip(3)).Trim() : "";
            if (cmd == "slay")
            {
                if (parts.Length == 1)
                {
                    privateChat(player, "sz_chat.generic_usage", cmd);
                    return HookResult.Handled;
                }
            } else {
                if (parts.Length < 3 || !(int.TryParse(length, out int min) && min >= 0)) 
                {
                    privateChat(player, "sz_chat.banning_usage", cmd);
                    return HookResult.Handled;
                }
                if (min > SourceBans.Durations[0]) 
                {
                    privateChat(player, "sz_chat.ban_exceeds", cmd);
                    return HookResult.Handled;
                }
                if (!adminFlags.Has(AdminFlags.Root | AdminFlags.Rcon) && min == 0)
                {
                    privateChat(player, "sz_chat.permission");
                    return HookResult.Handled;
                }
                if (reason.Length < 3)
                {
                    privateChat(player, "sz_chat.short_reason");
                    return HookResult.Handled;
                }
            }
            who    = parts.Length > 1 ? parts[1].Trim() : "";
            target = FindTarget(who);
            if (target != null && target.IsValid)
            {
                AdminAction(player, cmd, target, reason, int.Parse(length)*60);
            }
            else
            {
                privateChat(player, "sz_chat.target_notfound", who);
            }
            return HookResult.Handled;

            case "unban":
            case "ungag":
            case "unmute":
            case "unsilence":
            if (!adminFlags.Has(AdminFlags.Root | AdminFlags.Unban))
            {
                privateChat(player, "sz_chat.permission");
                return HookResult.Handled;
            }
            length = parts.Length > 1 ? parts[1] : "1";
            reason  = parts.Length > 2 ? string.Join(' ', parts.Skip(2)).Trim() : "";
            if (parts.Length < 3) 
            {
                privateChat(player, "sz_chat.generic_usage", cmd);
                return HookResult.Handled;
            }
            if (reason.Length < 3)
            {
                privateChat(player, "sz_chat.short_reason");
                return HookResult.Handled;
            }
            who    = parts.Length > 1 ? parts[1].Trim() : "";
            target = FindTarget(who);
            if (target != null && target.IsValid)
            {
                AdminAction(player, cmd, target, reason);
            }
            else
            {
                privateChat(player, "sz_chat.target_notfound", who);
            }
            return HookResult.Handled;

            case "team":
                 if (parts.Length < 3)
                 {
                     privateChat(player, "sz_chat.team_usage", cmd);
                     return HookResult.Handled;
                 }
                 var name = parts[2].ToLowerInvariant();
                 var team  = 0;
                 switch (name)
                 {
                    case "1":
                    case "s":
                    case "spec":
                    case "spectator":
                        team = 1;
                        name = "Spectator";
                    break;
                    case "2":
                    case "t":
                    case "tt":
                    case "terrorist":
                        team = 2;
                        name = "Terrorist";
                    break;
                    case "3":
                    case "ct":
                    case "counter-terrorist":
                    case "counterterrorist":
                        team = 3;
                        name = "Counter-Terrorist";
                    break;
                    default:
                        privateChat(player, "sz_chat.team_usage", cmd);
                    return HookResult.Handled;
                }
                who    = parts[1].Trim();
                target = FindTarget(who);
                if (target != null && target.IsValid)
                {
                    AdminAction(player,cmd,target,name,team);
                } else {
                    privateChat(player, "sz_chat.target_notfound", who);
                }
            return HookResult.Handled;

            case "players":
            case "camera":
                var opt = parts.Length > 1 ? parts[1].Trim() : "";
                int d = opt == "-d" ? 1 : 0;
                SourceBans.CameraCommand(player, d);
            return HookResult.Handled;

            case "refresh":
                if(!adminFlags.Has(AdminFlags.Root | AdminFlags.Rcon))
                {
                    privateChat(player, "sz_chat.permission");
                    return HookResult.Handled;
                }
                _ = SourceBans.Refresh();
            return HookResult.Handled;

            default:
            return HookResult.Continue;
        }
    }

    // --------------------- Console ---------------------
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
    private const int PollInterval = 6;
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
        var adminFlags = userData.Flags.ToFlags();

        var builder = new HLZMenuBuilder("Admin Menu")
                     .Add(T(player,"sz_menu.players"), _ => AdminPlayer(player))
                     .Add(T(player,"sz_menu.remove_ban"), _ => AdminPlayer(player,1));

        if (adminFlags.Has(AdminFlags.Root | AdminFlags.Map))
            builder.Add(T(player,"sz_menu.map_change"), _ => MapMenu(player));

        builder.Add(T(player,"sz_menu.history"), _ => AdminHistory(player));

        builder.Open(player, Instance!._menuManager);

    }

    public static void VoteMenu(CCSPlayerController player)
    {
        if (!SourceBans._enabled || player == null || !player.IsValid) return;

        SourceBans._userCache.TryGetValue(player.SteamID, out var userData);
        bool votekick = (SourceBans.VoteKick == "public" || (SourceBans.VoteKick == "admin" && userData.IsAdmin));
        bool votemap = (SourceBans.VoteMap == "public" || (SourceBans.VoteMap == "admin" && userData.IsAdmin));

        var builder = new HLZMenuBuilder("Vote Menu");
        if (!SourceBans._vote.ContainsKey("kick") && !SourceBans._vote.ContainsKey("map") && votekick)
        {
            builder.Add(T(player,"sz_menu.player_kick"), _ => VotePlayer(player));
        } else {
            builder.AddNoOp(T(player,"sz_menu.player_kick"));
        }
        if (SourceBans.Nominate && !SourceBans._userNominate.ContainsKey(player.SteamID) && SourceBans._canVote)
        {
            builder.Add("Nominate", _ => VoteMap(player, "nominate"));
        } else {
            if (SourceBans.Nominate)
                builder.AddNoOp("Nominate");
        }

        if (!SourceBans._vote.ContainsKey("kick") && !SourceBans._vote.ContainsKey("map") && SourceBans._canVote && SourceBans._mVote < 2 && votemap)
        {
            builder.Add(T(player,"sz_menu.map_change"), _ => VoteMap(player, "vote"));
        } else {
            if (votemap)
                builder.AddNoOp(T(player,"sz_menu.map_change"));
        }
        if (!SourceBans._vote.ContainsKey("kick") && !SourceBans._vote.ContainsKey("map") && SourceBans._canVote && SourceBans._mVote < 2 && votemap)
        {
            builder.Add(T(player,"sz_menu.map_rtv"), _ => AdminConfirm(player,player,"rtv"));
        } else {
            if (votemap)
                builder.AddNoOp(T(player,"sz_menu.map_rtv"));
        }
        builder.Add(T(player,"sz_menu.active_vote"), _ => VoteActive("check",player,null,null));

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

    public static void VoteMap(CCSPlayerController player, string type)
    {
        if (!SourceBans._enabled || player == null || !player.IsValid)
            return;

        var title = type == "vote"? "Vote Map" : "Nominate";
        var action = type == "vote"? "votemap" : "nominate";

        var maps = SourceBans.GetAvailableMaps(Instance!.Config, false);

        var builder = new HLZMenuBuilder($"{title}");
        foreach (var entry in maps)
        {
            string label = entry.IsSteamWorkshop ? $"[WS] {entry.DisplayName}" : entry.DisplayName;
            builder.Add(label, _ => AdminConfirm(player, player, $"{action} 1 {entry.DisplayName}"));
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
                player.PrintToChat("[HLstats\x07Z\x01] Invalid map choice");
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
            builder.AddNoOp(T(player,"sz_menu.no_active_vote"));

        if (type != "check")
             Instance!._menuManager.DestroyMenu(player);

        builder.Open(player, Instance!._menuManager);
    }

    private static void BuildVoteKickMenu(HLZMenuBuilder builder, CCSPlayerController player, CCSPlayerController? target)
    {
        string playerName = SourceBans._vote.TryGetValue("kick", out var vote) ? vote.target?.PlayerName ?? "?" : target?.PlayerName ?? "?";

        builder.AddNoOp(T(player,"sz_menu.vote_kick",playerName));
        builder.Add(T(player,"sz_menu.vote_yes"), _ => AddVote("kick", player, true));
        builder.Add(T(player,"sz_menu.vote_no"), _ => AddVote("kick", player, false));

        if (SourceBans._vote.TryGetValue("kick", out var result))
            builder.AddNoOp(T(player,"sz_menu.vote_result",result.YES, result.NO));
    }

    private static void BuildVoteMapMenu(HLZMenuBuilder builder, CCSPlayerController player, string? mapname)
    {
        string displayMap = SourceBans._vote.TryGetValue("map", out var vote) ? vote.MapName ?? mapname ?? "?" : mapname ?? "?";

        builder.AddNoOp(T(player,"sz_menu.vote_map", displayMap));
        builder.Add(T(player,"sz_menu.vote_yes"), _ => AddVote("map", player, true));
        builder.Add(T(player,"sz_menu.vote_no"), _ => AddVote("map", player, false));

        if (SourceBans._vote.TryGetValue("map", out var result))
            builder.AddNoOp(T(player,"sz_menu.vote_result",result.YES, result.NO));
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
                    if ((userData.Ban & (BanType.Kick | BanType.Ban))>0 && userData.ExpiryBan > DateTime.UtcNow) continue;
                    name = Instance?.Trunc(target.PlayerName, 20);
                    label = $"#{target.Slot} - {name}";
                    //if (player != target)
                        builder.Add(label, _ => AdminCMD(player, target));
                    //else
                       // builder.AddNoOp(label);
                break;
                case 1:
                    if (userData.Ban == BanType.None) continue;
                    count++;
                    name = Instance?.Trunc(target.PlayerName, 20);
                    label = $"{target.Slot} - {name}";
                    builder.Add(label, _ => AdminCMD2(player, target));
                break;
                default: break;

            }
        }
        if (count == 0 && type == 1)
           builder.AddNoOp(T(player,"sz_menu.noban"));

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
                builder.Add(T(player,"sz_menu.kick_ban"), _ => AdminCMD1(player,target,"Ban"));
            builder.Add(T(player,"sz_menu.gag"), _ => AdminCMD1(player,target,"Gag"));
            builder.Add(T(player,"sz_menu.mute"), _ => AdminCMD1(player,target,"Mute"));
            builder.Add(T(player,"sz_menu.silence"), _ => AdminCMD1(player,target,"Silence"));
            if (adminFlags.Has(AdminFlags.Root | AdminFlags.Slay))
                builder.Add(T(player,"sz_menu.slay"), _ => AdminConfirm(player,target,"Slay"));
            if (target.TeamNum > 0)
                builder.Add(T(player,"sz_menu.team"), _ => AdminCMD1(player,target,"team"));
        } else {
            if (adminFlags.Has(AdminFlags.Root | AdminFlags.Kick | AdminFlags.Ban))
                builder.Add(T(player,"sz_menu.kick"), _ => AdminConfirm(player,target,"kick"));
        }
        builder.Open(player, Instance!._menuManager);

    }

    public static void AdminHistory(CCSPlayerController player)
    {
        var builder = new HLZMenuBuilder("Server");
        foreach (var kvp in SourceBans._userCache)
        {
            var steamId = kvp.Key;
            var data = kvp.Value;
            var name = Instance?.Trunc(data.PlayerName,10);
            builder.Add($"{data.Updated.ToLocalTime():dd HH:mm} - {name}", _ => AdminDetails(player, steamId));
        }
        builder.Open(player, Instance!._menuManager);
    }

    public static void AdminDetails(CCSPlayerController player, ulong SteamId)
    {
        var userData = SourceBans._userCache[SteamId];
        var name = Instance?.Trunc(userData.PlayerName,20);
        var steam2 = SourceBans.ToSteam2(SteamId);
        var status = userData.Connected ? "Online" : "Offline";
        var builder = new HLZMenuBuilder("Server Details").NoNumber()
                     .AddNoOp($"{name} ({status})")
                     .AddNoOp($"IP: {userData.IP}")
                     .AddNoOp($"Steam64: {SteamId}")
                     .AddNoOp($"Steam2: {steam2}");
        if (SourceBans._userCache[SteamId].IsAdmin)
        {
            var adminFlags = SourceBans._userCache[SteamId].Flags.ToFlags();
            if (adminFlags.Has(AdminFlags.Root))
            {
                builder.AddNoOp("SuperAdmin");
            } else if (adminFlags.Has(AdminFlags.Generic)) {
                var flags = new List<string>();
                flags.Add("Admin");
                if (adminFlags.Has(AdminFlags.Kick))
                   flags.Add("Kick");
                if (adminFlags.Has(AdminFlags.Ban))
                   flags.Add("Ban");
                if (adminFlags.Has(AdminFlags.Unban))
                   flags.Add("Unban");
                if (adminFlags.Has(AdminFlags.Slay))
                   flags.Add("Slay");
                if (adminFlags.Has(AdminFlags.Map))
                   flags.Add("Map");
                if (adminFlags.Has(AdminFlags.Rcon))
                   flags.Add("Rcon");
                string Flags = string.Join(", ", flags);
                builder.AddNoOp($"{Flags}");
           }
        }
        DateTime now = DateTime.UtcNow;
        if (userData.CountBan > 0)
        {
            var active = (userData.Ban & BanType.Ban) == 0 ? T(player,"sz_menu.expired") : SourceBans.FormatTimeLeft(player, userData.ExpiryBan-now);
            builder.AddNoOp($"Ban: {userData.CountBan} ({SourceBans.FormatTimeLeft(player, TimeSpan.FromSeconds(userData.DurationBan))})({active})");
        }
        if (userData.CountMute > 0)
        {
            var active = (userData.Ban & BanType.Mute) == 0 ? T(player,"sz_menu.expired") : SourceBans.FormatTimeLeft(player, userData.ExpiryMute-now);
            builder.AddNoOp($"Mute: {userData.CountMute} ({SourceBans.FormatTimeLeft(player, TimeSpan.FromSeconds(userData.DurationMute))})({active})");
        }
        if (userData.CountGag > 0)
        {
            var active = (userData.Ban & BanType.Gag) == 0 ? T(player,"sz_menu.expired") : SourceBans.FormatTimeLeft(player, userData.ExpiryGag-now);
            builder.AddNoOp($"Gag: {userData.CountGag} ({SourceBans.FormatTimeLeft(player, TimeSpan.FromSeconds(userData.DurationGag))})({active})");
        }
        if (userData.CountSlay > 0)
        {
            builder.AddNoOp($"Slay: {userData.CountSlay}");

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
        bool superAdmin = adminFlags.Has(AdminFlags.Root | AdminFlags.Rcon);

        if (cmd == "team")
        {
            if (target.TeamNum != 2)
                builder.Add(T(player,"sz_menu.team2"), _ => AdminConfirm(player,target,$"team 2"));
            if (target.TeamNum != 3)
                builder.Add(T(player,"sz_menu.team3"), _ => AdminConfirm(player,target,$"team 3"));
            if (target.TeamNum != 1)
                builder.Add(T(player,"sz_menu.team1"), _ => AdminConfirm(player,target,$"team 1"));

        } else {
            if (adminFlags.Has(AdminFlags.Root | AdminFlags.Kick) && cmd == "Ban")
                builder.Add("Kick", _ => AdminCMD3(player,target, "kick", 2, "kick"));
            if (adminFlags.Has(AdminFlags.Root | AdminFlags.Ban))
            {
                if (SourceBans.Durations[1] > 0 || superAdmin)
                    builder.Add(T(player,"sz_menu.ban_duration_1",cmd), _ => AdminCMD3(player,target, cmd == "Ban"? "ban" : "comm", SourceBans.Durations[1], cmd));
                if (SourceBans.Durations[1] > 0 || superAdmin)
                    builder.Add(T(player,"sz_menu.ban_duration_2",cmd), _ => AdminCMD3(player,target, cmd == "Ban"? "ban" : "comm", SourceBans.Durations[2], cmd));
                if (SourceBans.Durations[1] > 0 || superAdmin)
                    builder.Add(T(player,"sz_menu.ban_duration_3",cmd), _ => AdminCMD3(player,target, cmd == "Ban"? "ban" : "comm", SourceBans.Durations[3], cmd));
                if (SourceBans.Durations[1] > 0 || superAdmin)
                    builder.Add(T(player,"sz_menu.ban_duration_4",cmd), _ => AdminCMD3(player,target, cmd == "Ban"? "ban" : "comm", SourceBans.Durations[4], cmd));
                if (SourceBans.Durations[1] > 0 || superAdmin)
                    builder.Add(T(player,"sz_menu.ban_duration_5",cmd), _ => AdminCMD3(player,target, cmd == "Ban"? "ban" : "comm", SourceBans.Durations[5], cmd));
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
        bool superAdmin = adminFlags.Has(AdminFlags.Root | AdminFlags.Rcon);
        DateTime now = DateTime.UtcNow;

        if ((targetData.Ban & BanType.Kick)>0) {
            bool _temp = targetData.ExpiryBan < DateTime.MaxValue;
            var remaining = !_temp ? T(player,"sz_menu.permanently") : SourceBans.FormatTimeLeft(player, targetData.ExpiryBan-now);
            var label = T(player,"sz_menu.unkick", remaining);
            builder.AddNoOp(label);
        }
        if ((targetData.Ban & (BanType.Banip | BanType.Ban))>0) {
            bool _temp = targetData.ExpiryBan < DateTime.MaxValue;
            var remaining = !_temp ? T(player,"sz_menu.permanently") : SourceBans.FormatTimeLeft(player, targetData.ExpiryBan-now);
            var label = T(player,"sz_menu.unban", remaining);
            if ((adminFlags.Has(AdminFlags.Root | AdminFlags.Unban) && _temp) || superAdmin)
            {
                builder.Add(label, _ => AdminCMD3(player,target, "unban", 0, "unban"));
            } else {
                builder.AddNoOp(label);
            }
        }
        if ((targetData.Ban & BanType.Gag)>0) {
            bool _temp = targetData.ExpiryGag < DateTime.MaxValue;
            var remaining = targetData.ExpiryGag == DateTime.MaxValue ? T(player,"sz_menu.permanently") : SourceBans.FormatTimeLeft(player, targetData.ExpiryGag - now);
            var label = T(player,"sz_menu.ungag", remaining);
             if (adminFlags.Has(AdminFlags.Root | AdminFlags.Unban) || superAdmin)
            {
                builder.Add(label, _ => AdminCMD3(player,target, "unban", 0, "ungag"));
            } else {
                builder.AddNoOp(label);
            }
        }
        if ((targetData.Ban & BanType.Mute)>0) {
            bool _temp = targetData.ExpiryMute < DateTime.MaxValue;
            var remaining = !_temp ? T(player,"sz_menu.permanently") : SourceBans.FormatTimeLeft(player, targetData.ExpiryMute-now);
            var label = T(player,"sz_menu.unmute", remaining);
            if (adminFlags.Has(AdminFlags.Root | AdminFlags.Unban) || superAdmin)
            {
                builder.Add(label, _ => AdminCMD3(player,target, "unban", 0, "unmute"));
            } else {
                builder.AddNoOp(label);
            }
        }
        if ((targetData.Ban & BanType.Gag)>0 && (targetData.Ban & BanType.Mute)>0 && targetData.ExpiryGag == targetData.ExpiryMute) {
            bool _temp = targetData.ExpiryGag < DateTime.MaxValue;
            var remaining = targetData.ExpiryBan == DateTime.MaxValue ? T(player,"sz_menu.permanently") : SourceBans.FormatTimeLeft(player, targetData.ExpiryGag-now);
            var label = T(player,"sz_menu.unsilence", remaining);
            if (adminFlags.Has(AdminFlags.Root | AdminFlags.Unban) || superAdmin)
            {
                builder.Add(label, _ => AdminCMD3(player,target, "unban", 0, "unsilence"));
            } else {
                builder.AddNoOp(label);
            }
        }
 
        builder.Open(player, Instance!._menuManager);

    }

    public static void AdminCMD3(CCSPlayerController player,CCSPlayerController target, string type, int duration, string cmd)
    {
        if (!SourceBans._enabled || player == null || !player.IsValid)
            return;

        var name = Instance?.Trunc(target.PlayerName,10);
        var builder = new HLZMenuBuilder($"{name}");
        switch (type)
        {
            case "kick":
            case "ban":
                for (int i = 1; i < 6; i++)
                {
                    var reason = T(player,$"sz_menu.ban_reason_{i}");
                    if (!string.IsNullOrEmpty(reason))
                    {
                        builder.Add(reason, _ => AdminAction(player,cmd,target,reason, duration));
                    }
                }
            break;
            case "comm":
                for (int i = 1; i < 6; i++)
                {
                    var reason = T(player,$"sz_menu.comm_reason_{i}");
                    if (!string.IsNullOrEmpty(reason))
                    {
                        builder.Add(reason, _ => AdminAction(player,cmd,target,reason, duration));
                    }
                }
            break;
            case "unban":
                for (int i = 1; i < 6; i++)
                {
                    var reason = T(player,$"sz_menu.unban_reason_{i}");
                    if (!string.IsNullOrEmpty(reason))
                    {
                        builder.Add(reason, _ => AdminAction(player,cmd,target,reason, duration));
                    }
                }
            break;
            default:
            break;
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

        builder.Add(T(player,"sz_menu.confirm"), _ => AdminAction(player,parts[0],target,level,length));

        builder.Open(player, Instance!._menuManager);

    }

    public static void AdminAction(CCSPlayerController admin,string action, CCSPlayerController? target, string? args, int number = 0)
    {
        if (admin == null || !admin.IsValid) return;

        var cmd     = action.Trim().ToLowerInvariant();
        var reason = string.IsNullOrWhiteSpace(args) ? "Admin Action" : args.Trim();

        var message = "";
        var type    = BanType.None;
        var page    = 0;
        int min     = 1;
        SourceBans._userCache.TryGetValue(admin.SteamID, out var userData);
        var adminFlags  = userData.Flags.ToFlags();
        bool rootAdmin  = adminFlags.Has(AdminFlags.Root);
        bool superAdmin = adminFlags.Has(AdminFlags.Root | AdminFlags.Rcon);
        switch (cmd)
        {
            case "kick": {
                if (target == null || !target.IsValid) return;
                target.CommitSuicide(false, true);
                if (SourceBans._userCache.TryGetValue(target.SteamID, out var targetData))
                {
                    SourceBans.UpdateBanUser(target.SteamID, BanType.Kick, 120, false, userData.Aid);
                    _ = DiscordWebhooks.Send(Instance!.Config, cmd, admin, target.SteamID, reason, Instance?.Logger);
                    SourceBans.DelayedCommand($"kickid {target.UserId} \"Kicked {target.PlayerName} ({reason})\"",3.0f);
                }
                publicChat("sz_chat.admin_kicked",admin.PlayerName,target.PlayerName,reason);
            break; }
            case "votekick": {
                if (target == null || !target.IsValid) return;
                if (SourceBans._vote.ContainsKey("kick") || SourceBans._vote.ContainsKey("map"))
                {
                    privateChat(admin, "sz_chat.vote_inprogress");
                    return;
                }
                min = (int)Math.Ceiling(Utilities.GetPlayers().Count(p => p != null && p.IsValid && !p.IsBot) * 0.6);
                SourceBans._vote["kick"] = (DateTime.UtcNow, target, target.PlayerName, null, 0, 0, Math.Max(1,min));
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p?.IsValid == true && p?.IsBot == false)
                        VoteActive("kick", p, target, null);
                }
                SourceBans.StartVoteTimer();
            return; }
            case "ban":
            case "banip":
            case "gag":
            case "mute":
            case "silence": {
                if (target == null || !target.IsValid) return;
                type = cmd == "ban" ? BanType.Ban :
                       cmd == "banip" ? BanType.Banip :
                       cmd == "gag" ? BanType.Gag :
                       cmd == "mute" ? BanType.Mute : BanType.Silence;
                message = cmd switch
                {
                    "ban"         => "sz_chat.admin_banned",
                    "banip"       => "sz_chat.admin_banned",
                    "gag"         => "sz_chat.admin_gagged",
                    "mute"        => "sz_chat.admin_muted",
                    "silence"     => "sz_chat.admin_silenced",
                    _             => "SnipeZilla Error"
                };
                if (!SourceBans._userCache.TryGetValue(target.SteamID, out var targetData))
                {
                    privateChat(admin, "sz_chat.player_record",target.PlayerName);
                    return;
                }
                if (((cmd == "ban" && targetData.ExpiryBan == DateTime.MaxValue) ||
                    (cmd == "silence" && (targetData.ExpiryMute == DateTime.MaxValue || targetData.ExpiryGag == DateTime.MaxValue)) ||
                    (cmd == "mute" && targetData.ExpiryMute == DateTime.MaxValue) ||
                    (cmd == "gag" && targetData.ExpiryGag == DateTime.MaxValue)) && !superAdmin)
                {
                    privateChat(admin, "sz_chat.permission");
                    return;
                }

                if (!rootAdmin && ((cmd == "ban" && (targetData.Ban & BanType.Ban)!=0 && targetData.aBan != userData.Aid) ||
                    (cmd == "gag" && (targetData.Ban & BanType.Gag)!=0 && targetData.aGag != userData.Aid) ||
                    (cmd == "mute" && (targetData.Ban & BanType.Mute)!=0 && targetData.aMute != userData.Aid) ||
                    (cmd == "silence" && (targetData.Ban & BanType.Silence)!=0  && (targetData.aMute != userData.Aid || targetData.aGag != userData.Aid))))
                {
                    privateChat(admin, "sz_chat.admin_rights");
                    return;
                }
                if (cmd == "mute" || cmd == "silence")
                    target.VoiceFlags = VoiceFlags.Muted;
                if (cmd == "silence")
                {
                    _ = SourceBans.WriteBan(target, admin, BanType.Mute, number, reason);
                    _ = SourceBans.WriteBan(target, admin, BanType.Gag, number, reason);
                } else {
                    _ = SourceBans.WriteBan(target, admin, type, number, reason);
                }
                SourceBans.UpdateBanUser(target.SteamID, type, number > 0 ? number : int.MaxValue, false, userData.Aid);
                DateTime now = DateTime.UtcNow.AddMinutes(-1);
                var _expiry   = cmd == "ban" ? targetData.DurationBan : cmd == "banip" ? targetData.DurationBan : cmd == "mute" ? targetData.DurationMute : cmd == "gag" ? targetData.DurationGag : targetData.DurationGag;
                var remaining = _expiry == int.MaxValue ? T(admin,"sz_menu.permanently") : SourceBans.FormatTimeLeft(admin, TimeSpan.FromSeconds(_expiry));
                _ = DiscordWebhooks.Send(Instance!.Config, cmd, admin, target.SteamID, reason, Instance?.Logger);
                publicChat(message,admin.PlayerName,target.PlayerName,remaining,reason);
                privateChat(target,"sz_chat.sourcebans_website",Instance!.Config.SourceBans.Website);
                if (cmd == "ban" || cmd == "banip")
                {
                    target.CommitSuicide(false, true);
                    SourceBans.DelayedCommand($"kickid {target.UserId} \"Banned {target.PlayerName} ({reason})\"",3.0f);
                }
            break; }
            case "ungag":
            case "unmute":
            case "unsilence":
            case "unban": {
                page=1;
                if (target == null || !target.IsValid) return;
                type = cmd == "ungag" ? BanType.Gag :
                       cmd == "unmute" ? BanType.Mute :
                       cmd == "unsilence" ? BanType.Silence : BanType.Ban;
                if (!SourceBans._userCache.TryGetValue(target.SteamID, out var targetData))
                {
                    privateChat(admin, "sz_chat.player_record",target.PlayerName);
                    return;
                }
                if ((cmd == "unban" && (targetData.Ban & BanType.Ban)==0) ||
                    (cmd == "ungag" && (targetData.Ban & BanType.Gag)==0) ||
                    (cmd == "unmute" && (targetData.Ban & BanType.Mute)==0) ||
                    (cmd == "unsilence" && (targetData.Ban & BanType.Silence)==0))
                {
                    privateChat(admin, "sz_chat.player_noban",target.PlayerName);
                    return;
                }
                if (((cmd == "unban" && targetData.aBan != userData.Aid) ||
                    (cmd == "unsilence" && (targetData.aMute != userData.Aid || targetData.aGag != userData.Aid)) ||
                    (cmd == "unmute" && targetData.aMute != userData.Aid) ||
                    (cmd == "ungag" && targetData.aGag != userData.Aid)) && !rootAdmin)
                {
                    privateChat(admin, "sz_chat.admin_rights");
                    return;
                }
                if (((cmd == "unban" && targetData.ExpiryBan == DateTime.MaxValue) ||
                    (cmd == "unsilence" && (targetData.ExpiryMute == DateTime.MaxValue || targetData.ExpiryGag == DateTime.MaxValue)) ||
                    (cmd == "unmute" && targetData.ExpiryMute == DateTime.MaxValue) ||
                    (cmd == "ungag" && targetData.ExpiryGag == DateTime.MaxValue)) && !superAdmin)
                {
                    privateChat(admin, "sz_chat.permission");
                    return;
                }
                if (cmd == "unmute" || cmd == "unsilence")
                    target.VoiceFlags = VoiceFlags.Normal;
                if (cmd == "unsilence")
                {
                    _ = SourceBans.WriteUnBan(target, admin, BanType.Mute, reason);
                    _ = SourceBans.WriteUnBan(target, admin, BanType.Gag, reason);
                } else {
                    _ = SourceBans.WriteUnBan(target, admin, type, reason);
                }
                SourceBans.UpdateBanUser(target.SteamID, type, 0,true, userData.Aid);
                message = cmd switch
                {
                    "ungag"       => "sz_chat.admin_ungagged",
                    "unmute"      => "sz_chat.admin_unmuted",
                    "unsilence"   => "sz_chat.admin_unsilenced",
                    "unban"       => "sz_chat.admin_unbanned",
                    _             => "SnipeZilla Error"
                };
                _ = DiscordWebhooks.Send(Instance!.Config, cmd, admin, target.SteamID, reason, Instance?.Logger);
                publicChat(message,admin.PlayerName,target.PlayerName,reason);
            break; }
            case "map": 
            case "votemap": {
                page = 2;
                if (args == null) return;
                if (cmd == "votemap" && (SourceBans._vote.ContainsKey("map") || SourceBans._vote.ContainsKey("kick")))
                {
                    privateChat(admin,"sz_chat.vote_inprogress");
                    return;
                }
                var availableMaps = SourceBans.GetAvailableMaps(Instance!.Config, true);
                var match = availableMaps.FirstOrDefault(m =>
                    string.Equals(m.DisplayName, args, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(m.MapName, args, StringComparison.OrdinalIgnoreCase) ||
                    (m.WorkshopId != null && string.Equals(m.WorkshopId, args, StringComparison.OrdinalIgnoreCase)));
                if (match == null)
                {
                    privateChat(admin, "sz_chat.map_notfound",args);
                    return;
                }
                if (cmd=="votemap") {
                    min = (int)Math.Ceiling(Utilities.GetPlayers().Count(p => p != null && p.IsValid && !p.IsBot) * 0.6);
                    SourceBans._vote["map"] = (DateTime.UtcNow, null, null, args, 0, 0, Math.Max(1,min));
                    foreach (var p in Utilities.GetPlayers())
                    {
                        if (p?.IsValid == true && p?.IsBot == false)
                            VoteActive("map",p,null,args);
                    }
                    SourceBans._mVote++;
                    SourceBans.StartVoteTimer();
                    return;
                }
                privateChat(admin, "sz_chat.admin_change_map", admin.PlayerName, match.MapName);
                var command = match.IsSteamWorkshop ? $"host_workshop_map {match.WorkshopId}" : $"changelevel {match.MapName}";
                SourceBans.DelayedCommand(command,3.0f);
            break; }
            case "slay":
                if (target == null || !target.IsValid) return;
                target.CommitSuicide(false, true);
                SourceBans.UpdateBanUser(target.SteamID, BanType.Slay, 0, false, userData.Aid);
                publicChat("sz_chat.admin_slayed",admin.PlayerName,target.PlayerName);
            break;
            case "team": {
                if (target == null || !target.IsValid) return;
                CsTeam csTeam;
                var team = "";
                switch ((TeamNum)number)
                {
                    case TeamNum.Spectator:
                        csTeam = CsTeam.Spectator;
                        team = T(admin,"sz_menu.team1");
                        break;
                    case TeamNum.Terrorist:
                        csTeam = CsTeam.Terrorist;
                        team = T(admin,"sz_menu.team2");
                        break;
                    case TeamNum.CounterTerrorist:
                        csTeam = CsTeam.CounterTerrorist;
                        team = T(admin,"sz_menu.team3");
                        break;
                    default:
                        SendPrivateChat(admin, $"[HLstats\x07Z\x01] Invalid team ID {number}");
                        return;
                }
                target.ChangeTeam(csTeam);
                publicChat("sz_chat.admin_change_team",admin.PlayerName,target.PlayerName,team);
            break; }
            case "rtv": {
                var allMaps = SourceBans.GetAvailableMaps(Instance!.Config, false);
                string currentMap = Server.MapName;
                var candidates = allMaps
                    .Where(m => !string.Equals(m.MapName, currentMap, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (candidates.Count == 0) {
                    privateChat(admin, "sz_chat.no_maps");
                    return;
                }
                var rng = new Random();
                SourceBans._rtv = candidates.OrderBy(_ => rng.Next()).Take(5).ToList();
                min = Utilities.GetPlayers().Count(p => p != null && p.IsValid && !p.IsBot);
                SourceBans._vote["map"] = (DateTime.UtcNow, null, admin.PlayerName, null, 0, 0, Math.Max(1,min));
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p?.IsValid == true && p?.IsBot == false)
                        VoteActive("rtv",p,null,args);
                }
                SourceBans._mVote++;
                SourceBans.StartVoteTimer();
            return; }
            case "nominate": {
                page = 2;
                if (args == null) return;
                ulong steamId = admin.SteamID;

                var availableMaps = SourceBans.GetAvailableMaps(Instance!.Config, false);
                var match = availableMaps.FirstOrDefault(m =>
                    string.Equals(m.DisplayName, args, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(m.MapName, args, StringComparison.OrdinalIgnoreCase) ||
                    (m.WorkshopId != null && string.Equals(m.WorkshopId, args, StringComparison.OrdinalIgnoreCase)));

                if (match == null)
                {
                    privateChat(admin, "sz_chat.map_notfound",args);
                    return;
                }
                publicChat("sz_chat.nominate",admin.PlayerName,args);
                SourceBans._userNominate[steamId] = args.ToLowerInvariant();
            break; }
            default:
            break;
        }

        if (Instance!._menuManager._activeMenus.ContainsKey(admin.SteamID))
        {
            if (page == 0) { Instance!._menuManager.RewindToLabel(admin, "Admin Players"); AdminPlayer(admin, page); }
            else if (page == 1) { Instance!._menuManager.RewindToLabel(admin, "Admin Unban"); AdminPlayer(admin, page); }
            else if (page == 2) { Instance!._menuManager.DestroyMenu(admin); }
        }
        return;
    }

    // ------------------ Event Handler ------------------
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        SourceBans._canVote = SourceBans._mVote < 2;
        return HookResult.Continue;
    }

    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        SourceBans._canVote = false;
        return HookResult.Continue;
    }

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

    public HookResult OnBombAbortdefuse(EventBombAbortdefuse @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;

        _ = SendLog(player, "Defuse_Aborted", "triggered");
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

        if (player == null || !player.IsValid || player.IsBot)
            return HookResult.Continue;

        if (SourceBans._userCache.TryGetValue(player.SteamID, out var cached) && cached.Updated < DateTime.UtcNow.AddMinutes(-2))
           SourceBans._userCache.Remove(player.SteamID);

        _ = SourceBans.PlayerCheck(player);

        return HookResult.Continue;
    }

    private void OnClientAuthorized(int slot, SteamID steamId)
    {
        SourceBans.Validator(null, steamId.SteamId64, true);
    }

    public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot)
            return HookResult.Continue;

        bool isValid = false;

        if (SourceBans._userCache.TryGetValue(player.SteamID, out var cached))
            isValid = SourceBans.Validator(player);

        if (!isValid)
        {
            _ = SourceBans.isAdmin(player);

        }

        return HookResult.Continue;
    }

    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;

        if (player != null && player.IsValid) {
            _menuManager.DestroyMenu(player);
            if (SourceBans._userNominate.ContainsKey(player.SteamID))
                SourceBans._userNominate.Remove(player.SteamID);
        }

        if (player != null)
        {
           if (!SourceBans._enabled) { SourceBans._userCache.Remove(player.SteamID);}
           else {SourceBans.UpdateBanUser(player.SteamID, BanType.None, 0, true, 0, false);}
        }
        return HookResult.Continue;
    }

    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.IsValid)
            _menuManager.DestroyMenu(player);
        return HookResult.Continue;
    }

    public HookResult OnCsWinPanelMatch(EventCsWinPanelMatch @event, GameEventInfo info)
    {
        if (!SourceBans._enabled) return HookResult.Continue;

        SourceBans._mVote   = 0;
        SourceBans._canVote = false;
        SourceBans._vote.Clear();
        SourceBans._rtv.Clear();
        SourceBans.StopVoteTimer();

        var nominate = SourceBans._userNominate.GroupBy(kv => kv.Value)
                                               .OrderByDescending(g => g.Count())
                                               .Select(g => g.Key)
                                               .FirstOrDefault();
        SourceBans._userNominate.Clear();

        if (nominate != null)
        {

            var availableMaps = SourceBans.GetAvailableMaps(HLstatsZ.Instance!.Config, true);
            var match = availableMaps.FirstOrDefault(m =>
                string.Equals(m.DisplayName, nominate, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m.MapName, nominate, StringComparison.OrdinalIgnoreCase) ||
                (m.WorkshopId != null && string.Equals(m.WorkshopId, nominate, StringComparison.OrdinalIgnoreCase)));

            if (match != null)
            {
                publicChat("sz_chat.nominate_validation",match.MapName);
                var command = match.IsSteamWorkshop ? $"host_workshop_map {match.WorkshopId}" : $"changelevel {match.MapName}";
                SourceBans.DelayedCommand(command, 3.0f);
            }

        }

        return HookResult.Continue;
    }

    private void OnMapStart(string name)
    {
        if (!SourceBans._enabled) return;
        SourceBans._mVote   = 0;
        SourceBans._canVote = true;
        SourceBans._userNominate.Clear();
        SourceBans._vote.Clear();
        SourceBans._rtv.Clear();
        SourceBans.StopVoteTimer();
    }
}
