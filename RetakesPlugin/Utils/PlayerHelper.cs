using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using System.Diagnostics.CodeAnalysis;

using RetakesPlugin.Configs;

namespace RetakesPlugin.Utils;

public static class PlayerHelper
{
    public static bool IsValid([NotNullWhen(true)] CCSPlayerController? player)
    {
        return player != null && player.IsValid;
    }

    public static bool IsConnected(CCSPlayerController player)
    {
        return player.Connected == PlayerConnectedState.Connected;
    }

    // The only safe long-lived identity for a player.
    //
    // CounterStrikeSharp's CEntityInstance compares with Equals(EntityHandle) but hashes with
    // Handle.GetHashCode() - the raw pointer - so a CCSPlayerController stored in a HashSet or
    // captured in a timer outlives the controller it points at and eventually dereferences
    // freed memory. A UserId is a plain int: store that, and resolve a live controller from it
    // with Utilities.GetPlayerFromUserid at the moment of use.
    public static bool TryGetUserId(CCSPlayerController? player, out int userId)
    {
        userId = 0;

        if (!IsValid(player) || !player.UserId.HasValue)
        {
            return false;
        }

        userId = player.UserId.Value;
        return true;
    }

    // Returns the controller for this UserId only while it is still valid and connected;
    // null once the player has gone, so callers prune instead of dereferencing.
    public static CCSPlayerController? ResolveConnected(int userId)
    {
        var player = Utilities.GetPlayerFromUserid(userId);

        return IsValid(player) && IsConnected(player) ? player : null;
    }

    public static bool TryChangeTeam(CCSPlayerController? player, CsTeam team)
    {
        if (!IsValid(player) || !IsConnected(player))
        {
            return false;
        }

        try
        {
            player.ChangeTeam(team);

            // ChangeTeam kills the player. During warmup nothing brings them back: retakes.cfg
            // disables mp_respawn_on_death_ct/t and the warmup timer can be paused, so there is
            // no round restart either. Without this they sit in death cam until they switch team
            // by hand. Every path that seats a player routes through here, so the guard lives in
            // this one place rather than in each caller. (Upstream PR #293.)
            if (team is CsTeam.Terrorist or CsTeam.CounterTerrorist
                && (GameRulesHelper.GetGameRulesOrNull()?.WarmupPeriod ?? false)
                && TryGetUserId(player, out var userId))
            {
                Server.NextFrame(() =>
                {
                    // Resolved from the UserId rather than captured: a player can drop inside
                    // the frame, and the captured controller would be a freed pointer.
                    var respawningPlayer = ResolveConnected(userId);

                    if (respawningPlayer != null && !respawningPlayer.PawnIsAlive)
                    {
                        respawningPlayer.Respawn();
                    }
                });
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning("PlayerHelper", $"Failed to change team for {player.PlayerName}: {ex.Message}");
            return false;
        }
    }

    public static bool TrySwitchTeam(CCSPlayerController? player, CsTeam team)
    {
        if (!IsValid(player) || !IsConnected(player))
        {
            return false;
        }

        try
        {
            player.SwitchTeam(team);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning("PlayerHelper", $"Failed to switch team for {player.PlayerName}: {ex.Message}");
            return false;
        }
    }

    public static bool HasAlivePawn(CCSPlayerController? player, bool shouldBeAlive = true)
    {
        if (!IsValid(player))
        {
            return false;
        }

        var playerPawn = player!.Pawn.Value;

        if (playerPawn == null || !playerPawn.IsValid)
        {
            return false;
        }

        // Subpatterns of a property pattern combine with AND, so `is { AbsOrigin: null,
        // AbsRotation: null }` only rejected a pawn missing BOTH vectors - a pawn with a null
        // AbsOrigin passed this check and went on to be read and teleported. Either one missing
        // means the pawn is not usable.
        if (playerPawn.AbsOrigin == null || playerPawn.AbsRotation == null)
        {
            return false;
        }

        if (shouldBeAlive && !(playerPawn.Health > 0))
        {
            return false;
        }

        return true;
    }

    public static int GetPlayerCount(CsTeam? csTeam = null)
    {
        var players = 0;

        foreach (var player in Utilities.GetPlayers().Where(IsValid))
        {
            if (csTeam == null || player.Team == csTeam)
            {
                players++;
            }
        }

        return players;
    }

    public static void GiveAndSwitchToBomb(CCSPlayerController player)
    {
        player.GiveNamedItem(CsItem.Bomb);
        NativeAPI.IssueClientCommand((int)player.UserId!, "slot5");
    }

    public static void RemoveHelmetAndHeavyArmour(CCSPlayerController player)
    {
        if (player.PlayerPawn.Value == null || player.PlayerPawn.Value.ItemServices == null)
        {
            return;
        }

        var itemServices = new CCSPlayer_ItemServices(player.PlayerPawn.Value.ItemServices.Handle);
        itemServices.HasHelmet = false;
    }

    public static bool HasQueuePriority(CCSPlayerController player, string[] queuePriorityFlags)
    {
        return PlayerHasAnyQueuePermission(player, queuePriorityFlags);
    }

    public static bool HasQueueImmunity(CCSPlayerController player, string[] queueImmunityFlags)
    {
        return PlayerHasAnyQueuePermission(player, queueImmunityFlags);
    }

    public static int GetQueuePriority(CCSPlayerController player, List<QueuePriorityFlagConfig> priorityFlags)
    {
        int maxPriority = int.MinValue;
        bool hasAnyPriority = false;

        foreach (var priorityFlag in priorityFlags)
        {
            if (string.IsNullOrWhiteSpace(priorityFlag.Flag))
            {
                continue;
            }

            if (AdminManager.PlayerHasPermissions(player, priorityFlag.Flag))
            {
                hasAnyPriority = true;
                if (priorityFlag.Priority > maxPriority)
                {
                    maxPriority = priorityFlag.Priority;
                }
            }
        }

        return hasAnyPriority ? maxPriority : int.MinValue;
    }

    public static int GetQueueImmunityPriority(CCSPlayerController player, List<QueuePriorityFlagConfig> immunityFlags)
    {
        int maxPriority = int.MinValue;
        bool hasAnyImmunity = false;

        foreach (var immunityFlag in immunityFlags)
        {
            if (string.IsNullOrWhiteSpace(immunityFlag.Flag))
            {
                continue;
            }

            if (AdminManager.PlayerHasPermissions(player, immunityFlag.Flag))
            {
                hasAnyImmunity = true;
                if (immunityFlag.Priority > maxPriority)
                {
                    maxPriority = immunityFlag.Priority;
                }
            }
        }

        return hasAnyImmunity ? maxPriority : int.MinValue;
    }

    public static string GetQueuePriorityDisplayName(CCSPlayerController player, List<QueuePriorityFlagConfig> priorityFlags)
    {
        int maxPriority = int.MinValue;
        string? displayName = null;

        foreach (var priorityFlag in priorityFlags)
        {
            if (string.IsNullOrWhiteSpace(priorityFlag.Flag))
            {
                continue;
            }

            if (AdminManager.PlayerHasPermissions(player, priorityFlag.Flag))
            {
                if (priorityFlag.Priority > maxPriority)
                {
                    maxPriority = priorityFlag.Priority;
                    displayName = string.IsNullOrWhiteSpace(priorityFlag.DisplayName) ? "VIP" : priorityFlag.DisplayName;
                }
            }
        }

        return displayName ?? "VIP";
    }

    public static bool HasAdminPermission(CCSPlayerController? player, params string[] permissionFlags)
    {
        if (player == null)
        {
            return true;
        }

        return PlayerHasAnyQueuePermission(player, permissionFlags);
    }

    private static bool PlayerHasAnyQueuePermission(CCSPlayerController player, IEnumerable<string> permissionFlags)
    {
        foreach (var permissionFlag in permissionFlags)
        {
            if (string.IsNullOrWhiteSpace(permissionFlag))
            {
                continue;
            }

            if (AdminManager.PlayerHasPermissions(player, permissionFlag))
            {
                return true;
            }
        }

        return false;
    }

    public static List<T> Shuffle<T>(IEnumerable<T> list, Random random)
    {
        var shuffledList = new List<T>(list);

        var n = shuffledList.Count;
        while (n > 1)
        {
            n--;
            var k = random.Next(n + 1);
            T value = shuffledList[k];
            shuffledList[k] = shuffledList[n];
            shuffledList[n] = value;
        }

        return shuffledList;
    }

    public static string GetCommandPermission(BaseConfigs config, string commandName, string category, string defaultPermission = "@css/root")
    {
        if (config?.Commands == null)
        {
            return defaultPermission;
        }

        Dictionary<string, string>? commandDict = category.ToLower() switch
        {
            "admin" => config.Commands.Admin,
            "mapconfig" => config.Commands.MapConfig,
            "spawneditor" => config.Commands.SpawnEditor,
            _ => null
        };

        if (commandDict == null)
        {
            return defaultPermission;
        }

        return commandDict.TryGetValue(commandName, out var permission) && !string.IsNullOrWhiteSpace(permission) ? permission : defaultPermission;
    }
}