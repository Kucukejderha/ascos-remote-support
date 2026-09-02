# Code signing policy

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/).

## Project and source

- Project: RotaLink (ASCOS Remote Support)
- Source repository: https://github.com/Kucukejderha/ascos-remote-support
- Release downloads: https://github.com/Kucukejderha/ascos-remote-support/releases
- License: GNU Affero General Public License v3.0 only (`AGPL-3.0-only`)
- Privacy policy: [PRIVACY.md](PRIVACY.md)

Only binaries built from this repository's source code and version-controlled build
scripts may be submitted for signing. Signing requests must reference a specific
Git commit and a successful GitHub Actions build. Signed artifacts must not be
modified after signing.

## Team roles

- Authors and committers: [Ali Haydar Sultan Eroglu (`Kucukejderha`)](https://github.com/Kucukejderha)
- Reviewers: [Ali Haydar Sultan Eroglu (`Kucukejderha`)](https://github.com/Kucukejderha)
- Signing approvers: [Ali Haydar Sultan Eroglu (`Kucukejderha`)](https://github.com/Kucukejderha)

All members in these roles must use multi-factor authentication for GitHub and
SignPath. Contributions from anyone other than a committer must be reviewed before
they are merged. Signing requests must be approved manually by a signing approver.

## Artifacts

The initial signing scope is the portable Windows Authenticode executable
`RotaLink.exe`. The executable contains RotaLink's service/helper components and
the MIT-licensed `System.Memory` runtime dependency as embedded resources. SignPath
will not be used to sign unrelated upstream software.

## Security and reporting

RotaLink requires visible local consent for a support session. It does not provide
unattended access, hidden persistence, credential capture, clipboard transfer,
file transfer, lock-screen control, or UAC secure-desktop bypass.

Security concerns may be reported privately to `ali@rotaniz.com`. Do not include
credentials, support codes, screen contents, or other sensitive information in a
public issue.
