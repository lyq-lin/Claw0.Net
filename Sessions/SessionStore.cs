using System.Text.Json;
using System.Text.Json.Serialization;

namespace Claw0.Sessions;

/// <summary>
/// 会话元数据
/// </summary>
public class SessionMetadata
{
    public required string SessionKey { get; set; }
    public required string SessionId { get; set; }
    public required string CreatedAt { get; set; }
    public required string UpdatedAt { get; set; }
    public int MessageCount { get; set; }
    public required string TranscriptFile { get; set; }
}

/// <summary>
/// Transcript 条目类型
/// </summary>
public class TranscriptEntry
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }
    
    [JsonPropertyName("ts")]
    public string? Timestamp { get; set; }
    
    [JsonPropertyName("content")]
    public JsonElement? Content { get; set; }
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("tool_use_id")]
    public string? ToolUseId { get; set; }
    
    [JsonPropertyName("input")]
    public JsonElement? Input { get; set; }
    
    [JsonPropertyName("output")]
    public string? Output { get; set; }
    
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    
    [JsonPropertyName("key")]
    public string? Key { get; set; }
    
    [JsonPropertyName("created")]
    public string? Created { get; set; }
}

/// <summary>
/// SessionStore -- 会话持久化的核心
/// 
/// 设计要点:
/// 1. sessions.json 是一个索引文件, 记录所有会话的元数据
/// 2. transcripts/ 目录下, 每个会话一个 .jsonl 文件, 只追加
/// 3. 加载会话时, 从 JSONL 重建 messages 数组
/// </summary>
public class SessionStore
{
    private readonly string _storePath;
    private readonly string _transcriptDir;
    private readonly Dictionary<string, SessionMetadata> _index;

    public SessionStore(string workspaceDir)
    {
        var sessionsDir = Path.Combine(workspaceDir, ".sessions");
        _storePath = Path.Combine(sessionsDir, "sessions.json");
        _transcriptDir = Path.Combine(sessionsDir, "transcripts");

        Directory.CreateDirectory(sessionsDir);
        Directory.CreateDirectory(_transcriptDir);

        _index = LoadIndex();
    }

    private Dictionary<string, SessionMetadata> LoadIndex()
    {
        if (!File.Exists(_storePath))
            return new Dictionary<string, SessionMetadata>();

        try
        {
            var json = File.ReadAllText(_storePath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, SessionMetadata>>(json);
            return dict ?? new Dictionary<string, SessionMetadata>();
        }
        catch
        {
            return new Dictionary<string, SessionMetadata>();
        }
    }

    private void SaveIndex()
    {
        var json = JsonSerializer.Serialize(_index, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_storePath, json);
    }

    public SessionMetadata CreateSession(string sessionKey)
    {
        var sessionId = Guid.NewGuid().ToString("N")[..12];
        var now = DateTime.UtcNow.ToString("O");
        var transcriptFile = $"{sessionKey.Replace(":", "_")}_{sessionId}.jsonl";

        var metadata = new SessionMetadata
        {
            SessionKey = sessionKey,
            SessionId = sessionId,
            CreatedAt = now,
            UpdatedAt = now,
            MessageCount = 0,
            TranscriptFile = transcriptFile
        };

        _index[sessionKey] = metadata;
        SaveIndex();

        // 写入 JSONL 的第一行: 会话元数据
        AppendTranscript(sessionKey, new TranscriptEntry
        {
            Type = "session",
            Id = sessionId,
            Key = sessionKey,
            Created = now
        });

        return metadata;
    }

    public (SessionMetadata Metadata, List<Claw0.Common.Message> History) LoadSession(string sessionKey)
    {
        if (!_index.TryGetValue(sessionKey, out var metadata))
        {
            metadata = CreateSession(sessionKey);
            return (metadata, new List<Claw0.Common.Message>());
        }

        var history = RebuildHistory(metadata.TranscriptFile);
        return (metadata, history);
    }

    public void SaveTurn(string sessionKey, string userMsg, List<object> assistantBlocks)
    {
        if (!_index.ContainsKey(sessionKey))
            CreateSession(sessionKey);

        var now = DateTime.UtcNow.ToString("O");

        // 记录用户消息
        AppendTranscript(sessionKey, new TranscriptEntry
        {
            Type = "user",
            Content = JsonSerializer.SerializeToElement(userMsg),
            Timestamp = now
        });

        // 记录助手回复的每个 block
        foreach (var block in assistantBlocks)
        {
            var blockType = block switch
            {
                Claw0.Common.TextContent => "text",
                Claw0.Common.ToolUseContent => "tool_use",
                _ => "unknown"
            };

            if (block is Claw0.Common.TextContent textContent)
            {
                AppendTranscript(sessionKey, new TranscriptEntry
                {
                    Type = "assistant",
                    Content = JsonSerializer.SerializeToElement(textContent.Text),
                    Timestamp = now
                });
            }
            else if (block is Claw0.Common.ToolUseContent toolContent)
            {
                AppendTranscript(sessionKey, new TranscriptEntry
                {
                    Type = "tool_use",
                    Name = toolContent.Name,
                    ToolUseId = toolContent.Id,
                    Input = JsonSerializer.Deserialize<JsonElement>(toolContent.Input?.ToString() ?? "{}"),
                    Timestamp = now
                });
            }
        }

        // 更新索引元数据
        var meta = _index[sessionKey];
        meta.UpdatedAt = now;
        meta.MessageCount++;
        SaveIndex();
    }

    public void SaveToolResult(string sessionKey, string toolUseId, string output)
    {
        var now = DateTime.UtcNow.ToString("O");
        AppendTranscript(sessionKey, new TranscriptEntry
        {
            Type = "tool_result",
            ToolUseId = toolUseId,
            Output = output,
            Timestamp = now
        });
    }

    private void AppendTranscript(string sessionKey, TranscriptEntry entry)
    {
        if (!_index.TryGetValue(sessionKey, out var metadata))
            return;

        var filepath = Path.Combine(_transcriptDir, metadata.TranscriptFile);
        var line = JsonSerializer.Serialize(entry);
        File.AppendAllText(filepath, line + Environment.NewLine);
    }

    private List<Claw0.Common.Message> RebuildHistory(string transcriptFile)
    {
        var filepath = Path.Combine(_transcriptDir, transcriptFile);
        if (!File.Exists(filepath))
            return new List<Claw0.Common.Message>();

        var messages = new List<Claw0.Common.Message>();
        var pendingToolUses = new List<Claw0.Common.ContentBase>();

        foreach (var line in File.ReadLines(filepath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var entry = JsonSerializer.Deserialize<TranscriptEntry>(line);
                if (entry == null) continue;

                switch (entry.Type)
                {
                    case "session":
                        // 会话元数据行, 跳过
                        break;

                    case "user":
                        // 如果有未处理的 tool_use, 先刷出 assistant 消息
                        if (pendingToolUses.Count > 0)
                        {
                            messages.Add(new Claw0.Common.Message(
                                Claw0.Common.RoleType.Assistant, 
                                pendingToolUses));
                            pendingToolUses = new List<Claw0.Common.ContentBase>();
                        }
                        
                        if (entry.Content.HasValue)
                        {
                            var content = entry.Content.Value;
                            if (content.ValueKind == JsonValueKind.String)
                            {
                                messages.Add(new Claw0.Common.Message(
                                    Claw0.Common.RoleType.User, 
                                    content.GetString()!));
                            }
                            else if (content.ValueKind == JsonValueKind.Array)
                            {
                                // tool_result 数组
                                var toolResults = new List<Claw0.Common.ContentBase>();
                                foreach (var item in content.EnumerateArray())
                                {
                                    if (item.TryGetProperty("type", out var typeProp) && 
                                        typeProp.GetString() == "tool_result")
                                    {
                                        toolResults.Add(new Claw0.Common.ToolResultContent
                                        {
                                            ToolUseId = item.GetProperty("tool_use_id").GetString()!,
                                            Content = item.GetProperty("content").GetString()!
                                        });
                                    }
                                }
                                if (toolResults.Count > 0)
                                    messages.Add(new Claw0.Common.Message(
                                        Claw0.Common.RoleType.User, 
                                        toolResults));
                            }
                        }
                        break;

                    case "assistant":
                        if (pendingToolUses.Count > 0)
                        {
                            messages.Add(new Claw0.Common.Message(
                                Claw0.Common.RoleType.Assistant, 
                                pendingToolUses));
                            pendingToolUses = new List<Claw0.Common.ContentBase>();
                        }
                        
                        if (entry.Content.HasValue)
                            messages.Add(new Claw0.Common.Message(
                                Claw0.Common.RoleType.Assistant, 
                                entry.Content.Value.GetString()!));
                        break;

                    case "tool_use":
                        pendingToolUses.Add(new Claw0.Common.ToolUseContent
                        {
                            Id = entry.ToolUseId!,
                            Name = entry.Name!,
                            Input = entry.Input.ToString()!
                        });
                        break;

                    case "tool_result":
                        // tool_result 需要先刷出 pending tool_use 作为 assistant 消息
                        if (pendingToolUses.Count > 0)
                        {
                            messages.Add(new Claw0.Common.Message(
                                Claw0.Common.RoleType.Assistant, 
                                pendingToolUses));
                            pendingToolUses = new List<Claw0.Common.ContentBase>();
                        }
                        messages.Add(new Claw0.Common.Message(
                            Claw0.Common.RoleType.User, 
                            new List<Claw0.Common.ContentBase>
                            {
                                new Claw0.Common.ToolResultContent
                                {
                                    ToolUseId = entry.ToolUseId!,
                                    Content = entry.Output!
                                }
                            }));
                        break;
                }
            }
            catch { /* 忽略解析错误 */ }
        }

        // 刷出最后的 pending tool_use
        if (pendingToolUses.Count > 0)
        {
            messages.Add(new Claw0.Common.Message(
                Claw0.Common.RoleType.Assistant, 
                pendingToolUses));
        }

        return messages;
    }

    public List<SessionMetadata> ListSessions()
    {
        var sessions = _index.Values.ToList();
        sessions.Sort((a, b) => string.Compare(b.UpdatedAt, a.UpdatedAt, StringComparison.Ordinal));
        return sessions;
    }

    public bool SessionExists(string sessionKey) => _index.ContainsKey(sessionKey);

    public bool DeleteSession(string sessionKey)
    {
        if (!_index.TryGetValue(sessionKey, out var metadata))
            return false;

        _index.Remove(sessionKey);
        SaveIndex();

        var filepath = Path.Combine(_transcriptDir, metadata.TranscriptFile);
        if (File.Exists(filepath))
            File.Delete(filepath);

        return true;
    }
}
