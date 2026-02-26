using Claw0.Channels;
using Spectre.Console;

namespace Claw0.Queue;

/// <summary>
/// 投递工作者 - 后台处理队列中的消息
/// </summary>
public class DeliveryWorker : IDisposable
{
    private readonly DeliveryQueue _queue;
    private readonly ChannelRegistry _channels;
    private CancellationTokenSource? _cts;
    private Task? _workerTask;

    public bool IsRunning { get; private set; }

    public DeliveryWorker(DeliveryQueue queue, ChannelRegistry channels)
    {
        _queue = queue;
        _channels = channels;
    }

    public void Start()
    {
        if (IsRunning)
            return;

        _cts = new CancellationTokenSource();
        _workerTask = RunAsync(_cts.Token);
        IsRunning = true;
        AnsiConsole.MarkupLine("[green][DeliveryWorker] Started[/]");
    }

    public void Stop()
    {
        if (!IsRunning)
            return;

        _cts?.Cancel();
        _workerTask?.Wait(TimeSpan.FromSeconds(5));
        IsRunning = false;
        AnsiConsole.MarkupLine("[green][DeliveryWorker] Stopped[/]");
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var messages = _queue.GetPendingMessages(limit: 10);

                if (messages.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    continue;
                }

                foreach (var message in messages)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    await ProcessMessageAsync(message);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red][DeliveryWorker] Error: {ex.Message.EscapeMarkup()}[/]");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }

    private async Task ProcessMessageAsync(DeliveryMessage message)
    {
        // 标记为处理中
        _queue.MarkProcessing(message.Id);

        try
        {
            // 获取通道
            var channel = _channels.Get(message.Channel);
            if (channel == null)
            {
                _queue.MarkFailed(message.Id, $"Channel not found: {message.Channel}");
                return;
            }

            // 发送消息
            await channel.SendAsync(message.Recipient, message.Content, message.ThreadId);

            // 标记为已投递
            _queue.MarkDelivered(message.Id);
            AnsiConsole.MarkupLine($"[green][DeliveryWorker] Delivered: {message.Id} to {message.Channel}:{message.Recipient}[/]");
        }
        catch (Exception ex)
        {
            _queue.MarkFailed(message.Id, ex.Message);
            AnsiConsole.MarkupLine($"[red][DeliveryWorker] Failed: {message.Id} - {ex.Message.EscapeMarkup()}[/]");
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
