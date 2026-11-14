using System.Text;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;

namespace RetakesPlugin.Utils;

public static class ServerHelper
{
    private static string RetakesCfgDirectory => Path.Combine(Server.GameDirectory, "csgo", "cfg", "cs2-retakes");
    private static string RetakesCfgPath => Path.Combine(RetakesCfgDirectory, "retakes.cfg");

    public static void ExecuteRetakesConfiguration()
    {
        if (!File.Exists(RetakesCfgPath))
        {
            CreateRetakesConfig();
        }
        else
        {
            MigrateRetakesConfig();
        }

        Server.ExecuteCommand("exec cs2-retakes/retakes.cfg");
        Logger.LogInfo("Server", "Retakes configuration executed");
    }

    // retakes.cfg is only written when it is missing, so a fix to the shipped defaults never
    // reaches a server that already has one. The old default mp_roundtime_defuse 0.25 (15 seconds)
    // makes CS2 play the "10 seconds remaining" warning right after every round starts. Only that
    // exact old default is migrated; a value the server owner picked is left alone.
    // (Ported from upstream B3none/cs2-retakes 3.1.0.)
    private static void MigrateRetakesConfig()
    {
        try
        {
            var contents = File.ReadAllText(RetakesCfgPath);

            var migrated = Regex.Replace(
                contents,
                @"(?m)^([ \t]*)mp_roundtime_defuse[ \t]+0\.25[ \t]*(\r?)$",
                "${1}mp_roundtime_defuse 1.25${2}");

            if (migrated != contents)
            {
                File.WriteAllText(RetakesCfgPath, migrated);
                Logger.LogInfo("Server", "Migrated mp_roundtime_defuse 0.25 -> 1.25 in retakes.cfg (fixes the 10 second warning at round start)");
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("Server", ex);
        }
    }

    private static void CreateRetakesConfig()
    {
        try
        {
            Directory.CreateDirectory(RetakesCfgDirectory);

            var retakesCfg = File.Create(RetakesCfgPath);

            var retakesCfgContents = @"
                // Things you shouldn't change:
                bot_kick
                bot_quota 0
                mp_autoteambalance 0
                mp_forcecamera 1
                mp_give_player_c4 0
                mp_halftime 0
                mp_ignore_round_win_conditions 0
                mp_join_grace_time 0
                mp_match_can_clinch 0
                mp_maxmoney 0
                mp_playercashawards 0
                mp_respawn_on_death_ct 0
                mp_respawn_on_death_t 0
                mp_solid_teammates 1
                mp_teamcashawards 0
                mp_warmup_pausetimer 0
                sv_skirmish_id 0

                // Things you can change, and may want to:
                mp_roundtime_defuse 1.25
                mp_autokick 0
                mp_c4timer 40
                mp_freezetime 1
                mp_friendlyfire 0
                mp_round_restart_delay 2
                sv_talk_enemy_dead 0
                sv_talk_enemy_living 0
                sv_deadtalk 1
                spec_replay_enable 0
                mp_maxrounds 30
                mp_match_end_restart 0
                mp_timelimit 0
                mp_match_restart_delay 10
                mp_death_drop_gun 1
                mp_death_drop_defuser 1
                mp_death_drop_grenade 1
                mp_warmuptime 15

                echo [Retakes] Config loaded!
            ";

            var retakesCfgBytes = Encoding.UTF8.GetBytes(retakesCfgContents);
            retakesCfg.Write(retakesCfgBytes, 0, retakesCfgBytes.Length);
            retakesCfg.Close();

            Logger.LogInfo("Server", "Created retakes.cfg file");
        }
        catch (Exception ex)
        {
            Logger.LogException("Server", ex);
        }
    }
}