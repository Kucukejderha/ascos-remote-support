namespace RemoteSupport.Signaling;

public sealed record RegisterDeviceRequest(string PublicKeySpkiBase64, string DisplayName);
public sealed record RegisterDeviceResponse(string DeviceId);
public sealed record ChallengeResponse(string ChallengeId, string NonceBase64, DateTimeOffset ExpiresAt);
public sealed record VerifyChallengeRequest(string ChallengeId, string SignatureBase64);
public sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAt);
public sealed record CreateSupportCodeResponse(string SessionId, string Code, DateTimeOffset ExpiresAt);
public sealed record RedeemSupportCodeRequest(string Code);
public sealed record RedeemSupportCodeResponse(string SessionId, string HostDeviceId, string GuestToken, DateTimeOffset ExpiresAt);
