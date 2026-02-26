using Spectre.Console;

namespace Claw0.Channels;

/// <summary>
/// CLI 通道 - 从控制台接收输入
/// </summary>
public class CliChannel : IChannel
{
    private readonly Queue<InboundMessage> _messageQueue = new();
    private readonly string _agentId;

    public string Id => "cli";
    public int MaxTextLength => 4000;

    public CliChannel(string agentId = "main")
    {
        _agentId = agentId;
    }

    public void EnqueueInput(string text, string sender = "user")
    {
        _messageQueue.Enqueue(new InboundMessage
        {
            Channel = Id,
            Sender = sender,
            Text = text,
            Timestamp = DateTime.UtcNow,
            ThreadId = $"{_agentId}:{Id}:{sender}"
        });
    }

    public Task<InboundMessage?> ReceiveAsync()
    {
        if (_messageQueue.TryDequeue(out var message))
            return Task.FromResult<InboundMessage?>(message);

        return Task.FromResult<InboundMessage?>(null);
    }

    public Task SendAsync(string recipient, string text, string? threadId = null)
    {
        var chunks = ChunkText(text);

        foreach (var chunk in chunks)
        {
            var panel = new Panel(chunk.EscapeMarkup())
            {
                Header = new PanelHeader("[cyan]CLI[/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Cyan1)
            };

            AnsiConsole.Write(panel);

            AnsiConsole.WriteLine();
        }

        return Task.CompletedTask;
    }

    public List<string> ChunkText(string text)
    {
        var chunks = new List<string>();

        if (text.Length <= MaxTextLength)
        {
            chunks.Add(text);

            return chunks;
        }

        // 尝试在段落边界分割
        var paragraphs = text.Split('\n');
        
        var currentChunk = "";

        foreach (var para in paragraphs)
        {
            if ((currentChunk + para).Length > MaxTextLength)
            {
                if (!string.IsNullOrEmpty(currentChunk))
                    chunks.Add(currentChunk.TrimEnd());
                currentChunk = para + "\n";
            }
            else
            {
                currentChunk += para + "\n";
            }
        }

        if (!string.IsNullOrEmpty(currentChunk))
        { 
            chunks.Add(currentChunk.TrimEnd());
        }

        return chunks;
    }
}
