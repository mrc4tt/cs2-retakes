using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;

using RetakesPlugin.Managers;
using RetakesPlugin.Utils;

namespace RetakesPlugin.Commands.Player;

public class QueueCommand
{
    private readonly RetakesPlugin _plugin;
    private readonly GameManager _gameManager;

    public QueueCommand(RetakesPlugin plugin, GameManager gameManager)
    {
        _plugin = plugin;
        _gameManager = gameManager;
    }

    public void OnCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (!PlayerHelper.IsValid(player))
        {
            commandInfo.ReplyToCommand(_plugin.Prefixed(player, "retakes.error.invalid_player"));
            return;
        }

        if (!_plugin.IsPluginEnabled)
        {
            commandInfo.ReplyToCommand(_plugin.Prefixed(player, "retakes.plugin.disabled"));
            return;
        }

        var queueManager = _gameManager.QueueManager;

        if (queueManager.IsActive(player))
        {
            commandInfo.ReplyToCommand(_plugin.Prefixed(player, "retakes.queue.status_playing", queueManager.QueuePlayerCount));
            return;
        }

        var position = queueManager.GetQueuePosition(player);
        if (position == null)
        {
            commandInfo.ReplyToCommand(_plugin.Prefixed(player, "retakes.queue.not_queued"));
            return;
        }

        commandInfo.ReplyToCommand(_plugin.Prefixed(player, "retakes.queue.position", position.Value.Position, position.Value.Total));
    }
}
