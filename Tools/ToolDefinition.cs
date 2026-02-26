using System.Text.Json.Serialization;

namespace Claw0.Tools;

/// <summary>
/// 工具定义 - 传递给 LLM 的 Schema
/// </summary>
public class ToolDefinition
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    public required string Description { get; set; }

    [JsonPropertyName("input_schema")]
    public required InputSchema InputSchema { get; set; }
}

public class InputSchema
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("properties")]
    public required Dictionary<string, PropertyDefinition> Properties { get; set; }

    [JsonPropertyName("required")]
    public List<string>? Required { get; set; }
}

public class PropertyDefinition
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [JsonPropertyName("description")]
    public required string Description { get; set; }
}
