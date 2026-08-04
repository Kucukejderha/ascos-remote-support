using System.Collections.Concurrent;
using System.Net.WebSockets;

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

    public bool TryAttach(string sessionId, string role, WebSocket socket)
    {
        var pair = _pairs.GetOrAdd(sessionId, _ => new Pair());
        lock (pair.Gate)
        {
            var target = role == "host" ? pair.Host : pair.Guest;
            if (target is { State: WebSocketState.Open }) return false;
            if (role == "host") pair.Host = socket;
            else pair.Guest = socket;
            return true;
        }
    }

    public WebSocket? GetPeer(string sessionId, string role)
    {
        if (!_pairs.TryGetValue(sessionId, out var pair)) return null;
        lock (pair.Gate) return role == "host" ? pair.Guest : pair.Host;
    }

    public void Detach(string sessionId, string role, WebSocket socket)
    {
        if (!_pairs.TryGetValue(sessionId, out var pair)) return;
        lock (pair.Gate)
        {
            if (role == "host" && ReferenceEquals(pair.Host, socket)) pair.Host = null;
            if (role == "guest" && ReferenceEquals(pair.Guest, socket)) pair.Guest = null;
            if (pair.Host is null && pair.Guest is null) _pairs.TryRemove(sessionId, out _);
        }
    }
}
