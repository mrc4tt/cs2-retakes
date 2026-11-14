using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

using RetakesPlugin.Configs;
using RetakesPlugin.Utils;

namespace RetakesPlugin.Commands.Player;

public class VoicesCommand
{
    private readonly HashSet<int> _hasMutedVoices;
    private readonly BaseConfigs _config;
    private readonly RetakesPlugin _plugin;

    public VoicesCommand(RetakesPlugin plugin, BaseConfigs config, HashSet<int> hasMutedVoices)
    {
        _plugin = plugin;
        _config = config;
        _hasMutedVoices = hasMutedVoices;
    }

    public void OnCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (!PlayerHelper.IsValid(player))
        {
            commandInfo.ReplyToCommand(_plugin.Prefixed(player, "retakes.error.invalid_player"));
            return;
        }

        if (!_config.MapConfig.EnableBombsiteAnnouncementVoices)
        {
            commandInfo.ReplyToCommand(_plugin.Prefixed(player, "retakes.voices.disabled_server"));
            return;
        }

        if (!PlayerHelper.TryGetUserId(player, out var userId))
        {
            commandInfo.ReplyToCommand(_plugin.Prefixed(player, "retakes.error.invalid_player"));
            return;
        }

        var didMute = false;
        if (!_hasMutedVoices.Contains(userId))
        {
            didMute = true;
            _hasMutedVoices.Add(userId);
        }
        else
        {
            _hasMutedVoices.Remove(userId);
        }

        var statusText = didMute ? _plugin.Tr(player, "retakes.enabled") : _plugin.Tr(player, "retakes.disabled");

        commandInfo.ReplyToCommand(_plugin.Prefixed(player, "retakes.voices.toggle", statusText));

        Logger.LogInfo("Commands", $"{player.PlayerName} {(didMute ? "muted" : "unmuted")} voice announcements");
    }
}