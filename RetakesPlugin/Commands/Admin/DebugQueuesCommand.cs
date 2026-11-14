using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;

using RetakesPlugin.Utils;
using RetakesPlugin.Managers;

namespace RetakesPlugin.Commands.Admin;

public class DebugQueuesCommand
{
    private readonly RetakesPlugin _plugin;
    private readonly GameManager _gameManager;

    public DebugQueuesCommand(RetakesPlugin plugin, GameManager gameManager)
    {
        _plugin = plugin;
        _gameManager = gameManager;
    }

    public void OnCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player != null)
        {
            commandInfo.ReplyToCommand(_plugin.Prefixed(player, "retakes.error.console_only"));
            return;
        }

        var requiredPermission = PlayerHelper.GetCommandPermission(_plugin.Config, "css_debugqueues", "Admin");
        if (!AdminManager.PlayerHasPermissions(player, requiredPermission))
        {
            commandInfo.ReplyToCommand(_plugin.Prefixed(player, "retakes.no_permissions"));
            return;
        }

        _gameManager.QueueManager.DebugQueues(true);
    }
}