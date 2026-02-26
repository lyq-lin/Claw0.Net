namespace Claw0.Routing;

/// <summary>
/// 路由绑定 - 将消息路由到正确的 Agent
/// </summary>
public class Binding
{
    /// <summary>
    /// 绑定 ID
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Agent ID
    /// </summary>
    public required string AgentId { get; set; }

    /// <summary>
    /// 通道 ID (如 cli, telegram, discord)
    /// </summary>
    public required string Channel { get; set; }

    /// <summary>
    /// 发送者标识
    /// </summary>
    public required string Peer { get; set; }

    /// <summary>
    /// 优先级 (数值越小优先级越高)
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 匹配条件: 通道
    /// </summary>
    public bool Matches(string channel, string peer)
    {
        return Channel == channel && Peer == peer;
    }
}

/// <summary>
/// 绑定查询结果
/// </summary>
public class BindingResult
{
    public required string AgentId { get; set; }
    public required string SessionKey { get; set; }
    public Binding? Binding { get; set; }
}
