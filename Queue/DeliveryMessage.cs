namespace Claw0.Queue;

/// <summary>
/// 投递消息状态
/// </summary>
public enum DeliveryStatus
{
    Pending,
    Processing,
    Delivered,
    Failed,
    DeadLetter
}

/// <summary>
/// 投递消息
/// </summary>
public class DeliveryMessage
{
    public required string Id { get; set; }
    public required string Channel { get; set; }
    public required string Recipient { get; set; }
    public required string Content { get; set; }
    public string? ThreadId { get; set; }
    public string? SessionKey { get; set; }
    
    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// 计划投递时间 (支持延迟投递)
    /// </summary>
    public DateTime? ScheduledAt { get; set; }
    
    /// <summary>
    /// 实际投递时间
    /// </summary>
    public DateTime? DeliveredAt { get; set; }
    
    /// <summary>
    /// 状态
    /// </summary>
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    
    /// <summary>
    /// 尝试次数
    /// </summary>
    public int AttemptCount { get; set; }
    
    /// <summary>
    /// 最大尝试次数
    /// </summary>
    public int MaxAttempts { get; set; } = 5;
    
    /// <summary>
    /// 上次错误信息
    /// </summary>
    public string? LastError { get; set; }
    
    /// <summary>
    /// 下次尝试时间 (用于退避重试)
    /// </summary>
    public DateTime? NextAttemptAt { get; set; }
    
    /// <summary>
    /// 优先级 (数值越大优先级越高)
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// 是否准备好投递
    /// </summary>
    public bool IsReadyToDeliver(DateTime now)
    {
        if (Status != DeliveryStatus.Pending && Status != DeliveryStatus.Failed)
            return false;
        
        if (AttemptCount >= MaxAttempts)
            return false;
        
        if (ScheduledAt.HasValue && ScheduledAt > now)
            return false;
        
        if (NextAttemptAt.HasValue && NextAttemptAt > now)
            return false;
        
        return true;
    }
}

/// <summary>
/// 投递结果
/// </summary>
public class DeliveryResult
{
    public required bool Success { get; set; }
    public string? Error { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
