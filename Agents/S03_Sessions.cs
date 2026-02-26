using Claw0.Common;
using Claw0.Sessions;
using Claw0.Tools;
using Spectre.Console;
using System.Text;
using System.Text.Json;

namespace Claw0.Agents;

/// <summary>
/// Section 03: Session Persistence
/// "Conversations that survive restarts"
/// 
/// 本节添加会话持久化功能:
/// - SessionStore: 管理会话的创建、加载、保存
/// - JSONL transcript: 只追加的完整消息日志
/// - 支持多会话切换
/// </summary>
public class S03_Sessions
{
    private readonly Config _config;
    private readonly DeepSeekClient _client;
    private readonly ToolRegistry _toolRegistry;
    private readonly SessionStore _sessionStore;

    private const string SystemPrompt = """
        You are a helpful assistant with access to local tools.
        You can read files and list directories on the user's machine.
        Keep answers concise. When using tools, explain what you found.
        """;

    public S03_Sessions(Config config)
    {
        _config = config;
        _client = new DeepSeekClient(config.DeepSeekApiKey, config.DeepSeekBaseUrl);
        _toolRegistry = new ToolRegistry(config.WorkspaceDir);
        _sessionStore = new SessionStore(config.WorkspaceDir);
        
        Directory.CreateDirectory(config.WorkspaceDir);
    }

    public async Task RunAsync()
    {
        // 默认会话 key
        var currentKey = GenerateSessionKey();
        var sessionData = _sessionStore.LoadSession(currentKey);
        var msgCount = sessionData.Metadata.MessageCount;

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[cyan bold]claw0 s03: Session Persistence[/]").RuleStyle("grey").LeftJustified());
        AnsiConsole.MarkupLine("[grey]  Model:[/] {0}", EscapeMarkup(_config.ModelId));
        AnsiConsole.MarkupLine("[grey]  Session:[/] {0}", EscapeMarkup(currentKey));
        if (msgCount > 0)
            AnsiConsole.MarkupLine("[grey]  Restored:[/] {0} previous turns", msgCount);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]Commands:[/]");
        AnsiConsole.MarkupLine("  [yellow]/new[/]              - Create a new session");
        AnsiConsole.MarkupLine("  [yellow]/sessions[/]         - List all sessions");
        AnsiConsole.MarkupLine("  [yellow]/switch <key>[/]     - Switch to session");
        AnsiConsole.MarkupLine("  [yellow]/history[/]          - Show current session history");
        AnsiConsole.MarkupLine("  [yellow]/delete <key>[/]     - Delete a session");
        AnsiConsole.MarkupLine("  [yellow]/quit[/]             - Exit");
        AnsiConsole.Write(new Rule().RuleStyle("grey"));
        AnsiConsole.WriteLine();

        while (true)
        {
            string? userInput;
            try
            {
                AnsiConsole.Markup("[grey][[{0}] > [/]", EscapeMarkup(currentKey));
                userInput = Console.ReadLine()?.Trim();
            }
            catch
            {
                AnsiConsole.MarkupLine("[grey]Bye.[/]");
                break;
            }

            if (string.IsNullOrEmpty(userInput))
                continue;

            // -- 命令处理 --
            if (userInput == "/quit")
            {
                AnsiConsole.MarkupLine("[grey]Bye.[/]");
                break;
            }

            if (userInput == "/new")
            {
                var tsSuffix = DateTime.UtcNow.ToString("HHmmss");
                currentKey = GenerateSessionKey(peer: $"user_{tsSuffix}");
                _sessionStore.CreateSession(currentKey);
                AnsiConsole.MarkupLine("  [green]New session:[/] {0}", EscapeMarkup(currentKey));
                continue;
            }

            if (userInput == "/sessions")
            {
                var sessions = _sessionStore.ListSessions();
                if (sessions.Count == 0)
                    AnsiConsole.MarkupLine("  [grey](no sessions)[/]");
                else
                {
                    AnsiConsole.MarkupLine("  [green]{0} session(s):[/]", sessions.Count);
                    foreach (var meta in sessions)
                    {
                        var marker = meta.SessionKey == currentKey ? " [green]*[/]" : "";
                        AnsiConsole.MarkupLine("  {0}{1}", EscapeMarkup(FormatSessionSummary(meta)), marker);
                    }
                }
                continue;
            }

            if (userInput.StartsWith("/switch "))
            {
                var parts = userInput.Split(' ', 2);
                if (parts.Length < 2)
                {
                    AnsiConsole.MarkupLine("  [yellow]Usage:[/] /switch <session_key>");
                    continue;
                }
                var targetKey = parts[1].Trim();
                if (!_sessionStore.SessionExists(targetKey))
                {
                    AnsiConsole.MarkupLine("  [red]Session not found:[/] {0}", EscapeMarkup(targetKey));
                    AnsiConsole.MarkupLine("  [grey]Use /sessions to list available sessions.[/]");
                    continue;
                }
                currentKey = targetKey;
                var meta = _sessionStore.LoadSession(currentKey).Metadata;
                AnsiConsole.MarkupLine("  [green]Switched to:[/] {0} ([grey]{1} msgs[/])", EscapeMarkup(currentKey), meta.MessageCount);
                continue;
            }

            if (userInput == "/history")
            {
                AnsiConsole.MarkupLine("  [green]Session:[/] {0}", EscapeMarkup(currentKey));
                PrintSessionHistory(currentKey);
                continue;
            }

            if (userInput.StartsWith("/delete "))
            {
                var parts = userInput.Split(' ', 2);
                if (parts.Length < 2)
                {
                    AnsiConsole.MarkupLine("  [yellow]Usage:[/] /delete <session_key>");
                    continue;
                }
                var targetKey = parts[1].Trim();
                if (targetKey == currentKey)
                {
                    AnsiConsole.MarkupLine("  [red]Cannot delete the current session. Switch first.[/]");
                    continue;
                }
                if (_sessionStore.DeleteSession(targetKey))
                    AnsiConsole.MarkupLine("  [green]Deleted:[/] {0}", EscapeMarkup(targetKey));
                else
                    AnsiConsole.MarkupLine("  [red]Session not found:[/] {0}", EscapeMarkup(targetKey));
                continue;
            }

            if (userInput.StartsWith("/"))
            {
                AnsiConsole.MarkupLine("  [red]Unknown command:[/] {0}", EscapeMarkup(userInput));
                continue;
            }

            // -- 调用 agent loop --
            try
            {
                var response = await AgentLoop(userInput, currentKey);
                AnsiConsole.WriteLine();
                AnsiConsole.WriteLine(response);
                AnsiConsole.WriteLine();
            }
            catch (Exception exc)
            {
                AnsiConsole.MarkupLine("[red]  Error: {0}[/]", EscapeMarkup(exc.Message));
            }
        }
    }

    private async Task<string> AgentLoop(string userInput, string sessionKey)
    {
        // 加载会话历史
        var (_, messages) = _sessionStore.LoadSession(sessionKey);

        // 追加用户消息
        messages.Add(new Message(RoleType.User, userInput));

        // 本轮所有 assistant content blocks (用于持久化)
        var allAssistantBlocks = new List<ContentBase>();

        // 工具调用循环
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

            // 收集本次响应的所有 content blocks
            allAssistantBlocks.AddRange(response.Content);

            // 检查是否有工具调用
            var toolUseBlocks = response.Content.OfType<ToolUseContent>().ToList();

            if (response.StopReason == "tool_calls" && toolUseBlocks.Any())
            {
                // 将 assistant 消息加入 messages (供下一轮 API 调用)
                messages.Add(new Message(RoleType.Assistant, response.Content));

                // 执行每个工具调用
                var toolResults = new List<ContentBase>();
                foreach (var toolBlock in toolUseBlocks)
                {
                    var inputDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(toolBlock.Input?.ToString() ?? "{}")!;
                    AnsiConsole.MarkupLine("  [magenta][tool] {0}[/]([grey]{1}[/])", 
                        EscapeMarkup(toolBlock.Name),
                        EscapeMarkup(JsonSerializer.Serialize(inputDict)));
                    var result = _toolRegistry.Execute(toolBlock.Name, inputDict);

                    toolResults.Add(new ToolResultContent
                    {
                        ToolUseId = toolBlock.Id,
                        Content = result
                    });

                    // 持久化工具结果
                    _sessionStore.SaveToolResult(sessionKey, toolBlock.Id, result);
                }

                // 将工具结果加入 messages
                messages.Add(new Message(RoleType.User, toolResults));
                continue;
            }

            // 没有更多工具调用, 提取最终文本
            var finalText = ExtractText(response);
            
            // 持久化本轮对话
            _sessionStore.SaveTurn(sessionKey, userInput, allAssistantBlocks.Cast<object>().ToList());

            return finalText;
        }
    }

    private void PrintSessionHistory(string sessionKey)
    {
        var (_, messages) = _sessionStore.LoadSession(sessionKey);
        if (messages.Count == 0)
        {
            AnsiConsole.MarkupLine("  [grey](empty session)[/]");
            return;
        }

        foreach (var msg in messages)
        {
            var role = msg.Role.ToString().ToLowerInvariant();
            var content = msg.Content;

            if (content is string text)
            {
                var display = text.Length > 200 ? text[..200] + "..." : text;
                AnsiConsole.MarkupLine("  [grey][{0}][/] {1}", role, EscapeMarkup(display));
            }
            else if (content is List<ContentBase> blocks)
            {
                foreach (var block in blocks)
                {
                    switch (block)
                    {
                        case ToolUseContent toolUse:
                            AnsiConsole.MarkupLine("  [grey][{0}:tool_use][/] [magenta]{1}[/](...)", role, EscapeMarkup(toolUse.Name));
                            break;
                        case ToolResultContent toolResult:
                            var output = toolResult.Content?.ToString() ?? "";
                            var display = output.Length > 100 ? output[..100] + "..." : output;
                            AnsiConsole.MarkupLine("  [grey][{0}:tool_result][/] {1}", role, EscapeMarkup(display));
                            break;
                        case TextContent txt:
                            var textDisplay = txt.Text.Length > 200 ? txt.Text[..200] + "..." : txt.Text;
                            AnsiConsole.MarkupLine("  [grey][{0}][/] {1}", role, EscapeMarkup(textDisplay));
                            break;
                    }
                }
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
        var updated = meta.UpdatedAt;
        var count = meta.MessageCount;
        if (updated.Length > 19)
            updated = updated[..19];
        return $"{key}  ({count} msgs, last: {updated})";
    }

    private static string EscapeMarkup(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Replace("[", "[[").Replace("]", "]]");
    }
}
