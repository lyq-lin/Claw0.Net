using Claw0.Common;
using Spectre.Console;
using System.Text;

namespace Claw0.Agents;

/// <summary>
/// Section 01: The Agent Loop
/// "One loop to rule them all"
/// 
/// AI Agent 的全部秘密就是一个 while 循环不断检查 stop_reason.
/// 本节展示最纯粹的对话循环 -- 没有工具, 没有花活, 只有:
///   用户输入 -> 历史消息 -> LLM -> 打印回复 -> 继续
/// </summary>
public class S01_AgentLoop
{
    private readonly Config _config;
    private readonly DeepSeekClient _client;
    
    private const string SystemPrompt = "You are a helpful AI assistant. Answer questions directly.";

    public S01_AgentLoop(Config config)
    {
        _config = config;

        _client = new DeepSeekClient(_config.DeepSeekApiKey, _config.DeepSeekBaseUrl);
    }

    public async Task RunAsync()
    {
        // messages 是整个 agent 的 "记忆"
        // 每轮对话的 user/assistant 消息都追加到这里
        var messages = new List<Message>();

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[cyan bold]Mini-Claw | Section 01: The Agent Loop[/]").RuleStyle("grey").LeftJustified());
        AnsiConsole.MarkupLine("[grey]  Model:[/] {0}", EscapeMarkup(_config.ModelId));
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

            if (userInput.ToLower() is "quit" or "exit")
            {
                AnsiConsole.MarkupLine("[grey]Goodbye.[/]");
                break;
            }

            // --- Step 2: 追加 user 消息到历史 ---
            messages.Add(new Message(RoleType.User, userInput));

            // --- Step 3: 调用 LLM ---
            string assistantText;
            try
            {
                var parameters = new MessageParameters
                {
                    Model = _config.ModelId,
                    MaxTokens = 8096,
                    System = new List<SystemMessage> { new SystemMessage(SystemPrompt) },
                    Messages = messages
                };

                var response = await _client.ChatCompletionAsync(parameters);
                
                // --- Step 4: 检查 stop_reason ---
                // 在本节中, stop_reason 只会是 "stop"
                assistantText = ExtractText(response);
                
                // 打印回复
                PrintAssistant(assistantText);

                // 追加 assistant 消息到历史 (保持上下文)
                messages.Add(new Message(RoleType.Assistant, response.Content.ToList()));
            }
            catch (Exception exc)
            {
                // API 错误不应该炸掉整个循环
                AnsiConsole.MarkupLine("[yellow]API Error: {0}[/]", EscapeMarkup(exc.Message));
                // 回滚刚追加的 user 消息, 让用户可以重试
                if (messages.Count > 0)
                    messages.RemoveAt(messages.Count - 1);
                continue;
            }

            // --- 循环继续, 等待下一次用户输入 ---
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
