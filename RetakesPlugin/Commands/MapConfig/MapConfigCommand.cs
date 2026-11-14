using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;

using RetakesPlugin.Utils;

namespace RetakesPlugin.Commands.MapConfig;

public class MapConfigCommand
{
    private readonly RetakesPlugin _plugin;
    private readonly string _moduleDirectory;
    private readonly Action<string> _onMapConfigLoad;

    public MapConfigCommand(RetakesPlugin plugin, string moduleDirectory, Action<string> onMapConfigLoad)
    {
        _plugin = plugin;
        _moduleDirectory = moduleDirectory;
        _onMapConfigLoad = onMapConfigLoad;
    }

    public void OnCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player != null && !PlayerHelper.IsValid(player))
        {
            return;
        }

        var commandName = commandInfo.GetArg(0);
        var requiredPermission = PlayerHelper.GetCommandPermission(_plugin.Config, commandName, "MapConfig");
        if (!AdminManager.PlayerHasPermissions(player, requiredPermission))
        {
            commandInfo.ReplyToCommand(_plugin.Prefixed(player, "retakes.no_permissions"));
            return;
        }

        if (commandInfo.ArgCount < 2)
        {
            commandInfo.ReplyToCommand(_plugin.Prefixed(player, "retakes.mapconfig.usage"));
            return;
        }

        var mapConfigFileName = commandInfo.GetArg(1).Trim().Replace(".json", "");

        if (mapConfigFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !MapConfigPaths.Exists(_moduleDirectory, mapConfigFileName))
        {
            commandInfo.ReplyToCommand(_plugin.Prefixed(player, "retakes.mapconfig.not_found"));
            return;
        }

        _onMapConfigLoad(mapConfigFileName);

        commandInfo.ReplyToCommand(_plugin.Prefixed(player, "retakes.mapconfig.loaded"));
        Logger.LogInfo("Commands", $"Map config '{mapConfigFileName}' loaded by {player?.PlayerName ?? "Console"}");
    }
}