using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Claw0.Queue;

/// <summary>
/// 投递队列 - 可靠消息投递
/// 
/// 特性:
/// - 磁盘持久化 (SQLite)
/// - 退避重试 (指数退避)
/// - 优先级支持
/// - 死信队列
/// - At-least-once 投递保证
/// </summary>
public class DeliveryQueue : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _dbPath;
    private readonly TimeSpan[] _backoffDelays = new[]
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5)
    };

    public DeliveryQueue(string workspaceDir)
    {
        var queueDir = Path.Combine(workspaceDir, ".queue");
        Directory.CreateDirectory(queueDir);
        _dbPath = Path.Combine(queueDir, "delivery.db");
        
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS messages (
                id TEXT PRIMARY KEY,
                channel TEXT NOT NULL,
                recipient TEXT NOT NULL,
                content TEXT NOT NULL,
                thread_id TEXT,
                session_key TEXT,
                created_at TEXT NOT NULL,
                scheduled_at TEXT,
                delivered_at TEXT,
                status INTEGER NOT NULL DEFAULT 0,
                attempt_count INTEGER NOT NULL DEFAULT 0,
                max_attempts INTEGER NOT NULL DEFAULT 5,
                last_error TEXT,
                next_attempt_at TEXT,
                priority INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_status ON messages(status);
            CREATE INDEX IF NOT EXISTS idx_next_attempt ON messages(next_attempt_at);
        ";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 入队消息
    /// </summary>
    public string Enqueue(string channel, string recipient, string content, 
        string? threadId = null, string? sessionKey = null, 
        DateTime? scheduledAt = null, int priority = 0)
    {
        var message = new DeliveryMessage
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Channel = channel,
            Recipient = recipient,
            Content = content,
            ThreadId = threadId,
            SessionKey = sessionKey,
            ScheduledAt = scheduledAt,
            Priority = priority
        };

        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO messages (id, channel, recipient, content, thread_id, session_key, 
                created_at, scheduled_at, status, attempt_count, max_attempts, priority)
            VALUES ($id, $channel, $recipient, $content, $threadId, $sessionKey,
                $createdAt, $scheduledAt, $status, $attemptCount, $maxAttempts, $priority)";
        
        cmd.Parameters.AddWithValue("$id", message.Id);
        cmd.Parameters.AddWithValue("$channel", message.Channel);
        cmd.Parameters.AddWithValue("$recipient", message.Recipient);
        cmd.Parameters.AddWithValue("$content", message.Content);
        cmd.Parameters.AddWithValue("$threadId", (object?)message.ThreadId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sessionKey", (object?)message.SessionKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$createdAt", message.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$scheduledAt", (object?)message.ScheduledAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", (int)message.Status);
        cmd.Parameters.AddWithValue("$attemptCount", message.AttemptCount);
        cmd.Parameters.AddWithValue("$maxAttempts", message.MaxAttempts);
        cmd.Parameters.AddWithValue("$priority", message.Priority);
        
        cmd.ExecuteNonQuery();
        return message.Id;
    }

    /// <summary>
    /// 获取待投递的消息 (按优先级和时间排序)
    /// </summary>
    public List<DeliveryMessage> GetPendingMessages(int limit = 10)
    {
        var now = DateTime.UtcNow.ToString("O");
        var messages = new List<DeliveryMessage>();

        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT * FROM messages 
            WHERE status IN (0, 3) 
            AND attempt_count < max_attempts
            AND (scheduled_at IS NULL OR scheduled_at <= $now)
            AND (next_attempt_at IS NULL OR next_attempt_at <= $now)
            ORDER BY priority DESC, created_at ASC
            LIMIT $limit";
        
        cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            messages.Add(ReadMessage(reader));
        }

        return messages;
    }

    /// <summary>
    /// 标记消息为处理中
    /// </summary>
    public void MarkProcessing(string messageId)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE messages 
            SET status = $status, attempt_count = attempt_count + 1
            WHERE id = $id";
        
        cmd.Parameters.AddWithValue("$status", (int)DeliveryStatus.Processing);
        cmd.Parameters.AddWithValue("$id", messageId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 标记消息投递成功
    /// </summary>
    public void MarkDelivered(string messageId)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE messages 
            SET status = $status, delivered_at = $deliveredAt, last_error = NULL
            WHERE id = $id";
        
        cmd.Parameters.AddWithValue("$status", (int)DeliveryStatus.Delivered);
        cmd.Parameters.AddWithValue("$deliveredAt", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", messageId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 标记消息投递失败, 并计算下次重试时间
    /// </summary>
    public void MarkFailed(string messageId, string error)
    {
        // 获取当前尝试次数
        var getCmd = _connection.CreateCommand();
        getCmd.CommandText = "SELECT attempt_count, max_attempts FROM messages WHERE id = $id";
        getCmd.Parameters.AddWithValue("$id", messageId);
        
        using var reader = getCmd.ExecuteReader();
        if (!reader.Read()) return;
        
        var attemptCount = reader.GetInt32(0);
        var maxAttempts = reader.GetInt32(1);
        reader.Close();

        // 计算退避延迟
        var backoffIndex = Math.Min(attemptCount - 1, _backoffDelays.Length - 1);
        var nextAttempt = DateTime.UtcNow.Add(_backoffDelays[backoffIndex]);
        
        // 如果超过最大尝试次数, 移入死信队列
        var status = attemptCount >= maxAttempts ? DeliveryStatus.DeadLetter : DeliveryStatus.Failed;

        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE messages 
            SET status = $status, last_error = $error, next_attempt_at = $nextAttempt
            WHERE id = $id";
        
        cmd.Parameters.AddWithValue("$status", (int)status);
        cmd.Parameters.AddWithValue("$error", error);
        cmd.Parameters.AddWithValue("$nextAttempt", attemptCount >= maxAttempts ? DBNull.Value : nextAttempt.ToString("O"));
        cmd.Parameters.AddWithValue("$id", messageId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 获取队列统计
    /// </summary>
    public QueueStats GetStats()
    {
        var stats = new QueueStats();

        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT status, COUNT(*) FROM messages GROUP BY status";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var status = (DeliveryStatus)reader.GetInt32(0);
            var count = reader.GetInt32(1);
            
            switch (status)
            {
                case DeliveryStatus.Pending: stats.PendingCount = count; break;
                case DeliveryStatus.Processing: stats.ProcessingCount = count; break;
                case DeliveryStatus.Delivered: stats.DeliveredCount = count; break;
                case DeliveryStatus.Failed: stats.FailedCount = count; break;
                case DeliveryStatus.DeadLetter: stats.DeadLetterCount = count; break;
            }
        }

        return stats;
    }

    /// <summary>
    /// 获取死信队列消息
    /// </summary>
    public List<DeliveryMessage> GetDeadLetters(int limit = 100)
    {
        var messages = new List<DeliveryMessage>();

        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"SELECT * FROM messages WHERE status = $status ORDER BY created_at DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$status", (int)DeliveryStatus.DeadLetter);
        cmd.Parameters.AddWithValue("$limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            messages.Add(ReadMessage(reader));
        }

        return messages;
    }

    /// <summary>
    /// 重试死信
    /// </summary>
    public bool RetryDeadLetter(string messageId)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE messages 
            SET status = $status, attempt_count = 0, last_error = NULL, next_attempt_at = NULL
            WHERE id = $id AND status = $deadLetter";
        
        cmd.Parameters.AddWithValue("$status", (int)DeliveryStatus.Pending);
        cmd.Parameters.AddWithValue("$id", messageId);
        cmd.Parameters.AddWithValue("$deadLetter", (int)DeliveryStatus.DeadLetter);
        
        return cmd.ExecuteNonQuery() > 0;
    }

    private static DeliveryMessage ReadMessage(SqliteDataReader reader)
    {
        return new DeliveryMessage
        {
            Id = reader.GetString(0),
            Channel = reader.GetString(1),
            Recipient = reader.GetString(2),
            Content = reader.GetString(3),
            ThreadId = reader.IsDBNull(4) ? null : reader.GetString(4),
            SessionKey = reader.IsDBNull(5) ? null : reader.GetString(5),
            CreatedAt = DateTime.Parse(reader.GetString(6)),
            ScheduledAt = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7)),
            DeliveredAt = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8)),
            Status = (DeliveryStatus)reader.GetInt32(9),
            AttemptCount = reader.GetInt32(10),
            MaxAttempts = reader.GetInt32(11),
            LastError = reader.IsDBNull(12) ? null : reader.GetString(12),
            NextAttemptAt = reader.IsDBNull(13) ? null : DateTime.Parse(reader.GetString(13)),
            Priority = reader.GetInt32(14)
        };
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}

/// <summary>
/// 队列统计
/// </summary>
public class QueueStats
{
    public int PendingCount { get; set; }
    public int ProcessingCount { get; set; }
    public int DeliveredCount { get; set; }
    public int FailedCount { get; set; }
    public int DeadLetterCount { get; set; }
    public int TotalCount => PendingCount + ProcessingCount + DeliveredCount + FailedCount + DeadLetterCount;
}
