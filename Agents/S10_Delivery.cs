using Claw0.Channels;
using Claw0.Common;
using Claw0.Gateway;
using Claw0.Queue;
using Claw0.Routing;
using Claw0.Scheduler;
using Claw0.Sessions;
using Claw0.Soul;
using Claw0.Tools;
using Spectre.Console;
using System.Text;
using System.Text.Json;

namespace Claw0.Agents;

/// <summary>
/// Section 10: Delivery Queue
/// "Messages never get lost"
/// 
/// 添加可靠投递队列:
/// - 磁盘持久化 (SQLite)
/// - 退避重试
/// - 死信队列
/// - At-least-once 投递保证
/// </summary>
public class S10_Delivery
{
    private readonly Config _config;
    private readonly DeepSeekClient _client;
    private readonly ToolRegistry _toolRegistry;
    private readonly SessionStore _sessionStore;
    private readonly ChannelRegistry _channelRegistry;
    private readonly GatewayServer _gatewayServer;
    private readonly Router _router;
    private readonly SoulStore _soulStore;
    private readonly MemoryStore _memoryStore;
    private readonly CronService _cronService;
    private readonly DeliveryQueue _deliveryQueue;
    private readonly DeliveryWorker _deliveryWorker;

    public S10_Delivery(Config config, int port = 8080)
    {
        _config = config;
        _client = new DeepSeekClient(config.DeepSeekApiKey, config.DeepSeekBaseUrl);
        _toolRegistry = new ToolRegistry(config.WorkspaceDir);
        _sessionStore = new SessionStore(config.WorkspaceDir);
        _channelRegistry = new ChannelRegistry();
        _channelRegistry.Register(new CliChannel());
        _channelRegistry.Register(new FileChannel(config.WorkspaceDir));
        
        _router = new Router(config.WorkspaceDir);
        _soulStore = new SoulStore(config.WorkspaceDir);
        _memoryStore = new MemoryStore(config.WorkspaceDir);
        _cronService = new CronService(config.WorkspaceDir);
        _deliveryQueue = new DeliveryQueue(config.WorkspaceDir);
        _deliveryWorker = new DeliveryWorker(_deliveryQueue, _channelRegistry);
        
        _gatewayServer = new GatewayServer(port);
        RegisterGatewayHandlers();
        
        Directory.CreateDirectory(config.WorkspaceDir);
    }

    private void RegisterGatewayHandlers()
    {
        _gatewayServer.RegisterHandler("send_message", async (paramsElement) =>
        {
            if (paramsElement == null) throw new ArgumentException("Missing params");
            var channel = paramsElement.Value.GetProperty("channel").GetString() ?? "gateway";
            var peer = paramsElement.Value.GetProperty("peer").GetString() ?? "user";
            var message = paramsElement.Value.GetProperty("message").GetString()!;

            var route = _router.Resolve(channel, peer);
            var soul = _soulStore.GetOrCreateSoul(route.AgentId);
            _sessionStore.LoadSession(route.SessionKey);

            var response = await AgentLoop(message, route.SessionKey, soul);
            
            // 通过队列投递响应
            _deliveryQueue.Enqueue(channel, peer, response, sessionKey: route.SessionKey);
            
            return new { success = true, queued = true, session_key = route.SessionKey, agent_id = route.AgentId };
        });

        _gatewayServer.RegisterHandler("queue_message", async (paramsElement) =>
        {
            if (paramsElement == null) throw new ArgumentException("Missing params");
            var channel = paramsElement.Value.GetProperty("channel").GetString()!;
            var recipient = paramsElement.Value.GetProperty("recipient").GetString()!;
            var content = paramsElement.Value.GetProperty("content").GetString()!;
            var priority = paramsElement.Value.TryGetProperty("priority", out var p) ? p.GetInt32() : 0;
            
            var messageId = _deliveryQueue.Enqueue(channel, recipient, content, priority: priority);
            return new { message_id = messageId };
        });

        _gatewayServer.RegisterHandler("queue_stats", async (_) =>
        {
            var stats = _deliveryQueue.GetStats();
            return new { stats };
        });

        _gatewayServer.RegisterHandler("list_dead_letters", async (_) =>
        {
            var deadLetters = _deliveryQueue.GetDeadLetters();
            return new { dead_letters = deadLetters };
        });

        _gatewayServer.RegisterHandler("retry_dead_letter", async (paramsElement) =>
        {
            if (paramsElement == null) throw new ArgumentException("Missing params");
            var messageId = paramsElement.Value.GetProperty("message_id").GetString()!;
            var success = _deliveryQueue.RetryDeadLetter(messageId);
            return new { success };
        });

        _gatewayServer.RegisterHandler("schedule_at", async (paramsElement) =>
        {
            if (paramsElement == null) throw new ArgumentException("Missing params");
            var agentId = paramsElement.Value.GetProperty("agent_id").GetString() ?? "main";
            var name = paramsElement.Value.GetProperty("name").GetString()!;
            var prompt = paramsElement.Value.GetProperty("prompt").GetString()!;
            var at = DateTime.Parse(paramsElement.Value.GetProperty("at").GetString()!);
            
            var job = _cronService.CreateAtJob(agentId, name, prompt, at);
            return new { job };
        });

        _gatewayServer.RegisterHandler("list_jobs", async (_) =>
        {
            var jobs = _cronService.GetAllJobs();
            return new { jobs };
        });

        _gatewayServer.RegisterHandler("list_bindings", async (_) =>
        {
            var bindings = _router.GetAllBindings();
            return new { bindings };
        });

        _gatewayServer.RegisterHandler("list_sessions", async (_) =>
        {
            var sessions = _sessionStore.ListSessions();
            return new { sessions };
        });
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var currentKey = GenerateSessionKey();
        _sessionStore.CreateSession(currentKey);
        _router.CreateBinding("main", "cli", "user", 10);
        var soul = _soulStore.GetOrCreateSoul("main");

        // 启动服务
        var gatewayTask = _gatewayServer.StartAsync(cancellationToken);
        var cronTask = RunCronLoopAsync(cancellationToken);
        _deliveryWorker.Start();

        AnsiConsole.Write(new Rule("[cyan]claw0 s10: Delivery Queue[/]").RuleStyle("grey"));
        AnsiConsole.MarkupLine("[grey]  Messages never get lost[/]");
        AnsiConsole.MarkupLine($"[grey]  Model: {_config.ModelId.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"[grey]  Session: {currentKey.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"[grey]  Gateway: ws://localhost:{_gatewayServer.Port}/ws[/]");
        AnsiConsole.WriteLine();
        
        AnsiConsole.MarkupLine("[yellow]  Queue Commands:[/]");
        AnsiConsole.MarkupLine("    [yellow]/qstats[/]              Show queue statistics");
        AnsiConsole.MarkupLine("    [yellow]/deadletters[/]         List dead letter messages");
        AnsiConsole.MarkupLine("    [yellow]/retry[/] <msg_id>      Retry dead letter");
        AnsiConsole.MarkupLine("    [yellow]/sendq[/] <ch> <msg>    Send via queue");
        AnsiConsole.WriteLine();
        
        AnsiConsole.MarkupLine("[yellow]  Commands:[/]");
        AnsiConsole.MarkupLine("    [yellow]/jobs[/]                List scheduled jobs");
        AnsiConsole.MarkupLine("    [yellow]/sessions[/]            List sessions");
        AnsiConsole.MarkupLine("    [yellow]/new[/]                 Create new session");
        AnsiConsole.MarkupLine("    [yellow]/quit[/]                Exit");
        AnsiConsole.Write(new Rule().RuleStyle("grey"));

        while (!cancellationToken.IsCancellationRequested)
        {
            string? userInput;
            try
            {
                userInput = AnsiConsole.Prompt(
                    new TextPrompt<string>($"[{soul.Name.EscapeMarkup()}] > ")
                        .PromptStyle("cyan"));
            }
            catch
            {
                break;
            }

            if (string.IsNullOrEmpty(userInput))
                continue;

            if (userInput == "/quit")
                break;

            // 队列命令
            if (userInput == "/qstats")
            {
                var stats = _deliveryQueue.GetStats();
                var table = new Table();
                table.Border(TableBorder.Rounded);
                table.Title("[cyan]Queue Statistics[/]");
                table.AddColumn("[yellow]Status[/]");
                table.AddColumn("[yellow]Count[/]");
                
                table.AddRow("Pending", $"[blue]{stats.PendingCount}[/]");
                table.AddRow("Processing", $"[yellow]{stats.ProcessingCount}[/]");
                table.AddRow("Delivered", $"[green]{stats.DeliveredCount}[/]");
                table.AddRow("Failed", $"[red]{stats.FailedCount}[/]");
                table.AddRow("Dead Letter", $"[grey]{stats.DeadLetterCount}[/]");
                table.AddRow("Total", stats.TotalCount.ToString());
                
                AnsiConsole.WriteLine();
                AnsiConsole.Write(table);
                AnsiConsole.WriteLine();
                continue;
            }

            if (userInput == "/deadletters")
            {
                var deadLetters = _deliveryQueue.GetDeadLetters(10);
                if (deadLetters.Count == 0)
                    AnsiConsole.MarkupLine("[grey]  No dead letters[/]");
                else
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[yellow]  {deadLetters.Count} dead letter(s):[/]");
                    foreach (var msg in deadLetters)
                    {
                        AnsiConsole.MarkupLine($"[grey]    [{msg.Id.EscapeMarkup()}] {msg.Channel.EscapeMarkup()}:{msg.Recipient.EscapeMarkup()}[/]");
                        AnsiConsole.MarkupLine($"[red]      Error: {msg.LastError?.EscapeMarkup() ?? "Unknown"}[/]");
                    }
                    AnsiConsole.WriteLine();
                }
                continue;
            }

            if (userInput.StartsWith("/retry "))
            {
                var msgId = userInput[7..].Trim();
                if (_deliveryQueue.RetryDeadLetter(msgId))
                    AnsiConsole.MarkupLine($"[green]  Retry queued: {msgId.EscapeMarkup()}[/]");
                else
                    AnsiConsole.MarkupLine($"[red]  Message not found: {msgId.EscapeMarkup()}[/]");
                continue;
            }

            if (userInput.StartsWith("/sendq "))
            {
                var parts = userInput.Split(' ', 3);
                if (parts.Length < 3)
                {
                    AnsiConsole.MarkupLine("[red]  Usage: /sendq <channel> <message>[/]");
                    continue;
                }
                var channel = parts[1];
                var message = parts[2];
                var msgId = _deliveryQueue.Enqueue(channel, "user", message);
                AnsiConsole.MarkupLine($"[green]  Queued: {msgId.EscapeMarkup()}[/]");
                continue;
            }

            // 其他命令
            if (userInput == "/jobs")
            {
                var jobs = _cronService.GetAllJobs();
                AnsiConsole.MarkupLine($"[grey]  {jobs.Count} job(s)[/]");
                continue;
            }

            if (userInput == "/sessions")
            {
                var sessions = _sessionStore.ListSessions();
                AnsiConsole.MarkupLine($"[grey]  {sessions.Count} session(s)[/]");
                continue;
            }

            if (userInput == "/new")
            {
                currentKey = GenerateSessionKey(peer: $"user_{DateTime.UtcNow:HHmmss}");
                _sessionStore.CreateSession(currentKey);
                AnsiConsole.MarkupLine($"[green]  New session: {currentKey.EscapeMarkup()}[/]");
                continue;
            }

            // 处理普通消息 (响应通过队列投递)
            try
            {
                var route = _router.Resolve("cli", "user");
                var agentSoul = _soulStore.GetOrCreateSoul(route.AgentId);
                var response = await AgentLoop(userInput, route.SessionKey, agentSoul);
                
                // 通过队列投递响应
                _deliveryQueue.Enqueue("cli", "user", response, sessionKey: route.SessionKey);
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[green][Response queued for delivery][/]");
                AnsiConsole.WriteLine();
            }
            catch (Exception exc)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[red]  Error: {exc.Message.EscapeMarkup()}[/]");
                AnsiConsole.WriteLine();
            }
        }

        _deliveryWorker.Stop();
        _gatewayServer.Stop();
    }

    private async Task RunCronLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var dueJobs = _cronService.GetDueJobs(now);

                foreach (var job in dueJobs)
                {
                    try
                    {
                        var soul = _soulStore.GetOrCreateSoul(job.AgentId);
                        var sessionKey = $"{job.AgentId}:cron:{job.Id}";
                        _sessionStore.CreateSession(sessionKey);
                        
                        var response = await AgentLoop(job.Prompt, sessionKey, soul);
                        
                        var result = new CronJobResult
                        {
                            JobId = job.Id,
                            Success = true,
                            Response = response
                        };
                        
                        _cronService.MarkExecuted(job, result);
                        
                        // 通过队列投递结果
                        _deliveryQueue.Enqueue("file", "cron", $"[{job.Name}] {response}");
                    }
                    catch (Exception ex)
                    {
                        var result = new CronJobResult
                        {
                            JobId = job.Id,
                            Success = false,
                            Error = ex.Message
                        };
                        _cronService.MarkExecuted(job, result);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }
    }

    private async Task<string> AgentLoop(string userInput, string sessionKey, SoulConfig soul)
    {
        var (_, messages) = _sessionStore.LoadSession(sessionKey);
        var relevantMemories = _memoryStore.Retrieve(userInput, sessionKey, 3);
        var memoryContext = relevantMemories.Count > 0
            ? "\n\nRelevant memories:\n" + string.Join("\n", relevantMemories.Select(m => $"- {m.Content}"))
            : "";

        messages.Add(new Message(RoleType.User, userInput + memoryContext));
        var allAssistantBlocks = new List<ContentBase>();

        while (true)
        {
            var tools = ToolRegistry.ConvertToDeepSeekTools(_toolRegistry.Definitions);
            var parameters = new MessageParameters
            {
                Model = _config.ModelId,
                MaxTokens = 4096,
                System = [new SystemMessage(soul.ToSystemPrompt())],
                Messages = messages,
                Tools = tools,
                ToolChoice = new ToolChoice { Type = ToolChoiceType.Auto }
            };

            var response = await _client.ChatCompletionAsync(parameters);
            allAssistantBlocks.AddRange(response.Content);

            var toolUseBlocks = response.Content.OfType<ToolUseContent>().ToList();
            if (response.StopReason == "tool_calls" && toolUseBlocks.Any())
            {
                messages.Add(new Message(RoleType.Assistant, response.Content));
                var toolResults = new List<ContentBase>();
                foreach (var toolBlock in toolUseBlocks)
                {
                    var inputDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(toolBlock.Input?.ToString() ?? "{}")!;
                    var result = _toolRegistry.Execute(toolBlock.Name, inputDict);
                    toolResults.Add(new ToolResultContent { ToolUseId = toolBlock.Id, Content = result });
                    _sessionStore.SaveToolResult(sessionKey, toolBlock.Id, result);
                }
                messages.Add(new Message(RoleType.User, toolResults));
                continue;
            }

            var finalText = ExtractText(response);
            _memoryStore.AddMemory($"User: {userInput}\nAssistant: {finalText}", sessionKey, importance: 0.5f);
            _sessionStore.SaveTurn(sessionKey, userInput, allAssistantBlocks.Cast<object>().ToList());
            return finalText;
        }
    }

    private static string ExtractText(DeepSeekResponse response)
    {
        var sb = new StringBuilder();
        foreach (var content in response.Content)
            if (content is TextContent textContent)
                sb.Append(textContent.Text);
        return sb.ToString();
    }

    private static string GenerateSessionKey(string agentId = "main", string channel = "cli", string peer = "user")
    {
        return $"{agentId}:{channel}:{peer}";
    }

    private static void PrintInfo(string text)
    {
        AnsiConsole.MarkupLine($"[grey]{text.EscapeMarkup()}[/]");
    }
}
