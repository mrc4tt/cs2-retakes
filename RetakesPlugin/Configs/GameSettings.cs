using System.Text.Json.Serialization;

namespace RetakesPlugin.Configs;

public class GameSettings
{
    [JsonPropertyName("MaxPlayers")]
    public int MaxPlayers { get; set; } = 9;

    [JsonPropertyName("ShouldBreakBreakables")]
    public bool ShouldBreakBreakables { get; set; } = false;

    [JsonPropertyName("ShouldOpenDoors")]
    public bool ShouldOpenDoors { get; set; } = false;

    // Off by default: nearly every server runs a standalone allocator (see the Allocators
    // section of the README), and having both hand out weapons produces duplicate,
    // droppable guns on spawn. Set to true to use this plugin's built-in allocation.
    [JsonPropertyName("EnableFallbackAllocation")]
    public bool EnableFallbackAllocation { get; set; } = false;
}