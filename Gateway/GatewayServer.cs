using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Spectre.Console;

namespace Claw0.Gateway;

/// <summary>
/// Gateway Server - WebSocket + JSON-RPC 网关
/// 
/// 架构:
///   Client WebSocket --> GatewayServer --> Agent Loop
///                               |
///                         JSON-RPC dispatch
/// </summary>
public class GatewayServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly Dictionary<string, Func<JsonElement?, Task<object?>>> _handlers = new();
    private readonly List<WebSocket> _connectedClients = new();
    private bool _isRunning;
    private CancellationTokenSource? _cts;

    public int Port { get; }
    public bool IsRunning => _isRunning;

    public GatewayServer(int port = 8080)
    {
        Port = port;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public void RegisterHandler(string method, Func<JsonElement?, Task<object?>> handler)
    {
        _handlers[method] = handler;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener.Start();
        _isRunning = true;

        AnsiConsole.MarkupLine($"[blue][Gateway] Server started on ws://localhost:{Port}/ws[/]");

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = HandleRequestAsync(context, _cts.Token);
            }
            catch (HttpListenerException) when (_cts.Token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red][Gateway] Error: {ex.Message.EscapeMarkup()}[/]");
            }
        }
    }

    public void Stop()
    {
        _isRunning = false;
        _cts?.Cancel();
        _listener.Stop();
        
        // 关闭所有 WebSocket 连接
        lock (_connectedClients)
        {
            foreach (var client in _connectedClients)
            {
                try { client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None).Wait(); } catch { }
            }
            _connectedClients.Clear();
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        if (context.Request.Url?.AbsolutePath == "/ws" && context.Request.IsWebSocketRequest)
        {
            await HandleWebSocketAsync(context, ct);
        }
        else if (context.Request.Url?.AbsolutePath == "/health")
        {
            await HandleHealthCheckAsync(context);
        }
        else
        {
            context.Response.StatusCode = 404;
            context.Response.Close();
        }
    }

    private async Task HandleWebSocketAsync(HttpListenerContext context, CancellationToken ct)
    {
        var wsContext = await context.AcceptWebSocketAsync(null);
        var webSocket = wsContext.WebSocket;

        lock (_connectedClients)
            _connectedClients.Add(webSocket);

        AnsiConsole.MarkupLine($"[blue][Gateway] Client connected. Total: {_connectedClients.Count}[/]");

        try
        {
            var buffer = new byte[4096];
            while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", ct);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await ProcessJsonRpcMessageAsync(webSocket, message);
                }
            }
        }
        catch (WebSocketException) { }
        catch (OperationCanceledException) { }
        finally
        {
            lock (_connectedClients)
                _connectedClients.Remove(webSocket);
            AnsiConsole.MarkupLine($"[blue][Gateway] Client disconnected. Total: {_connectedClients.Count}[/]");
        }
    }

    private async Task ProcessJsonRpcMessageAsync(WebSocket webSocket, string message)
    {
        try
        {
            var request = JsonSerializer.Deserialize<JsonRpcRequest>(message);
            if (request == null)
            {
                await SendErrorAsync(webSocket, null, -32700, "Parse error");
                return;
            }

            if (!_handlers.TryGetValue(request.Method, out var handler))
            {
                if (request.Id != null)
                    await SendErrorAsync(webSocket, request.Id, -32601, $"Method not found: {request.Method}");
                return;
            }

            try
            {
                var result = await handler(request.Params);
                
                // 只有请求 (有 id) 才发送响应
                if (request.Id != null)
                {
                    var response = new JsonRpcResponse
                    {
                        Id = request.Id,
                        Result = result != null ? JsonSerializer.SerializeToElement(result) : null
                    };
                    await SendMessageAsync(webSocket, response);
                }
            }
            catch (Exception ex)
            {
                if (request.Id != null)
                    await SendErrorAsync(webSocket, request.Id, -32603, ex.Message);
            }
        }
        catch (Exception ex)
        {
            await SendErrorAsync(webSocket, null, -32603, $"Internal error: {ex.Message}");
        }
    }

    private static async Task SendMessageAsync(WebSocket webSocket, object message)
    {
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        await webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            CancellationToken.None);
    }

    private static async Task SendErrorAsync(WebSocket webSocket, object? id, int code, string message)
    {
        var response = new JsonRpcResponse
        {
            Id = id ?? "null",
            Error = new JsonRpcError
            {
                Code = code,
                Message = message
            }
        };
        await SendMessageAsync(webSocket, response);
    }

    private static async Task HandleHealthCheckAsync(HttpListenerContext context)
    {
        var response = new { status = "ok", timestamp = DateTime.UtcNow };
        var json = JsonSerializer.Serialize(response);
        var bytes = Encoding.UTF8.GetBytes(json);
        
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = 200;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    public void Dispose()
    {
        Stop();
        _listener.Close();
        _cts?.Dispose();
    }
}
