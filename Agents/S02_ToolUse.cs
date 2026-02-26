using Claw0.Common;
using Claw0.Tools;
using Spectre.Console;
using System.Text;
using System.Text.Json;

namespace Claw0.Agents;

/// <summary>
/// Section 02: Tool Use
/// "Give the model hands"
/// 
/// Agent 循环本身没变 -- 我们只是加了一张调度表.
/// 当 stop_reason == "tool_use" 时, 从 TOOL_HANDLERS 查到函数, 执行, 把结果塞回去,
/// 然后继续循环. 就这么简单.
/// </summary>
public class S02_ToolUse
{
    private readonly Config _config;
    private readonly DeepSeekClient _client;
    private readonly ToolRegistry _toolRegistry;

    private const string SystemPrompt = """
        You are a helpful AI assistant with access to tools.
        Use the tools to help the user with file operations and shell commands.
        Always read a file before editing it.
        When using edit_file, the old_string must match EXACTLY (including whitespace).
        """;

    public S02_ToolUse(Config config)
    {
        _config = config;
        _client = new DeepSeekClient(config.DeepSeekApiKey, config.DeepSeekBaseUrl);
        _toolRegistry = new ToolRegistry(config.WorkspaceDir);
        
        // 确保工作目录存在
        Directory.CreateDirectory(config.WorkspaceDir);
    }

    public async Task RunAsync()
    {
        var messages = new List<Message>();

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[cyan bold]Mini-Claw | Section 02: Tool Use[/]").RuleStyle("grey").LeftJustified());
        AnsiConsole.MarkupLine("[grey]  Model:[/] {0}", EscapeMarkup(_config.ModelId));
        AnsiConsole.MarkupLine("[grey]  Workdir:[/] {0}", EscapeMarkup(_config.WorkspaceDir));
        AnsiConsole.MarkupLine("[grey]  Tools:[/] {0}", EscapeMarkup(string.Join(", ", _toolRegistry.Definitions.Select(t => t.Name))));
        AnsiConsole.MarkupLine("[grey]  Type 'quit' or 'exit' to leave. Ctrl+C also works.[/]");
        AnsiConsole.Write(new Rule().RuleStyle("grey"));
        AnsiConsole.WriteLine();

        while (true)
        {
            // --- Step 1: 获取用户输入 ---
            string? userInput;
            try
            {
                AnsiConsole.Markup("[cyan bold]You >[/] ");
                userInput = Console.ReadLine()?.Trim();
            }
            catch
            {
                AnsiConsole.MarkupLine("[grey]Goodbye.[/]");
                break;
            }

            if (string.IsNullOrEmpty(userInput))
                continue;

            if (userInput.ToLowerInvariant() is "quit" or "exit")
            {
                AnsiConsole.MarkupLine("[grey]Goodbye.[/]");
                break;
            }

            // --- Step 2: 追加 user 消息 ---
            messages.Add(new Message(RoleType.User, userInput));

            // --- Step 3: Agent 内循环 ---
            // 模型可能连续调用多个工具才最终给出文本回复.
            // 所以我们用 while 循环, 直到 stop_reason != "tool_use"
            var allAssistantBlocks = new List<ContentBase>();

            while (true)
            {
                try
                {
                    var tools = ToolRegistry.ConvertToDeepSeekTools(_toolRegistry.Definitions);
                    
                    var parameters = new MessageParameters
                    {
                        Model = _config.ModelId,
                        MaxTokens = 8096,
                        System = [new SystemMessage(SystemPrompt)],
                        Messages = messages,
                        Tools = tools,
                        ToolChoice = new ToolChoice { Type = ToolChoiceType.Auto }
                    };

                    var response = await _client.ChatCompletionAsync(parameters);

                    // 收集本次响应的所有 content blocks
                    allAssistantBlocks.AddRange(response.Content);

                    // 追加 assistant 回复到历史
                    messages.Add(new Message(RoleType.Assistant, response.Content));

                    // --- 检查 stop_reason ---
                    var toolUseBlocks = response.Content.OfType<ToolUseContent>().ToList();

                    if (response.StopReason == "tool_calls" && toolUseBlocks.Any())
                    {
                        // 执行每个工具调用
                        var toolResults = new List<ContentBase>();
                        foreach (var toolBlock in toolUseBlocks)
                        {
                            var inputDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(toolBlock.Input?.ToString() ?? "{}")!;
                            
                            AnsiConsole.MarkupLine("  [magenta][tool] {0}[/] [grey]{1}[/]", 
                                EscapeMarkup(toolBlock.Name), 
                                EscapeMarkup(JsonSerializer.Serialize(inputDict)));
                            
                            var result = _toolRegistry.Execute(toolBlock.Name, inputDict);

                            toolResults.Add(new ToolResultContent
                            {
                                ToolUseId = toolBlock.Id,
                                Content = result
                            });
                        }

                        // 把所有工具结果作为一条 user 消息追加
                        messages.Add(new Message(RoleType.User, toolResults));

                        // 继续内循环 -- 模型会看到工具结果并决定下一步
                        continue;
                    }

                    // 没有更多工具调用, 提取最终文本
                    var finalText = ExtractText(response);
                    if (!string.IsNullOrEmpty(finalText))
                        PrintAssistant(finalText);

                    break;
                }
                catch (Exception exc)
                {
                    AnsiConsole.MarkupLine("[yellow]API Error: {0}[/]", EscapeMarkup(exc.Message));
                    // 出错时回滚本轮所有消息到最近的 user 消息
                    while (messages.Count > 0 && messages[^1].Role != RoleType.User)
                        messages.RemoveAt(messages.Count - 1);
                    if (messages.Count > 0)
                        messages.RemoveAt(messages.Count - 1);
                    break;
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
            {
                sb.Append(textContent.Text);
            }
        }
        return sb.ToString();
    }

    private static void PrintAssistant(string text)
    {
        var panel = new Panel(EscapeMarkup(text))
        {
            Header = new PanelHeader(" Assistant ", Justify.Center),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Green),
            Padding = new Padding(1, 0)
        };
        AnsiConsole.WriteLine();
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    private static string EscapeMarkup(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Replace("[", "[[").Replace("]", "]]");
    }
}
