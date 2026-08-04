namespace RemoteSupport.SessionAgent;

public enum ConsentStatus { Pending, Approved, Denied, Stopped, Expired }

public sealed class ConsentStateMachine
{
    private readonly object _gate = new();
    private Guid _sessionId;
    private ConsentStatus _status = ConsentStatus.Stopped;
    private DateTimeOffset _expiresAt;

    public ConsentStateMachine() { }

    public void Request(Guid sessionId, TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromMinutes(30)) throw new ArgumentOutOfRangeException(nameof(lifetime));
        lock (_gate)
        {
            _sessionId = sessionId;
            _expiresAt = DateTimeOffset.UtcNow.Add(lifetime);
            _status = ConsentStatus.Pending;
        }
    }

    public bool Decide(Guid sessionId, bool approved)
    {
        lock (_gate)
        {
            RefreshExpiry();
            if (_sessionId != sessionId || _status != ConsentStatus.Pending) return false;
            _status = approved ? ConsentStatus.Approved : ConsentStatus.Denied;
            return true;
        }
    }

    public bool IsControlAllowed(Guid sessionId)
    {
        lock (_gate)
        {
            RefreshExpiry();
            return _sessionId == sessionId && _status == ConsentStatus.Approved;
        }
    }

    public void Stop(Guid sessionId)
    {
        lock (_gate) if (_sessionId == sessionId) _status = ConsentStatus.Stopped;
    }

    private void RefreshExpiry()
    {
        if ((_status == ConsentStatus.Pending || _status == ConsentStatus.Approved) && _expiresAt <= DateTimeOffset.UtcNow)
            _status = ConsentStatus.Expired;
    }
}
