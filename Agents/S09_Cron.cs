using Claw0.Channels;
using Claw0.Common;
using Claw0.Gateway;
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
/// Section 09: Cron Scheduler
/// "The right thing at the right time"
/// 
/// 添加 Cron 调度系统, 支持三种调度类型:
/// - at: 在指定时间执行一次
/// - every: 每隔一段时间执行
/// - cron: 使用 cron 表达式调度
/// </summary>
public class S09_Cron
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

    public S09_Cron(Config config, int port = 8080)
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

        // Cron 相关接口
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

        _gatewayServer.RegisterHandler("schedule_every", async (paramsElement) =>
        {
            if (paramsElement == null) throw new ArgumentException("Missing params");
            var agentId = paramsElement.Value.GetProperty("agent_id").GetString() ?? "main";
            var name = paramsElement.Value.GetProperty("name").GetString()!;
            var prompt = paramsElement.Value.GetProperty("prompt").GetString()!;
            var interval = paramsElement.Value.GetProperty("interval").GetString()!; // e.g., "30s", "5m", "1h"
            var maxRuns = paramsElement.Value.TryGetProperty("max_runs", out var mr) ? mr.GetInt32() : (int?)null;
            
            var job = _cronService.CreateEveryJob(agentId, name, prompt, interval, maxRuns);
            return new { job };
        });

        _gatewayServer.RegisterHandler("schedule_cron", async (paramsElement) =>
        {
            if (paramsElement == null) throw new ArgumentException("Missing params");
            var agentId = paramsElement.Value.GetProperty("agent_id").GetString() ?? "main";
            var name = paramsElement.Value.GetProperty("name").GetString()!;
            var prompt = paramsElement.Value.GetProperty("prompt").GetString()!;
            var cron = paramsElement.Value.GetProperty("cron").GetString()!; // e.g., "0 */6 * * *"
            var maxRuns = paramsElement.Value.TryGetProperty("max_runs", out var mr2) ? mr2.GetInt32() : (int?)null;
            
            var job = _cronService.CreateCronJob(agentId, name, prompt, cron, maxRuns);
            return new { job };
        });

        _gatewayServer.RegisterHandler("list_jobs", async (_) =>
        {
            var jobs = _cronService.GetAllJobs();
            return new { jobs };
        });

        _gatewayServer.RegisterHandler("delete_job", async (paramsElement) =>
        {
            if (paramsElement == null) throw new ArgumentException("Missing params");
            var jobId = paramsElement.Value.GetProperty("job_id").GetString()!;
            var success = _cronService.DeleteJob(jobId);
            return new { success };
        });

        _gatewayServer.RegisterHandler("toggle_job", async (paramsElement) =>
        {
            if (paramsElement == null) throw new ArgumentException("Missing params");
            var jobId = paramsElement.Value.GetProperty("job_id").GetString()!;
            var enabled = paramsElement.Value.GetProperty("enabled").GetBoolean();
            var success = _cronService.SetJobEnabled(jobId, enabled);
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
        var cronTask = RunCronLoopAsync(cancellationToken);

        AnsiConsole.Write(new Rule("[cyan]claw0 s09: Cron Scheduler[/]").RuleStyle("grey"));
        AnsiConsole.MarkupLine("[grey]  The right thing at the right time[/]");
        AnsiConsole.MarkupLine($"[grey]  Model: {_config.ModelId.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"[grey]  Session: {currentKey.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"[grey]  Gateway: ws://localhost:{_gatewayServer.Port}/ws[/]");
        AnsiConsole.WriteLine();
        
        AnsiConsole.MarkupLine("[yellow]  Cron Commands:[/]");
        AnsiConsole.MarkupLine("    [yellow]/at[/] <name> <time> <prompt>      Schedule one-time job");
        AnsiConsole.MarkupLine("    [yellow]/every[/] <name> <interval> <prompt>  Schedule recurring job");
        AnsiConsole.MarkupLine("    [yellow]/cron[/] <name> <expression> <prompt> Schedule cron job");
        AnsiConsole.MarkupLine("    [yellow]/jobs[/]                           List all jobs");
        AnsiConsole.MarkupLine("    [yellow]/toggle[/] <job_id> <on|off>       Enable/disable job");
        AnsiConsole.MarkupLine("    [yellow]/rmjob[/] <job_id>                 Remove job");
        AnsiConsole.WriteLine();
        
        AnsiConsole.MarkupLine("[yellow]  Examples:[/]");
        AnsiConsole.MarkupLine("    [grey]/at 'reminder' '2025-12-25T09:00:00Z' 'Wish merry christmas'[/]");
        AnsiConsole.MarkupLine("    [grey]/every 'check' '5m' 'Check system status'[/]");
        AnsiConsole.MarkupLine("    [grey]/cron 'report' '0 9 * * 1' 'Weekly status report'[/]");
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

            // Cron 命令处理
            if (userInput.StartsWith("/at "))
            {
                // /at <name> <time> <prompt>
                var match = System.Text.RegularExpressions.Regex.Match(userInput, @"^/at\s+'([^']+)'\s+'([^']+)'\s+(.+)$");
                if (!match.Success)
                {
                    AnsiConsole.MarkupLine("[red]  Usage: /at 'name' '2025-12-25T09:00:00Z' 'prompt'[/]");
                    continue;
                }
                var name = match.Groups[1].Value;
                var timeStr = match.Groups[2].Value;
                var prompt = match.Groups[3].Value.Trim('\'', '"');
                
                if (!DateTime.TryParse(timeStr, out var at))
                {
                    AnsiConsole.MarkupLine("[red]  Invalid time format. Use ISO 8601 format.[/]");
                    continue;
                }
                
                var job = _cronService.CreateAtJob("main", name, prompt, at);
                AnsiConsole.MarkupLine($"[green]  Scheduled: {job.Id.EscapeMarkup()} at {at:yyyy-MM-dd HH:mm:ss}[/]");
                continue;
            }

            if (userInput.StartsWith("/every "))
            {
                // /every <name> <interval> <prompt>
                var match = System.Text.RegularExpressions.Regex.Match(userInput, @"^/every\s+'([^']+)'\s+(\d+[smhd])\s+(.+)$");
                if (!match.Success)
                {
                    AnsiConsole.MarkupLine("[red]  Usage: /every 'name' 5m 'prompt'[/]");
                    AnsiConsole.MarkupLine("[grey]  Interval: <number>[s|m|h|d] e.g., 30s, 5m, 2h, 1d[/]");
                    continue;
                }
                var name = match.Groups[1].Value;
                var interval = match.Groups[2].Value;
                var prompt = match.Groups[3].Value.Trim('\'', '"');
                
                var job = _cronService.CreateEveryJob("main", name, prompt, interval);
                AnsiConsole.MarkupLine($"[green]  Scheduled: {job.Id.EscapeMarkup()} every {interval} (next: {job.NextRun:HH:mm:ss})[/]");
                continue;
            }

            if (userInput.StartsWith("/cron "))
            {
                // /cron <name> <expression> <prompt>
                var match = System.Text.RegularExpressions.Regex.Match(userInput, @"^/cron\s+'([^']+)'\s+'([^']+)'\s+(.+)$");
                if (!match.Success)
                {
                    AnsiConsole.MarkupLine("[red]  Usage: /cron 'name' '0 9 * * 1' 'prompt'[/]");
                    continue;
                }
                var name = match.Groups[1].Value;
                var cron = match.Groups[2].Value;
                var prompt = match.Groups[3].Value.Trim('\'', '"');
                
                try
                {
                    var job = _cronService.CreateCronJob("main", name, prompt, cron);
                    if (job.NextRun == null)
                    {
                        AnsiConsole.MarkupLine("[red]  Invalid cron expression.[/]");
                        continue;
                    }
                    AnsiConsole.MarkupLine($"[green]  Scheduled: {job.Id.EscapeMarkup()} cron '{cron.EscapeMarkup()}' (next: {job.NextRun:yyyy-MM-dd HH:mm})[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]  Error: {ex.Message.EscapeMarkup()}[/]");
                }
                continue;
            }

            if (userInput == "/jobs")
            {
                var jobs = _cronService.GetAllJobs();
                if (jobs.Count == 0)
                    AnsiConsole.MarkupLine("[grey]  No scheduled jobs[/]");
                else
                {
                    var table = new Table();
                    table.Border(TableBorder.Rounded);
                    table.AddColumn("[yellow]ID[/]");
                    table.AddColumn("[yellow]Name[/]");
                    table.AddColumn("[yellow]Type[/]");
                    table.AddColumn("[yellow]Status[/]");
                    table.AddColumn("[yellow]Next Run[/]");
                    
                    foreach (var job in jobs)
                    {
                        var status = job.Enabled ? (job.IsExpired ? "[grey]expired[/]" : "[green]enabled[/]") : "[red]disabled[/]";
                        var nextRun = job.NextRun?.ToString("MM-dd HH:mm") ?? "-";
                        var typeStr = job.JobType.ToString().ToLower();
                        table.AddRow(
                            job.Id.EscapeMarkup(),
                            job.Name.EscapeMarkup(),
                            typeStr,
                            status,
                            nextRun
                        );
                    }
                    AnsiConsole.WriteLine();
                    AnsiConsole.Write(table);
                    AnsiConsole.WriteLine();
                }
                continue;
            }

            if (userInput.StartsWith("/toggle "))
            {
                var parts = userInput.Split(' ', 3);
                if (parts.Length < 3 || !(parts[2] == "on" || parts[2] == "off"))
                {
                    AnsiConsole.MarkupLine("[red]  Usage: /toggle <job_id> on|off[/]");
                    continue;
                }
                var jobId = parts[1];
                var enabled = parts[2] == "on";
                if (_cronService.SetJobEnabled(jobId, enabled))
                    AnsiConsole.MarkupLine($"[green]  Job {jobId.EscapeMarkup()} {(enabled ? "enabled" : "disabled")}[/]");
                else
                    AnsiConsole.MarkupLine($"[red]  Job not found: {jobId.EscapeMarkup()}[/]");
                continue;
            }

            if (userInput.StartsWith("/rmjob "))
            {
                var jobId = userInput[7..].Trim();
                if (_cronService.DeleteJob(jobId))
                    AnsiConsole.MarkupLine($"[green]  Deleted: {jobId.EscapeMarkup()}[/]");
                else
                    AnsiConsole.MarkupLine($"[red]  Job not found: {jobId.EscapeMarkup()}[/]");
                continue;
            }

            // 其他命令
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

        _gatewayServer.Stop();
    }

    /// <summary>
    /// Cron 执行循环
    /// </summary>
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
                        AnsiConsole.MarkupLine($"[[[grey]{now:HH:mm:ss}[/]]] Executing job: [cyan]{job.Name.EscapeMarkup()}[/]");
                        
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
                        AnsiConsole.MarkupLine($"[[[grey]{DateTime.UtcNow:HH:mm:ss}[/]]] [green]Job {job.Name.EscapeMarkup()} completed[/]");
                        
                        // 发送结果到文件通道
                        await _channelRegistry.SendAsync("file", "cron", $"[{job.Name}] {response}");
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
                        AnsiConsole.MarkupLine($"[[[grey]{DateTime.UtcNow:HH:mm:ss}[/]]] [red]Job {job.Name.EscapeMarkup()} failed: {ex.Message.EscapeMarkup()}[/]");
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[CronLoop] [red]Error: {ex.Message.EscapeMarkup()}[/]");
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
