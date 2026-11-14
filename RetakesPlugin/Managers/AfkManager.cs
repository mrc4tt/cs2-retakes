using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

using RetakesPlugin.Utils;

namespace RetakesPlugin.Managers;

// Moves active players who stay AFK for a number of rounds to spectator, so they stop holding a
// slot that someone in the queue could use.
//
// A player counts as AFK for a round when neither their position nor their view angles changed
// between freeze end and the moment the round was over for them (their death, or the round end
// if they survived). Rounds that were over too quickly to judge are skipped and change nothing,
// so a 1v0 round or an early death never counts against anyone.
public class AfkManager
{
    private const float MinRoundSecondsToJudge = 10.0f;
    private const float MoveDistanceThreshold = 32.0f;
    private const float AngleThreshold = 5.0f;

    private readonly RetakesPlugin _plugin;
    private readonly int _roundsToSpectator;

    private readonly record struct Snapshot(float X, float Y, float Z, float Pitch, float Yaw);

    private readonly Dictionary<int, Snapshot> _snapshots = new();
    private readonly Dictionary<int, int> _afkRounds = new();
    private readonly HashSet<int> _flagged = [];
    private float _freezeEndTime = float.MinValue;

    public AfkManager(RetakesPlugin plugin, int? roundsToSpectator)
    {
        _plugin = plugin;
        _roundsToSpectator = Math.Max(0, roundsToSpectator ?? 3);
    }

    private bool IsEnabled => _roundsToSpectator > 0;

    public void OnFreezeEnd(IEnumerable<CCSPlayerController> activePlayers)
    {
        _snapshots.Clear();
        _freezeEndTime = Server.CurrentTime;

        if (!IsEnabled)
        {
            return;
        }

        foreach (var player in activePlayers)
        {
            if (player.IsBot || !PlayerHelper.TryGetUserId(player, out var userId))
            {
                continue;
            }

            var snapshot = TakeSnapshot(player);
            if (snapshot != null)
            {
                _snapshots[userId] = snapshot.Value;
            }
        }
    }

    public void OnPlayerDeath(CCSPlayerController? victim)
    {
        if (!PlayerHelper.TryGetUserId(victim, out var userId) || !_snapshots.ContainsKey(userId))
        {
            return;
        }

        Judge(victim!, userId);
    }

    // Judges everyone still alive at round end. wasRealRound is false for rounds that should not
    // count at all (warmup, an empty side, a round ended by the plugin).
    public void OnRoundEnd(bool wasRealRound)
    {
        if (!IsEnabled || !wasRealRound)
        {
            _snapshots.Clear();
            return;
        }

        foreach (var userId in _snapshots.Keys.ToList())
        {
            var player = PlayerHelper.ResolveConnected(userId);
            if (player == null)
            {
                _snapshots.Remove(userId);
                continue;
            }

            Judge(player, userId);
        }
    }

    // Runs at round pre-start, before the queue update, so the freed slots are filled this round.
    public void MoveFlaggedToSpectator(QueueManager queueManager)
    {
        if (_flagged.Count == 0)
        {
            return;
        }

        foreach (var userId in _flagged)
        {
            _afkRounds.Remove(userId);

            var player = PlayerHelper.ResolveConnected(userId);
            if (player == null || !queueManager.IsActive(player))
            {
                continue;
            }

            if (queueManager.MoveActiveToSpectator(player))
            {
                player.PrintToChat(_plugin.Prefixed(player, "retakes.afk.moved"));
                Logger.LogInfo("AfkManager", $"{player.PlayerName} moved to spectator after {_roundsToSpectator} AFK rounds");
            }
        }

        _flagged.Clear();
    }

    public void Forget(int userId)
    {
        _snapshots.Remove(userId);
        _afkRounds.Remove(userId);
        _flagged.Remove(userId);
    }

    private void Judge(CCSPlayerController player, int userId)
    {
        if (!_snapshots.Remove(userId, out var before))
        {
            return;
        }

        // Over too quickly to tell an AFK player from one who simply had no time to move.
        if (Server.CurrentTime - _freezeEndTime < MinRoundSecondsToJudge)
        {
            return;
        }

        var after = TakeSnapshot(player);
        if (after == null)
        {
            return;
        }

        if (HasMoved(before, after.Value))
        {
            _afkRounds.Remove(userId);
            return;
        }

        var afkRounds = _afkRounds.GetValueOrDefault(userId) + 1;
        _afkRounds[userId] = afkRounds;
        Logger.LogDebug("AfkManager", $"{player.PlayerName} AFK for {afkRounds}/{_roundsToSpectator} rounds");

        if (afkRounds >= _roundsToSpectator)
        {
            _flagged.Add(userId);
        }
        else if (afkRounds == _roundsToSpectator - 1)
        {
            player.PrintToChat(_plugin.Prefixed(player, "retakes.afk.warning"));
        }
    }

    private static Snapshot? TakeSnapshot(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
        {
            return null;
        }

        var origin = pawn.AbsOrigin;
        if (origin == null)
        {
            return null;
        }

        var angles = pawn.EyeAngles;
        return new Snapshot(origin.X, origin.Y, origin.Z, angles.X, angles.Y);
    }

    private static bool HasMoved(Snapshot before, Snapshot after)
    {
        var dx = after.X - before.X;
        var dy = after.Y - before.Y;
        var dz = after.Z - before.Z;
        if (dx * dx + dy * dy + dz * dz > MoveDistanceThreshold * MoveDistanceThreshold)
        {
            return true;
        }

        return AngleDelta(before.Pitch, after.Pitch) > AngleThreshold
               || AngleDelta(before.Yaw, after.Yaw) > AngleThreshold;
    }

    private static float AngleDelta(float a, float b)
    {
        var delta = Math.Abs(a - b) % 360.0f;
        return delta > 180.0f ? 360.0f - delta : delta;
    }
}
