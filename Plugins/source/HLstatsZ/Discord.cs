using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Logging;
using System.Xml.Linq;
using System.Collections.Concurrent;

namespace HLstatsZ;

public static class DiscordWebhooks
{
    private static HttpClient Http => HLstatsZ.Http;

    private static readonly JsonSerializerOptions _json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task Send(HLstatsZMainConfig cfg, string cmd, CCSPlayerController? admin, ulong steam64, string reason, ILogger? logger = null )
    {
        if (cfg is null || !cfg.Enable_Discord || string.IsNullOrWhiteSpace(cfg.Discord.WebhookUrl)) return;
        if (!SourceBans._userCache.TryGetValue(steam64, out var userData)) return;

        var title = cmd switch
        {
            "kick"      => cfg.Discord.TitleKick,
            "ban"       => cfg.Discord.TitleBan,
            "banip"     => cfg.Discord.TitleBan,
            "gag"       => cfg.Discord.TitleGag,
            "mute"      => cfg.Discord.TitleMute,
            "silence"   => cfg.Discord.TitleSilence,
            "unban"     => cfg.Discord.TitleUnban,
            "ungag"     => cfg.Discord.TitleUngag,
            "unmute"    => cfg.Discord.TitleUnmute,
            "unsilence" => cfg.Discord.TitleUnsilence,
            _           => ""
        };
        int duration = cmd switch
        {
            "kick"      => 120,
            "ban"       => userData.DurationBan,
            "banip"     => userData.DurationBan,
            "gag"       => userData.DurationGag,
            "mute"      => userData.DurationMute,
            "silence"   => userData.DurationMute,
            "unban"     => userData.DurationBan,
            "ungag"     => userData.DurationGag,
            "unmute"    => userData.DurationMute,
            "unsilence" => userData.DurationMute,
            _           => 0
        };


        if (string.IsNullOrWhiteSpace(title)) return;

        var adminName = admin == null ? "Console" : Clean(admin.PlayerName);
        var adminValue = admin == null ? adminName : $"[{adminName}](https://steamcommunity.com/profiles/{admin.SteamID})";
        var targetName = Clean(userData.PlayerName);
        var steam2 = SourceBans.ToSteam2(steam64);
        reason = Clean(reason);
        string serverName = SourceBans.hostName ?? "Counter-Strike 2";

        bool _temp = duration < int.MaxValue;
        var durationText = !_temp ? "Permanently" : SourceBans.FormatTimeLeft(null, TimeSpan.FromSeconds(duration));
        var color = cmd.StartsWith("un") ? HexToInt(cfg.Discord.ColorUnban) :
        (_temp ? HexToInt(cfg.Discord.ColorWithExpiration) : HexToInt(cfg.Discord.ColorPermanent));

        var type = cmd.Contains("ban") ? "banlist" : "commslist";
        var urlBase = cfg.SourceBans.Website.TrimEnd('/');
        var steamEscaped = Uri.EscapeDataString(steam2);
        var description = cfg.Discord.Description;
        if (!string.IsNullOrWhiteSpace(urlBase))
        {
            var urlWeb = $"{urlBase}/index.php?p={type}&searchText={steamEscaped}";
            var host = new Uri(urlBase).Host;
            description = $"**{cfg.Discord.Description}** [{host}]({urlWeb})";
            if (admin == null)
                adminValue = $"[{adminName}]({urlBase})";
        }

        var fields = new List<object>
        {
            new { name = "SteamID", value = $"`{steam2}`", inline = true },
            new { name = "Reason",   value = $"`{reason}`", inline = true },
            new { name = "Duration", value = $"`{durationText}`", inline = true },
            new { name = "Server Address", value = $"`{SourceBans.serverAddr}`", inline = true }
        };

        if (cfg.Discord.ShowAdmin)
        {
            fields.Add(new { name = "Admin", value = $"{adminValue}", inline = true });
        }
       fields.Add(new { name = "Server",   value = $"`{serverName}`", inline = false });


        var payload = new
        {
            username = cfg.Discord.Username,
            embeds = new[] {
                new {
                    title = $"{title} - {targetName}",
                    url = $"https://steamcommunity.com/profiles/{steam64}",
                    description = description,
                    color = color,
                    thumbnail = new {
                        url = await GetAvatar(steam64,logger)
                    },
                    fields = fields,
                    timestamp = DateTime.UtcNow.ToString("o"),
                }
            }
        };

        var json = JsonSerializer.Serialize(payload, _json);
        await PostWebhook(cfg.Discord.WebhookUrl, json, logger);
    }

    public static async Task<string?> GetAvatar(ulong steam64, ILogger? logger = null)
    {
        string? avatarUrl = null;
        try
        {
            string xmlUrl = $"https://steamcommunity.com/profiles/{steam64}?xml=1";
            using var resp = await Http.GetAsync(xmlUrl);

            if (resp.IsSuccessStatusCode)
            {
                var xml = await resp.Content.ReadAsStringAsync();
                var doc = XDocument.Parse(xml);

                avatarUrl = doc.Root?.Element("avatarFull")?.Value?.Trim();

                if (string.IsNullOrWhiteSpace(avatarUrl))
                    avatarUrl = "https://avatars.fastly.steamstatic.com/fef49e7fa7e1997310d705b2a6158ff8dc1cdfeb_full.jpg";
            }
        } catch (Exception ex) {
            logger?.LogDebug(ex, "Failed to fetch Steam avatar via XML");
        }

        return avatarUrl;
    }

    private static async Task PostWebhook(string url, string json, ILogger? logger = null)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var resp = await Http.PostAsync(url, content);

            if (!resp.IsSuccessStatusCode)
            {
                var msg = await resp.Content.ReadAsStringAsync();
                logger?.LogWarning($"[Discord] Webhook failed ({resp.StatusCode}): {msg}");
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[Discord] Webhook error");
        }
    }

    private static int HexToInt(string hex, int fallback = 0xFF0000)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return fallback;
    
        hex = hex.Trim().Replace("#", "");
    
        if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int value))
            return value;
    
        return fallback;
    }

    private static string Clean(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (!char.IsControl(c) || c == '\n' || c == '\r' || c == '\t')
                sb.Append(c);
        }
        return sb.ToString();
    }


}
