# Rotaniz Remote Support — Phase 1

[Türkçe belge](README.tr.md)

Current release status and live endpoints are recorded in `STATUS.md`.

Consent-first remote support foundation for Windows 10/11 and the Rotaniz server.

## Components

- `server/RemoteSupport.Signaling`: ASP.NET Core signaling API, device registration, signed challenge authentication, short-lived support codes, IP rate limiting, and authenticated host/guest WebSocket relay.
- `client/RemoteSupport.Protocol`: cross-target (net8 + net48) binary codecs and IPC primitives; the net8 verification surface adds HMAC-authenticated envelopes with replay protection.
- `client/RemoteSupport.SessionAgent`: per-user process boundary for capture and approved input handling.
- `client/RemoteSupport.Service`: Windows Service boundary (lifecycle/device identity; Session 0 never captures the desktop).

Phase 1 deliberately does **not** enable unattended access, silent operation, file transfer, UAC secure-desktop control, or persistent remote input. Every remote-control session must be visible and locally approved.

## Usable MVP flow

1. Run the signaling server.
2. On the Windows computer receiving support, run `dotnet run --project client/RemoteSupport.SessionAgent -- https://your-ascos-host`.
3. Share the displayed 9-digit code; sharing the code is the user's consent for the session.
4. The operator opens `/operator`, enters the code, and controls the visible desktop.
5. The local user closes the RotaLink window at any time to terminate the session.

The current transport captures at 960×540 and targets 10 FPS. It skips unchanged frames, sends XOR delta frames between two-second lossless keyframes, and gzip-compresses every transmitted frame. The browser decodes frames in order before rendering, substantially reducing relay bandwidth while improving text clarity. A future DXGI/H.264/WebRTC transport can replace this codec without changing the authenticated session boundary.

The protocol provides both a `CurrentUserOnly` development pipe and a Windows service pipe whose protected ACL allows only `LocalSystem` plus the selected interactive user SID. The service device private key is P-256 and is persisted with Windows DPAPI `CurrentUser` scope; under the installed service this binds decryption to the service account.

## Run locally

```powershell
dotnet build AscosRemoteSupport.sln
dotnet run --project server/RemoteSupport.Signaling
```

The API listens on the URL configured by ASP.NET Core. `GET /health` returns service status.

With the server running locally, verify signed device registration, one-time code redemption, and bidirectional session pairing:

```powershell
dotnet run --project tests/RemoteSupport.Smoke -- http://127.0.0.1:5188
```

The smoke test also verifies binary codec round-trips, coordinate boundaries, IPC replay rejection, the security store's expiry and single-use behavior, and DPAPI device-key persistence on Windows.

## Build the Windows package

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build.ps1
```

For the full release (host payload zip, self-contained installer, and a release manifest with SHA-256 hashes), pass `-Full`. For end users, distribute only `artifacts/RotaLink-Kurulum.exe`. It is a self-contained, double-click installer with the Rotaniz server address embedded; it requires no PowerShell or preinstalled .NET runtime.

For zero-install support, distribute `artifacts/RotaLink.exe`. It is a self-contained single executable: double-clicking it immediately opens the support-code and consent flow without copying files or creating shortcuts.

Extract `artifacts/Rotaniz-Remote-Support-Windows.zip` and run:

```powershell
.\Install-ASCOS-RemoteSupport.ps1 -ServerUrl https://45.87.173.201.nip.io
```

The installer is per-user, creates a visible desktop shortcut, and does not install hidden persistence or unattended access.

Before starting the Linux compose deployment, make `deploy/data` writable by the ASP.NET container user (`1654:1654`). Audit records are then retained in `deploy/data/audit.jsonl`.

## ASCOS deployment

Build `server/RemoteSupport.Signaling/Dockerfile` on the ASCOS host and expose it behind HTTPS. Do not expose the development HTTP endpoint publicly. Persistent device and session state is intentionally deferred until PostgreSQL migrations and key-rotation policy are approved.
