# Release Guide

PrintHub can already be published for local testing with `scripts/publish.*`.
For a distributable end-user package, use the dedicated release scripts.

## Build a release package

macOS/Linux:

```bash
./scripts/release/build-release.sh
```

Windows PowerShell:

```powershell
./scripts/release/build-release.ps1
```

Outputs:

- staged release folder in `output/release/<runtime>/PrintHub-<version>-<runtime>`
- packaged archive:
  - macOS: `PrintHub-<version>-<runtime>.zip`
  - Linux: `PrintHub-<version>-<runtime>.tar.gz`
  - Windows: `PrintHub-<version>-<runtime>.zip`
- checksum file `*.sha256`
- `RELEASE-MANIFEST.json`
- copied `CHANGELOG.md`

The staged folder contains:

- `payload/` with the self-contained publish output
- `docs/` with README, API reference and user guide
- on macOS: `Applications/PrintHub.app` created from the packaged launcher flow

## Verify a release package

Before signing or shipping, run a local smoke check against either:

- a publish directory
- a staged release directory

macOS/Linux:

```bash
./scripts/release/verify-release.sh output/release/osx-arm64/PrintHub-0.1.0-osx-arm64
```

Windows PowerShell:

```powershell
./scripts/release/verify-release.ps1 -SourceDir output/release/win-x64/PrintHub-0.1.0-win-x64
```

The verify scripts check:

1. install for the current user into a temporary location
2. platform shortcuts / launchers are created
3. `run-printhub` starts the service
4. `/health` returns successfully
5. `stop-printhub` stops the service
6. diagnostics artifacts are captured into `verify-artifacts/`

Set `PRINTHUB_VERIFY_KEEP=true` on macOS/Linux or `-KeepFiles` on Windows if you want to inspect the temporary install after verification.

When verification fails, the workspace is kept automatically and the script prints tails from installer / launcher logs so startup problems are easier to diagnose.

The release version comes from [VERSION](../VERSION) and is also written into:

- the .NET assemblies
- macOS app bundle metadata
- release archive names
- `RELEASE-MANIFEST.json`

## macOS signing

Set the signing identity and run:

```bash
export PRINTHUB_CODESIGN_IDENTITY="Developer ID Application: Your Name (TEAMID)"
./scripts/release/macos/sign-macos-release.sh output/release/osx-arm64/PrintHub-osx-arm64
```

Optional overrides:

- `PRINTHUB_CODESIGN_ENTITLEMENTS`

The script signs:

- `Applications/PrintHub.app`
- `Applications/PrintHub Tray.app` when present

It also recreates the release zip and checksum.

## macOS notarization

Create a notarytool keychain profile once, then:

```bash
export PRINTHUB_NOTARY_PROFILE="PrintHubNotary"
export PRINTHUB_NOTARY_TEAM_ID="TEAMID"
./scripts/release/macos/notarize-macos-release.sh output/release/osx-arm64/PrintHub-osx-arm64 output/release/osx-arm64/PrintHub-osx-arm64.zip
```

The script:

- submits the signed archive to Apple notary service
- waits for completion
- staples the notarization ticket to `PrintHub.app`
- staples `PrintHub Tray.app` when present

## Recommended release checklist

1. Run `dotnet test PrintHub.sln`.
2. Build the release package.
3. Install from the staged package on a clean user account.
4. Verify onboarding, tray, auto-start, printers and test print.
5. For public macOS distribution, sign and notarize the release.
