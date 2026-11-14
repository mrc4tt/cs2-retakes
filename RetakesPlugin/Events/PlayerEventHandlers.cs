using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

using RetakesPlugin.Managers;
using RetakesPlugin.Utils;

namespace RetakesPlugin.Events;

public class PlayerEventHandlers
{
    private readonly RetakesPlugin _plugin;
    private readonly GameManager _gameManager;
    private readonly HashSet<int> _hasMutedVoices;

    public PlayerEventHandlers(RetakesPlugin plugin, GameManager gameManager, HashSet<int> hasMutedVoices)
    {
        _plugin = plugin;
        _gameManager = gameManager;
        _hasMutedVoices = hasMutedVoices;
    }

    public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;

        if (!PlayerHelper.IsValid(player))
        {
            return HookResult.Continue;
        }

        player.ForceTeamTime = 3600.0f;

        if(_plugin.Config.Queue.ShouldAutoJoinSpectators && PlayerHelper.TryGetUserId(player, out var connectedUserId))
        {
            _plugin.AddTimer(1.0f, () =>
            {
                // Resolve from the UserId rather than capturing the controller: a player who
                // drops inside this 1 second window frees the controller, and the captured
                // wrapper would be a pointer into freed memory that IsValid cannot detect.
                var player = PlayerHelper.ResolveConnected(connectedUserId);
                if (player == null)
                {
                    return;
                }

                // A fast joiner can pick a team inside this 1 second window. Firing the
                // auto-spectate anyway yanked them straight back out of the round they had
                // just been placed in, RemoveSpectators then emptied ActivePlayers, and the
                // resulting restart storm is what the crash logs show. Only auto-spectate a
                // player who has not chosen a side and is not already tracked by the queue.
                if (player.Team == CsTeam.Terrorist || player.Team == CsTeam.CounterTerrorist)
                {
                    Logger.LogDebug("Player", $"[{player.PlayerName}] Already picked a team, skipping auto-spectate");
                    return;
                }

                if (_gameManager.QueueManager.IsActive(player) ||
                    _gameManager.QueueManager.IsQueued(player))
                {
                    Logger.LogDebug("Player", $"[{player.PlayerName}] Already queued, skipping auto-spectate");
                    return;
                }

                PlayerHelper.TryChangeTeam(player, CsTeam.Spectator);
                player.ExecuteClientCommand("teammenu");
            });
        }

        Logger.LogInfo("Player", $"{player.PlayerName} connected");
        return HookResult.Continue;
    }

    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;

        if (!PlayerHelper.IsValid(player) || !PlayerHelper.IsConnected(player))
        {
            return HookResult.Continue;
        }

        Logger.LogDebug("Player", $"[{player.PlayerName}] Spawned");

        if (!_gameManager.QueueManager.IsActive(player))
        {
            if (player.PlayerPawn.Value != null && player.PlayerPawn.IsValid && player.PlayerPawn.Value.IsValid)
            {
                player.PlayerPawn.Value.CommitSuicide(false, true);
            }

            if (!player.IsBot)
            {
                PlayerHelper.TryChangeTeam(player, CsTeam.Spectator);
            }
            else if (!player.IsHLTV)
            {
                _gameManager.QueueManager.AddActivePlayer(player);
                Logger.LogInfo("Player", $"Force added bot {player.PlayerName} to active players");
            }

            return HookResult.Continue;
        }

        return HookResult.Continue;
    }

    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        _gameManager.AfkManager.OnPlayerDeath(@event.Userid);

        var attacker = @event.Attacker;
        var assister = @event.Assister;

        if (PlayerHelper.IsValid(attacker))
        {
            _gameManager.AddKill(attacker);
        }

        if (PlayerHelper.IsValid(assister))
        {
            _gameManager.AddAssist(assister);
        }

        return HookResult.Continue;
    }

    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;

        if (player == null)
        {
            return HookResult.Continue;
        }

        var playerName = PlayerHelper.IsValid(player) ? player.PlayerName : "Unknown";

        if (PlayerHelper.TryGetUserId(player, out var userId))
        {
            _hasMutedVoices.Remove(userId);
            _gameManager.AfkManager.Forget(userId);
        }

        _gameManager.QueueManager.RemovePlayerFromQueues(player);

        Logger.LogInfo("Player", $"{playerName} disconnected");
        return HookResult.Continue;
    }

    public HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        @event.Silent = true;
        return _gameManager.RemoveSpectators(@event, _hasMutedVoices);
    }
}