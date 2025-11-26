# HLstatsZ Classics
A lightweight, dependency-free plugin for CS2 servers using CounterStrikeSharp.<br>
Designed for a smooth transition from SourceMod, preserving your existing HLstats and SourceBans database. Admins, bans, and stats remain intact — no migration needed.<br>
HLstats and SourceBans integration are fully supported — but <b>not required</b>.<br>
You can disable either system without affecting core functionality or admin workflows.

✅ No external dependencies<br>
✅ Compatible with existing MySQL schema<br>
✅ Reload-safe and performance-optimized<br>
✅ Native command support with privilege flags<br>
✅ Discord webhook notification


## 🎮 Player Chat Commands
| Command               | HLstats | MapChooser | SourceBans | Description                   |
| ---------------------|-------- |------------|------------|--------------------------------|
| **!menu / !hlx_menu** | ✅     | ✅        |            |Open the main menu              |
| **!rank**            | ✅      |           |            | Show your personal rank        |
| **!top10**           | ✅      |           |            | Show Top 10 players            |
| **!next**            | ✅      |           |            | Show progress to next rank     |
| **!rtv**             |         | ✅        |            | Rock-the-vote                  |
| **!nominate**        |         | ✅        |            | Nominate a map for rtv         |
| **!votemap**         |         | ✅        |            | Start a map vote               |
| **!nextmap/!next**   |         | ✅        |            | Force the next nominate map    |
| **@**                |         |           | ✅         |User → Message all admins       |

## 🛡️ Admin Commands Chat & Client console (SourceBans)
| Command         | Description                        |
| --------------- | ---------------------------------- |
| **!@s**          | Admin → Spectators chat            |
| **!@ct**         | Admin → Counter-Terrorists         |
| **!@t**          | Admin → Terrorists                 |
| **!@<username>** | Private message to specific player |
| **!say**         | Admin message to global chat       |
| **!psay**        | Private admin message              |
| **!hsay**        | Admin center-screen message        |
| **!kick**        | Kick a player                      |
| **!slay**        | Kill a player                      |
| **!ban**         | Ban a player by SteamID            |
| **!banip**       | Ban a player by IP                 |
| **!unban**       | Remove a ban                       |
| **!gag**         | Disable text chat                  |
| **!mute**        | Disable voice chat                 |
| **!silence**     | Disable both voice & text          |
| **!ungag**       | Remove text gag                    |
| **!unmute**      | Remove voice mute                  |
| **!unsilence**   | Remove silence                     |
| **!team**        | Move a player to a team            |
| **!camera**      | Show duplicate IP                  |
| **!players**     | Show current player list           |
| **!map**         | Change map immediately             |
| **!refresh**     | Refresh admin permissions cache    |

## 🛡️ Console RCON Commands (SourceBans)
| Command         | Description                        |
| --------------- | ---------------------------------- |
| **kick**        | Kick a player                      |
| **slay**        | Kill a player                      |
| **ban**         | Ban a player by SteamID            |
| **banip**       | Ban a player by IP                 |
| **unban**       | Remove a ban                       |
| **gag**         | Disable text chat                  |
| **mute**        | Disable voice chat                 |
| **silence**     | Disable both voice & chat          |
| **ungag**       | Remove chat gag                    |
| **unmute**      | Remove voice mute                  |
| **unsilence**   | Remove silence                     |
| **team**        | Move a player to a team            |
| **camera**      | Show duplicate IP                  |
| **players**     | Show current player list           |
| **refresh**     | Refresh admin permissions cache    |

## Flags
| Flag | Description                     |
|------|---------------------------------|
| b    | Generic admin (basic abilities) |
| c    | Kick players                    |
| d    | Ban players                     |
| e    | Unban players                   |
| f    | Slay players                    |
| g    | Change map                      |
| i    | Configs (!refresh)              |
| o    | Banip                           |
| p    | Ban permanently                 |
| q    | Unban anyone                    |
| z    | Root admin (full access)        |

<img width="251" height="154" alt="image" src="https://github.com/user-attachments/assets/cfff1a70-e269-4cc3-b856-b9b2157c7944" />
<img width="250" height="152" alt="image" src="https://github.com/user-attachments/assets/ad7c6025-613f-4c04-80e3-7729298e257e" />
<img width="252" height="148" alt="image" src="https://github.com/user-attachments/assets/0e598253-f811-40cd-85d2-7c38d3239ccb" />

## HLstatsZ.json
```
{
  "Enable_HLstats": false,
  "Enable_Sourcebans": true,
  "Log_Address": "64.74.97.164",
  "Log_Port": 27500,
  "BroadcastAll": 0,
  "ServerAddr": "",
  "Version": 2,

  "SourceBans": {
    "Host": "64.74.97.164",
    "Port": 3306,
    "Database": "sourcebans",
    "Prefix": "sb",
    "User": "",
    "Password": "",
    "Website": "https://bans.example.com",
    "VoteKick": "public",
    "Chat_Ban_Duration_Max": 10080,
    "Menu_Ban1_Duration": 15,
    "Menu_Ban2_Duration": 60,
    "Menu_Ban3_Duration": 1440,
    "Menu_Ban4_Duration": 10080,
    "Menu_Ban5_Duration": 0,
  },

  "Maps": {
    "VoteMap": "public",
    "Nominate": true,
    "MapCycle": {
      "Admin": {
        "Maps": ["workshop/3240327667/awp_lego_3"]
      },
      "Public": {
        "Maps": ["de_inferno",
                 "de_mirage",
                 "de_nuke",
                 "de_overpass",
                 "de_ancient",
                 "workshop/3552466076/mocha"]
      }
    }
  },

  "Avertissements": [
  { "Message": "Welcome To Snipe{red}Zilla{default}", "PrintType": "html", "EveryMinutes": 12 },
  { "Message": "Visit Snipe{red}Zilla{default}.com", "PrintType": "say", "EveryMinutes": 15 }
  ],

  "Discord": {
    "Username": "HLstatsZ",
    "WebhookUrl": "https://discord.com/api/webhooks/123456789/........",
	"LogsWebhookUrl": "https://discord.com/api/webhooks/123456789/........",
    "ColorPermanent": "#FF0000",
    "ColorWithExpiration": "#FF9900",
    "ColorUnban": "#00FF00",
    "ShowAdmin": true
  }
}
```


