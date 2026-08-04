# ASCOS Remote Support — Release Status

Release date: 2026-08-04

## Live service

- Operator UI: https://45.87.173.201.nip.io/operator
- Health: https://45.87.173.201.nip.io/health
- Host server argument: `https://45.87.173.201.nip.io`
- Deployment path: `/opt/ascos-remote-support`
- Containers: `ascos_remote_support`, `ascos_remote_support_proxy`
- TLS: automatic Let's Encrypt certificate through Caddy
- Audit: `/opt/ascos-remote-support/deploy/data/audit.jsonl`

## Verified release behavior

- Release build: zero warnings, zero errors
- Windows package executable starts and prints help
- Self-contained Windows installer installs the embedded host payload and creates the desktop shortcut without PowerShell interaction
- HTTPS device registration and P-256 signed challenge authentication
- Five-minute, single-use, nine-digit support code
- Separate host and guest WSS authentication
- Browser WebSocket subprotocol token handling
- Host-to-guest 921,605-byte frame relay without corruption
- Authenticated Named Pipe framing and replay rejection
- Windows DPAPI device identity round-trip
- Input rejected before explicit local consent
- Persistent security audit entries
- Existing ASCOS API (`:3000`) and dashboard (`:3001`) remain healthy

## Product boundary

This release is the completed consent-first instant-support MVP. It intentionally excludes unattended access, hidden persistence, UAC secure-desktop control, clipboard transfer, and file transfer. Video uses a functional 640×360, 5 FPS raw relay; DXGI/H.264/WebRTC is a future performance upgrade rather than a requirement for this release.
