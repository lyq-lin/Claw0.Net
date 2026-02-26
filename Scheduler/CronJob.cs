using NCrontab;

namespace Claw0.Scheduler;

/// <summary>
/// Cron 作业类型
/// </summary>
public enum CronJobType
{
    /// <summary>
    /// 一次性任务 (在指定时间执行)
    /// </summary>
    At,
    
    /// <summary>
    /// 周期性任务 (每隔一段时间执行)
    /// </summary>
    Every,
    
    /// <summary>
    /// Cron 表达式任务
    /// </summary>
    Cron
}

/// <summary>
/// Cron 作业
/// </summary>
public class CronJob
{
    public required string Id { get; set; }
    public required string AgentId { get; set; }
    public required string Name { get; set; }
    public required string Prompt { get; set; }
    public required CronJobType JobType { get; set; }
    
    /// <summary>
    /// Cron 表达式 或 时间间隔
    /// </summary>
    public required string Schedule { get; set; }
    
    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// 下次执行时间
    /// </summary>
    public DateTime? NextRun { get; set; }
    
    /// <summary>
    /// 上次执行时间
    /// </summary>
    public DateTime? LastRun { get; set; }
    
    /// <summary>
    /// 执行次数
    /// </summary>
    public int RunCount { get; set; }
    
    /// <summary>
    /// 最大执行次数 (null = 无限制)
    /// </summary>
    public int? MaxRuns { get; set; }
    
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// 是否已过期 (at 类型用过一次后过期)
    /// </summary>
    public bool IsExpired => JobType == CronJobType.At && RunCount > 0;

    /// <summary>
    /// 计算下次执行时间
    /// </summary>
    public DateTime? CalculateNextRun(DateTime from)
    {
        if (!Enabled || IsExpired)
            return null;

        if (MaxRuns.HasValue && RunCount >= MaxRuns.Value)
            return null;

        return JobType switch
        {
            CronJobType.At => DateTime.TryParse(Schedule, out var atTime) ? atTime : null,
            CronJobType.Every => ParseEvery(Schedule, from),
            CronJobType.Cron => GetNextCronOccurrence(Schedule, from),
            _ => null
        };
    }

    private static DateTime? ParseEvery(string schedule, DateTime from)
    {
        // 格式: "30s", "5m", "2h", "1d"
        if (schedule.Length < 2)
            return null;

        var value = schedule[..^1];
        var unit = schedule[^1];

        if (!double.TryParse(value, out var amount))
            return null;

        var interval = unit switch
        {
            's' => TimeSpan.FromSeconds(amount),
            'm' => TimeSpan.FromMinutes(amount),
            'h' => TimeSpan.FromHours(amount),
            'd' => TimeSpan.FromDays(amount),
            _ => TimeSpan.Zero
        };

        if (interval == TimeSpan.Zero)
            return null;

        return from + interval;
    }

    private static DateTime? GetNextCronOccurrence(string cronExpression, DateTime from)
    {
        try
        {
            var schedule = CrontabSchedule.Parse(cronExpression);
            return schedule.GetNextOccurrence(from);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Cron 作业执行结果
/// </summary>
public class CronJobResult
{
    public required string JobId { get; set; }
    public required bool Success { get; set; }
    public string? Response { get; set; }
    public string? Error { get; set; }
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
}
