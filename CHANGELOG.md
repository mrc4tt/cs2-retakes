# Changelog

## 3.0.5
- First fshost release, based on B3none/cs2-retakes 3.0.4.
- Built against CounterStrikeSharp 1.0.407 (mrc4tt/CounterStrikeSharp).
- Works when loaded mid-map with `css_plugins load`: sets up the current map and queues the players already on a team, instead of waiting for the next map.
- Auto-plant: reliable bomb detonation (mp_c4timer read as an integer).
- Queue, spawn and round handling fixes; spawns are safe across hot reloads.
- Fallback weapon allocation is off by default (use a standalone allocator such as RetakesAllocator).
