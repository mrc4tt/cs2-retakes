using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

using RetakesPlugin.Utils;
using RetakesPluginShared.Enums;

namespace RetakesPlugin.Services;

public class AnnouncementService
{
    private readonly RetakesPlugin _plugin;
    private readonly Random _random;
    private readonly HashSet<int> _hasMutedVoices;
    private readonly bool _voicesEnabled;
    private readonly bool _centerEnabled;

    private static readonly string[] BombsiteAnnouncers =
    [
        "balkan_epic",
        "leet_epic",
        "professional_epic",
        "professional_fem",
        "seal_epic",
        "swat_epic",
        "swat_fem"
    ];

    public AnnouncementService(RetakesPlugin plugin, Random random, HashSet<int> hasMutedVoices, bool voicesEnabled, bool centerEnabled)
    {
        _plugin = plugin;
        _random = random;
        _hasMutedVoices = hasMutedVoices;
        _voicesEnabled = voicesEnabled;
        _centerEnabled = centerEnabled;
    }

    public void AnnounceBombsite(Bombsite bombsite, bool onlyCenter = false)
    {
        var numTerrorist = PlayerHelper.GetPlayerCount(CounterStrikeSharp.API.Modules.Utils.CsTeam.Terrorist);
        var numCounterTerrorist = PlayerHelper.GetPlayerCount(CounterStrikeSharp.API.Modules.Utils.CsTeam.CounterTerrorist);

        var bombsiteName = bombsite.ToString();

        // GetPlayers() also hands back bots, HLTV proxies and half-connected controllers;
        // PrintToChat / ExecuteClientCommand on those has no receiver and can touch a pawn that
        // is not there yet.
        var recipients = Utilities.GetPlayers()
            .Where(player => PlayerHelper.IsValid(player) && PlayerHelper.IsConnected(player))
            .Where(player => !player.IsBot && !player.IsHLTV)
            .ToList();

        foreach (var player in recipients)
        {
            if (!PlayerHelper.TryGetUserId(player, out var userId))
            {
                continue;
            }

            if (!onlyCenter)
            {
                player.PrintToChat(_plugin.Prefixed(player, "retakes.bombsite.announcement", bombsiteName, numTerrorist, numCounterTerrorist));

                if (_voicesEnabled && !_hasMutedVoices.Contains(userId))
                {
                    var bombsiteAnnouncer = BombsiteAnnouncers[_random.Next(BombsiteAnnouncers.Length)];
                    player.ExecuteClientCommand($"play sounds/vo/agents/{bombsiteAnnouncer}/loc_{bombsiteName.ToLower()}_01");
                }

                continue;
            }

            if (!_centerEnabled)
            {
                continue;
            }

            if (player.Team == CounterStrikeSharp.API.Modules.Utils.CsTeam.CounterTerrorist)
            {
                player.PrintToCenter(_plugin.Tr(player, "retakes.center.bombsite.announcement", bombsiteName, numTerrorist, numCounterTerrorist));
            }
        }

        Logger.LogInfo("Announcement", $"Announced bombsite {bombsite} ({numTerrorist}T vs {numCounterTerrorist}CT)");
    }
}