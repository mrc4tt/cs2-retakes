using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

using RetakesPlugin.Utils;
using RetakesPlugin.Managers;
using RetakesPlugin.Services;
using RetakesPlugin.Commands.SpawnEditor;
using RetakesPluginShared.Enums;
using RetakesPluginShared.Events;

namespace RetakesPlugin.Events;

public class RoundEventHandlers
{
    private readonly RetakesPlugin _plugin;
    private readonly GameManager _gameManager;
    private readonly SpawnManager _spawnManager;
    private readonly BreakerManager? _breakerManager;
    private readonly AllocationService _allocationService;
    private readonly AnnouncementService _announcementService;
    private readonly bool _isAutoPlantEnabled;
    private readonly bool _enableFallbackAllocation;
    private readonly bool _enableFallbackBombsiteAnnouncement;
    private readonly Random _random;
    private ShowSpawnsCommand? _showSpawnsCommand;

    private Bombsite _currentBombsite = Bombsite.A;
    // The planter is remembered by UserId across the round: the controller it came from can be
    // freed by a disconnect between round start and freeze end, and reading IsValid through that
    // pointer is exactly the dereference this is meant to avoid.
    private int? _planterUserId;
    private CsTeam _lastRoundWinner = CsTeam.None;

    // Only a real retake (both sides had players at round start, and the round was not ended by
    // the plugin itself) may count towards the T win streak. Rounds ended by a player joining
    // (CheckRoundDone -> TerroristsWin), 1v0 rounds and game restarts used to be counted too, so
    // the "won N rounds in a row" message ran ahead of the rounds actually played.
    private bool _roundWasContested;
    private Bombsite? _forcedBombsite;

    public RoundEventHandlers(RetakesPlugin plugin, GameManager gameManager, SpawnManager spawnManager, BreakerManager? breakerManager, AllocationService allocationService, AnnouncementService announcementService, bool isAutoPlantEnabled, bool enableFallbackAllocation, bool enableFallbackBombsiteAnnouncement, Random random)
    {
        _plugin = plugin;
        _gameManager = gameManager;
        _spawnManager = spawnManager;
        _breakerManager = breakerManager;
        _allocationService = allocationService;
        _announcementService = announcementService;
        _isAutoPlantEnabled = isAutoPlantEnabled;
        _enableFallbackAllocation = enableFallbackAllocation;
        _enableFallbackBombsiteAnnouncement = enableFallbackBombsiteAnnouncement;
        _random = random;

        Logger.LogInfo("RoundEventHandlers", $"EnableFallbackAllocation inicializado a: {_enableFallbackAllocation}");
    }

    public void SetCommandReferences(ShowSpawnsCommand? showSpawnsCommand)
    {
        _showSpawnsCommand = showSpawnsCommand;
    }

    public void SetForcedBombsite(Bombsite? bombsite)
    {
        _forcedBombsite = bombsite;
    }

    public HookResult OnRoundPreStart(EventRoundPrestart @event, GameEventInfo info)
    {
        // The previous round's termination has landed; allow the next one to be requested.
        GameRulesHelper.ClearRoundEndPending();

        var gameRules = GameRulesHelper.GetGameRulesOrNull();
        if (gameRules == null)
        {
            Logger.LogDebug("Round", "Game rules not available yet, skipping pre-start logic");
            return HookResult.Continue;
        }

        _roundWasContested = false;

        if (gameRules.WarmupPeriod)
        {
            Logger.LogDebug("Round", "Warmup round, skipping pre-start logic");
            return HookResult.Continue;
        }

        _gameManager.QueueManager.ClearRoundTeams();

        Logger.LogDebug("Round", "Updating queues");
        _gameManager.QueueManager.DebugQueues(true);
        _gameManager.QueueManager.Update();
        _gameManager.QueueManager.DebugQueues(false);

        _gameManager.OnRoundPreStart(_lastRoundWinner);
        _gameManager.QueueManager.SetRoundTeams();

        _roundWasContested = PlayerHelper.GetPlayerCount(CsTeam.Terrorist) > 0
                             && PlayerHelper.GetPlayerCount(CsTeam.CounterTerrorist) > 0;

        Logger.LogInfo("Round", "Round pre-start complete");
        return HookResult.Continue;
    }

    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        // Handle weird alive spectators bug. GetPlayers() includes HLTV proxies and controllers
        // that are still connecting, and the list has to be materialised before suiciding anyone:
        // the lazy query was being walked while CommitSuicide mutated the very state it filters on.
        var weirdAliveSpectators = Utilities.GetPlayers()
            .Where(player => PlayerHelper.IsValid(player) && !player.IsHLTV)
            .Where(player => player.TeamNum < (int)CsTeam.Terrorist && player.PawnIsAlive)
            .ToList();

        if (weirdAliveSpectators.Count > 0)
        {
            Server.ExecuteCommand("mp_autoteambalance 0");

            foreach (var weirdAliveSpectator in weirdAliveSpectators)
            {
                weirdAliveSpectator.CommitSuicide(false, true);
            }
        }

        var gameRules = GameRulesHelper.GetGameRulesOrNull();
        if (gameRules == null)
        {
            Logger.LogDebug("Round", "Game rules not available yet, skipping round start logic");
            return HookResult.Continue;
        }

        if (gameRules.WarmupPeriod)
        {
            Logger.LogDebug("Round", "Warmup round, skipping.");
            if (_showSpawnsCommand?.ShowingSpawnsForBombsite != null && _plugin.MapConfigService != null)
            {
                SpawnService.ShowSpawns(_plugin, _plugin.MapConfigService.GetSpawnsClone(), _showSpawnsCommand.ShowingSpawnsForBombsite);
                Logger.LogDebug("Round", $"Re-showing spawns for bombsite {_showSpawnsCommand.ShowingSpawnsForBombsite}");
            }

            return HookResult.Continue;
        }

        if (_gameManager == null)
        {
            Logger.LogDebug("Round", "Game manager not loaded.");
            return HookResult.Continue;
        }

        if (_spawnManager == null)
        {
            Logger.LogDebug("Round", "Spawn manager not loaded.");
            return HookResult.Continue;
        }

        _breakerManager?.Handle();
        _currentBombsite = _forcedBombsite ?? (_random.Next(0, 2) == 0 ? Bombsite.A : Bombsite.B);
        _gameManager.ResetPlayerScores();

        var planter = _spawnManager.HandleRoundSpawns(_currentBombsite, _gameManager.QueueManager.ActivePlayers);
        _planterUserId = PlayerHelper.TryGetUserId(planter, out var planterUserId) ? planterUserId : null;

        if (_enableFallbackBombsiteAnnouncement)
        {
            _announcementService.AnnounceBombsite(_currentBombsite);
        }

        RetakesPlugin.RetakesPluginEventSenderCapability.Get()?.TriggerEvent(new AnnounceBombsiteEvent(_currentBombsite));

        Logger.LogInfo("Round", $"Round started on bombsite {_currentBombsite}");
        return HookResult.Continue;
    }

    public HookResult OnRoundPostStart(EventRoundPoststart @event, GameEventInfo info)
    {
        var gameRules = GameRulesHelper.GetGameRulesOrNull();
        if (gameRules == null)
        {
            Logger.LogDebug("Round", "Game rules not available yet, skipping post-start logic");
            return HookResult.Continue;
        }

        if (gameRules.WarmupPeriod)
        {
            Logger.LogDebug("Round", "Warmup round, skipping post-start logic");
            return HookResult.Continue;
        }

        Logger.LogDebug("Round", $"EnableFallbackAllocation: {_enableFallbackAllocation}");

        foreach (var player in _gameManager.QueueManager.ActivePlayers)
        {
            PlayerHelper.RemoveHelmetAndHeavyArmour(player);
            player.RemoveWeapons();

            if (!_isAutoPlantEnabled
                && _planterUserId.HasValue
                && PlayerHelper.TryGetUserId(player, out var userId)
                && userId == _planterUserId.Value)
            {
                PlayerHelper.GiveAndSwitchToBomb(player);
            }

            if (_enableFallbackAllocation)
            {
                Logger.LogDebug("Round", $"Asignando armas a {player.PlayerName} (fallback allocation habilitado)");
                _allocationService.AllocatePlayer(player);
            }
            else
            {
                Logger.LogDebug("Round", $"No asignando armas a {player.PlayerName} (fallback allocation deshabilitado)");
            }
        }

        RetakesPlugin.RetakesPluginEventSenderCapability.Get()?.TriggerEvent(new AllocateEvent());

        Logger.LogInfo("Round", "Round post-start complete");
        return HookResult.Continue;
    }

    public HookResult OnRoundFreezeEnd(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        var gameRules = GameRulesHelper.GetGameRulesOrNull();
        if (gameRules == null)
        {
            Logger.LogDebug("Round", "Game rules not available yet, skipping freeze end logic");
            return HookResult.Continue;
        }

        if (gameRules.WarmupPeriod)
        {
            Logger.LogDebug("Round", "Warmup round, skipping freeze end logic");
            return HookResult.Continue;
        }

        if (PlayerHelper.GetPlayerCount(CsTeam.Terrorist) > 0)
        {
            HandleAutoPlant();
        }

        return HookResult.Continue;
    }

    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        _lastRoundWinner = (CsTeam)@event.Winner;
        Logger.LogInfo("Round", $"Round ended. Winner: {_lastRoundWinner}");

        if (!_roundWasContested || GameRulesHelper.IsRoundEndPending())
        {
            Logger.LogDebug("Round", "Round was not a real retake, not counting it towards the win streak");
            _lastRoundWinner = CsTeam.None;
        }

        _roundWasContested = false;
        return HookResult.Continue;
    }

    public HookResult OnBombPlanted(EventBombPlanted @event, GameEventInfo info)
    {
        Logger.LogInfo("Round", "Bomb planted");

        _plugin.AddTimer(4.1f, () =>
        {
            _announcementService.AnnounceBombsite(_currentBombsite, true);
        });

        return HookResult.Continue;
    }

    public HookResult OnBombDefused(EventBombDefused @event, GameEventInfo info)
    {
        var player = @event.Userid;

        if (PlayerHelper.IsValid(player))
        {
            _gameManager.AddDefuse(player);
        }

        Logger.LogInfo("Round", $"Bomb defused by {player?.PlayerName ?? "unknown"}");
        return HookResult.Continue;
    }

    private void HandleAutoPlant()
    {
        if (!_isAutoPlantEnabled)
        {
            return;
        }

        var planter = _planterUserId.HasValue ? PlayerHelper.ResolveConnected(_planterUserId.Value) : null;

        if (planter != null)
        {
            BombService.PlantTickingBomb(planter, _currentBombsite);
            Logger.LogInfo("Round", $"Auto-planted bomb at {_currentBombsite}");
        }
        else
        {
            Logger.LogWarning("Round", "No valid planter found, terminating round");
            GameRulesHelper.TerminateRound(CounterStrikeSharp.API.Modules.Entities.Constants.RoundEndReason.RoundDraw);
        }
    }
}