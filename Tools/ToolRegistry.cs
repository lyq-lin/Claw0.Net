using System.Text.Json;
using Claw0.Common;
using Spectre.Console;

namespace Claw0.Tools;

/// <summary>
/// 工具注册表 - 调度表: 工具名 -> 处理函数
/// </summary>
public class ToolRegistry
{
    private readonly Dictionary<string, Func<Dictionary<string, JsonElement>, string>> _handlers = new();
    private readonly List<ToolDefinition> _definitions = new();
    private readonly string _workspaceDir;
    private const int MaxToolOutput = 50000;

    public ToolRegistry(string workspaceDir)
    {
        _workspaceDir = workspaceDir;
        RegisterBuiltInTools();
    }

    public IReadOnlyList<ToolDefinition> Definitions => _definitions;

    public string Execute(string toolName, Dictionary<string, JsonElement> input)
    {
        if (!_handlers.TryGetValue(toolName, out var handler))
            return $"Error: Unknown tool '{toolName}'";

        try
        {
            var summary = GetInputSummary(input).EscapeMarkup();
            AnsiConsole.MarkupLine($"  [magenta dim][tool: {toolName.EscapeMarkup()}] {summary}[/]");
            return handler(input);
        }
        catch (Exception exc)
        {
            return $"Error: {toolName} failed: {exc.Message}";
        }
    }

    private void RegisterTool(ToolDefinition definition, Func<Dictionary<string, JsonElement>, string> handler)
    {
        _definitions.Add(definition);
        _handlers[definition.Name] = handler;
    }

    private void RegisterBuiltInTools()
    {
        // Tool: bash
        RegisterTool(
            new ToolDefinition
            {
                Name = "bash",
                Description = "Run a shell command and return its output. Use for system commands, git, package managers, etc.",
                InputSchema = new InputSchema
                {
                    Properties = new Dictionary<string, PropertyDefinition>
                    {
                        ["command"] = new() { Type = "string", Description = "The shell command to execute." },
                        ["timeout"] = new() { Type = "integer", Description = "Timeout in seconds. Default 30." }
                    },
                    Required = ["command"]
                }
            },
            BashTool
        );

        // Tool: read_file
        RegisterTool(
            new ToolDefinition
            {
                Name = "read_file",
                Description = "Read the contents of a file.",
                InputSchema = new InputSchema
                {
                    Properties = new Dictionary<string, PropertyDefinition>
                    {
                        ["file_path"] = new() { Type = "string", Description = "Path to the file (relative to working directory)." }
                    },
                    Required = ["file_path"]
                }
            },
            ReadFileTool
        );

        // Tool: write_file
        RegisterTool(
            new ToolDefinition
            {
                Name = "write_file",
                Description = "Write content to a file. Creates parent directories if needed. Overwrites existing content.",
                InputSchema = new InputSchema
                {
                    Properties = new Dictionary<string, PropertyDefinition>
                    {
                        ["file_path"] = new() { Type = "string", Description = "Path to the file (relative to working directory)." },
                        ["content"] = new() { Type = "string", Description = "The content to write." }
                    },
                    Required = ["file_path", "content"]
                }
            },
            WriteFileTool
        );

        // Tool: edit_file
        RegisterTool(
            new ToolDefinition
            {
                Name = "edit_file",
                Description = "Replace an exact string in a file with a new string. The old_string must appear exactly once in the file. Always read the file first to get the exact text to replace.",
                InputSchema = new InputSchema
                {
                    Properties = new Dictionary<string, PropertyDefinition>
                    {
                        ["file_path"] = new() { Type = "string", Description = "Path to the file (relative to working directory)." },
                        ["old_string"] = new() { Type = "string", Description = "The exact text to find and replace. Must be unique." },
                        ["new_string"] = new() { Type = "string", Description = "The replacement text." }
                    },
                    Required = ["file_path", "old_string", "new_string"]
                }
            },
            EditFileTool
        );
    }

    // Tool Implementations
    private string BashTool(Dictionary<string, JsonElement> input)
    {
        var command = input["command"].GetString()!;
        var timeout = input.TryGetValue("timeout", out var t) ? t.GetInt32() : 30;

        // 基础安全检查
        string[] dangerous = ["rm -rf /", "mkfs", "> /dev/sd", "dd if="];
        foreach (var pattern in dangerous)
        {
            if (command.Contains(pattern))
                return $"Error: Refused to run dangerous command containing '{pattern}'";
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd",
                Arguments = $"/c {command}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = _workspaceDir
            };

            using var process = System.Diagnostics.Process.Start(psi)!;
            process.WaitForExit(timeout * 1000);

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();

            if (!process.HasExited)
            {
                process.Kill();
                return $"Error: Command timed out after {timeout}s";
            }

            var result = "";
            if (!string.IsNullOrEmpty(output))
                result += output;
            if (!string.IsNullOrEmpty(error))
                result += (result.Length > 0 ? "\n--- stderr ---\n" : "") + error;
            if (process.ExitCode != 0)
                result += $"\n[exit code: {process.ExitCode}]";

            return Truncate(result.Length > 0 ? result : "[no output]");
        }
        catch (Exception exc)
        {
            return $"Error: {exc.Message}";
        }
    }

    private string ReadFileTool(Dictionary<string, JsonElement> input)
    {
        var filePath = input["file_path"].GetString()!;
        try
        {
            var target = SafePath(filePath);
            if (!File.Exists(target))
                return $"Error: File not found: {filePath}";
            
            var content = File.ReadAllText(target);
            return Truncate(content);
        }
        catch (Exception exc)
        {
            return $"Error: {exc.Message}";
        }
    }

    private string WriteFileTool(Dictionary<string, JsonElement> input)
    {
        var filePath = input["file_path"].GetString()!;
        var content = input["content"].GetString()!;

        try
        {
            var target = SafePath(filePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, content);
            return $"Successfully wrote {content.Length} chars to {filePath}";
        }
        catch (Exception exc)
        {
            return $"Error: {exc.Message}";
        }
    }

    private string EditFileTool(Dictionary<string, JsonElement> input)
    {
        var filePath = input["file_path"].GetString()!;
        var oldString = input["old_string"].GetString()!;
        var newString = input["new_string"].GetString()!;

        try
        {
            var target = SafePath(filePath);
            if (!File.Exists(target))
                return $"Error: File not found: {filePath}";

            var content = File.ReadAllText(target);
            var count = content.Split(oldString).Length - 1;

            if (count == 0)
                return "Error: old_string not found in file. Make sure it matches exactly.";
            if (count > 1)
                return $"Error: old_string found {count} times. It must be unique. Provide more surrounding context.";

            var newContent = content.Replace(oldString, newString);
            File.WriteAllText(target, newContent);
            return $"Successfully edited {filePath}";
        }
        catch (Exception exc)
        {
            return $"Error: {exc.Message}";
        }
    }

    private string SafePath(string raw)
    {
        var target = Path.GetFullPath(Path.Combine(_workspaceDir, raw));
        if (!target.StartsWith(Path.GetFullPath(_workspaceDir)))
            throw new Exception($"Path traversal blocked: {raw} resolves outside WORKDIR");
        return target;
    }

    private string Truncate(string text)
    {
        if (text.Length <= MaxToolOutput)
            return text;
        return text[..MaxToolOutput] + $"\n... [truncated, {text.Length} total chars]";
    }

    private static string GetInputSummary(Dictionary<string, JsonElement> input)
    {
        if (input.TryGetValue("command", out var cmd))
            return cmd.GetString() ?? "";
        if (input.TryGetValue("file_path", out var fp))
            return fp.GetString() ?? "";
        return "";
    }

    /// <summary>
    /// 将工具定义转换为 DeepSeek/OpenAI 格式
    /// </summary>
    public static List<Claw0.Common.Tool> ConvertToDeepSeekTools(IReadOnlyList<ToolDefinition> definitions)
    {
        return definitions.Select(d => new Claw0.Common.Tool
        {
            Function = new Claw0.Common.Function
            {
                Name = d.Name,
                Description = d.Description,
                Parameters = new Claw0.Common.InputSchema
                {
                    Type = d.InputSchema.Type,
                    Properties = d.InputSchema.Properties.ToDictionary(
                        p => p.Key,
                        p => new Claw0.Common.PropertyDefinition
                        {
                            Type = p.Value.Type,
                            Description = p.Value.Description
                        }
                    ),
                    Required = d.InputSchema.Required
                }
            }
        }).ToList();
    }
}
