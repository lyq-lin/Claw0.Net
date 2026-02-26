using Spectre.Console;

namespace Claw0.Common;

/// <summary>
/// Spectre.Console 辅助类 - 统一美化控制台输出
/// </summary>
public static class SpectreConsole
{
    // 颜色方案
    public static readonly Color PrimaryColor = Color.Cyan1;
    public static readonly Color SuccessColor = Color.Green;
    public static readonly Color WarningColor = Color.Yellow;
    public static readonly Color ErrorColor = Color.Red;
    public static readonly Color InfoColor = Color.Blue;
    public static readonly Color MutedColor = Color.Grey;
    public static readonly Color AccentColor = Color.Magenta1;

    #region 基础输出

    public static void WriteLine(string text = "")
    {
        AnsiConsole.MarkupLine(text);
    }

    public static void Write(string text)
    {
        AnsiConsole.Markup(text);
    }

    public static string? ReadLine(string prompt)
    {
        return AnsiConsole.Ask<string>(prompt);
    }

    public static string ReadInput(string prompt)
    {
        return AnsiConsole.Prompt(new TextPrompt<string>(prompt).AllowEmpty());
    }

    #endregion

    #region 样式化输出

    public static void Muted(string text)
    {
        AnsiConsole.MarkupLine($"[grey]{EscapeMarkup(text)}[/]");
    }

    public static void Success(string text)
    {
        AnsiConsole.MarkupLine($"[green]✓[/] {EscapeMarkup(text)}");
    }

    public static void Warning(string text)
    {
        AnsiConsole.MarkupLine($"[yellow]⚠[/] {EscapeMarkup(text)}");
    }

    public static void Error(string text)
    {
        AnsiConsole.MarkupLine($"[red]✗[/] {EscapeMarkup(text)}");
    }

    public static void Info(string text)
    {
        AnsiConsole.MarkupLine($"[blue]ℹ[/] {EscapeMarkup(text)}");
    }

    public static void Prompt(string text)
    {
        AnsiConsole.Markup($"[cyan bold]{EscapeMarkup(text)}[/] ");
    }

    #endregion

    #region Agent/会话输出

    public static void AgentPrompt(string agentName)
    {
        AnsiConsole.Markup($"[cyan bold][[{EscapeMarkup(agentName)}] > [/]");
    }

    public static void AssistantResponse(string agentName, string response)
    {
        var panel = new Panel(EscapeMarkup(response))
        {
            Header = new PanelHeader($" {agentName} ", Justify.Center),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Green),
            Padding = new Padding(1, 0)
        };
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    public static void UserMessage(string message)
    {
        AnsiConsole.MarkupLine($"[cyan bold]You >[/] {EscapeMarkup(message)}");
    }

    public static void SessionInfo(string sessionKey)
    {
        AnsiConsole.MarkupLine($"[grey]Session: {EscapeMarkup(sessionKey)}[/]");
    }

    #endregion

    #region 命令/列表输出

    public static void CommandHelp(string command, string description)
    {
        AnsiConsole.MarkupLine($"  [yellow]{command}[/] - {EscapeMarkup(description)}");
    }

    public static void ListItem(string label, string value, string? marker = null)
    {
        var prefix = marker != null ? $"[green]{marker}[/] " : "  ";
        AnsiConsole.MarkupLine($"{prefix}[white]{label}:[/] {EscapeMarkup(value)}");
    }

    public static void EmptyList(string message = "(empty)")
    {
        AnsiConsole.MarkupLine($"[grey]  {message}[/]");
    }

    #endregion

    #region 消息/通道输出

    public static void Message(string sender, string text, string? channel = null)
    {
        var prefix = channel != null ? $"[{EscapeMarkup(channel)}] " : "";
        AnsiConsole.MarkupLine($"[grey]{prefix}{EscapeMarkup(sender)}:[/] {EscapeMarkup(text)}");
    }

    public static void ToolCall(string toolName, string? details = null)
    {
        var detailStr = details != null ? $" [grey]{EscapeMarkup(details)}[/]" : "";
        AnsiConsole.MarkupLine($"  [magenta][tool] {EscapeMarkup(toolName)}[/]{detailStr}");
    }

    public static void ToolResult(string result)
    {
        AnsiConsole.MarkupLine($"  [grey][tool_result] {EscapeMarkup(result.Truncate(100))}[/]");
    }

    #endregion

    #region 标题/分隔线

    public static void Title(string title)
    {
        AnsiConsole.Write(new Rule($"[cyan bold]{EscapeMarkup(title)}[/]").RuleStyle("grey").LeftJustified());
    }

    public static void Section(string section)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[green]Section {section}[/]").RuleStyle("green"));
        AnsiConsole.WriteLine();
    }

    public static void Separator()
    {
        AnsiConsole.Write(new Rule().RuleStyle("grey"));
    }

    #endregion

    #region 表格/面板

    public static Table CreateTable(string title)
    {
        return new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title($"[bold]{EscapeMarkup(title)}[/]");
    }

    public static void ShowPanel(string title, string content, Color borderColor)
    {
        var panel = new Panel(EscapeMarkup(content))
        {
            Header = new PanelHeader(title),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(borderColor)
        };
        AnsiConsole.Write(panel);
    }

    #endregion

    #region 进度/状态

    public static async Task<T> ShowProgressAsync<T>(string description, Func<Task<T>> action)
    {
        return await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("green bold"))
            .StartAsync(description, async ctx => await action());
    }

    public static void Status(string message, string status = "pending")
    {
        var icon = status switch
        {
            "success" => "[green]✓[/]",
            "error" => "[red]✗[/]",
            "warning" => "[yellow]⚠[/]",
            _ => "[blue]ℹ[/]"
        };
        AnsiConsole.MarkupLine($"  {icon} {EscapeMarkup(message)}");
    }

    #endregion

    #region 辅助方法

    private static string EscapeMarkup(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Replace("[", "[[").Replace("]", "]]");
    }

    private static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
    }

    #endregion
}
