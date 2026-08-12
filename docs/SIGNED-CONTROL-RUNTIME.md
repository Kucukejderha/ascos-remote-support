# RotaLink signed control runtime

The alpha.14 diagnostics eliminated transport, coordinates, session selection, IPC, WinSta0 attachment and input-desktop switching as the source of the control failure. Windows rejects `SendInput` with `ERROR_ACCESS_DENIED (5)`.

Alpha.16 uses a persistent `%ProgramFiles%\RotaLink\Runtime\1.1.0-alpha.16` runtime. The service launches SessionHelper as LocalSystem in the active interactive session, so the helper no longer requests UIAccess. The helper accepts IPC only from the RotaLink client process ID passed through the service. Production builds still require trusted Authenticode signatures and RotaLink validates each extracted executable with `WinVerifyTrust` before registering or starting the service.

Use `scripts/build-signed-client.ps1` with either a PFX path (password supplied only through `ROTALINK_SIGNING_PASSWORD`) or a certificate-store thumbprint. The script signs in dependency order: SessionHelper, Service, then the client containing their signed bytes. `build-light-client.ps1` creates an explicitly named unsigned development build for controlled testing only; it must not be published to customers.
