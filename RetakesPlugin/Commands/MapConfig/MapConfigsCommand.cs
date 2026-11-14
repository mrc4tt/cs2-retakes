using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;

using RetakesPlugin.Utils;

namespace RetakesPlugin.Commands.MapConfig;

public class MapConfigsCommand
{
    private readonly RetakesPlugin _plugin;
    private readonly string _moduleDirectory;

    public MapConfigsCommand(RetakesPlugin plugin, string moduleDirectory)
    {
        _plugin = plugin;
        _moduleDirectory = moduleDirectory;
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

        var mapConfigs = MapConfigPaths.List(_moduleDirectory);

        if (mapConfigs.Count == 0)
        {
            commandInfo.ReplyToCommand(_plugin.Prefixed(player, "retakes.mapconfigs.none_found"));
            return;
        }

        foreach (var (name, isCustom) in mapConfigs)
        {
            var line = $"{_plugin.Tr(player, "retakes.prefix")} !mapconfig {name}{(isCustom ? $" {_plugin.Tr(player, "retakes.mapconfigs.custom")}" : "")}";
            commandInfo.ReplyToCommand(line);
            player?.PrintToConsole(line);
        }

        commandInfo.ReplyToCommand(_plugin.Prefixed(player, "retakes.mapconfigs.listed"));
    }
}