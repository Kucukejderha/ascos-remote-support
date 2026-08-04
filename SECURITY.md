# Security model

- The host must display a native Windows consent dialog for every session.
- No unattended access, hidden mode, secure-desktop bypass, credential capture, clipboard, or file transfer is implemented.
- The local user can terminate control at any time; consent expires after 15 minutes.
- Device authentication uses ECDSA P-256 signed challenges. Support codes are random and rate-limited; once the host is connected, the same code remains usable until the local host ends the session. Each redemption rotates the guest token.
- Host and guest WebSockets are separately authenticated and scoped to one session.
- Browser guest tokens are carried as a WebSocket subprotocol rather than a URL query value.
- Input messages are allow-listed, size-limited, coordinate-limited, capped at 240 events/second, and ignored before explicit consent.
- Service IPC messages are length-limited, HMAC authenticated, and replay protected. Production pipe ACLs contain only LocalSystem and the selected interactive user SID.
- Production deployment must use HTTPS/WSS. Plain HTTP is for loopback development only.

## Known MVP limits

- Screen transport uses 960×540 capture at up to 10 FPS, unchanged-frame suppression, lossless XOR deltas, two-second keyframes, and gzip compression. DXGI plus hardware H.264/WebRTC remains the future path for high-motion video.
- Server session state is ephemeral. Restarting the server terminates active support sessions.
- Windows UAC secure desktop and the lock screen are deliberately not controlled.
- Binaries must be Authenticode-signed before broad distribution.
