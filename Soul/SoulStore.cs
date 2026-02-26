using System.Text.Json;
using System.Text.RegularExpressions;

namespace Claw0.Soul;

/// <summary>
/// Soul 存储 - 管理 Agent 的人格配置
/// </summary>
public class SoulStore
{
    private readonly string _soulDir;

    public SoulStore(string workspaceDir)
    {
        _soulDir = Path.Combine(workspaceDir, ".souls");
        Directory.CreateDirectory(_soulDir);
    }

    /// <summary>
    /// 加载 Soul 配置
    /// </summary>
    public SoulConfig? LoadSoul(string agentId)
    {
        var soulPath = Path.Combine(_soulDir, $"{agentId}.md");
        if (!File.Exists(soulPath))
            return null;

        try
        {
            var content = File.ReadAllText(soulPath);
            return ParseSoulMarkdown(content);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 保存 Soul 配置
    /// </summary>
    public void SaveSoul(string agentId, SoulConfig soul)
    {
        var soulPath = Path.Combine(_soulDir, $"{agentId}.md");
        var content = ToSoulMarkdown(soul);
        File.WriteAllText(soulPath, content);
    }

    /// <summary>
    /// 创建默认 Soul
    /// </summary>
    public SoulConfig CreateDefaultSoul(string agentId)
    {
        var soul = new SoulConfig
        {
            Name = agentId,
            Description = $"I am {agentId}, a helpful AI assistant.",
            Personality = "Friendly, concise, and professional.",
            Goals = new List<string> { "Help users with their tasks", "Be accurate and helpful" },
            Rules = new List<string> { "Always be polite", "Keep answers concise" }
        };
        SaveSoul(agentId, soul);
        return soul;
    }

    /// <summary>
    /// 获取或创建 Soul
    /// </summary>
    public SoulConfig GetOrCreateSoul(string agentId)
    {
        return LoadSoul(agentId) ?? CreateDefaultSoul(agentId);
    }

    /// <summary>
    /// 列出所有 Soul
    /// </summary>
    public List<string> ListSouls()
    {
        if (!Directory.Exists(_soulDir))
            return new List<string>();

        return Directory.GetFiles(_soulDir, "*.md")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrEmpty(name))
            .Cast<string>()
            .ToList();
    }

    /// <summary>
    /// 解析 SOUL.md 格式
    /// </summary>
    private static SoulConfig ParseSoulMarkdown(string content)
    {
        var soul = new SoulConfig { Name = "Agent" };

        // 尝试解析 YAML frontmatter
        var frontmatterMatch = Regex.Match(content, @"^---\s*\n(.*?)\n---\s*\n(.*)$", RegexOptions.Singleline);
        
        if (frontmatterMatch.Success)
        {
            var yaml = frontmatterMatch.Groups[1].Value;
            var body = frontmatterMatch.Groups[2].Value.Trim();
            
            soul.Description = body;

            // 简单 YAML 解析
            var lines = yaml.Split('\n');
            string? currentList = null;
            var listItems = new List<string>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                
                // 键值对
                var kvMatch = Regex.Match(trimmed, @"^(\w+):\s*(.*)$");
                if (kvMatch.Success)
                {
                    // 保存之前的列表
                    if (currentList != null && listItems.Count > 0)
                    {
                        SetListProperty(soul, currentList, listItems);
                        listItems.Clear();
                    }

                    var key = kvMatch.Groups[1].Value.ToLower();
                    var value = kvMatch.Groups[2].Value.Trim();

                    if (value.StartsWith("[") && value.EndsWith("]"))
                    {
                        // JSON 数组
                        try
                        {
                            var array = JsonSerializer.Deserialize<List<string>>(value);
                            if (array != null)
                                SetListProperty(soul, key, array);
                        }
                        catch { /* 忽略解析错误 */ }
                    }
                    else if (value.StartsWith("{") && value.EndsWith("}"))
                    {
                        // JSON 对象
                        try
                        {
                            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(value);
                            if (dict != null)
                                soul.Preferences = dict;
                        }
                        catch { /* 忽略解析错误 */ }
                    }
                    else if (string.IsNullOrEmpty(value))
                    {
                        // 可能是列表的开始
                        currentList = key;
                    }
                    else
                    {
                        // 简单值
                        switch (key)
                        {
                            case "name": soul.Name = value.Trim('"', '\''); break;
                            case "personality": soul.Personality = value.Trim('"', '\''); break;
                            case "description": soul.Description = value.Trim('"', '\''); break;
                        }
                    }
                }
                else if (trimmed.StartsWith("- "))
                {
                    // 列表项
                    listItems.Add(trimmed[2..].Trim());
                }
            }

            // 保存最后的列表
            if (currentList != null && listItems.Count > 0)
            {
                SetListProperty(soul, currentList, listItems);
            }
        }
        else
        {
            // 没有 frontmatter, 整个内容作为描述
            soul.Description = content;
        }

        return soul;
    }

    private static void SetListProperty(SoulConfig soul, string key, List<string> values)
    {
        switch (key)
        {
            case "goals": soul.Goals = values; break;
            case "rules": soul.Rules = values; break;
        }
    }

    /// <summary>
    /// 转换为 SOUL.md 格式
    /// </summary>
    private static string ToSoulMarkdown(SoulConfig soul)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"name: \"{soul.Name}\"");
        
        if (!string.IsNullOrEmpty(soul.Personality))
            sb.AppendLine($"personality: \"{soul.Personality}\"");
        
        if (soul.Goals.Count > 0)
        {
            sb.AppendLine("goals:");
            foreach (var goal in soul.Goals)
                sb.AppendLine($"  - {goal}");
        }
        
        if (soul.Rules.Count > 0)
        {
            sb.AppendLine("rules:");
            foreach (var rule in soul.Rules)
                sb.AppendLine($"  - {rule}");
        }
        
        if (soul.Preferences.Count > 0)
        {
            sb.AppendLine($"preferences: {JsonSerializer.Serialize(soul.Preferences)}");
        }
        
        sb.AppendLine("---");
        
        if (!string.IsNullOrEmpty(soul.Description))
        {
            sb.AppendLine();
            sb.AppendLine(soul.Description);
        }
        
        return sb.ToString();
    }
}
