using System.Text.Json.Serialization;

namespace RetakesPlugin.Configs;

public class QueueSettings
{
    [JsonPropertyName("QueuePriorityFlag")]
    public List<QueuePriorityFlagConfig>? QueuePriorityFlag { get; set; } = new()
    {
        new QueuePriorityFlagConfig { DisplayName = "VIP", Flag = "@css/vip", Priority = 0 }
    };

    [JsonPropertyName("QueueImmunityFlag")]
    public List<QueuePriorityFlagConfig>? QueueImmunityFlag { get; set; } = new()
    {
        new QueuePriorityFlagConfig { DisplayName = "VIP", Flag = "@css/vip", Priority = 0 }
    };

    [JsonPropertyName("ShouldRemoveSpectators")]
    public bool ShouldRemoveSpectators { get; set; } = true;

    [JsonPropertyName("ShouldAutoJoinSpectators")]
    public bool ShouldAutoJoinSpectators { get; set; } = true;

    // Put connecting players straight into the game (or the queue when it is full) instead of
    // showing the team menu. Takes precedence over ShouldAutoJoinSpectators.
    [JsonPropertyName("ShouldAutoJoinGame")]
    public bool ShouldAutoJoinGame { get; set; } = false;

    // Active players who stay AFK this many rounds in a row are moved to spectator so the queue
    // can take their slot. 0 disables AFK handling.
    [JsonPropertyName("AfkRoundsToSpectator")]
    public int AfkRoundsToSpectator { get; set; } = 3;

    public List<QueuePriorityFlagConfig> GetPriorityFlags()
    {
        if (QueuePriorityFlag == null || QueuePriorityFlag.Count == 0)
        {
            return new List<QueuePriorityFlagConfig>();
        }

        return QueuePriorityFlag;
    }

    public List<QueuePriorityFlagConfig> GetImmunityFlags()
    {
        if (QueueImmunityFlag == null || QueueImmunityFlag.Count == 0)
        {
            return new List<QueuePriorityFlagConfig>();
        }

        return QueueImmunityFlag;
    }
}