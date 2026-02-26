namespace Claw0.Soul;

/// <summary>
/// Soul 配置 - Agent 的人格设定
/// 
/// SOUL.md 格式:
/// ---
/// name: "Agent Name"
/// personality: "描述性格..."
/// goals: ["目标1", "目标2"]
/// preferences: { "key": "value" }
/// ---
/// </summary>
public class SoulConfig
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Personality { get; set; }
    public List<string> Goals { get; set; } = new();
    public Dictionary<string, string> Preferences { get; set; } = new();
    public List<string> Rules { get; set; } = new();

    public string ToSystemPrompt()
    {
        var sb = new System.Text.StringBuilder();
        
        sb.AppendLine($"You are {Name}.");
        
        if (!string.IsNullOrEmpty(Description))
            sb.AppendLine(Description);
        
        if (!string.IsNullOrEmpty(Personality))
            sb.AppendLine($"Personality: {Personality}");
        
        if (Goals.Count > 0)
        {
            sb.AppendLine("Your goals:");
            foreach (var goal in Goals)
                sb.AppendLine($"  - {goal}");
        }
        
        if (Rules.Count > 0)
        {
            sb.AppendLine("Rules you must follow:");
            foreach (var rule in Rules)
                sb.AppendLine($"  - {rule}");
        }
        
        if (Preferences.Count > 0)
        {
            sb.AppendLine("Preferences:");
            foreach (var (key, value) in Preferences)
                sb.AppendLine($"  - {key}: {value}");
        }
        
        return sb.ToString().Trim();
    }
}
