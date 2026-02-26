namespace Claw0.Channels;

/// <summary>
/// 入站消息 - 所有通道的消息标准化为此格式
/// </summary>
public class InboundMessage
{
    public required string Channel { get; set; }
    public required string Sender { get; set; }
    public required string Text { get; set; }
    public List<string>? MediaUrls { get; set; }
    public string? ThreadId { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 通道插件接口
/// </summary>
public interface IChannel
{
    /// <summary>
    /// 通道唯一标识
    /// </summary>
    string Id { get; }

    /// <summary>
    /// 单条消息最大字符数
    /// </summary>
    int MaxTextLength { get; }

    /// <summary>
    /// 非阻塞轮询, 返回 InboundMessage 或 null
    /// </summary>
    Task<InboundMessage?> ReceiveAsync();

    /// <summary>
    /// 发送文本 (自动分块)
    /// </summary>
    Task SendAsync(string recipient, string text, string? threadId = null);

    /// <summary>
    /// 按通道限制拆分长文本
    /// </summary>
    List<string> ChunkText(string text);
}
