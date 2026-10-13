using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

using RetakesPlugin.Models;
using RetakesPlugin.Utils;

namespace RetakesPlugin.Managers;

public class GameManager
{
    private readonly RetakesPlugin _plugin;
    private Dictionary<int, PlayerScore> _playerScores = new();
    public readonly QueueManager QueueManager;
    private readonly int _consecutiveRoundWinsToScramble;
    private readonly bool _isScrambleEnabled;
    private readonly bool _removeSpectatorsEnabled;
    private readonly bool _isBalanceEnabled;

    public const int ScoreForKill = 50;
    public const int ScoreForAssist = 25;
    public const int ScoreForDefuse = 50;

    public GameManager(RetakesPlugin plugin, QueueManager queueManager, int? roundsToScramble, bool? isScrambleEnabled, bool? removeSpectatorsEnabled, bool? isBalanceEnabled)
    {
        _plugin = plugin;
        QueueManager = queueManager;
        _consecutiveRoundWinsToScramble = roundsToScramble ?? 5;
        _isScrambleEnabled = isScrambleEnabled ?? true;
        _removeSpectatorsEnabled = removeSpectatorsEnabled ?? false;
        _isBalanceEnabled = isBalanceEnabled ?? true;

        Logger.LogInfo("GameManager", "Game manager initialized");
    }

    private const float RestartGameCooldownSeconds = 3.0f;
    private float _lastRestartGameTime = -RestartGameCooldownSeconds;

    // Jointeam from the team menu fires this once per player; without the cooldown, two
    // players joining in quick succession run mp_restartgame twice within the same second,
    // and the second restart lands while the first is still tearing the round down - that
    // is the crash when players join spectator and pick a team (upstream issue #186).
    public void RestartGameIfEmpty()
    {
        if (QueueManager.ActivePlayerCount != 0)
        {
            return;
        }

        var timeSinceLastRestart = Server.CurrentTime - _lastRestartGameTime;
        if (timeSinceLastRestart >= 0 && timeSinceLastRestart < RestartGameCooldownSeconds)
        {
            Logger.LogDebug("GameManager", "Skipping game restart, cooldown active");
            return;
        }

        // Claim the cooldown synchronously so two players joining in the same frame cannot both
        // queue a restart, then run the restart itself a frame later: this is reached from inside
        // the jointeam hook, and mp_restartgame plus the TerminateRound behind RestartGame() must
        // not re-enter the engine while it is still handling that command.
        _lastRestartGameTime = Server.CurrentTime;

        Server.NextFrame(() =>
        {
            // Someone may have picked a team in the meantime - only restart if still empty.
            if (QueueManager.ActivePlayerCount != 0)
            {
                Logger.LogDebug("GameManager", "Players joined before the restart landed, skipping");
                return;
            }

            Logger.LogDebug("GameManager", "No active players, updating queue and restarting game");
            QueueManager.ClearRoundTeams();
            QueueManager.Update();
            GameRulesHelper.RestartGame();
        });
    }

    private bool _scrambleNextRound;

    public void ScrambleNextRound(CCSPlayerController? admin = null)
    {
        _scrambleNextRound = true;
        var message = $"{_plugin.Localizer["retakes.prefix"]} {_plugin.Localizer["retakes.teams.admin_scramble", admin?.PlayerName ?? "The server owner"]}";
        Server.PrintToChatAll(message);
        Logger.LogInfo("GameManager", $"Teams will be scrambled next round by {admin?.PlayerName ?? "server"}");
    }

    private void ScrambleTeams()
    {
        _scrambleNextRound = false;
        _consecutiveRoundsWon = 0;

        var shuffledActivePlayers = PlayerHelper.Shuffle(QueueManager.ActivePlayers, new Random());

        var newTerrorists = shuffledActivePlayers.Take(QueueManager.GetTargetNumTerrorists()).ToList();
        var newCounterTerrorists = ExceptByUserId(shuffledActivePlayers, newTerrorists);

        SetTeams(newTerrorists, newCounterTerrorists);
        Logger.LogInfo("GameManager", "Teams scrambled");
    }

    public void ResetPlayerScores()
    {
        _playerScores = new Dictionary<int, PlayerScore>();
        Logger.LogDebug("GameManager", "Player scores reset");
    }

    public void AddScore(CCSPlayerController player, int score)
    {
        if (!PlayerHelper.IsValid(player) || player.UserId == null)
        {
            return;
        }

        var playerId = (int)player.UserId;

        if (!_playerScores.ContainsKey(playerId))
        {
            _playerScores[playerId] = new PlayerScore
            {
                UserId = playerId,
                PlayerName = player.PlayerName
            };
        }

        _playerScores[playerId].AddScore(score);
        Logger.LogDebug("GameManager", $"{player.PlayerName} scored {score} points (Total: {_playerScores[playerId].Score})");
    }

    public void AddKill(CCSPlayerController player)
    {
        if (!PlayerHelper.IsValid(player) || player.UserId == null) return;

        var playerId = (int)player.UserId;
        if (!_playerScores.ContainsKey(playerId))
        {
            _playerScores[playerId] = new PlayerScore
            {
                UserId = playerId,
                PlayerName = player.PlayerName
            };
        }

        _playerScores[playerId].AddKill();
    }

    public void AddAssist(CCSPlayerController player)
    {
        if (!PlayerHelper.IsValid(player) || player.UserId == null) return;

        var playerId = (int)player.UserId;
        if (!_playerScores.ContainsKey(playerId))
        {
            _playerScores[playerId] = new PlayerScore
            {
                UserId = playerId,
                PlayerName = player.PlayerName
            };
        }

        _playerScores[playerId].AddAssist();
    }

    public void AddDefuse(CCSPlayerController player)
    {
        if (!PlayerHelper.IsValid(player) || player.UserId == null) return;

        var playerId = (int)player.UserId;
        if (!_playerScores.ContainsKey(playerId))
        {
            _playerScores[playerId] = new PlayerScore
            {
                UserId = playerId,
                PlayerName = player.PlayerName
            };
        }

        _playerScores[playerId].AddDefuse();
    }

    private int _consecutiveRoundsWon;

    private void TerroristRoundWin()
    {
        _consecutiveRoundsWon++;

        var shouldScrambleNow = _isScrambleEnabled && _consecutiveRoundsWon == _consecutiveRoundWinsToScramble;
        var roundsLeftToScramble = _consecutiveRoundWinsToScramble - _consecutiveRoundsWon;
        var shouldAlmostScramble = _isScrambleEnabled && roundsLeftToScramble > 0 && roundsLeftToScramble <= 2;

        if (shouldScrambleNow)
        {
            var message = $"{_plugin.Localizer["retakes.prefix"]} {_plugin.Localizer["retakes.teams.scramble", _consecutiveRoundWinsToScramble]}";
            Server.PrintToChatAll(message);
            Logger.LogInfo("GameManager", $"Scrambling teams after {_consecutiveRoundWinsToScramble} T wins");
            ScrambleTeams();
        }
        else if (shouldAlmostScramble)
        {
            var message = $"{_plugin.Localizer["retakes.prefix"]} {_plugin.Localizer["retakes.teams.almost_scramble", _consecutiveRoundsWon, roundsLeftToScramble]}";
            Server.PrintToChatAll(message);
        }
        else if (_consecutiveRoundsWon >= 3)
        {
            var message = $"{_plugin.Localizer["retakes.prefix"]} {_plugin.Localizer["retakes.teams.win_streak", _consecutiveRoundsWon]}";
            Server.PrintToChatAll(message);
        }
    }

    private void CounterTerroristRoundWin()
    {
        if (_consecutiveRoundsWon >= 3)
        {
            var message = $"{_plugin.Localizer["retakes.prefix"]} {_plugin.Localizer["retakes.teams.win_streak_over", _consecutiveRoundsWon]}";
            Server.PrintToChatAll(message);
            Logger.LogInfo("GameManager", $"CT broke {_consecutiveRoundsWon} round win streak");
        }

        _consecutiveRoundsWon = 0;

        var targetNumTerrorists = QueueManager.GetTargetNumTerrorists();
        var sortedCounterTerroristPlayers = GetSortedActivePlayers(CsTeam.CounterTerrorist);

        var newTerrorists = sortedCounterTerroristPlayers
            .Where(p => GetPlayerScore(p) > 0)
            .Take(targetNumTerrorists)
            .ToList();

        if (newTerrorists.Count < targetNumTerrorists)
        {
            var playersLeft = PlayerHelper.Shuffle(
                ExceptByUserId(sortedCounterTerroristPlayers, newTerrorists),
                new Random()
            );
            newTerrorists.AddRange(playersLeft.Take(targetNumTerrorists - newTerrorists.Count));
        }

        if (newTerrorists.Count < targetNumTerrorists)
        {
            newTerrorists.AddRange(
                GetSortedActivePlayers(CsTeam.Terrorist)
                    .Take(targetNumTerrorists - newTerrorists.Count)
            );
        }

        var newCounterTerrorists = ExceptByUserId(QueueManager.ActivePlayers, newTerrorists);

        SetTeams(newTerrorists, newCounterTerrorists);
    }

    private void BalanceTeams()
    {
        List<CCSPlayerController> newTerrorists = [];
        List<CCSPlayerController> newCounterTerrorists = [];

        var currentNumTerrorist = PlayerHelper.GetPlayerCount(CsTeam.Terrorist);
        var numTerroristsNeeded = QueueManager.GetTargetNumTerrorists() - currentNumTerrorist;
        if (numTerroristsNeeded > 0)
        {
            var sortedCounterTerroristPlayers = GetSortedActivePlayers(CsTeam.CounterTerrorist);

            newTerrorists = sortedCounterTerroristPlayers
                .Where(p => GetPlayerScore(p) > 0)
                .Take(numTerroristsNeeded)
                .ToList();

            if (newTerrorists.Count < numTerroristsNeeded)
            {
                var playersLeft = PlayerHelper.Shuffle(
                    ExceptByUserId(sortedCounterTerroristPlayers, newTerrorists),
                    new Random()
                );
                newTerrorists.AddRange(playersLeft.Take(numTerroristsNeeded - newTerrorists.Count));
            }
        }

        var currentNumCounterTerroristAfterBalance = PlayerHelper.GetPlayerCount(CsTeam.CounterTerrorist);
        var numCounterTerroristsNeeded = QueueManager.GetTargetNumCounterTerrorists() - currentNumCounterTerroristAfterBalance;

        if (numCounterTerroristsNeeded > 0)
        {
            var newTerroristIds = UserIdsOf(newTerrorists);

            var terroristsWithZeroScore = QueueManager.ActivePlayers
                .Where(player => player.Team == CsTeam.Terrorist && GetPlayerScore(player) == 0)
                .Where(player => PlayerHelper.TryGetUserId(player, out var userId) && !newTerroristIds.Contains(userId))
                .ToList();

            newCounterTerrorists = PlayerHelper.Shuffle(terroristsWithZeroScore, new Random())
                .Take(numCounterTerroristsNeeded)
                .ToList();

            if (numCounterTerroristsNeeded > newCounterTerrorists.Count)
            {
                var alreadyPickedIds = UserIdsOf(newCounterTerrorists);
                alreadyPickedIds.UnionWith(newTerroristIds);

                newCounterTerrorists.AddRange(
                    QueueManager.ActivePlayers
                        .Where(player => PlayerHelper.TryGetUserId(player, out var userId) && !alreadyPickedIds.Contains(userId))
                        .Where(player => player.Team == CsTeam.Terrorist)
                        .OrderBy(GetPlayerScore)
                        // Was numTerroristsNeeded: the CT top-up was sized against the number of
                        // Ts still wanted, which is a different (often smaller or negative)
                        // number, so CT was left under-filled.
                        .Take(numCounterTerroristsNeeded - newCounterTerrorists.Count)
                        .ToList()
                );
            }
        }

        SetTeams(newTerrorists, newCounterTerrorists);
    }

    public void OnRoundPreStart(CsTeam winningTeam)
    {
        Logger.LogDebug("GameManager", $"Round pre-start. Winning team: {winningTeam}");

        switch (winningTeam)
        {
            case CsTeam.CounterTerrorist:
                if (_isBalanceEnabled)
                {
                    CounterTerroristRoundWin();
                }
                break;

            case CsTeam.Terrorist:
                TerroristRoundWin();
                break;
        }

        if (_scrambleNextRound)
        {
            ScrambleTeams();
        }

        if (_isBalanceEnabled)
        {
            BalanceTeams();
        }
    }

    private List<CCSPlayerController> GetSortedActivePlayers(CsTeam? team = null)
    {
        return QueueManager.ActivePlayers
            .Where(player => team == null || player.Team == team)
            .OrderByDescending(GetPlayerScore)
            .ToList();
    }

    private int GetPlayerScore(CCSPlayerController player)
    {
        if (player.UserId == null) return 0;
        return _playerScores.TryGetValue((int)player.UserId, out var score) ? score.Score : 0;
    }

    private void SetTeams(List<CCSPlayerController>? terrorists, List<CCSPlayerController>? counterTerrorists)
    {
        terrorists ??= [];
        counterTerrorists ??= [];

        // Membership is decided by UserId: the controllers in these lists were resolved before
        // the switches above and List.Contains would fall back to pointer-shaped identity.
        var terroristIds = UserIdsOf(terrorists);
        var counterTerroristIds = UserIdsOf(counterTerrorists);

        foreach (var player in QueueManager.ActivePlayers)
        {
            if (!PlayerHelper.TryGetUserId(player, out var userId))
            {
                continue;
            }

            if (terroristIds.Contains(userId))
            {
                PlayerHelper.TrySwitchTeam(player, CsTeam.Terrorist);
            }
            else if (counterTerroristIds.Contains(userId))
            {
                PlayerHelper.TrySwitchTeam(player, CsTeam.CounterTerrorist);
            }
        }

        Logger.LogDebug("GameManager", $"Teams set: {terrorists.Count} T, {counterTerrorists.Count} CT");
    }

    public HookResult RemoveSpectators(EventPlayerTeam @event, HashSet<int> hasMutedVoices)
    {
        if (!_removeSpectatorsEnabled) return HookResult.Continue;

        CCSPlayerController? player = @event.Userid;
        if (!PlayerHelper.TryGetUserId(player, out var userId))
        {
            return HookResult.Continue;
        }

        int team = @event.Team;
        if (team == (int)CsTeam.Spectator)
        {
            if (QueueManager.IsActive(player))
            {
                QueueManager.RemovePlayerFromQueues(player);
                hasMutedVoices.Remove(userId);
                Logger.LogInfo("GameManager", $"{player!.PlayerName} moved to spectator and removed from queues");
            }
        }

        return HookResult.Continue;
    }

    // Set operations over players are done on UserIds, never on controller instances - see the
    // comment on QueueManager's id sets for why controller identity cannot be trusted.
    private static HashSet<int> UserIdsOf(IEnumerable<CCSPlayerController> players)
    {
        HashSet<int> userIds = [];

        foreach (var player in players)
        {
            if (PlayerHelper.TryGetUserId(player, out var userId))
            {
                userIds.Add(userId);
            }
        }

        return userIds;
    }

    private static List<CCSPlayerController> ExceptByUserId(IEnumerable<CCSPlayerController> players, IEnumerable<CCSPlayerController> excluded)
    {
        var excludedIds = UserIdsOf(excluded);

        return players
            .Where(player => PlayerHelper.TryGetUserId(player, out var userId) && !excludedIds.Contains(userId))
            .ToList();
    }
}