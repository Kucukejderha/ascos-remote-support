using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace RemoteSupport.Signaling;

public sealed class SecurityStore
{
    private sealed record Device(string Id, byte[] PublicKey, string DisplayName);
    private sealed record Challenge(string Id, string DeviceId, byte[] Nonce, DateTimeOffset ExpiresAt);
    private sealed record AccessGrant(string DeviceId, DateTimeOffset ExpiresAt);
    private sealed record SupportCode(string Code, string SessionId, string HostDeviceId, DateTimeOffset ExpiresAt);
    private sealed class SupportSession(string id, string code, string hostDeviceId, DateTimeOffset expiresAt)
    {
        public object Gate { get; } = new();
        public string Id { get; } = id;
        public string Code { get; } = code;
        public string HostDeviceId { get; } = hostDeviceId;
        public DateTimeOffset ExpiresAt { get; private set; } = expiresAt;
        public bool HostConnected { get; private set; }
        public string? GuestToken { get; set; }

        public void MarkHostConnected()
        {
            HostConnected = true;
            ExpiresAt = DateTimeOffset.MaxValue;
        }
    }

    private readonly ConcurrentDictionary<string, Device> _devices = new();
    private readonly ConcurrentDictionary<string, Challenge> _challenges = new();
    private readonly ConcurrentDictionary<string, AccessGrant> _tokens = new();
    private readonly ConcurrentDictionary<string, SupportCode> _codes = new();
    private readonly ConcurrentDictionary<string, SupportSession> _sessions = new();
    private readonly TimeProvider _clock;

    public SecurityStore(TimeProvider clock) => _clock = clock;

    public string Register(string publicKeySpkiBase64, string displayName)
    {
        byte[] publicKey;
        try { publicKey = Convert.FromBase64String(publicKeySpkiBase64); }
        catch (FormatException) { throw new ArgumentException("Invalid public key encoding."); }

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(publicKey, out var read);
        if (read != publicKey.Length) throw new ArgumentException("Invalid public key.");

        var fingerprint = SHA256.HashData(publicKey);
        var id = Convert.ToHexString(fingerprint[..8]);
        _devices[id] = new Device(id, publicKey, displayName.Trim()[..Math.Min(80, displayName.Trim().Length)]);
        return id;
    }

    public ChallengeResponse CreateChallenge(string deviceId)
    {
        if (!_devices.ContainsKey(deviceId)) throw new KeyNotFoundException();
        var challenge = new Challenge(Guid.NewGuid().ToString("N"), deviceId, RandomNumberGenerator.GetBytes(32), _clock.GetUtcNow().AddMinutes(2));
        _challenges[challenge.Id] = challenge;
        return new(challenge.Id, Convert.ToBase64String(challenge.Nonce), challenge.ExpiresAt);
    }

    public TokenResponse Verify(string deviceId, string challengeId, string signatureBase64)
    {
        if (!_challenges.TryRemove(challengeId, out var challenge) || challenge.DeviceId != deviceId || challenge.ExpiresAt <= _clock.GetUtcNow())
            throw new UnauthorizedAccessException();
        if (!_devices.TryGetValue(deviceId, out var device)) throw new UnauthorizedAccessException();

        byte[] signature;
        try { signature = Convert.FromBase64String(signatureBase64); }
        catch (FormatException) { throw new UnauthorizedAccessException(); }
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(device.PublicKey, out _);
        if (!ecdsa.VerifyData(challenge.Nonce, signature, HashAlgorithmName.SHA256)) throw new UnauthorizedAccessException();

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expires = _clock.GetUtcNow().AddMinutes(15);
        _tokens[token] = new(deviceId, expires);
        return new(token, expires);
    }

    public bool TryAuthenticate(string? authorization, out string deviceId)
    {
        deviceId = string.Empty;
        if (authorization is null || !authorization.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
        var token = authorization[7..];
        if (!_tokens.TryGetValue(token, out var grant) || grant.ExpiresAt <= _clock.GetUtcNow()) return false;
        deviceId = grant.DeviceId;
        return true;
    }

    public CreateSupportCodeResponse CreateCode(string deviceId)
    {
        string code;
        do { code = RandomNumberGenerator.GetInt32(0, 1_000_000_000).ToString("D9"); }
        while (_codes.ContainsKey(code));

        var sessionId = Guid.NewGuid().ToString("N");
        var expires = _clock.GetUtcNow().AddMinutes(10);
        _codes[code] = new(code, sessionId, deviceId, expires);
        _sessions[sessionId] = new(sessionId, code, deviceId, _clock.GetUtcNow().AddMinutes(15));
        return new(sessionId, code, expires);
    }

    public RedeemSupportCodeResponse Redeem(string code)
    {
        if (!_codes.TryGetValue(code, out var item)) throw new UnauthorizedAccessException();
        if (!_sessions.TryGetValue(item.SessionId, out var session)) throw new UnauthorizedAccessException();
        lock (session.Gate)
        {
            if (!session.HostConnected && item.ExpiresAt <= _clock.GetUtcNow()) throw new UnauthorizedAccessException();
            var guestToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            session.GuestToken = guestToken;
            return new(session.Id, session.HostDeviceId, guestToken, session.ExpiresAt);
        }
    }

    public void MarkHostConnected(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return;
        lock (session.Gate) session.MarkHostConnected();
    }

    public void EndSession(string sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out var session)) return;
        _codes.TryRemove(session.Code, out _);
    }

    public bool TryAuthorizeSession(string sessionId, string role, string? authorization)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return false;
        lock (session.Gate)
        {
            if (!session.HostConnected && session.ExpiresAt <= _clock.GetUtcNow()) return false;
            if (role == "host")
                return TryAuthenticate(authorization, out var deviceId) && deviceId == session.HostDeviceId;
            if (role != "guest" || session.GuestToken is null || authorization is null || !authorization.StartsWith("Bearer ", StringComparison.Ordinal))
                return false;
            var supplied = System.Text.Encoding.ASCII.GetBytes(authorization[7..]);
            var expected = System.Text.Encoding.ASCII.GetBytes(session.GuestToken);
            return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected);
        }
    }

    public bool TryAuthorizeGuestProtocol(string sessionId, string? protocol)
    {
        const string prefix = "ascos.guest.";
        if (protocol is null || !protocol.StartsWith(prefix, StringComparison.Ordinal)) return false;
        return TryAuthorizeSession(sessionId, "guest", $"Bearer {protocol[prefix.Length..]}");
    }
}
