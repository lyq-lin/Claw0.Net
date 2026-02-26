namespace Claw0.Channels;

/// <summary>
/// 文件通道 - 通过文件模拟 webhook 接收/发送
/// </summary>
public class FileChannel : IChannel
{
    private readonly string _inboxPath;
    private readonly string _outboxPath;
    private long _lastPosition;

    public string Id => "file";
    public int MaxTextLength => 2000;

    public FileChannel(string workspaceDir)
    {
        var channelsDir = Path.Combine(workspaceDir, ".channels");
        Directory.CreateDirectory(channelsDir);
        _inboxPath = Path.Combine(channelsDir, "file_inbox.txt");
        _outboxPath = Path.Combine(channelsDir, "file_outbox.txt");
        _lastPosition = 0;

        // 创建文件如果不存在
        if (!File.Exists(_inboxPath))
            File.WriteAllText(_inboxPath, "# FileChannel inbox - one message per line\n# format: sender|message\n");
        if (!File.Exists(_outboxPath))
            File.WriteAllText(_outboxPath, "# FileChannel outbox\n");
    }

    public Task<InboundMessage?> ReceiveAsync()
    {
        var info = new FileInfo(_inboxPath);
        if (info.Length <= _lastPosition)
            return Task.FromResult<InboundMessage?>(null);

        using var stream = new FileStream(_inboxPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(_lastPosition, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        var line = reader.ReadLine();
        _lastPosition = stream.Position;

        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
            return Task.FromResult<InboundMessage?>(null);

        var parts = line.Split('|', 2);
        if (parts.Length < 2)
            return Task.FromResult<InboundMessage?>(null);

        return Task.FromResult<InboundMessage?>(new InboundMessage
        {
            Channel = Id,
            Sender = parts[0].Trim(),
            Text = parts[1].Trim(),
            Timestamp = DateTime.UtcNow,
            ThreadId = $"main:{Id}:{parts[0].Trim()}"
        });
    }

    public async Task SendAsync(string recipient, string text, string? threadId = null)
    {
        var chunks = ChunkText(text);
        foreach (var chunk in chunks)
        {
            var line = $"[{DateTime.UtcNow:O}] TO {recipient}: {chunk}{Environment.NewLine}";
            await File.AppendAllTextAsync(_outboxPath, line);
        }
    }

    public List<string> ChunkText(string text)
    {
        var chunks = new List<string>();
        if (text.Length <= MaxTextLength)
        {
            chunks.Add(text);
            return chunks;
        }

        // 简单按长度分割
        for (int i = 0; i < text.Length; i += MaxTextLength)
        {
            var length = Math.Min(MaxTextLength, text.Length - i);
            chunks.Add(text.Substring(i, length));
        }
        return chunks;
    }
}
