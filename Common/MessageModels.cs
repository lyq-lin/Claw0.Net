using System.Text.Json;
using System.Text.Json.Serialization;

namespace Claw0.Common;

/// <summary>
/// 角色类型
/// </summary>
public enum RoleType
{
    User,
    Assistant
}

/// <summary>
/// 工具选择类型
/// </summary>
public enum ToolChoiceType
{
    Auto,
    Any,
    Tool
}

/// <summary>
/// 工具选择
/// </summary>
public class ToolChoice
{
    public ToolChoiceType Type { get; set; } = ToolChoiceType.Auto;
    public string? Name { get; set; }
}

/// <summary>
/// 消息模型
/// </summary>
public class Message
{
    [JsonPropertyName("role")]
    public RoleType Role { get; set; }
    
    [JsonPropertyName("content")]
    public object Content { get; set; }

    public Message(RoleType role, string content)
    {
        Role = role;
        Content = content;
    }

    public Message(RoleType role, List<ContentBase> content)
    {
        Role = role;
        Content = content;
    }
}

/// <summary>
/// 内容基类
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextContent), "text")]
[JsonDerivedType(typeof(ToolUseContent), "tool_use")]
[JsonDerivedType(typeof(ToolResultContent), "tool_result")]
public abstract class ContentBase
{
    [JsonPropertyName("type")]
    public abstract string Type { get; }
}

/// <summary>
/// 文本内容
/// </summary>
public class TextContent : ContentBase
{
    [JsonIgnore]
    public override string Type => "text";
    
    [JsonPropertyName("text")]
    public required string Text { get; set; }
}

/// <summary>
/// 工具调用内容
/// </summary>
public class ToolUseContent : ContentBase
{
    [JsonIgnore]
    public override string Type => "tool_use";
    
    [JsonPropertyName("id")]
    public required string Id { get; set; }
    
    [JsonPropertyName("name")]
    public required string Name { get; set; }
    
    [JsonPropertyName("input")]
    public object? Input { get; set; }
}

/// <summary>
/// 工具结果内容
/// </summary>
public class ToolResultContent : ContentBase
{
    [JsonIgnore]
    public override string Type => "tool_result";
    
    [JsonPropertyName("tool_use_id")]
    public required string ToolUseId { get; set; }
    
    [JsonPropertyName("content")]
    public object? Content { get; set; }
}

/// <summary>
/// 系统消息
/// </summary>
public class SystemMessage
{
    [JsonPropertyName("text")]
    public string Text { get; set; }

    public SystemMessage(string text)
    {
        Text = text;
    }
}

/// <summary>
/// 工具定义 (用于 DeepSeek/OpenAI API)
/// </summary>
public class Tool
{
    [JsonPropertyName("type")]
    public string Type => "function";
    
    [JsonPropertyName("function")]
    public required Function Function { get; set; }
}

/// <summary>
/// 函数定义
/// </summary>
public class Function
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }
    
    [JsonPropertyName("description")]
    public required string Description { get; set; }
    
    [JsonPropertyName("parameters")]
    public required InputSchema Parameters { get; set; }

    public Function() { }

    public Function(string name, string description, InputSchema parameters)
    {
        Name = name;
        Description = description;
        Parameters = parameters;
    }
}

/// <summary>
/// 输入 Schema
/// </summary>
public class InputSchema
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("properties")]
    public required Dictionary<string, PropertyDefinition> Properties { get; set; }

    [JsonPropertyName("required")]
    public List<string>? Required { get; set; }
}

/// <summary>
/// 属性定义
/// </summary>
public class PropertyDefinition
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [JsonPropertyName("description")]
    public required string Description { get; set; }
}

/// <summary>
/// 消息参数
/// </summary>
public class MessageParameters
{
    public string Model { get; set; } = "deepseek-chat";
    public int MaxTokens { get; set; } = 4096;
    public List<Message> Messages { get; set; } = new();
    public List<Tool>? Tools { get; set; }
    public ToolChoice? ToolChoice { get; set; }
    public List<SystemMessage>? System { get; set; }
}

/// <summary>
/// 消息响应 (适配 DeepSeek 响应)
/// </summary>
public class MessageResponse
{
    public string? Id { get; set; }
    public string? Model { get; set; }
    public string? StopReason { get; set; }
    public List<ContentBase> Content { get; set; } = new();
}
