# cs2-gungame
# GunGame for Counter-Strike 2

GunGame is a gameplay plugin inspired by the SourceMode GunGame plugin. [Original Plugin Thread](https://forums.alliedmods.net/showthread.php?t=93977).

## Description

GunGame challenges players with various weapons, requiring kills with each to progress. Players start with one weapon and must eliminate opponents to advance through the weapon sequence and ultimately win the game.

## Commands and Cvars

*Note: CVars are still in development by CounterStrikeSharp.*

Commands available now:
- `gg_restart` - Restarts the whole game from the beginning.
- `gg_enable` - Turn on gungame and restart the game.
- `gg_disable` - Turn off gungame and restart the game.
- `gg_reset` - Reset all gungame stats.
- `gg_config <foldername>` - Request to start gungame with settings in a different folder.
- `gg_respawn <value>` - Switch between behaviour if the players respawns are managed by plugin or server.	0 - disabled, 1 - T only, 2 - CT only, 3 - Both teams, 4 - Deathmatch spawns.
- `!top` - Show the top winners on the server.
- `!rank` - Show your current place in stats.
- `!music` - Turn Off or On all plugin sounds for the player.
- `!lang ..` - Player can change language of plugin messages. 
> [!WARNING]  
> Only works with ISO codes e.g.: `!lang en` or `!lang lv` You need the corresponding localisation file with the same name (en.json and ru.json are included). If you add GeoLite2-Country.mmdb to cfg folder, plugin will detect the player language based on his IP address. 


## Requirements

- Counter-Strike 2
- Metamod:Source v1282+
- Counter Strike Share v.204+

## Installation

1. Install Metamod:Source and Counter Strike Sharp.
2. Copy DLLs to `csgo/addons/counterstrikesharp/plugins/GG2`.
3. Place config files in `csgo/cfg/gungame`.
4. Place GeoLite2-Country.mmdb if you have it to `csgo/cfg`

*Config Files:*

- `weapons.json` - Weapon settings (modification not recommended).
- `gungame.json` - Main settings with comments for guidance. Some of them marked as (it does not work now), keep that in mind.
- `gungame_weapons.json` - Customizable weapon order.
- *Additional system executable configs with in-file comments.*

## Translations

Available in English and Russian.

## Upgrade

Please read the release notes carefully for upgrade instructions.

## Development

A GunGame API has been developed. Plugin developers can subscribe to GunGame events or request player data from the GunGame plugin.
API dlls are located in csgo/addons/counterstrikesharp/shared/GunGameAPI folder. API description will be available in the next release. 

## TODO

Future enhancements include:

- Additional functionalities from the original plugin.
- I've already added at least one important part that was not in the original plugin: shooting and knife protection, which you can turn off if you don't like it.
I'm going to improve this plugin a lot in the near future. However, it will not be distributed for free. I'll describe it later. These improvements include
- leader and loser management (highlight leaders and losers)
- Team balance management
- Fair play settings to allow not only "professional" players to win,
- etc. (we have lots of ideas :)

## FAQ

**Q: Why doesn't the map change after a win?**
A: GunGame doesn't handle map changes; it triggers a command in `gungame.mapvote.cfg` for map voting.

**Q: What do I put for game_mode and game_type in CS2**
A: Use `+game_type 0 +game_mode 0`.

**Q: What if something isn't working?**
A: Feel free to ask on the Counter Strike Sharp Discord. Assistance will be provided, though fixes are not guaranteed.

**Q: Where can I find geo database for ip addresses - GeoLite2-Country.mmdb?**
A: You can get it from: https://dev.maxmind.com/geoip/geolite2-free-geolocation-data
Or from release

*This README is a work in progress and will be updated as the plugin develops.*

## Credits

Special thanks to altex for the original plugin, aproxje for the ideas from Language Manager Plugin, the Counter Strike Sharp Discord community, and Chat-GPT for assistance, I hope it will remember how polite I was.

