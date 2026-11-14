# Changelog

## 3.0.9
- New `Queue.ShouldAutoJoinGame` (default false): connecting players are put straight into the game, or the queue when it is full, without the team menu (ported from upstream 3.1.0).
- The team menu's auto-select button now joins the queue like picking T or CT; before, it was ignored and the player was left out (ported from upstream 3.1.1).

## 3.0.8
- New `retakes_enabled` convar: `0` turns retakes off on the fly (clears the queues and restores the server's convars), `1` turns it back on and restarts with the players already on a team (ported from upstream 3.1.0).
- New `cfg/cs2-retakes/retakes_unload.cfg`, executed on `retakes_enabled 0` and on `css_plugins unload`, restores the convars `retakes.cfg` changes. It is created on first map start so it can be adjusted up front; its `bot_quota` is 0 so leaving retakes adds no bots.

## 3.0.7
- Chat, center and console messages are shown in each player's own language (set with `!lang`), instead of the server language for everyone.
- Command replies that were English-only (usage hints, errors, spawn editor and map config messages) are now translated in all languages.
- Uzbek bombsite announcement no longer shows a literal `[BLUE]` tag.
- Added Albanian (`sq`) translation.
- Joining a team never ends the running round; queued players wait for the next round, also in 1v0.
- On an empty server the first join no longer restarts the game instantly: players joining within 5 seconds are collected and start together with a single restart.
- T win streak ("won N rounds in a row" / scramble) only counts real retakes: rounds with an empty side, rounds ended by the plugin and game restarts no longer count.
- AFK players are moved to spectator after `AfkRoundsToSpectator` rounds (default 3, 0 = off) without moving or aiming, with a warning one round before; the queue fills their slot.
- New `!queue` command shows your position in the waiting queue; the first in line is told when a slot opens.
- Removed the hardcoded upstream contributor steam IDs that were granted `@css/vip` (queue priority) on every server.
- Round timer no longer shows the "10 seconds remaining" warning right after every round starts: default `mp_roundtime_defuse` is 1.25, and existing `retakes.cfg` files with the old 0.25 default are migrated automatically (ported from upstream 3.1.0).
- New `Game.MinimumPlayers` (default 0 = off): the server waits in a paused warmup until enough players have joined, then starts the game (ported from upstream 3.1.0).

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
