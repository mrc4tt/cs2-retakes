using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;

using RetakesPlugin.Utils;
using RetakesPluginShared.Enums;

namespace RetakesPlugin.Services;

public class BombService
{
    public static void PlantTickingBomb(CCSPlayerController? player, Bombsite bombsite)
    {
        if (player == null || !player.IsValid)
        {
            throw new Exception("Player controller is not valid");
        }

        var playerPawn = player.Pawn.Value;

        if (playerPawn == null || !playerPawn.IsValid)
        {
            throw new Exception("Player pawn is not valid");
        }

        if (playerPawn.AbsOrigin == null)
        {
            throw new Exception("Player pawn abs origin is null");
        }

        if (playerPawn.AbsRotation == null)
        {
            throw new Exception("Player pawn abs rotation is null");
        }

        var plantedC4 = Utilities.CreateEntityByName<CPlantedC4>("planted_c4");

        if (plantedC4 == null)
        {
            throw new Exception("c4 is null");
        }

        if (plantedC4.AbsOrigin == null)
        {
            throw new Exception("c4.AbsOrigin is null");
        }

        plantedC4.AbsOrigin.X = playerPawn.AbsOrigin.X;
        plantedC4.AbsOrigin.Y = playerPawn.AbsOrigin.Y;
        plantedC4.AbsOrigin.Z = playerPawn.AbsOrigin.Z;
        plantedC4.HasExploded = false;

        plantedC4.BombSite = (int)bombsite;
        plantedC4.BombTicking = true;
        plantedC4.CannotBeDefused = false;

        plantedC4.DispatchSpawn();

        // Re-apply the position AFTER spawn. Setting AbsOrigin before DispatchSpawn
        // doesn't reliably stick in CS2, which can leave the bomb (and its beep
        // sound source) at world origin, making the ticking play non-positional /
        // full volume for everyone. Teleporting after spawn pins the sound in 3D
        // so it attenuates with distance like a normal plant.
        plantedC4.Teleport(
            new Vector(playerPawn.AbsOrigin.X, playerPawn.AbsOrigin.Y, playerPawn.AbsOrigin.Z),
            playerPawn.AbsRotation,
            new Vector(0, 0, 0)
        );

        // Pin the bomb timer explicitly. A manually-spawned planted_c4 does NOT
        // reliably inherit m_flTimerLength / m_flC4Blow from mp_c4timer during
        // DispatchSpawn, so the client's 10-second fast-beep is timed off a stale
        // C4Blow and starts far too early (mid-round instead of at ~10s left).
        // Setting TimerLength + C4Blow = now + mp_c4timer fixes the beep cadence.
        // mp_c4timer is an Int32 convar — reading it as float throws
        // "ConVar is a Int32 but you are trying to get a System.Single value",
        // which previously aborted the plant before the timer/round-extend ran.
        var c4Timer = (float)(ConVar.Find("mp_c4timer")?.GetPrimitiveValue<int>() ?? 40);
        if (c4Timer is <= 0.0f or > 120.0f)
        {
            // Guard a bad/mistyped convar read so the beep never falls back to a
            // broken timer; 40 is the retakes default set in ServerHelper.
            c4Timer = 40.0f;
        }
        plantedC4.TimerLength = c4Timer;
        plantedC4.C4Blow = Server.CurrentTime + c4Timer;

        // TimerLength/C4Blow are raw schema refs (Schema.GetRef) — writing them updates
        // server memory but does NOT flag the field for replication, so clients keep the
        // stale value networked at DispatchSpawn and beep off the wrong time. Mark both
        // dirty so the corrected timer actually reaches clients.
        Utilities.SetStateChanged(plantedC4, "CPlantedC4", "m_flTimerLength");
        Utilities.SetStateChanged(plantedC4, "CPlantedC4", "m_flC4Blow");

        var gameRules = GameRulesHelper.GetGameRules();
        gameRules.BombPlanted = true;
        gameRules.BombDefused = false;

        // A faked plant (CreateEntityByName + bomb_planted event) never runs the engine's
        // real plant handler, so the round still ends at RoundStartTime + RoundTime. With the
        // retakes-short mp_roundtime, that fires a time-based CT win before the C4 detonates.
        // Extend the deadline past the C4 timer so the round is decided by detonation (T win)
        // or defuse, like a genuine plant. Deadline = now + c4Timer + small margin.
        var elapsed = Server.CurrentTime - gameRules.RoundStartTime;
        gameRules.RoundTime = (int)Math.Ceiling(elapsed + c4Timer) + 5;

        // Defer the event to the next server frame so the planted_c4 entity
        // is fully registered by the engine, avoiding the "Player or C4 missing" error.
        Server.NextFrame(() =>
        {
            SendBombPlantedEvent(player, bombsite);
        });

        Logger.LogDebug("Bomb", $"Bomb planted at {bombsite} by {player.PlayerName}");
    }

    private static void SendBombPlantedEvent(CCSPlayerController bombCarrier, Bombsite bombsite)
    {
        if (!bombCarrier.IsValid || bombCarrier.PlayerPawn.Value == null)
        {
            return;
        }

        var eventPtr = NativeAPI.CreateEvent("bomb_planted", true);
        NativeAPI.SetEventPlayerController(eventPtr, "userid", bombCarrier.Handle);
        NativeAPI.SetEventInt(eventPtr, "site", (int)bombsite);

        NativeAPI.FireEvent(eventPtr, false);
    }
}
