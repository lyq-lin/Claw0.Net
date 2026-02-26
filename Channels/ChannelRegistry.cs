namespace Claw0.Channels;

/// <summary>
/// 通道注册表 - 管理所有通道插件
/// </summary>
public class ChannelRegistry
{
    private readonly Dictionary<string, IChannel> _channels = new();

    public void Register(IChannel channel)
    {
        _channels[channel.Id] = channel;
    }

    public IChannel? Get(string channelId)
    {
        return _channels.TryGetValue(channelId, out var channel) ? channel : null;
    }

    public IReadOnlyCollection<IChannel> AllChannels => _channels.Values;

    public async Task<Dictionary<string, InboundMessage>> PollAllAsync()
    {
        var results = new Dictionary<string, InboundMessage>();
        foreach (var channel in _channels.Values)
        {
            var message = await channel.ReceiveAsync();
            if (message != null)
                results[channel.Id] = message;
        }
        return results;
    }

    public async Task SendAsync(string channelId, string recipient, string text, string? threadId = null)
    {
        if (_channels.TryGetValue(channelId, out var channel))
            await channel.SendAsync(recipient, text, threadId);
    }
}
