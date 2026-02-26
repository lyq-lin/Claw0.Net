using Claw0.Channels;
using Claw0.Common;
using Claw0.Gateway;
using Claw0.Routing;
using Claw0.Sessions;
using Claw0.Soul;
using Claw0.Tools;
using Spectre.Console;
using System.Text;
using System.Text.Json;

namespace Claw0.Agents;

/// <summary>
/// Section 08: Heartbeat
/// "Not just reactive - proactive"
/// 
/// 添加心跳系统, 让 Agent 可以主动执行任务.
/// </summary>
public class S08_Heartbeat
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
    private readonly HeartbeatRunner _heartbeatRunner;

    public S08_Heartbeat(Config config, int port = 8080)
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
        _heartbeatRunner = new HeartbeatRunner();
        
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
            return new { success = true, response, session_key = route.SessionKey, agent_id = route.AgentId };
        });

        _gatewayServer.RegisterHandler("create_binding", async (paramsElement) =>
        {
            if (paramsElement == null) throw new ArgumentException("Missing params");
            var agentId = paramsElement.Value.GetProperty("agent_id").GetString()!;
            var channel = paramsElement.Value.GetProperty("channel").GetString()!;
            var peer = paramsElement.Value.GetProperty("peer").GetString()!;
            var priority = paramsElement.Value.TryGetProperty("priority", out var p) ? p.GetInt32() : 100;
            
            var binding = _router.CreateBinding(agentId, channel, peer, priority);
            _soulStore.GetOrCreateSoul(agentId);
            return new { binding };
        });

        _gatewayServer.RegisterHandler("schedule_heartbeat", async (paramsElement) =>
        {
            if (paramsElement == null) throw new ArgumentException("Missing params");
            var agentId = paramsElement.Value.GetProperty("agent_id").GetString() ?? "main";
            var intervalSeconds = paramsElement.Value.TryGetProperty("interval_seconds", out var i) ? i.GetInt32() : 60;
            var prompt = paramsElement.Value.GetProperty("prompt").GetString() ?? "Check system status and report any issues.";
            
            var jobId = _heartbeatRunner.Schedule(agentId, TimeSpan.FromSeconds(intervalSeconds), async () =>
            {
                var soul = _soulStore.GetOrCreateSoul(agentId);
                var sessionKey = $"{agentId}:heartbeat:auto";
                _sessionStore.CreateSession(sessionKey);
                
                var response = await AgentLoop(prompt, sessionKey, soul);
                AnsiConsole.MarkupLine($"[[[grey]{DateTime.UtcNow:HH:mm:ss}[/]]] Heartbeat [[cyan]{agentId.EscapeMarkup()}[/]]: {response.EscapeMarkup()}");
                
                // 发送结果到文件通道
                await _channelRegistry.SendAsync("file", "system", $"Heartbeat [{agentId}]: {response}");
            });
            
            return new { job_id = jobId, interval_seconds = intervalSeconds };
        });

        _gatewayServer.RegisterHandler("cancel_heartbeat", async (paramsElement) =>
        {
            if (paramsElement == null) throw new ArgumentException("Missing params");
            var jobId = paramsElement.Value.GetProperty("job_id").GetString()!;
            var success = _heartbeatRunner.Cancel(jobId);
            return new { success };
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

        var gatewayTask = _gatewayServer.StartAsync(cancellationToken);
        var heartbeatTask = _heartbeatRunner.RunAsync(cancellationToken);

        AnsiConsole.Write(new Rule("[cyan]claw0 s08: Heartbeat[/]").RuleStyle("grey"));
        AnsiConsole.MarkupLine("[grey]  Not just reactive - proactive[/]");
        AnsiConsole.MarkupLine($"[grey]  Model: {_config.ModelId.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"[grey]  Session: {currentKey.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"[grey]  Gateway: ws://localhost:{_gatewayServer.Port}/ws[/]");
        AnsiConsole.WriteLine();
        
        AnsiConsole.MarkupLine("[yellow]  Heartbeat Commands:[/]");
        AnsiConsole.MarkupLine("    [yellow]/heartbeat[/] <interval> <prompt>  Schedule heartbeat");
        AnsiConsole.MarkupLine("    [yellow]/heartbeats[/]                     List active heartbeats");
        AnsiConsole.MarkupLine("    [yellow]/cancel[/] <job_id>                Cancel heartbeat");
        AnsiConsole.WriteLine();
        
        AnsiConsole.MarkupLine("[yellow]  Commands:[/]");
        AnsiConsole.MarkupLine("    [yellow]/new[/]            Create new session");
        AnsiConsole.MarkupLine("    [yellow]/sessions[/]       List all sessions");
        AnsiConsole.MarkupLine("    [yellow]/soul[/]           Show soul config");
        AnsiConsole.MarkupLine("    [yellow]/remember[/]       Add memory");
        AnsiConsole.MarkupLine("    [yellow]/quit[/]           Exit");
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

            // Heartbeat 命令
            if (userInput.StartsWith("/heartbeat "))
            {
                var parts = userInput.Split(' ', 3);
                if (parts.Length < 3 || !int.TryParse(parts[1], out var interval))
                {
                    AnsiConsole.MarkupLine("[red]  Usage: /heartbeat <interval_seconds> <prompt>[/]");
                    continue;
                }
                var prompt = parts[2];
                var jobId = _heartbeatRunner.Schedule("main", TimeSpan.FromSeconds(interval), async () =>
                {
                    var sessionKey = $"main:heartbeat:{Guid.NewGuid():N}";
                    _sessionStore.CreateSession(sessionKey);
                    var response = await AgentLoop(prompt, sessionKey, soul);
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[[[grey]{DateTime.UtcNow:HH:mm:ss}[/]]] Heartbeat: {response.EscapeMarkup()}");
                    AnsiConsole.WriteLine();
                });
                AnsiConsole.MarkupLine($"[green]  Scheduled heartbeat: {jobId.EscapeMarkup()} (every {interval}s)[/]");
                continue;
            }

            if (userInput == "/heartbeats")
            {
                var jobs = _heartbeatRunner.ListJobs();
                if (jobs.Count == 0)
                    AnsiConsole.MarkupLine("[grey]  No active heartbeats[/]");
                else
                {
                    AnsiConsole.MarkupLine($"[grey]  {jobs.Count} active heartbeat(s):[/]");
                    foreach (var job in jobs)
                        AnsiConsole.MarkupLine($"[grey]    {job.EscapeMarkup()}[/]");
                }
                continue;
            }

            if (userInput.StartsWith("/cancel "))
            {
                var jobId = userInput[8..].Trim();
                if (_heartbeatRunner.Cancel(jobId))
                    AnsiConsole.MarkupLine($"[green]  Cancelled: {jobId.EscapeMarkup()}[/]");
                else
                    AnsiConsole.MarkupLine($"[red]  Job not found: {jobId.EscapeMarkup()}[/]");
                continue;
            }

            // 其他命令 (复用之前的功能)
            if (userInput == "/soul")
            {
                AnsiConsole.MarkupLine($"[grey]  Name: {soul.Name.EscapeMarkup()}[/]");
                AnsiConsole.MarkupLine($"[grey]  Personality: {soul.Personality.EscapeMarkup()}[/]");
                continue;
            }

            if (userInput.StartsWith("/remember "))
            {
                var memory = userInput[10..];
                _memoryStore.AddMemory(memory, currentKey, importance: 1.0f);
                AnsiConsole.MarkupLine("[green]  Memory added.[/]");
                continue;
            }

            if (userInput == "/new")
            {
                currentKey = GenerateSessionKey(peer: $"user_{DateTime.UtcNow:HHmmss}");
                _sessionStore.CreateSession(currentKey);
                AnsiConsole.MarkupLine($"[green]  New session: {currentKey.EscapeMarkup()}[/]");
                continue;
            }

            if (userInput == "/sessions")
            {
                var sessions = _sessionStore.ListSessions();
                AnsiConsole.MarkupLine($"[grey]  {sessions.Count} session(s)[/]");
                continue;
            }

            // 处理普通消息
            try
            {
                var route = _router.Resolve("cli", "user");
                var agentSoul = _soulStore.GetOrCreateSoul(route.AgentId);
                var response = await AgentLoop(userInput, route.SessionKey, agentSoul);
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Panel(response.EscapeMarkup())
                    .Header(new PanelHeader(agentSoul.Name.EscapeMarkup(), Justify.Left))
                    .Border(BoxBorder.Rounded)
                    .BorderStyle(new Style(Color.Cyan1)));
                AnsiConsole.WriteLine();
            }
            catch (Exception exc)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[red]  Error: {exc.Message.EscapeMarkup()}[/]");
                AnsiConsole.WriteLine();
            }
        }

        _heartbeatRunner.Stop();
        _gatewayServer.Stop();
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

/// <summary>
/// 心跳运行器 - 管理定时任务
/// </summary>
public class HeartbeatRunner
{
    private readonly Dictionary<string, HeartbeatJob> _jobs = new();
    private CancellationTokenSource? _cts;

    public string Schedule(string agentId, TimeSpan interval, Func<Task> action)
    {
        var jobId = Guid.NewGuid().ToString("N")[..8];
        _jobs[jobId] = new HeartbeatJob
        {
            Id = jobId,
            AgentId = agentId,
            Interval = interval,
            Action = action,
            NextRun = DateTime.UtcNow.Add(interval)
        };
        return jobId;
    }

    public bool Cancel(string jobId)
    {
        return _jobs.Remove(jobId);
    }

    public List<string> ListJobs()
    {
        return _jobs.Select(j => $"{j.Value.Id}: {j.Value.AgentId} (every {j.Value.Interval.TotalSeconds}s)").ToList();
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        while (!_cts.Token.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var jobsToRun = _jobs.Values.Where(j => j.NextRun <= now).ToList();

            foreach (var job in jobsToRun)
            {
                try
                {
                    await job.Action();
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[[[grey]{now:HH:mm:ss}[/]]] [red]Heartbeat error [[{job.AgentId.EscapeMarkup()}]]: {ex.Message.EscapeMarkup()}[/]");
                }
                job.NextRun = now.Add(job.Interval);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), _cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
    }
}

public class HeartbeatJob
{
    public required string Id { get; set; }
    public required string AgentId { get; set; }
    public required TimeSpan Interval { get; set; }
    public required Func<Task> Action { get; set; }
    public DateTime NextRun { get; set; }
}
