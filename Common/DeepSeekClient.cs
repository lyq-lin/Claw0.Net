using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Claw0.Common;

/// <summary>
/// DeepSeek API 客户端 - 使用 HttpClient 直接调用 API (OpenAI 兼容格式)
/// </summary>
public class DeepSeekClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public DeepSeekClient(string apiKey, string? baseUrl = null)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl ?? "https://api.deepseek.com/v1")
        };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// 发送消息到 DeepSeek API
    /// </summary>
    public async Task<DeepSeekResponse> ChatCompletionAsync(MessageParameters parameters, CancellationToken cancellationToken = default)
    {
        var request = new DeepSeekRequest
        {
            Model = parameters.Model,
            MaxTokens = parameters.MaxTokens,
            Messages = ConvertMessages(parameters.Messages),
            Tools = parameters.Tools,
            ToolChoice = parameters.ToolChoice?.Type == ToolChoiceType.Auto ? "auto" : null
        };

        // 如果有 system prompt，转换为 system message 放在开头
        if (parameters.System?.FirstOrDefault() is { } systemMsg)
        {
            request.Messages.Insert(0, new ChatMessage { Role = "system", Content = systemMsg.Text });
        }

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("/chat/completions", content, cancellationToken);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new DeepSeekApiException($"API request failed: {response.StatusCode} - {responseJson}");
        }

        var result = JsonSerializer.Deserialize<DeepSeekResponse>(responseJson, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        if (result == null)
        {
            throw new DeepSeekApiException("Failed to deserialize API response");
        }

        return result;
    }

    private static List<ChatMessage> ConvertMessages(List<Message> messages)
    {
        var result = new List<ChatMessage>();
        
        foreach (var msg in messages)
        {
            if (msg.Content is string text)
            {
                result.Add(new ChatMessage 
                { 
                    Role = msg.Role == RoleType.User ? "user" : "assistant", 
                    Content = text 
                });
            }
            else if (msg.Content is List<ContentBase> contents)
            {
                // 处理包含 tool_use 或 tool_result 的消息
                var message = new ChatMessage
                {
                    Role = msg.Role == RoleType.User ? "user" : "assistant",
                    Content = null,
                    ToolCalls = null,
                    ToolCallId = null
                };

                var toolCalls = new List<ToolCall>();
                foreach (var content in contents)
                {
                    switch (content)
                    {
                        case TextContent txt:
                            message.Content = txt.Text;
                            break;
                        case ToolUseContent toolUse:
                            toolCalls.Add(new ToolCall
                            {
                                Id = toolUse.Id,
                                Type = "function",
                                Function = new FunctionCall
                                {
                                    Name = toolUse.Name,
                                    Arguments = toolUse.Input?.ToString() ?? "{}"
                                }
                            });
                            break;
                        case ToolResultContent toolResult:
                            // Tool result 作为独立的 user message
                            result.Add(new ChatMessage
                            {
                                Role = "tool",
                                ToolCallId = toolResult.ToolUseId,
                                Content = toolResult.Content?.ToString() ?? ""
                            });
                            break;
                    }
                }
                
                if (toolCalls.Count > 0)
                {
                    message.ToolCalls = toolCalls;
                    message.Content = null;
                }
                
                if (message.Content != null || message.ToolCalls != null)
                {
                    result.Add(message);
                }
            }
        }
        
        return result;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class DeepSeekApiException : Exception
{
    public DeepSeekApiException(string message) : base(message) { }
    public DeepSeekApiException(string message, Exception inner) : base(message, inner) { }
}

// DeepSeek API 请求/响应模型

public class DeepSeekRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }
    
    [JsonPropertyName("messages")]
    public required List<ChatMessage> Messages { get; set; }
    
    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 4096;
    
    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<Tool>? Tools { get; set; }
    
    [JsonPropertyName("tool_choice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolChoice { get; set; }
}

public class ChatMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; set; }
    
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; set; }
    
    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ToolCall>? ToolCalls { get; set; }
    
    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; set; }
}

public class ToolCall
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }
    
    [JsonPropertyName("type")]
    public required string Type { get; set; }
    
    [JsonPropertyName("function")]
    public required FunctionCall Function { get; set; }
}

public class FunctionCall
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }
    
    [JsonPropertyName("arguments")]
    public required string Arguments { get; set; }
}

public class DeepSeekResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    
    [JsonPropertyName("model")]
    public string? Model { get; set; }
    
    [JsonPropertyName("choices")]
    public List<Choice>? Choices { get; set; }
    
    [JsonPropertyName("usage")]
    public Usage? Usage { get; set; }
    
    public string? StopReason => Choices?.FirstOrDefault()?.FinishReason;
    
    public List<ContentBase> Content 
    { 
        get
        {
            var message = Choices?.FirstOrDefault()?.Message;
            if (message == null) return new List<ContentBase>();
            
            var result = new List<ContentBase>();
            
            // 添加文本内容
            if (!string.IsNullOrEmpty(message.Content))
            {
                result.Add(new TextContent { Text = message.Content });
            }
            
            // 添加 tool calls
            if (message.ToolCalls != null)
            {
                foreach (var tc in message.ToolCalls)
                {
                    result.Add(new ToolUseContent 
                    { 
                        Id = tc.Id, 
                        Name = tc.Function.Name, 
                        Input = tc.Function.Arguments 
                    });
                }
            }
            
            return result;
        }
    }
}

public class Choice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }
    
    [JsonPropertyName("message")]
    public ChatMessage? Message { get; set; }
    
    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

public class Usage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }
    
    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }
    
    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}
