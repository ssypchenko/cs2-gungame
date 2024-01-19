# cs2-gungame
# GunGame for Counter-Strike 2

GunGame is a gameplay plugin inspired by the SourceMode GunGame plugin. [Original Plugin Thread](https://forums.alliedmods.net/showthread.php?t=93977).

## Description

GunGame challenges players with various weapons, requiring kills with each to progress. Players start with one weapon and must eliminate opponents to advance through the weapon sequence and ultimately win the game.

## Commands and Cvars

*Note: Commands and CVars are still in development. At least the following will be available:*

- `gungame_enabled` - Displays the GunGame status.
- `gg_version` - Shows the plugin version.
- `gg_status` - Displays the current game state.
- `gg_restart` - Restarts the whole game from the beginning.
- `gg_enable` - Turn on gungame and restart the game.
- `gg_disable` - Turn off gungame and restart the game.
- `gg_rebuild` - Rebuilds the top10 rank from the player data information.

Commands available now:
- `gg_reset` - Reset all gungame stats.
- `!top` - Show the top winners on the server.
- `!rank` - Show your current place in stats.
- `!music` - Turn Off or On all plugin sounds for the player.


## Requirements

- Linux server (unfortunately, plugin does not work on Windows server yet)
- Counter-Strike 2
- Metamod:Source v1275+
- Counter Strike Share v.142+

## Installation

1. Install Metamod:Source and Counter Strike Sharp.
2. Copy DLLs to `csgo/addons/counterstrikesharp/plugins/GG2`.
3. Place config files in `csgo/cfg/gungame`.

*Config Files:*

- `weapons.json` - Weapon settings (modification not recommended).
- `gungame.json` - Main settings with comments for guidance. Some of them marked as (it does not work now), keep that in mind.
- `gungame_weapons.json` - Customizable weapon order.
- *Additional system executable configs with in-file comments.*

## Credits

Special thanks to altex for the original plugin, the Counter Strike Sharp Discord community, and Chat-GPT for assistance, I hope it will remember how polite I was.

## Translations

Available in English and Russian.

## TODO

Future enhancements include:

- Additional functionalities from the original plugin.
- I've already added at least one important part that was not in the original plugin: shooting and knife protection, which you can turn off if you don't like it.
I'm going to improve this plugin a lot in the near future. However, it will not be distributed for free. I'll describe it later. These improvements include
- different folders with different settings for "special events" with the ability to run them from within the plugin or even schedule them.
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

*This README is a work in progress and will be updated as the plugin develops.*
