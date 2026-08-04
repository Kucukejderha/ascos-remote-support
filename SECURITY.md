# Security model

- The host must display a native Windows consent dialog for every session.
- No unattended access, hidden mode, secure-desktop bypass, credential capture, clipboard, or file transfer is implemented.
- The local user can terminate control at any time; consent expires after 15 minutes.
- Device authentication uses ECDSA P-256 signed challenges. Support codes are random, single-use, expire after five minutes, and are rate-limited.
- Host and guest WebSockets are separately authenticated and scoped to one session.
- Browser guest tokens are carried as a WebSocket subprotocol rather than a URL query value.
- Input messages are allow-listed, size-limited, coordinate-limited, capped at 240 events/second, and ignored before explicit consent.
- Service IPC messages are length-limited, HMAC authenticated, and replay protected. Production pipe ACLs contain only LocalSystem and the selected interactive user SID.
- Production deployment must use HTTPS/WSS. Plain HTTP is for loopback development only.

## Known MVP limits

- Screen transport is a 640×360, 5 FPS raw BGRA relay. It is bandwidth-heavy and is not a replacement for DXGI + hardware H.264/WebRTC.
- Server session state is ephemeral. Restarting the server terminates active support sessions.
- Windows UAC secure desktop and the lock screen are deliberately not controlled.
- Binaries must be Authenticode-signed before broad distribution.
