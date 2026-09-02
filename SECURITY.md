# Security model

- The host must display a native Windows consent dialog for every session.
- No unattended access, hidden mode, secure-desktop bypass, credential capture, clipboard, or file transfer is implemented.
- The local user can terminate control at any time; consent expires after 15 minutes.
- Device authentication uses ECDSA P-256 signed challenges. Support codes are random, rate-limited, expire after 10 minutes, and are atomically consumed by the first successful redemption. Network reconnects use the session-scoped guest token instead of redeeming the support code again; ending the local host session invalidates that token.
- Host and guest WebSockets are separately authenticated and scoped to one session.
- Browser guest tokens are carried as a WebSocket subprotocol rather than a URL query value.
- Input messages are allow-listed, size-limited, coordinate-limited, capped at 240 events/second, and ignored before explicit consent.
- The interactive input pipe is ACL-restricted to the interactive user (and SYSTEM); the connecting process identity and image name are verified, and per-connection sequence numbers reject replay.
- Production deployment must use HTTPS/WSS. Plain HTTP is for loopback development only.

## Known MVP limits

- Screen transport uses 960×540 capture at up to 10 FPS, unchanged-frame suppression, lossless XOR deltas, two-second keyframes, and gzip compression. DXGI plus hardware H.264/WebRTC remains the future path for high-motion video.
- Server session state is ephemeral. Restarting the server terminates active support sessions.
- Windows UAC secure desktop and the lock screen are deliberately not controlled.
- Binaries must be Authenticode-signed before broad distribution.
