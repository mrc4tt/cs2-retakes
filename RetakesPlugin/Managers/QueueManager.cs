using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

using RetakesPlugin.Utils;
using RetakesPlugin.Configs;

namespace RetakesPlugin.Managers;

public class QueueManager
{
    private readonly RetakesPlugin _plugin;
    private readonly int _maxRetakesPlayers;
    private readonly float _terroristRatio;
    private readonly List<QueuePriorityFlagConfig> _queuePriorityFlags;
    private readonly List<QueuePriorityFlagConfig> _queueImmunityFlags;
    private readonly bool _shouldForceEvenTeamsWhenPlayerCountIsMultipleOf10;
    private readonly bool _shouldPreventTeamChangesMidRound;

    // Players are tracked by UserId, never by CCSPlayerController.
    //
    // CounterStrikeSharp's CEntityInstance compares with Equals(EntityHandle) but hashes with
    // Handle.GetHashCode() - the raw pointer. A HashSet<CCSPlayerController> therefore buckets
    // entries by pointer, so once a controller is freed on disconnect its entry can no longer be
    // found from a freshly resolved wrapper: Remove() silently misses, the stale entry survives,
    // the active count stays inflated (wrong T/CT ratio, missed restart-when-empty), and the next
    // ChangeTeam / CommitSuicide / PrintToChat on that entry touches freed memory and takes the
    // server down. UserIds are plain ints with value semantics; every use re-resolves a live
    // controller, and a player who has gone simply resolves to null and is pruned.
    private readonly HashSet<int> _activePlayerIds = [];
    private readonly HashSet<int> _queuePlayerIds = [];
    private readonly HashSet<int> _roundTerroristIds = [];
    private readonly HashSet<int> _roundCounterTerroristIds = [];

    public QueueManager(RetakesPlugin plugin, int? retakesMaxPlayers, float? retakesTerroristRatio, List<QueuePriorityFlagConfig>? queuePriorityFlags, List<QueuePriorityFlagConfig>? queueImmunityFlags, bool? shouldForceEvenTeamsWhenPlayerCountIsMultipleOf10, bool? shouldPreventTeamChangesMidRound)
    {
        _plugin = plugin;
        _maxRetakesPlayers = retakesMaxPlayers ?? 9;
        _terroristRatio = retakesTerroristRatio ?? 0.45f;

        _queuePriorityFlags = queuePriorityFlags ?? new List<QueuePriorityFlagConfig>();
        _queueImmunityFlags = queueImmunityFlags ?? new List<QueuePriorityFlagConfig>();

        _shouldForceEvenTeamsWhenPlayerCountIsMultipleOf10 = shouldForceEvenTeamsWhenPlayerCountIsMultipleOf10 ?? true;
        _shouldPreventTeamChangesMidRound = shouldPreventTeamChangesMidRound ?? true;

        Logger.LogInfo("QueueManager", $"Queue manager initialized (Max: {_maxRetakesPlayers}, T Ratio: {_terroristRatio})");
    }

    #region Identity

    // Resolves each tracked UserId to a live controller, dropping the ones that no longer
    // resolve. Every read of the queues goes through here, so stale ids can never reach a
    // caller: the pruning is the point, not a side effect.
    private List<CCSPlayerController> ResolvePlayers(HashSet<int> userIds)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        List<CCSPlayerController> players = [];
        List<int>? staleUserIds = null;

        foreach (var userId in userIds)
        {
            var player = PlayerHelper.ResolveConnected(userId);

            if (player == null)
            {
                (staleUserIds ??= []).Add(userId);
                continue;
            }

            players.Add(player);
        }

        if (staleUserIds != null)
        {
            foreach (var staleUserId in staleUserIds)
            {
                Forget(staleUserId);
            }
        }

        return players;
    }

    private void Forget(int userId)
    {
        _activePlayerIds.Remove(userId);
        _queuePlayerIds.Remove(userId);
        _roundTerroristIds.Remove(userId);
        _roundCounterTerroristIds.Remove(userId);
    }

    public List<CCSPlayerController> ActivePlayers => ResolvePlayers(_activePlayerIds);
    public List<CCSPlayerController> QueuePlayers => ResolvePlayers(_queuePlayerIds);

    public int ActivePlayerCount => ResolvePlayers(_activePlayerIds).Count;
    public int QueuePlayerCount => ResolvePlayers(_queuePlayerIds).Count;

    public bool IsActive(CCSPlayerController? player) =>
        PlayerHelper.TryGetUserId(player, out var userId) && _activePlayerIds.Contains(userId);

    public bool IsQueued(CCSPlayerController? player) =>
        PlayerHelper.TryGetUserId(player, out var userId) && _queuePlayerIds.Contains(userId);

    // The order Update() pulls players out of the queue in: highest priority first, then slot.
    private List<CCSPlayerController> OrderedQueuePlayers()
    {
        return QueuePlayers
            .Select(player => new
            {
                Player = player,
                Priority = PlayerHelper.GetQueuePriority(player, _queuePriorityFlags)
            })
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Player.Slot)
            .Select(x => x.Player)
            .ToList();
    }

    // 1-based position in the queue, or null when the player is not queued.
    public (int Position, int Total)? GetQueuePosition(CCSPlayerController? player)
    {
        if (!PlayerHelper.TryGetUserId(player, out var userId) || !_queuePlayerIds.Contains(userId))
        {
            return null;
        }

        var ordered = OrderedQueuePlayers();
        var index = ordered.FindIndex(p => PlayerHelper.TryGetUserId(p, out var id) && id == userId);

        return index < 0 ? null : (index + 1, ordered.Count);
    }

    // Takes an active player out of the game without putting them in the queue (AFK handling).
    // They rejoin the queue the normal way, by picking a team.
    public bool MoveActiveToSpectator(CCSPlayerController player)
    {
        if (!PlayerHelper.TryGetUserId(player, out var userId) || !_activePlayerIds.Contains(userId))
        {
            return false;
        }

        Forget(userId);
        PlayerHelper.TryChangeTeam(player, CsTeam.Spectator);
        return true;
    }

    public void AddActivePlayer(CCSPlayerController? player)
    {
        if (PlayerHelper.TryGetUserId(player, out var userId))
        {
            _activePlayerIds.Add(userId);
        }
    }

    #endregion

    public int MaxRetakesPlayers => _maxRetakesPlayers;

    // Humans who want to play: active and queued. Queued players count too, otherwise bots in
    // the active slots could keep a MinimumPlayers hold from ever releasing.
    public int GetHumanPlayerCount()
    {
        return ActivePlayers.Count(IsHuman) + QueuePlayers.Count(IsHuman);
    }

    private static bool IsHuman(CCSPlayerController player) => !player.IsBot && !player.IsHLTV;

    public int GetTargetNumTerrorists()
    {
        var activePlayerCount = ActivePlayerCount;

        var shouldForceEvenTeams = _shouldForceEvenTeamsWhenPlayerCountIsMultipleOf10 && activePlayerCount % 10 == 0;
        var ratio = (shouldForceEvenTeams ? 0.5 : _terroristRatio) * activePlayerCount;
        var numTerrorists = (int)Math.Round(ratio);

        return numTerrorists > 0 ? numTerrorists : 1;
    }

    public int GetTargetNumCounterTerrorists()
    {
        return ActivePlayerCount - GetTargetNumTerrorists();
    }

    public HookResult PlayerJoinedTeam(CCSPlayerController player, CsTeam fromTeam, CsTeam toTeam)
    {
        Logger.LogDebug("QueueManager", $"[{player.PlayerName}] Team change: {fromTeam} -> {toTeam}");

        if (!PlayerHelper.TryGetUserId(player, out var userId))
        {
            return HookResult.Continue;
        }

        if (fromTeam == CsTeam.None && toTeam == CsTeam.Spectator)
        {
            return HookResult.Continue;
        }

        if (_activePlayerIds.Contains(userId))
        {
            Logger.LogDebug("QueueManager", $"[{player.PlayerName}] Player is active");

            if (toTeam == CsTeam.Spectator)
            {
                Logger.LogInfo("QueueManager", $"[{player.PlayerName}] Switched to spectator");
                RemovePlayerFromQueues(player);
                GameRulesHelper.CheckRoundDone();
                return HookResult.Continue;
            }

            var gameRules = GameRulesHelper.GetGameRulesOrNull();
            if (!_shouldPreventTeamChangesMidRound || (gameRules?.WarmupPeriod ?? false))
            {
                return HookResult.Continue;
            }

            if (
                _roundTerroristIds.Count > 0
                && _roundCounterTerroristIds.Count > 0
                && (
                    (toTeam == CsTeam.CounterTerrorist && !_roundCounterTerroristIds.Contains(userId))
                    || (toTeam == CsTeam.Terrorist && !_roundTerroristIds.Contains(userId))
                )
            )
            {
                Logger.LogInfo("QueueManager", $"[{player.PlayerName}] Prevented mid-round team change");
                _activePlayerIds.Remove(userId);
                _queuePlayerIds.Add(userId);

                if (player.PawnIsAlive)
                {
                    player.CommitSuicide(false, true);
                }

                PlayerHelper.TryChangeTeam(player, CsTeam.Spectator);
                return HookResult.Handled;
            }

            // The team change is blocked (Handled), so the team counts cannot have changed and
            // there is nothing for CheckRoundDone to react to. Joining must never end a round.
            return HookResult.Handled;
        }

        if (!_queuePlayerIds.Contains(userId))
        {
            // Players explicitly choosing to spectate should not be tracked as wanting to play.
            // Without this, jointeam 1 while already spectating adds the player to the queue,
            // and the next queue update yanks them into a team mid-restart.
            if (toTeam != CsTeam.Terrorist && toTeam != CsTeam.CounterTerrorist)
            {
                return HookResult.Continue;
            }

            var gameRules = GameRulesHelper.GetGameRulesOrNull();
            if ((gameRules?.WarmupPeriod ?? false) && ActivePlayerCount < _maxRetakesPlayers)
            {
                Logger.LogInfo("QueueManager", $"[{player.PlayerName}] Added to active players (warmup)");
                _activePlayerIds.Add(userId);
                return HookResult.Continue;
            }

            Logger.LogInfo("QueueManager", $"[{player.PlayerName}] Added to queue");
            player.PrintToChat(_plugin.Prefixed(player, "retakes.queue.joined"));
            _queuePlayerIds.Add(userId);
        }

        // Never end the running round because someone joined: a queued player waits for the next
        // round, even when one side is empty (1v0). That round still ends on its own through the
        // normal win conditions. Ending it here also used to feed fake T wins into the win streak.
        return HookResult.Handled;
    }

    private void PruneDisconnectedPlayers()
    {
        var activeCountBefore = _activePlayerIds.Count;
        var queueCountBefore = _queuePlayerIds.Count;

        // Resolving is what prunes; the lists themselves are not needed here.
        ResolvePlayers(_activePlayerIds);
        ResolvePlayers(_queuePlayerIds);

        var removedActive = activeCountBefore - _activePlayerIds.Count;
        if (removedActive > 0)
        {
            Logger.LogDebug("QueueManager", $"Removing {removedActive} disconnected active players");
        }

        var removedQueue = queueCountBefore - _queuePlayerIds.Count;
        if (removedQueue > 0)
        {
            Logger.LogDebug("QueueManager", $"Removing {removedQueue} disconnected queue players");
        }
    }

    private void HandleQueuePriority()
    {
        if (ActivePlayerCount != _maxRetakesPlayers)
        {
            return;
        }

        var queuePlayersWithPriority = QueuePlayers
            .Select(player => new
            {
                Player = player,
                Priority = PlayerHelper.GetQueuePriority(player, _queuePriorityFlags)
            })
            .Where(x => x.Priority > int.MinValue)
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Player.Slot)
            .ToList();

        if (queuePlayersWithPriority.Count <= 0)
        {
            return;
        }

        foreach (var queuePlayerData in queuePlayersWithPriority)
        {
            var queuePlayer = queuePlayerData.Player;
            var queuePlayerPriority = queuePlayerData.Priority;

            // The swap below changes the queues, so re-resolve on every iteration rather than
            // reusing controllers captured before the previous swap.
            if (!PlayerHelper.TryGetUserId(queuePlayer, out var queueUserId) || !_queuePlayerIds.Contains(queueUserId))
            {
                continue;
            }

            var replaceablePlayers = ActivePlayers
                .Select(player => new
                {
                    Player = player,
                    Priority = PlayerHelper.GetQueuePriority(player, _queuePriorityFlags),
                    ImmunityPriority = PlayerHelper.GetQueueImmunityPriority(player, _queueImmunityFlags)
                })
                .Where(x =>
                    x.Priority < queuePlayerPriority &&
                    x.ImmunityPriority < queuePlayerPriority)
                .OrderBy(x => x.Priority)
                .ThenByDescending(x => x.Player.Slot)
                .ToList();

            if (replaceablePlayers.Count == 0)
            {
                Logger.LogDebug("QueueManager", $"No replaceable players found for {queuePlayer.PlayerName} (priority: {queuePlayerPriority})");
                continue;
            }

            var replaceablePlayerData = replaceablePlayers.First();
            var replaceablePlayer = replaceablePlayerData.Player;

            if (!PlayerHelper.TryGetUserId(replaceablePlayer, out var replaceableUserId))
            {
                continue;
            }

            var queuePlayerDisplayName = PlayerHelper.GetQueuePriorityDisplayName(queuePlayer, _queuePriorityFlags);

            // Move the queued player into the game first; if that fails there is no free
            // seat and nobody should be displaced to spectator.
            if (!PlayerHelper.TryChangeTeam(queuePlayer, CsTeam.CounterTerrorist))
            {
                Logger.LogWarning("QueueManager", $"Could not move {queuePlayer.PlayerName} to CT, skipping queue priority swap");
                continue;
            }

            PlayerHelper.TryChangeTeam(replaceablePlayer, CsTeam.Spectator);
            _activePlayerIds.Remove(replaceableUserId);
            _queuePlayerIds.Add(replaceableUserId);
            replaceablePlayer.PrintToChat(_plugin.Prefixed(replaceablePlayer, "retakes.queue.replaced_by_vip", queuePlayer.PlayerName, queuePlayerDisplayName));

            _activePlayerIds.Add(queueUserId);
            _queuePlayerIds.Remove(queueUserId);
            queuePlayer.PrintToChat(_plugin.Prefixed(queuePlayer, "retakes.queue.vip_took_place", replaceablePlayer.PlayerName, queuePlayerDisplayName));

            Logger.LogInfo("QueueManager", $"{queuePlayer.PlayerName} ({queuePlayerDisplayName}, priority: {queuePlayerPriority}) replaced {replaceablePlayer.PlayerName} (priority: {replaceablePlayerData.Priority})");
        }
    }

    public void Update()
    {
        PruneDisconnectedPlayers();

        var activePlayerCount = ActivePlayerCount;
        Logger.LogDebug("QueueManager", $"Update: Max={_maxRetakesPlayers}, Active={activePlayerCount}, Queue={_queuePlayerIds.Count}");

        var playersToAdd = _maxRetakesPlayers - activePlayerCount;
        if (playersToAdd > 0 && _queuePlayerIds.Count > 0)
        {
            var playersToAddList = OrderedQueuePlayers()
                .Take(playersToAdd)
                .ToList();

            foreach (var player in playersToAddList)
            {
                if (!PlayerHelper.TryGetUserId(player, out var userId))
                {
                    continue;
                }

                _queuePlayerIds.Remove(userId);

                // Only mark the player active once the team change actually succeeded;
                // a failed ChangeTeam (disconnected pawn, mid-restart) must not leave a
                // spectator counted as an active player.
                if (!PlayerHelper.TryChangeTeam(player, CsTeam.CounterTerrorist))
                {
                    Logger.LogWarning("QueueManager", $"Could not move {player.PlayerName} from queue to active, skipping");
                    continue;
                }

                _activePlayerIds.Add(userId);
                Logger.LogInfo("QueueManager", $"Moved {player.PlayerName} from queue to active");
            }
        }

        HandleQueuePriority();

        activePlayerCount = ActivePlayerCount;
        if (activePlayerCount == _maxRetakesPlayers && _queuePlayerIds.Count > 0)
        {
            foreach (var player in QueuePlayers)
            {
                player.PrintToChat(_plugin.Prefixed(player, "retakes.queue.waiting", activePlayerCount));
            }
        }
    }

    public void RemovePlayerFromQueues(CCSPlayerController? player)
    {
        if (!PlayerHelper.TryGetUserId(player, out var userId))
        {
            // The controller is already gone, so there is no id to look up. Drop whatever no
            // longer resolves instead of leaving the entry behind.
            PruneDisconnectedPlayers();
            GameRulesHelper.CheckRoundDone();
            return;
        }

        var wasActive = _activePlayerIds.Contains(userId);
        Forget(userId);

        Logger.LogDebug("QueueManager", $"Removed {player!.PlayerName} from all queues");

        // Tell whoever is first in line that they are in next round.
        if (wasActive && ActivePlayerCount < _maxRetakesPlayers)
        {
            var next = OrderedQueuePlayers().FirstOrDefault();
            next?.PrintToChat(_plugin.Prefixed(next, "retakes.queue.next"));
        }

        GameRulesHelper.CheckRoundDone();
    }

    public void DebugQueues(bool isBefore)
    {
        var prefix = isBefore ? "BEFORE" : "AFTER";

        var activePlayers = ActivePlayers;
        if (activePlayers.Count == 0)
        {
            Logger.LogDebug("QueueManager", $"ActivePlayers ({prefix}): Empty");
        }
        else
        {
            Logger.LogDebug("QueueManager", $"ActivePlayers ({prefix}): {string.Join(", ", activePlayers.Select(p => p.PlayerName))}");
        }

        var queuePlayers = QueuePlayers;
        if (queuePlayers.Count == 0)
        {
            Logger.LogDebug("QueueManager", $"QueuePlayers ({prefix}): Empty");
        }
        else
        {
            Logger.LogDebug("QueueManager", $"QueuePlayers ({prefix}): {string.Join(", ", queuePlayers.Select(p => p.PlayerName))}");
        }
    }

    public void ClearRoundTeams()
    {
        _roundTerroristIds.Clear();
        _roundCounterTerroristIds.Clear();
        Logger.LogDebug("QueueManager", "Round teams cleared");
    }

    public void SetRoundTeams()
    {
        if (!_shouldPreventTeamChangesMidRound)
        {
            return;
        }

        _roundTerroristIds.Clear();
        _roundCounterTerroristIds.Clear();

        foreach (var player in ActivePlayers)
        {
            if (!PlayerHelper.TryGetUserId(player, out var userId))
            {
                continue;
            }

            switch (player.Team)
            {
                case CsTeam.Terrorist:
                    _roundTerroristIds.Add(userId);
                    break;

                case CsTeam.CounterTerrorist:
                    _roundCounterTerroristIds.Add(userId);
                    break;
            }
        }

        Logger.LogDebug("QueueManager", $"Round teams set: {_roundTerroristIds.Count} T, {_roundCounterTerroristIds.Count} CT");
    }
}
