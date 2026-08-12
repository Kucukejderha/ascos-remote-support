# RotaLink signed control runtime

The alpha.14 diagnostics eliminated transport, coordinates, session selection, IPC, WinSta0 attachment and input-desktop switching as the source of the control failure. Windows rejects `SendInput` with `ERROR_ACCESS_DENIED (5)`.

Alpha.15 replaces the temporary ProgramData runtime with a persistent `%ProgramFiles%\RotaLink\Runtime\1.1.0-alpha.15` runtime. The embedded service and UIAccess SessionHelper must both carry a trusted Authenticode signature. RotaLink validates each extracted executable with `WinVerifyTrust` before registering or starting the service.

Use `scripts/build-signed-client.ps1` with either a PFX path (password supplied only through `ROTALINK_SIGNING_PASSWORD`) or a certificate-store thumbprint. The script signs in dependency order: SessionHelper, Service, then the client containing their signed bytes. Unsigned development builds are deliberately not distributable.
