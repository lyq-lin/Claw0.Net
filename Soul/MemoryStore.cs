using System.Text.Json;
using System.Text.RegularExpressions;

namespace Claw0.Soul;

/// <summary>
/// 记忆条目
/// </summary>
public class Memory
{
    public required string Id { get; set; }
    public required string Content { get; set; }
    public required string SessionKey { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<string> Tags { get; set; } = new();
    public float? Importance { get; set; }
}

/// <summary>
/// Memory Store - 简单的关键字搜索记忆系统
/// 
/// 注意: 这是教学版本的简化实现.
/// 生产环境应使用向量数据库 (如 SQLite-vec, Chroma, Pinecone)
/// </summary>
public class MemoryStore
{
    private readonly List<Memory> _memories = new();
    private readonly string _storePath;
    private const int MaxMemories = 1000;
    private const int RetrievalLimit = 5;

    public MemoryStore(string workspaceDir)
    {
        var memoryDir = Path.Combine(workspaceDir, ".memory");
        Directory.CreateDirectory(memoryDir);
        _storePath = Path.Combine(memoryDir, "memories.jsonl");
        LoadMemories();
    }

    private void LoadMemories()
    {
        if (!File.Exists(_storePath))
            return;

        foreach (var line in File.ReadLines(_storePath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var memory = JsonSerializer.Deserialize<Memory>(line);
                if (memory != null)
                    _memories.Add(memory);
            }
            catch { /* 忽略解析错误 */ }
        }
    }

    private void SaveMemories()
    {
        using var writer = new StreamWriter(_storePath);
        foreach (var memory in _memories)
        {
            var json = JsonSerializer.Serialize(memory);
            writer.WriteLine(json);
        }
    }

    /// <summary>
    /// 添加记忆
    /// </summary>
    public Memory AddMemory(string content, string sessionKey, List<string>? tags = null, float? importance = null)
    {
        var memory = new Memory
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Content = content,
            SessionKey = sessionKey,
            Tags = tags ?? new List<string>(),
            Importance = importance
        };

        _memories.Add(memory);

        // 限制内存大小
        if (_memories.Count > MaxMemories)
        {
            // 移除最旧的记忆
            _memories.RemoveAt(0);
        }

        SaveMemories();
        return memory;
    }

    /// <summary>
    /// 检索相关记忆 (简单关键字匹配)
    /// </summary>
    public List<Memory> Retrieve(string query, string? sessionKey = null, int limit = RetrievalLimit)
    {
        var keywords = ExtractKeywords(query);
        var scoredMemories = new List<(Memory Memory, float Score)>();

        foreach (var memory in _memories)
        {
            // 可选: 限制在同一会话
            if (sessionKey != null && memory.SessionKey != sessionKey)
                continue;

            float score = 0;
            var memoryText = memory.Content.ToLower();

            // 关键字匹配得分
            foreach (var keyword in keywords)
            {
                if (memoryText.Contains(keyword))
                    score += 1;
            }

            // 标签匹配加分
            foreach (var tag in memory.Tags)
            {
                if (query.ToLower().Contains(tag.ToLower()))
                    score += 0.5f;
            }

            // 重要性加权
            if (memory.Importance.HasValue)
                score *= (1 + memory.Importance.Value);

            if (score > 0)
                scoredMemories.Add((memory, score));
        }

        return scoredMemories
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .Select(x => x.Memory)
            .ToList();
    }

    /// <summary>
    /// 获取会话的所有记忆
    /// </summary>
    public List<Memory> GetSessionMemories(string sessionKey)
    {
        return _memories.Where(m => m.SessionKey == sessionKey).ToList();
    }

    /// <summary>
    /// 删除记忆
    /// </summary>
    public bool DeleteMemory(string memoryId)
    {
        var memory = _memories.FirstOrDefault(m => m.Id == memoryId);
        if (memory == null)
            return false;

        _memories.Remove(memory);
        SaveMemories();
        return true;
    }

    /// <summary>
    /// 获取最近添加的记忆
    /// </summary>
    public List<Memory> GetRecentMemories(int count = 10)
    {
        return _memories
            .OrderByDescending(m => m.CreatedAt)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// 提取查询关键字
    /// </summary>
    private static List<string> ExtractKeywords(string text)
    {
        // 简单分词: 移除停用词, 提取有意义的关键词
        var stopWords = new HashSet<string> { "the", "a", "an", "is", "are", "was", "were", 
            "be", "been", "being", "have", "has", "had", "do", "does", "did", "will", 
            "would", "could", "should", "may", "might", "must", "shall", "can", "need",
            "的", "了", "是", "在", "我", "有", "和", "就", "不", "人", "都", "一", 
            "一个", "上", "也", "很", "到", "说", "要", "去", "你", "会", "着", "没有",
            "看", "好", "自己", "这" };

        // 匹配单词
        var words = Regex.Matches(text.ToLower(), @"\b\w+\b")
            .Cast<Match>()
            .Select(m => m.Value)
            .Where(w => w.Length > 2 && !stopWords.Contains(w))
            .ToList();

        return words;
    }

    /// <summary>
    /// 记忆数量
    /// </summary>
    public int Count => _memories.Count;
}
