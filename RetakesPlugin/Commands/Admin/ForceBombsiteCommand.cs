using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;

using RetakesPlugin.Utils;
using RetakesPlugin.Events;
using RetakesPluginShared.Enums;

namespace RetakesPlugin.Commands.Admin;

public class ForceBombsiteCommand
{
    private readonly RetakesPlugin _plugin;
    private readonly RoundEventHandlers _roundEventHandlers;

    public ForceBombsiteCommand(RetakesPlugin plugin, RoundEventHandlers roundEventHandlers)
    {
        _plugin = plugin;
        _roundEventHandlers = roundEventHandlers;
    }

    public void OnCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player != null && !PlayerHelper.IsValid(player))
        {
            return;
        }

        var requiredPermission = PlayerHelper.GetCommandPermission(_plugin.Config, "css_forcebombsite", "Admin");
        if (!AdminManager.PlayerHasPermissions(player, requiredPermission))
        {
            commandInfo.ReplyToCommand(_plugin.Prefixed(player, "retakes.no_permissions"));
            return;
        }

        if (commandInfo.ArgCount < 2)
        {
            commandInfo.ReplyToCommand(_plugin.Prefixed(player, "retakes.forcebombsite.usage"));
            return;
        }

        var bombsite = commandInfo.GetArg(1).ToUpper();
        if (bombsite != "A" && bombsite != "B")
        {
            commandInfo.ReplyToCommand(_plugin.Prefixed(player, "retakes.error.specify_bombsite"));
            return;
        }

        var forcedBombsite = bombsite == "A" ? Bombsite.A : Bombsite.B;
        _roundEventHandlers.SetForcedBombsite(forcedBombsite);

        commandInfo.ReplyToCommand(_plugin.Prefixed(player, "retakes.forcebombsite.forced", forcedBombsite.ToString()));
        Logger.LogInfo("Commands", $"Bombsite forced to {forcedBombsite} by {player?.PlayerName ?? "Console"}");
    }
}