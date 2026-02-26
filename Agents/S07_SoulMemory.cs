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
/// Section 07: Soul & Memory
/// "Give it a soul, let it remember"
/// 
/// 添加人格配置 (SOUL.md) 和记忆系统.
/// </summary>
public class S07_SoulMemory
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

    public S07_SoulMemory(Config config, int port = 8080)
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
        
        _gatewayServer = new GatewayServer(port);
        RegisterGatewayHandlers();
        
        Directory.CreateDirectory(config.WorkspaceDir);
    }

    private void RegisterGatewayHandlers()
    {
        _gatewayServer.RegisterHandler("send_message", async (paramsElement) =>
        {
            if (paramsElement == null)
                throw new ArgumentException("Missing params");

            var channel = paramsElement.Value.GetProperty("channel").GetString() ?? "gateway";
            var peer = paramsElement.Value.GetProperty("peer").GetString() ?? "user";
            var message = paramsElement.Value.GetProperty("message").GetString()!;

            var route = _router.Resolve(channel, peer);
            var soul = _soulStore.GetOrCreateSoul(route.AgentId);
            
            _sessionStore.LoadSession(route.SessionKey);

            var response = await AgentLoop(message, route.SessionKey, soul);
            return new { 
                success = true, 
                response, 
                session_key = route.SessionKey,
                agent_id = route.AgentId,
                soul = soul.Name
            };
        });

        _gatewayServer.RegisterHandler("create_binding", async (paramsElement) =>
        {
            if (paramsElement == null)
                throw new ArgumentException("Missing params");

            var agentId = paramsElement.Value.GetProperty("agent_id").GetString()!;
            var channel = paramsElement.Value.GetProperty("channel").GetString()!;
            var peer = paramsElement.Value.GetProperty("peer").GetString()!;
            var priority = paramsElement.Value.TryGetProperty("priority", out var p) ? p.GetInt32() : 100;

            var binding = _router.CreateBinding(agentId, channel, peer, priority);
            
            // 为 Agent 创建默认 Soul
            _soulStore.GetOrCreateSoul(agentId);
            
            return new { binding };
        });

        _gatewayServer.RegisterHandler("update_soul", async (paramsElement) =>
        {
            if (paramsElement == null)
                throw new ArgumentException("Missing params");

            var agentId = paramsElement.Value.GetProperty("agent_id").GetString()!;
            var soul = _soulStore.GetOrCreateSoul(agentId);
            
            if (paramsElement.Value.TryGetProperty("name", out var name))
                soul.Name = name.GetString()!;
            if (paramsElement.Value.TryGetProperty("personality", out var personality))
                soul.Personality = personality.GetString()!;
            if (paramsElement.Value.TryGetProperty("description", out var desc))
                soul.Description = desc.GetString()!;
            
            _soulStore.SaveSoul(agentId, soul);
            return new { soul };
        });

        _gatewayServer.RegisterHandler("get_soul", async (paramsElement) =>
        {
            var agentId = paramsElement?.GetProperty("agent_id").GetString() ?? "main";
            var soul = _soulStore.GetOrCreateSoul(agentId);
            return new { soul };
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

        _gatewayServer.RegisterHandler("search_memories", async (paramsElement) =>
        {
            if (paramsElement == null)
                throw new ArgumentException("Missing params");

            var query = paramsElement.Value.GetProperty("query").GetString()!;
            var sessionKey = paramsElement.Value.TryGetProperty("session_key", out var sk) ? sk.GetString() : null;
            var limit = paramsElement.Value.TryGetProperty("limit", out var l) ? l.GetInt32() : 5;
            
            var memories = _memoryStore.Retrieve(query, sessionKey, limit);
            return new { memories };
        });
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var currentKey = GenerateSessionKey();
        _sessionStore.CreateSession(currentKey);
        _router.CreateBinding("main", "cli", "user", priority: 10);
        
        // 创建/加载 main agent 的 soul
        var soul = _soulStore.GetOrCreateSoul("main");

        var gatewayTask = _gatewayServer.StartAsync(cancellationToken);

        AnsiConsole.Write(new Rule("[grey]claw0 s07: Soul & Memory[/]") { Justification = Justify.Left });
        AnsiConsole.MarkupLine($"[grey]  Model: {_config.ModelId}[/]");
        AnsiConsole.MarkupLine($"[grey]  Session: {currentKey}[/]");
        AnsiConsole.MarkupLine($"[grey]  Soul: {soul.Name}[/]");
        AnsiConsole.MarkupLine($"[grey]  Gateway: ws://localhost:{_gatewayServer.Port}/ws[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]  Soul Commands:[/]");
        AnsiConsole.MarkupLine("[yellow]    /soul[/]                    Show current soul");
        AnsiConsole.MarkupLine("[yellow]    /soul_set <key> <value>[/]  Update soul property");
        AnsiConsole.MarkupLine("[yellow]    /remember <text>[/]         Add memory");
        AnsiConsole.MarkupLine("[yellow]    /recall [query][/]          Search memories");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]  Routing:[/]");
        AnsiConsole.MarkupLine("[yellow]    /bind <agent> <channel> <peer>[/]  Create binding");
        AnsiConsole.MarkupLine("[yellow]    /bindings[/]                       List bindings");
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
                AnsiConsole.Markup($"[yellow][{Markup.Escape(soul.Name ?? "")}] > [/]");
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

            // Soul 命令
            if (userInput == "/soul")
            {
                AnsiConsole.MarkupLine($"[grey]  Name: {Markup.Escape(soul.Name ?? "")}[/]");
                AnsiConsole.MarkupLine($"[grey]  Personality: {Markup.Escape(soul.Personality ?? "")}[/]");
                AnsiConsole.MarkupLine($"[grey]  Description: {Markup.Escape(soul.Description ?? "")}[/]");
                AnsiConsole.MarkupLine($"[grey]  Goals: {Markup.Escape(string.Join(", ", soul.Goals))}[/]");
                AnsiConsole.MarkupLine($"[grey]  Rules: {Markup.Escape(string.Join(", ", soul.Rules))}[/]");
                continue;
            }

            if (userInput.StartsWith("/soul_set "))
            {
                var parts = userInput.Split(' ', 3);
                if (parts.Length < 3)
                {
                    AnsiConsole.MarkupLine("[red]  Usage: /soul_set <key> <value>[/]");
                    continue;
                }
                var key = parts[1];
                var value = parts[2];
                
                switch (key)
                {
                    case "name": soul.Name = value; break;
                    case "personality": soul.Personality = value; break;
                    case "description": soul.Description = value; break;
                    default:
                        AnsiConsole.MarkupLine($"[red]  Unknown property: {key}[/]");
                        continue;
                }
                _soulStore.SaveSoul("main", soul);
                AnsiConsole.MarkupLine($"[green]  Updated {key}[/]");
                continue;
            }

            if (userInput.StartsWith("/remember "))
            {
                var memory = userInput[10..];
                _memoryStore.AddMemory(memory, currentKey, importance: 1.0f);
                AnsiConsole.MarkupLine("[green]  Memory added.[/]");
                continue;
            }

            if (userInput.StartsWith("/recall"))
            {
                var query = userInput.Length > 7 ? userInput[8..].Trim() : "";
                var memories = string.IsNullOrEmpty(query) 
                    ? _memoryStore.GetRecentMemories(10)
                    : _memoryStore.Retrieve(query, currentKey, 5);
                
                AnsiConsole.MarkupLine($"[grey]  {memories.Count} memory(s):[/]");
                foreach (var m in memories)
                {
                    var display = m.Content.Length > 80 ? m.Content[..80] + "..." : m.Content;
                    AnsiConsole.MarkupLine($"[grey]  [{m.CreatedAt:MM-dd HH:mm}] {Markup.Escape(display)}[/]");
                }
                continue;
            }

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
                _soulStore.GetOrCreateSoul(agentId);
                AnsiConsole.MarkupLine($"[green]  Created binding: {binding.Id}[/]");
                continue;
            }

            if (userInput == "/bindings")
            {
                var bindings = _router.GetAllBindings();
                AnsiConsole.MarkupLine($"[grey]  {bindings.Count} binding(s):[/]");
                foreach (var b in bindings)
                    AnsiConsole.MarkupLine($"[grey]  [{b.Id}] {b.AgentId} <- {b.Channel}:{b.Peer}[/]");
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

            // 处理普通消息 (带 Soul 和 Memory)
            try
            {
                var route = _router.Resolve("cli", "user");
                var agentSoul = _soulStore.GetOrCreateSoul(route.AgentId);
                
                AnsiConsole.MarkupLine($"[grey]  [using soul: {agentSoul.Name}][/]");
                var response = await AgentLoop(userInput, route.SessionKey, agentSoul);
                // 使用 Panel 显示 Assistant 回复
                AnsiConsole.Write(new Panel(Markup.Escape(response))
                    .Header(Markup.Escape(agentSoul.Name))
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

    private async Task<string> AgentLoop(string userInput, string sessionKey, SoulConfig soul)
    {
        var (_, messages) = _sessionStore.LoadSession(sessionKey);

        // 检索相关记忆并添加到上下文
        var relevantMemories = _memoryStore.Retrieve(userInput, sessionKey, 3);
        var memoryContext = relevantMemories.Count > 0
            ? "\n\nRelevant memories:\n" + string.Join("\n", relevantMemories.Select(m => $"- {m.Content}"))
            : "";

        var enhancedInput = userInput + memoryContext;
        messages.Add(new Message(RoleType.User, enhancedInput));

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
            
            // 保存这条对话到记忆
            _memoryStore.AddMemory($"User: {userInput}\nAssistant: {finalText}", sessionKey, importance: 0.5f);
            
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
