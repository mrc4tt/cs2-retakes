# Changelog

## 3.0.7
- Chat, center and console messages are shown in each player's own language (set with `!lang`), instead of the server language for everyone.
- Command replies that were English-only (usage hints, errors, spawn editor and map config messages) are now translated in all languages.
- Uzbek bombsite announcement no longer shows a literal `[BLUE]` tag.
- Added Albanian (`sq`) translation.
- Joining a team never ends the running round; queued players wait for the next round, also in 1v0.
- On an empty server the first join no longer restarts the game instantly: players joining within 5 seconds are collected and start together with a single restart.
- T win streak ("won N rounds in a row" / scramble) only counts real retakes: rounds with an empty side, rounds ended by the plugin and game restarts no longer count.

## 3.0.6
- Spawns edited or added in game (spawn editor) are saved to `addons/counterstrikesharp/configs/plugins/RetakesPlugin/map_config/<map>.json` and survive plugin updates.
- A map config in `configs/plugins/RetakesPlugin/map_config/` overrides the shipped one in `plugins/RetakesPlugin/map_config/`; maps you never edited keep getting updated spawns with each release.
- A map without spawns no longer writes an empty config file, so spawns shipped later for that map are picked up.
- `!mapconfigs` lists configs from both places and marks server-edited ones with "(custom)".

## 3.0.5
- First fshost release, based on B3none/cs2-retakes 3.0.4.
- Built against CounterStrikeSharp 1.0.407 (mrc4tt/CounterStrikeSharp).
- Works when loaded mid-map with `css_plugins load`: sets up the current map and queues the players already on a team, instead of waiting for the next map.
- Auto-plant: reliable bomb detonation (mp_c4timer read as an integer).
- Queue, spawn and round handling fixes; spawns are safe across hot reloads.
- Fallback weapon allocation is off by default (use a standalone allocator such as RetakesAllocator).
