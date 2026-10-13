# Changelog

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
