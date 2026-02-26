using Claw0.Channels;
using Claw0.Common;
using Claw0.Gateway;
using Claw0.Sessions;
using Claw0.Tools;
using Spectre.Console;
using System.Text;
using System.Text.Json;

namespace Claw0.Agents;

/// <summary>
/// Section 05: Gateway Server
/// "The switchboard"
/// 
/// 添加 WebSocket + JSON-RPC 网关服务器,
/// 允许远程客户端连接到 Agent.
/// </summary>
public class S05_Gateway
{
    private readonly Config _config;
    private readonly DeepSeekClient _client;
    private readonly ToolRegistry _toolRegistry;
    private readonly SessionStore _sessionStore;
    private readonly ChannelRegistry _channelRegistry;
    private readonly GatewayServer _gatewayServer;

    private const string SystemPrompt = """
        You are a helpful assistant with access to local tools.
        You can read files and list directories on the user's machine.
        Keep answers concise. When using tools, explain what you found.
        """;

    public S05_Gateway(Config config, int port = 8080)
    {
        _config = config;
        _client = new DeepSeekClient(config.DeepSeekApiKey, config.DeepSeekBaseUrl);
        _toolRegistry = new ToolRegistry(config.WorkspaceDir);
        _sessionStore = new SessionStore(config.WorkspaceDir);
        _channelRegistry = new ChannelRegistry();
        _channelRegistry.Register(new CliChannel());
        _channelRegistry.Register(new FileChannel(config.WorkspaceDir));
        
        _gatewayServer = new GatewayServer(port);
        RegisterGatewayHandlers();
        
        Directory.CreateDirectory(config.WorkspaceDir);
    }

    private void RegisterGatewayHandlers()
    {
        // 发送消息到 Agent
        _gatewayServer.RegisterHandler("send_message", async (paramsElement) =>
        {
            if (paramsElement == null)
                throw new ArgumentException("Missing params");

            var sessionKey = paramsElement.Value.GetProperty("session_key").GetString()!;
            var message = paramsElement.Value.GetProperty("message").GetString()!;

            var response = await AgentLoop(message, sessionKey);
            return new { success = true, response, session_key = sessionKey };
        });

        // 获取会话列表
        _gatewayServer.RegisterHandler("list_sessions", async (_) =>
        {
            var sessions = _sessionStore.ListSessions();
            return new { sessions };
        });

        // 创建新会话
        _gatewayServer.RegisterHandler("create_session", async (paramsElement) =>
        {
            var peer = paramsElement?.GetProperty("peer").GetString() ?? Guid.NewGuid().ToString("N")[..8];
            var sessionKey = GenerateSessionKey(peer: peer);
            var metadata = _sessionStore.CreateSession(sessionKey);
            return new { session_key = sessionKey, metadata };
        });

        // 获取会话历史
        _gatewayServer.RegisterHandler("get_history", async (paramsElement) =>
        {
            if (paramsElement == null)
                throw new ArgumentException("Missing params");

            var sessionKey = paramsElement.Value.GetProperty("session_key").GetString()!;
            var (_, history) = _sessionStore.LoadSession(sessionKey);
            return new { session_key = sessionKey, message_count = history.Count };
        });
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var currentKey = GenerateSessionKey();
        _sessionStore.CreateSession(currentKey);

        // 启动网关服务器
        var gatewayTask = _gatewayServer.StartAsync(cancellationToken);

        AnsiConsole.Write(new Rule("[grey]claw0 s05: Gateway Server[/]") { Justification = Justify.Left });
        AnsiConsole.MarkupLine($"[grey]  Model: {_config.ModelId}[/]");
        AnsiConsole.MarkupLine($"[grey]  Session: {currentKey}[/]");
        AnsiConsole.MarkupLine($"[grey]  Gateway: ws://localhost:{_gatewayServer.Port}/ws[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]  JSON-RPC Methods:[/]");
        AnsiConsole.MarkupLine("[yellow]    send_message[/]    - Send message to agent");
        AnsiConsole.MarkupLine("[yellow]    list_sessions[/]   - List all sessions");
        AnsiConsole.MarkupLine("[yellow]    create_session[/]  - Create new session");
        AnsiConsole.MarkupLine("[yellow]    get_history[/]     - Get session history");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]  Commands:[/]");
        AnsiConsole.MarkupLine("[yellow]    /new[/]            Create new session");
        AnsiConsole.MarkupLine("[yellow]    /sessions[/]       List all sessions");
        AnsiConsole.MarkupLine("[yellow]    /history[/]        Show current session history");
        AnsiConsole.MarkupLine("[yellow]    /quit[/]           Exit");
        AnsiConsole.Write(new Rule() { Style = "grey" });
        AnsiConsole.WriteLine();

        // 本地 CLI 循环
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

            // 处理普通消息
            try
            {
                var response = await AgentLoop(userInput, currentKey);
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

    private static string GenerateSessionKey(string agentId = "main", string channel = "gateway", string peer = "user")
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
