using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;

namespace RetakesPlugin.Utils;

public static class GameRulesHelper
{
    public static CCSGameRules? GetGameRulesOrNull()
    {
        var gameRulesEntities = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules");
        var gameRulesProxy = gameRulesEntities.FirstOrDefault();

        if (gameRulesProxy == null)
        {
            return null;
        }

        return gameRulesProxy.GameRules;
    }

    public static CCSGameRules GetGameRules()
    {
        var gameRules = GetGameRulesOrNull();

        if (gameRules == null)
        {
            throw new InvalidOperationException("Game rules are not available yet. The server may still be initializing.");
        }

        return gameRules;
    }

    // A TerminateRound(0.1f) is asynchronous - the engine ends the round a tick later. Every
    // caller below (PlayerJoinedTeam, RemovePlayerFromQueues, RestartGame, auto-plant) used to
    // fire it unconditionally, so a single "jointeam" ran TerminateRound -> TerminateRound ->
    // mp_restartgame within ~0.35s and the server tore the same round down three times. That is
    // the double "Round ended. Winner: Terrorist" with no round start between them in the crash
    // logs, and stacking those teardowns is what kills the server when players are connected.
    // Gate every termination on a single pending flag, cleared when the next round pre-starts.
    private const float RoundEndPendingTimeoutSeconds = 5.0f;
    private static float _roundEndRequestedAt = float.MinValue;

    public static bool IsRoundEndPending()
    {
        var elapsed = Server.CurrentTime - _roundEndRequestedAt;

        // Treat a stale request as not pending so a termination that never landed
        // (map change, gamerules swap) cannot soft-lock the round forever.
        return elapsed >= 0.0f && elapsed < RoundEndPendingTimeoutSeconds;
    }

    public static void ClearRoundEndPending()
    {
        _roundEndRequestedAt = float.MinValue;
    }

    public static void RestartGame()
    {
        var gameRules = GetGameRulesOrNull();
        if (gameRules == null)
        {
            Logger.LogWarning("GameRules", "Game rules not available, skipping game restart");
            return;
        }

        if (!gameRules.WarmupPeriod)
        {
            CheckRoundDone();
        }

        Server.ExecuteCommand("mp_restartgame 1");
    }

    // Runs the action at the start of the next frame instead of inline.
    //
    // Every caller of TerminateRound reaches it from inside an engine hook - a jointeam command
    // listener, an EventPlayerTeam handler, a disconnect handler - so the engine is part-way
    // through its own work on the player list when the plugin calls back into it. Tearing the
    // round down from there re-enters engine code that is still iterating the state the
    // termination mutates. One frame of delay is invisible in-game (TerminateRound already
    // schedules the end 0.1s out) and leaves the engine's own call stack alone.
    private static void NextFrame(Action action)
    {
        Server.NextFrame(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Logger.LogWarning("GameRules", $"Deferred game rules action failed: {ex.Message}");
            }
        });
    }

    public static void CheckRoundDone()
    {
        var gameRules = GetGameRulesOrNull();
        if (gameRules == null || gameRules.WarmupPeriod)
        {
            return;
        }

        var tHumanCount = PlayerHelper.GetPlayerCount(CsTeam.Terrorist);
        var ctHumanCount = PlayerHelper.GetPlayerCount(CsTeam.CounterTerrorist);

        if (tHumanCount == 0 || ctHumanCount == 0)
        {
            TerminateRound(RoundEndReason.TerroristsWin);
        }
    }

    public static void TerminateRound(RoundEndReason roundEndReason)
    {
        if (IsRoundEndPending())
        {
            Logger.LogDebug("GameRules", "Round end already pending, skipping termination");
            return;
        }

        // Claim the pending slot synchronously so a second call in this same frame is still
        // deduplicated, then do the native work a frame later.
        _roundEndRequestedAt = Server.CurrentTime;

        NextFrame(() =>
        {
            try
            {
                GetGameRules().TerminateRound(0.1f, roundEndReason);
            }
            catch
            {
                Logger.LogWarning("GameRules",
                    "Incorrect signature detected (Can't use TerminateRound), killing all alive players instead.");

                var alivePlayers = Utilities.GetPlayers()
                    .Where(PlayerHelper.IsValid)
                    .Where(player => player.PawnIsAlive)
                    .ToList();

                foreach (var player in alivePlayers)
                {
                    player.CommitSuicide(false, true);
                }
            }
        });
    }

    public static double GetDistanceBetweenVectors(Vector v1, Vector v2)
    {
        var dx = v1.X - v2.X;
        var dy = v1.Y - v2.Y;

        return Math.Sqrt(Math.Pow(dx, 2) + Math.Pow(dy, 2));
    }
}