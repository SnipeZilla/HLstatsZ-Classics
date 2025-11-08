# HLstatsZ Classics
A lightweight, dependency-free plugin for CS2 servers using CounterStrikeSharp.
Designed for a smooth transition from SourceMod, preserving your existing HLstats and SourceBans database. Admins, bans, and stats remain intact — no migration needed.
HLstats and SourceBans integration are fully supported — but <b>not required</b>. You can disable either system without affecting core functionality or admin workflows.

✅ No external dependencies<br>
✅ Compatible with existing MySQL schema<br>
✅ Reload-safe and performance-optimized<br>
✅ Native command support with privilege flags


## Common
```
 menu
 hlx_menu
```

## HLstats
```
rank
top10
next
```
## SourceBans
```
say
psay
hsay
kick
ban
banip
unban
silence
unsilenced
mute
unmute
gag
ungag
votekick
slay
team
players, camera
players -d, camera -d (only duplicate ips)
refresh
```
## MapChooser
```
nominate
votemap
map
rtv
```
## Flags
| Flag | Description                     |
|------|---------------------------------|
| b    | Generic admin (basic abilities) |
| c    | Kick players                    |
| d    | Ban players                     |
| e    | Unban players                   |
| f    | Slay players                    |
| g    | Change map                      |
| m    | RCON access(Use for perma ban)  |
| z    | Root admin (full access)        |


