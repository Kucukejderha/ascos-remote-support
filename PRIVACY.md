# Privacy policy

Last updated: 2 September 2026

RotaLink is a consent-based remote support application maintained by Ali Haydar
Sultan Eroglu. Questions about this policy may be sent to `ali@rotaniz.com`.

## Data processed during use

When RotaLink connects to the configured signaling service, it processes the
following information as required to establish and operate a support session:

- a generated device identifier, device display name and public authentication key;
- short-lived authentication challenges, session identifiers, access tokens and the
  support code deliberately shared by the local user;
- the visible desktop frames sent during an active, locally approved session;
- mouse and keyboard control messages sent by the connected operator;
- IP addresses, timestamps, event names, device/session identifiers and limited
  failure diagnostics recorded in the server security audit log; and
- local diagnostic logs created on the supported computer for troubleshooting.

The signaling service relays screen and control traffic between the supported
computer and operator. It is not designed to persist screen frames, keystrokes or
mouse events. Active session state is held in memory and is discarded when the
session ends or the service restarts. Security audit logs are retained and rotated
for abuse prevention, incident response and operational diagnosis. Local diagnostic
logs remain on the supported computer unless its user deliberately shares them.

## User control

Every support session is visible and requires local consent. The local user chooses
whether to share the displayed support code and may end the session by closing
RotaLink. The software does not implement unattended access, hidden operation,
credential capture, clipboard transfer, file transfer, UAC secure-desktop bypass or
lock-screen control.

## Network services

The official build connects to `https://ascos.rotaniz.com` for device registration,
support-code creation, update checks and support-session signaling. An operator who
receives the support code uses the corresponding web interface to establish the
session. The application does not transfer information to unrelated networked
systems unless the user, operator or administrator explicitly configures a different
RotaLink server.

## Source code and third parties

The source code is available at
https://github.com/Kucukejderha/ascos-remote-support. RotaLink uses Microsoft .NET
and the MIT-licensed `System.Memory` package. Those components remain subject to
their respective licenses and privacy terms.

## Contact and requests

Requests concerning server audit information may be sent to `ali@rotaniz.com`.
The requester may be asked for sufficient information to identify the relevant
session and verify that disclosure or deletion would not compromise another user,
security investigation or applicable legal obligation.
