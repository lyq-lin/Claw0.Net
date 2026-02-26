using Claw0.Channels;
using Claw0.Common;
using Claw0.Gateway;
using Claw0.Routing;
using Claw0.Sessions;
using Claw0.Tools;
using Spectre.Console;
using System.Text;
using System.Text.Json;

namespace Claw0.Agents;

/// <summary>
/// Section 06: Routing
/// "Every message finds its home"
/// 
/// 添加路由系统, 支持多 Agent 绑定和优先级路由.
/// </summary>
public class S06_Routing
{
    private readonly Config _config;
    private readonly DeepSeekClient _client;
    private readonly ToolRegistry _toolRegistry;
    private readonly SessionStore _sessionStore;
    private readonly ChannelRegistry _channelRegistry;
    private readonly GatewayServer _gatewayServer;
    private readonly Router _router;

    private const string SystemPrompt = """
        You are a helpful assistant with access to local tools.
        You can read files and list directories on the user's machine.
        Keep answers concise. When using tools, explain what you found.
        """;

    public S06_Routing(Config config, int port = 8080)
    {
        _config = config;
        _client = new DeepSeekClient(config.DeepSeekApiKey, config.DeepSeekBaseUrl);
        _toolRegistry = new ToolRegistry(config.WorkspaceDir);
        _sessionStore = new SessionStore(config.WorkspaceDir);
        _channelRegistry = new ChannelRegistry();
        _channelRegistry.Register(new CliChannel());
        _channelRegistry.Register(new FileChannel(config.WorkspaceDir));
        
        _router = new Router(config.WorkspaceDir);
        _gatewayServer = new GatewayServer(port);
        RegisterGatewayHandlers();
        
        Directory.CreateDirectory(config.WorkspaceDir);
    }

    private void RegisterGatewayHandlers()
    {
        // 发送消息到 Agent (支持路由)
        _gatewayServer.RegisterHandler("send_message", async (paramsElement) =>
        {
            if (paramsElement == null)
                throw new ArgumentException("Missing params");

            var channel = paramsElement.Value.GetProperty("channel").GetString() ?? "gateway";
            var peer = paramsElement.Value.GetProperty("peer").GetString() ?? "user";
            var message = paramsElement.Value.GetProperty("message").GetString()!;

            // 路由解析
            var route = _router.Resolve(channel, peer);
            
            // 确保会话存在
            _sessionStore.LoadSession(route.SessionKey);

            var response = await AgentLoop(message, route.SessionKey);
            return new { 
                success = true, 
                response, 
                session_key = route.SessionKey,
                agent_id = route.AgentId,
                routed = route.Binding != null
            };
        });

        // 创建绑定
        _gatewayServer.RegisterHandler("create_binding", async (paramsElement) =>
        {
            if (paramsElement == null)
                throw new ArgumentException("Missing params");

            var agentId = paramsElement.Value.GetProperty("agent_id").GetString()!;
            var channel = paramsElement.Value.GetProperty("channel").GetString()!;
            var peer = paramsElement.Value.GetProperty("peer").GetString()!;
            var priority = paramsElement.Value.TryGetProperty("priority", out var p) ? p.GetInt32() : 100;

            var binding = _router.CreateBinding(agentId, channel, peer, priority);
            return new { binding };
        });

        // 列出绑定
        _gatewayServer.RegisterHandler("list_bindings", async (_) =>
        {
            var bindings = _router.GetAllBindings();
            return new { bindings };
        });

        // 删除绑定
        _gatewayServer.RegisterHandler("delete_binding", async (paramsElement) =>
        {
            if (paramsElement == null)
                throw new ArgumentException("Missing params");

            var bindingId = paramsElement.Value.GetProperty("binding_id").GetString()!;
            var success = _router.RemoveBinding(bindingId);
            return new { success };
        });

        // 其他原有方法...
        _gatewayServer.RegisterHandler("list_sessions", async (_) =>
        {
            var sessions = _sessionStore.ListSessions();
            return new { sessions };
        });

        _gatewayServer.RegisterHandler("create_session", async (paramsElement) =>
        {
            var agentId = paramsElement?.TryGetProperty("agent_id", out var a) == true ? a.GetString() : "main";
            var channel = paramsElement?.TryGetProperty("channel", out var c) == true ? c.GetString() : "gateway";
            var peer = paramsElement?.TryGetProperty("peer", out var p) == true ? p.GetString() : Guid.NewGuid().ToString("N")[..8];
            
            var sessionKey = $"{agentId}:{channel}:{peer}";
            var metadata = _sessionStore.CreateSession(sessionKey);
            return new { session_key = sessionKey, metadata };
        });
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var currentKey = GenerateSessionKey();
        _sessionStore.CreateSession(currentKey);

        // 创建默认绑定
        _router.CreateBinding("main", "cli", "user", priority: 10);

        var gatewayTask = _gatewayServer.StartAsync(cancellationToken);

        AnsiConsole.Write(new Rule("[grey]claw0 s06: Routing[/]") { Justification = Justify.Left });
        AnsiConsole.MarkupLine($"[grey]  Model: {_config.ModelId}[/]");
        AnsiConsole.MarkupLine($"[grey]  Session: {currentKey}[/]");
        AnsiConsole.MarkupLine($"[grey]  Gateway: ws://localhost:{_gatewayServer.Port}/ws[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]  Routing Commands:[/]");
        AnsiConsole.MarkupLine("[yellow]    /bind <agent> <channel> <peer> [priority][/]  Create binding");
        AnsiConsole.MarkupLine("[yellow]    /bindings[/]                                    List bindings");
        AnsiConsole.MarkupLine("[yellow]    /unbind <id>[/]                                 Remove binding");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]  Commands:[/]");
        AnsiConsole.MarkupLine("[yellow]    /new[/]            Create new session");
        AnsiConsole.MarkupLine("[yellow]    /sessions[/]       List all sessions");
        AnsiConsole.MarkupLine("[yellow]    /history[/]        Show current session history");
        AnsiConsole.MarkupLine("[yellow]    /quit[/]           Exit");
        AnsiConsole.Write(new Rule() { Style = "grey" });
        AnsiConsole.WriteLine();

        while (!cancellationToken.IsCancellationRequested)
        {
            string? userInput;
            try
            {
                AnsiConsole.Markup("[yellow][cli] > [/]");
                userInput = Console.ReadLine()?.Trim();
            }
            catch
            {
                break;
            }

            if (string.IsNullOrEmpty(userInput))
                continue;

            if (userInput == "/quit")
                break;

            // 路由命令
            if (userInput.StartsWith("/bind "))
            {
                var parts = userInput.Split(' ', 5);
                if (parts.Length < 4)
                {
                    AnsiConsole.MarkupLine("[red]  Usage: /bind <agent> <channel> <peer> [priority][/]");
                    continue;
                }
                var agentId = parts[1];
                var channel = parts[2];
                var peer = parts[3];
                var priority = parts.Length > 4 && int.TryParse(parts[4], out var p) ? p : 100;
                
                var binding = _router.CreateBinding(agentId, channel, peer, priority);
                AnsiConsole.MarkupLine($"[green]  Created binding: {binding.Id} ({agentId} -> {channel}:{peer}, priority={priority})[/]");
                continue;
            }

            if (userInput == "/bindings")
            {
                var bindings = _router.GetAllBindings();
                AnsiConsole.MarkupLine($"[grey]  {bindings.Count} binding(s):[/]");
                foreach (var b in bindings)
                {
                    var status = b.Enabled ? "[green]enabled[/]" : "[red]disabled[/]";
                    AnsiConsole.MarkupLine($"[grey]  [{b.Id}] {b.AgentId} <- {b.Channel}:{b.Peer} (priority={b.Priority}, {status})[/]");
                }
                continue;
            }

            if (userInput.StartsWith("/unbind "))
            {
                var parts = userInput.Split(' ', 2);
                if (parts.Length < 2)
                {
                    AnsiConsole.MarkupLine("[red]  Usage: /unbind <binding_id>[/]");
                    continue;
                }
                var bindingId = parts[1];
                if (_router.RemoveBinding(bindingId))
                    AnsiConsole.MarkupLine($"[green]  Removed binding: {bindingId}[/]");
                else
                    AnsiConsole.MarkupLine($"[red]  Binding not found: {bindingId}[/]");
                continue;
            }

            if (userInput == "/new")
            {
                currentKey = GenerateSessionKey(peer: $"user_{DateTime.UtcNow:HHmmss}");
                _sessionStore.CreateSession(currentKey);
                AnsiConsole.MarkupLine($"[green]  New session: {currentKey}[/]");
                continue;
            }

            if (userInput == "/sessions")
            {
                var sessions = _sessionStore.ListSessions();
                AnsiConsole.MarkupLine($"[grey]  {sessions.Count} session(s):[/]");
                foreach (var meta in sessions)
                {
                    var marker = meta.SessionKey == currentKey ? " [green]*[/]" : "";
                    AnsiConsole.MarkupLine($"[grey]  {Markup.Escape(FormatSessionSummary(meta))}{marker}[/]");
                }
                continue;
            }

            if (userInput == "/history")
            {
                PrintSessionHistory(currentKey);
                continue;
            }

            // 处理普通消息 (带路由)
            try
            {
                var route = _router.Resolve("cli", "user");
                AnsiConsole.MarkupLine($"[grey]  [routed to {route.AgentId}][/]");
                
                var response = await AgentLoop(userInput, route.SessionKey);
                // 使用 Panel 显示 Assistant 回复
                AnsiConsole.Write(new Panel(Markup.Escape(response))
                    .Header("Assistant")
                    .Border(BoxBorder.Rounded)
                    .BorderStyle(new Style(Color.Cyan1)));
                AnsiConsole.WriteLine();
            }
            catch (Exception exc)
            {
                AnsiConsole.MarkupLine($"[red]\n  Error: {Markup.Escape(exc.Message)}\n[/]");
            }
        }

        _gatewayServer.Stop();
    }

    private async Task<string> AgentLoop(string userInput, string sessionKey)
    {
        var (_, messages) = _sessionStore.LoadSession(sessionKey);
        messages.Add(new Message(RoleType.User, userInput));

        var allAssistantBlocks = new List<ContentBase>();

        while (true)
        {
            var tools = ToolRegistry.ConvertToDeepSeekTools(_toolRegistry.Definitions);
            
            var parameters = new MessageParameters
            {
                Model = _config.ModelId,
                MaxTokens = 4096,
                System = [new SystemMessage(SystemPrompt)],
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

                    toolResults.Add(new ToolResultContent
                    {
                        ToolUseId = toolBlock.Id,
                        Content = result
                    });

                    _sessionStore.SaveToolResult(sessionKey, toolBlock.Id, result);
                }

                messages.Add(new Message(RoleType.User, toolResults));
                continue;
            }

            var finalText = ExtractText(response);
            _sessionStore.SaveTurn(sessionKey, userInput, allAssistantBlocks.Cast<object>().ToList());
            return finalText;
        }
    }

    private void PrintSessionHistory(string sessionKey)
    {
        var (_, messages) = _sessionStore.LoadSession(sessionKey);
        AnsiConsole.MarkupLine($"[grey]  Session: {sessionKey}[/]");
        if (messages.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]  (empty session)[/]");
            return;
        }

        foreach (var msg in messages)
        {
            var role = msg.Role.ToString().ToLowerInvariant();
            if (msg.Content is string text)
            {
                var display = text.Length > 200 ? text[..200] + "..." : text;
                AnsiConsole.MarkupLine($"[grey]  [{role}] {Markup.Escape(display)}[/]");
            }
        }
    }

    private static string ExtractText(DeepSeekResponse response)
    {
        var sb = new StringBuilder();
        foreach (var content in response.Content)
        {
            if (content is TextContent textContent)
                sb.Append(textContent.Text);
        }
        return sb.ToString();
    }

    private static string GenerateSessionKey(string agentId = "main", string channel = "cli", string peer = "user")
    {
        return $"{agentId}:{channel}:{peer}";
    }

    private static string FormatSessionSummary(SessionMetadata meta)
    {
        var key = meta.SessionKey;
        var updated = meta.UpdatedAt[..Math.Min(19, meta.UpdatedAt.Length)];
        return $"  {key}  ({meta.MessageCount} msgs, last: {updated})";
    }
}
