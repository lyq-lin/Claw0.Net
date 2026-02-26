using Claw0.Channels;
using Claw0.Common;
using Claw0.Sessions;
using Claw0.Tools;
using Spectre.Console;
using System.Text;
using System.Text.Json;

namespace Claw0.Agents;

/// <summary>
/// Section 04: Multi-Channel Abstraction
/// "Same brain, many mouths"
/// 
/// OpenClaw 和 Claude Code 最大的区别在于:
/// - Claude Code = 单通道 CLI 工具
/// - OpenClaw   = 多通道 AI 网关
/// </summary>
public class S04_MultiChannel
{
    private readonly Config _config;
    private readonly DeepSeekClient _client;
    private readonly ToolRegistry _toolRegistry;
    private readonly SessionStore _sessionStore;
    private readonly ChannelRegistry _channelRegistry;
    private readonly CliChannel _cliChannel;

    private const string SystemPrompt = """
        You are a helpful assistant with access to local tools.
        You can read files and list directories on the user's machine.
        Keep answers concise. When using tools, explain what you found.
        """;

    public S04_MultiChannel(Config config)
    {
        _config = config;
        _client = new DeepSeekClient(config.DeepSeekApiKey, config.DeepSeekBaseUrl);
        _toolRegistry = new ToolRegistry(config.WorkspaceDir);
        _sessionStore = new SessionStore(config.WorkspaceDir);
        _channelRegistry = new ChannelRegistry();
        
        // 注册 CLI 通道
        _cliChannel = new CliChannel();
        _channelRegistry.Register(_cliChannel);
        
        // 注册文件通道 (模拟 webhook)
        _channelRegistry.Register(new FileChannel(config.WorkspaceDir));
        
        Directory.CreateDirectory(config.WorkspaceDir);
    }

    public async Task RunAsync()
    {
        var currentKey = GenerateSessionKey();
        var currentChannel = "cli";
        var sessionData = _sessionStore.LoadSession(currentKey);
        var msgCount = sessionData.Metadata.MessageCount;

        AnsiConsole.Write(new Rule("[grey]claw0 s04: Multi-Channel[/]") { Justification = Justify.Left });
        AnsiConsole.MarkupLine($"[grey]  Model: {_config.ModelId}[/]");
        AnsiConsole.MarkupLine($"[grey]  Session: {currentKey}[/]");
        AnsiConsole.MarkupLine($"[grey]  Channels: {string.Join(", ", _channelRegistry.AllChannels.Select(c => c.Id))}[/]");
        if (msgCount > 0)
            AnsiConsole.MarkupLine($"[grey]  Restored: {msgCount} previous turns[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]  Commands:[/]");
        AnsiConsole.MarkupLine("[yellow]    /channels[/]       List registered channels");
        AnsiConsole.MarkupLine("[yellow]    /poll[/]           Poll all channels for messages");
        AnsiConsole.MarkupLine("[yellow]    /send <ch> <msg>[/]  Send via channel (testing)");
        AnsiConsole.MarkupLine("[yellow]    /new[/]            Create new session");
        AnsiConsole.MarkupLine("[yellow]    /sessions[/]       List all sessions");
        AnsiConsole.MarkupLine("[yellow]    /history[/]        Show current session history");
        AnsiConsole.MarkupLine("[yellow]    /quit[/]           Exit");
        AnsiConsole.Write(new Rule() { Style = "grey" });
        AnsiConsole.WriteLine();

        while (true)
        {
            string? userInput;
            try
            {
                AnsiConsole.Markup($"[yellow][{currentChannel}] > [/]");
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

            if (userInput == "/channels")
            {
                AnsiConsole.MarkupLine("[grey]  Registered channels:[/]");
                foreach (var ch in _channelRegistry.AllChannels)
                    AnsiConsole.MarkupLine($"[grey]    - {ch.Id} (max {ch.MaxTextLength} chars)[/]");
                continue;
            }

            if (userInput == "/poll")
            {
                var messages = await _channelRegistry.PollAllAsync();
                if (messages.Count == 0)
                    AnsiConsole.MarkupLine("[grey]  (no new messages)[/]");
                else
                {
                    foreach (var (chId, msg) in messages)
                        AnsiConsole.MarkupLine($"[grey]  [{chId}] {Markup.Escape(msg.Sender)}: {Markup.Escape(msg.Text)}[/]");
                }
                continue;
            }

            if (userInput.StartsWith("/send "))
            {
                var parts = userInput.Split(' ', 3);
                if (parts.Length < 3)
                {
                    AnsiConsole.MarkupLine("[red]  Usage: /send <channel> <message>[/]");
                    continue;
                }
                var chId = parts[1];
                var msg = parts[2];
                await _channelRegistry.SendAsync(chId, "user", msg);
                AnsiConsole.MarkupLine($"[green]  Sent to {chId}[/]");
                continue;
            }

            if (userInput == "/new")
            {
                var tsSuffix = DateTime.UtcNow.ToString("HHmmss");
                currentKey = GenerateSessionKey(peer: $"user_{tsSuffix}");
                _sessionStore.CreateSession(currentKey);
                AnsiConsole.MarkupLine($"[green]  New session: {currentKey}[/]");
                continue;
            }

            if (userInput == "/sessions")
            {
                var sessions = _sessionStore.ListSessions();
                if (sessions.Count == 0)
                    AnsiConsole.MarkupLine("[grey]  (no sessions)[/]");
                else
                {
                    AnsiConsole.MarkupLine($"[grey]  {sessions.Count} session(s):[/]");
                    foreach (var meta in sessions)
                    {
                        var marker = meta.SessionKey == currentKey ? " [green]*[/]" : "";
                        AnsiConsole.MarkupLine($"[grey]  {Markup.Escape(FormatSessionSummary(meta))}{marker}[/]");
                    }
                }
                continue;
            }

            if (userInput == "/history")
            {
                PrintSessionHistory(currentKey);
                continue;
            }

            if (userInput.StartsWith("/"))
            {
                AnsiConsole.MarkupLine($"[red]  Unknown command: {Markup.Escape(userInput)}[/]");
                continue;
            }

            // -- 通过 CLI 通道处理消息 --
            _cliChannel.EnqueueInput(userInput);
            var inboundMsg = await _cliChannel.ReceiveAsync();
            if (inboundMsg != null)
            {
                try
                {
                    var response = await AgentLoop(inboundMsg.Text, inboundMsg.ThreadId!);
                    await _cliChannel.SendAsync(inboundMsg.Sender, response);
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
        }
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
        if (messages.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]  (empty session)[/]");
            return;
        }

        foreach (var msg in messages)
        {
            var role = msg.Role.ToString().ToLowerInvariant();
            var content = msg.Content;

            if (content is string text)
            {
                var display = text.Length > 200 ? text[..200] + "..." : text;
                AnsiConsole.MarkupLine($"[grey]  [{role}] {Markup.Escape(display)}[/]");
            }
            else if (content is List<ContentBase> blocks)
            {
                foreach (var block in blocks)
                {
                    switch (block)
                    {
                        case ToolUseContent toolUse:
                            AnsiConsole.MarkupLine($"[grey]  [{role}:tool_use] {toolUse.Name}(...)[/]");
                            break;
                        case ToolResultContent toolResult:
                            var output = toolResult.Content?.ToString() ?? "";
                            var display = output.Length > 100 ? output[..100] + "..." : output;
                            AnsiConsole.MarkupLine($"[grey]  [{role}:tool_result] {Markup.Escape(display)}[/]");
                            break;
                        case TextContent txt:
                            var textDisplay = txt.Text.Length > 200 ? txt.Text[..200] + "..." : txt.Text;
                            AnsiConsole.MarkupLine($"[grey]  [{role}] {Markup.Escape(textDisplay)}[/]");
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
        return $"  {key}  ({count} msgs, last: {updated})";
    }
}
