using System.Text.Json;

namespace Claw0.Routing;

/// <summary>
/// 路由器 - 管理绑定并解析消息路由
/// 
/// 路由优先级:
/// 1. 精确匹配: channel + peer
/// 2. 通配符匹配: channel + "*"
/// 3. 默认 Agent: "default"
/// </summary>
public class Router
{
    private readonly List<Binding> _bindings = new();
    private readonly string _storePath;
    private readonly string _defaultAgentId;

    public Router(string workspaceDir, string defaultAgentId = "main")
    {
        _defaultAgentId = defaultAgentId;
        var routingDir = Path.Combine(workspaceDir, ".routing");
        Directory.CreateDirectory(routingDir);
        _storePath = Path.Combine(routingDir, "bindings.json");
        LoadBindings();
    }

    private void LoadBindings()
    {
        if (!File.Exists(_storePath))
            return;

        try
        {
            var json = File.ReadAllText(_storePath);
            var bindings = JsonSerializer.Deserialize<List<Binding>>(json);
            if (bindings != null)
                _bindings.AddRange(bindings);
        }
        catch { /* 忽略加载错误 */ }
    }

    private void SaveBindings()
    {
        var json = JsonSerializer.Serialize(_bindings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_storePath, json);
    }

    /// <summary>
    /// 创建绑定
    /// </summary>
    public Binding CreateBinding(string agentId, string channel, string peer, int priority = 100)
    {
        // 检查是否已存在
        var existing = _bindings.FirstOrDefault(b => b.Matches(channel, peer) && b.AgentId == agentId);
        if (existing != null)
        {
            existing.Priority = priority;
            SaveBindings();
            return existing;
        }

        var binding = new Binding
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            AgentId = agentId,
            Channel = channel,
            Peer = peer,
            Priority = priority,
            Enabled = true
        };

        _bindings.Add(binding);
        SaveBindings();
        return binding;
    }

    /// <summary>
    /// 删除绑定
    /// </summary>
    public bool RemoveBinding(string bindingId)
    {
        var binding = _bindings.FirstOrDefault(b => b.Id == bindingId);
        if (binding == null)
            return false;

        _bindings.Remove(binding);
        SaveBindings();
        return true;
    }

    /// <summary>
    /// 解析路由 - 找到消息应该发送给哪个 Agent
    /// </summary>
    public BindingResult Resolve(string channel, string peer)
    {
        // 1. 查找精确匹配
        var binding = _bindings
            .Where(b => b.Enabled && b.Channel == channel && b.Peer == peer)
            .OrderBy(b => b.Priority)
            .FirstOrDefault();

        if (binding != null)
        {
            return new BindingResult
            {
                AgentId = binding.AgentId,
                SessionKey = $"{binding.AgentId}:{channel}:{peer}",
                Binding = binding
            };
        }

        // 2. 查找通配符匹配 (peer = "*")
        binding = _bindings
            .Where(b => b.Enabled && b.Channel == channel && b.Peer == "*")
            .OrderBy(b => b.Priority)
            .FirstOrDefault();

        if (binding != null)
        {
            return new BindingResult
            {
                AgentId = binding.AgentId,
                SessionKey = $"{binding.AgentId}:{channel}:{peer}",
                Binding = binding
            };
        }

        // 3. 使用默认 Agent
        return new BindingResult
        {
            AgentId = _defaultAgentId,
            SessionKey = $"{_defaultAgentId}:{channel}:{peer}",
            Binding = null
        };
    }

    /// <summary>
    /// 获取所有绑定
    /// </summary>
    public IReadOnlyList<Binding> GetAllBindings()
    {
        return _bindings.OrderBy(b => b.Priority).ToList();
    }

    /// <summary>
    /// 获取 Agent 的所有绑定
    /// </summary>
    public IReadOnlyList<Binding> GetBindingsForAgent(string agentId)
    {
        return _bindings.Where(b => b.AgentId == agentId).OrderBy(b => b.Priority).ToList();
    }

    /// <summary>
    /// 启用/禁用绑定
    /// </summary>
    public bool SetBindingEnabled(string bindingId, bool enabled)
    {
        var binding = _bindings.FirstOrDefault(b => b.Id == bindingId);
        if (binding == null)
            return false;

        binding.Enabled = enabled;
        SaveBindings();
        return true;
    }
}
