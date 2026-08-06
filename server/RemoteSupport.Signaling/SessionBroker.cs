using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading.Channels;

namespace RemoteSupport.Signaling;

public sealed class SessionBroker
{
    private sealed class Pair
    {
        public object Gate { get; } = new();
        public WebSocket? Host { get; set; }
        public WebSocket? Guest { get; set; }
    }

    private readonly ConcurrentDictionary<string, Pair> _pairs = new();
    private readonly ConcurrentDictionary<string, Channel<byte[]>> _video = new();

    private static string Key(string sessionId, string channel) => sessionId + ":" + channel;

    public bool TryAttach(string sessionId, string role, string channel, WebSocket socket)
    {
        var pair = _pairs.GetOrAdd(Key(sessionId, channel), _ => new Pair());
        lock (pair.Gate)
        {
            var target = role == "host" ? pair.Host : pair.Guest;
            if (target is { State: WebSocketState.Open }) return false;
            if (role == "host") pair.Host = socket;
            else pair.Guest = socket;
            return true;
        }
    }

    public WebSocket? GetPeer(string sessionId, string role, string channel)
    {
        if (!_pairs.TryGetValue(Key(sessionId, channel), out var pair)) return null;
        lock (pair.Gate) return role == "host" ? pair.Guest : pair.Host;
    }

    public void Detach(string sessionId, string role, string channel, WebSocket socket)
    {
        var key = Key(sessionId, channel);
        if (!_pairs.TryGetValue(key, out var pair)) return;
        lock (pair.Gate)
        {
            if (role == "host" && ReferenceEquals(pair.Host, socket)) pair.Host = null;
            if (role == "guest" && ReferenceEquals(pair.Guest, socket)) pair.Guest = null;
            if (pair.Host is null && pair.Guest is null) _pairs.TryRemove(key, out _);
        }
        if (channel == "video" && role == "host" && _video.TryRemove(sessionId, out var video))
            video.Writer.TryComplete();
    }

    public IReadOnlyList<WebSocket> GetAllPeers(string sessionId, WebSocket except)
    {
        var prefix = sessionId + ":";
        var peers = new List<WebSocket>();
        foreach (var entry in _pairs)
        {
            if (!entry.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            lock (entry.Value.Gate)
            {
                if (entry.Value.Host is { } host && !ReferenceEquals(host, except)) peers.Add(host);
                if (entry.Value.Guest is { } guest && !ReferenceEquals(guest, except)) peers.Add(guest);
            }
        }
        return peers;
    }

    public void PublishLatestVideo(string sessionId, byte[] frame)
    {
        var video = _video.GetOrAdd(sessionId, _ => Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        }));
        video.Writer.TryWrite(frame);
    }

    public async ValueTask<byte[]> ReadLatestVideoAsync(string sessionId, CancellationToken token)
    {
        var video = _video.GetOrAdd(sessionId, _ => Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        }));
        var frame = await video.Reader.ReadAsync(token);
        while (video.Reader.TryRead(out var newer)) frame = newer;
        return frame;
    }
}
