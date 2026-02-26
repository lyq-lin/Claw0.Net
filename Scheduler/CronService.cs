using System.Text.Json;

namespace Claw0.Scheduler;

/// <summary>
/// Cron 调度服务 - 管理定时任务
/// 
/// 支持三种调度类型:
/// - at: 在指定时间执行一次
/// - every: 每隔一段时间执行
/// - cron: 使用 cron 表达式调度
/// </summary>
public class CronService
{
    private readonly List<CronJob> _jobs = new();
    private readonly string _storePath;
    private readonly Dictionary<string, CronJobResult> _lastResults = new();

    public CronService(string workspaceDir)
    {
        var schedulerDir = Path.Combine(workspaceDir, ".scheduler");
        Directory.CreateDirectory(schedulerDir);
        _storePath = Path.Combine(schedulerDir, "jobs.jsonl");
        LoadJobs();
    }

    private void LoadJobs()
    {
        if (!File.Exists(_storePath))
            return;

        foreach (var line in File.ReadLines(_storePath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var job = JsonSerializer.Deserialize<CronJob>(line);
                if (job != null && !job.IsExpired)
                    _jobs.Add(job);
            }
            catch { /* 忽略解析错误 */ }
        }
    }

    private void SaveJobs()
    {
        using var writer = new StreamWriter(_storePath);
        foreach (var job in _jobs.Where(j => !j.IsExpired))
        {
            var json = JsonSerializer.Serialize(job);
            writer.WriteLine(json);
        }
    }

    /// <summary>
    /// 创建 at 类型任务 (一次性)
    /// </summary>
    public CronJob CreateAtJob(string agentId, string name, string prompt, DateTime at)
    {
        var job = new CronJob
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            AgentId = agentId,
            Name = name,
            Prompt = prompt,
            JobType = CronJobType.At,
            Schedule = at.ToString("O"),
            NextRun = at,
            MaxRuns = 1
        };

        _jobs.Add(job);
        SaveJobs();
        return job;
    }

    /// <summary>
    /// 创建 every 类型任务 (周期性)
    /// </summary>
    public CronJob CreateEveryJob(string agentId, string name, string prompt, string interval, int? maxRuns = null)
    {
        var job = new CronJob
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            AgentId = agentId,
            Name = name,
            Prompt = prompt,
            JobType = CronJobType.Every,
            Schedule = interval,
            MaxRuns = maxRuns
        };

        job.NextRun = job.CalculateNextRun(DateTime.UtcNow);
        _jobs.Add(job);
        SaveJobs();
        return job;
    }

    /// <summary>
    /// 创建 cron 类型任务
    /// </summary>
    public CronJob CreateCronJob(string agentId, string name, string prompt, string cronExpression, int? maxRuns = null)
    {
        var job = new CronJob
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            AgentId = agentId,
            Name = name,
            Prompt = prompt,
            JobType = CronJobType.Cron,
            Schedule = cronExpression,
            MaxRuns = maxRuns
        };

        job.NextRun = job.CalculateNextRun(DateTime.UtcNow);
        _jobs.Add(job);
        SaveJobs();
        return job;
    }

    /// <summary>
    /// 删除任务
    /// </summary>
    public bool DeleteJob(string jobId)
    {
        var job = _jobs.FirstOrDefault(j => j.Id == jobId);
        if (job == null)
            return false;

        _jobs.Remove(job);
        SaveJobs();
        return true;
    }

    /// <summary>
    /// 启用/禁用任务
    /// </summary>
    public bool SetJobEnabled(string jobId, bool enabled)
    {
        var job = _jobs.FirstOrDefault(j => j.Id == jobId);
        if (job == null)
            return false;

        job.Enabled = enabled;
        if (enabled && job.NextRun == null)
            job.NextRun = job.CalculateNextRun(DateTime.UtcNow);
        SaveJobs();
        return true;
    }

    /// <summary>
    /// 获取所有任务
    /// </summary>
    public IReadOnlyList<CronJob> GetAllJobs()
    {
        return _jobs.OrderBy(j => j.NextRun ?? DateTime.MaxValue).ToList();
    }

    /// <summary>
    /// 获取 Agent 的所有任务
    /// </summary>
    public IReadOnlyList<CronJob> GetJobsForAgent(string agentId)
    {
        return _jobs.Where(j => j.AgentId == agentId).OrderBy(j => j.NextRun ?? DateTime.MaxValue).ToList();
    }

    /// <summary>
    /// 获取到期需要执行的任务
    /// </summary>
    public List<CronJob> GetDueJobs(DateTime now)
    {
        return _jobs.Where(j => j.Enabled && !j.IsExpired && j.NextRun <= now).ToList();
    }

    /// <summary>
    /// 标记任务已执行
    /// </summary>
    public void MarkExecuted(CronJob job, CronJobResult result)
    {
        job.LastRun = DateTime.UtcNow;
        job.RunCount++;
        
        // 计算下次执行时间
        job.NextRun = job.CalculateNextRun(DateTime.UtcNow);
        
        _lastResults[job.Id] = result;
        SaveJobs();
    }

    /// <summary>
    /// 获取上次执行结果
    /// </summary>
    public CronJobResult? GetLastResult(string jobId)
    {
        return _lastResults.TryGetValue(jobId, out var result) ? result : null;
    }
}
