using Claw0.Agents;
using Claw0.Common;
using Spectre.Console;

namespace Claw0;

/// <summary>
/// claw0.net - From Zero to One: Build an OpenClaw-like AI Gateway
/// 
/// 10 progressive sections, each introducing one core mechanism.
/// Each section is a runnable program you can execute immediately.
/// 
/// Usage:
///   dotnet run [section_number]
/// 
/// Examples:
///   dotnet run 01    # Section 01: Agent Loop
///   dotnet run 02    # Section 02: Tool Use
///   dotnet run 03    # Section 03: Sessions
///   dotnet run 04    # Section 04: Multi-Channel
///   dotnet run 05    # Section 05: Gateway Server
///   dotnet run 06    # Section 06: Routing
///   dotnet run 07    # Section 07: Soul & Memory
///   dotnet run 08    # Section 08: Heartbeat
///   dotnet run 09    # Section 09: Cron Scheduler
///   dotnet run 10    # Section 10: Delivery Queue
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        var config = new Config();
        
        // 检查 API Key
        if (string.IsNullOrEmpty(config.DeepSeekApiKey) || config.DeepSeekApiKey == "sk-xxxxx")
        {
            AnsiConsole.MarkupLine("[red bold]Error: DEEPSEEK_API_KEY not set.[/]");
            AnsiConsole.MarkupLine("Please set the environment variable or create [yellow]appsettings.json[/]:");
            AnsiConsole.Write(new Panel(@"{
  ""DEEPSEEK_API_KEY"": ""sk-xxxxx"",
  ""MODEL_ID"": ""deepseek-chat""
}")
            {
                Header = new PanelHeader("Example Configuration"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Yellow)
            });
            return;
        }

        // 解析命令行参数
        var section = args.Length > 0 ? args[0] : "10";

        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule($"[cyan bold]claw0.net - Section {section}[/]").RuleStyle("grey").Centered());
            AnsiConsole.WriteLine();

            switch (section)
            {
                case "01":
                    var s01 = new S01_AgentLoop(config);
                    await s01.RunAsync();
                    break;

                case "02":
                    var s02 = new S02_ToolUse(config);
                    await s02.RunAsync();
                    break;

                case "03":
                    var s03 = new S03_Sessions(config);
                    await s03.RunAsync();
                    break;

                case "04":
                    var s04 = new S04_MultiChannel(config);
                    await s04.RunAsync();
                    break;

                case "05":
                    var s05 = new S05_Gateway(config);
                    await s05.RunAsync(cts.Token);
                    break;

                case "06":
                    var s06 = new S06_Routing(config);
                    await s06.RunAsync(cts.Token);
                    break;

                case "07":
                    var s07 = new S07_SoulMemory(config);
                    await s07.RunAsync(cts.Token);
                    break;

                case "08":
                    var s08 = new S08_Heartbeat(config);
                    await s08.RunAsync(cts.Token);
                    break;

                case "09":
                    var s09 = new S09_Cron(config);
                    await s09.RunAsync(cts.Token);
                    break;

                case "10":
                    var s10 = new S10_Delivery(config);
                    await s10.RunAsync(cts.Token);
                    break;

                default:
                    AnsiConsole.MarkupLine($"[red]Unknown section: {section}[/]");
                    AnsiConsole.MarkupLine("[grey]Usage: dotnet run [01-10][/]");
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("\n[grey]Cancelled by user.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red bold]Error: {ex.Message.EscapeMarkup()}[/]");
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
        }
    }
}
