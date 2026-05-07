# PrintHub

PrintHub is a local HTTP service for printer discovery and PDF print jobs.
It is meant to bridge web systems and printers installed on a user workstation.

Quick links:

- [Changelog](./CHANGELOG.md)
- [User guide](./docs/user-guide.md)
- [API reference](./docs/api.md)
- [Release guide](./docs/release.md)

Current release candidate:

- `0.1.0`

## Project status

PrintHub is now in a solid public demo state:

- local dashboard with onboarding, printer registry and job history
- REST API for health, printers, print jobs, settings and diagnostics
- background worker, queue controls, retry/cancel, SQLite job history
- self-contained publish, user installers, tray helper and release scripts

What still needs real-world QA before calling it production-ready:

- live printer verification on real Windows and macOS machines
- final macOS signing and notarization for distribution outside local testing

## Why this exists

Cloud systems do not have direct access to printers installed on a user machine.
PrintHub runs locally, accepts HTTP requests, and forwards documents to local printers
through a platform backend.

Typical flow:

1. A website, ERP or CRM calls `POST /print-jobs`.
2. PrintHub receives or downloads the PDF.
3. PrintHub places the job into a local queue.
4. A background worker sends it to the selected printer.

## What works now

- printer discovery and registration
- default printer selection and test print
- print jobs via URL, multipart upload and Base64
- queue pause/resume/clear and job cancel/retry/delete
- diagnostics report and support bundle export
- local auth via `Authorization: Bearer <api-key>`
- cross-platform packaging flow for local installs

## Run in development

```bash
dotnet run --project src/PrintHub.Api
```

The local dashboard is served from:

- `http://localhost:5051`

On first launch the dashboard opens with a built-in onboarding flow:

- generate or enter the API key
- optionally enable auto-start for the current user session
- finish setup and then add printers from the local dashboard

For external integrations, use:

- `Authorization: Bearer <api-key>` as the preferred auth style
- the configured custom API key header only for legacy compatibility

The development config and `.http` examples intentionally use a clearly fake value:

- `demo-local-api-key`

## Runtime data location

By default PrintHub stores runtime data outside the build output:

- macOS: `~/Library/Application Support/PrintHub`
- Linux: `~/.local/share/PrintHub`
- Windows: `%LOCALAPPDATA%\PrintHub`

Stored data includes:

- `data/settings.json`
- `data/jobs.db`
- `data/documents/`
- `data/logs/`

Override the runtime data root with:

```bash
export PRINTHUB_HOME=/absolute/path
```

On Windows PowerShell:

```powershell
$env:PRINTHUB_HOME = "C:\absolute\path"
```

## Publish

The publish scripts default to a self-contained build. That means the resulting
folder can be shipped to an end user without asking them to install the .NET runtime.

macOS/Linux:

```bash
./scripts/publish.sh
```

Windows PowerShell:

```powershell
./scripts/publish.ps1
```

Both scripts publish `src/PrintHub.Api` into `output/publish/<runtime>`.
The publish folder now also includes a copied `VERSION` file, so installers and tray metadata keep the same release number as the repo.

For a distributable archive intended for end users, use:

```bash
./scripts/release/build-release.sh
```

```powershell
./scripts/release/build-release.ps1
```

Then verify the package locally before shipping:

```bash
./scripts/release/verify-release.sh output/release/osx-arm64/PrintHub-0.1.0-osx-arm64
```

```powershell
./scripts/release/verify-release.ps1 -SourceDir output/release/win-x64/PrintHub-0.1.0-win-x64
```

Override self-contained mode only if you explicitly want a framework-dependent build:

```bash
SELF_CONTAINED=false ./scripts/publish.sh
```

```powershell
./scripts/publish.ps1 -SelfContained false
```

## CI

The repository includes a basic GitHub Actions workflow that restores, builds and tests
the solution on every push and pull request:

- [.github/workflows/ci.yml](./.github/workflows/ci.yml)

## Start the published app

After publish, the output folder contains launcher scripts for the end user:

- macOS/Linux: `run-printhub.sh` and `stop-printhub.sh`
- Windows PowerShell: `run-printhub.ps1` and `stop-printhub.ps1`

The launcher:

- creates `PRINTHUB_HOME` if it does not exist
- reads the saved `port` from `settings.json` when available
- starts PrintHub in the background
- waits for `/health`
- opens the dashboard in the browser

On macOS the publish output may also contain `PrintHub Tray.app`. This is a small
menu bar helper that can start, stop and reopen PrintHub without keeping a terminal open.
The publish folder also includes `open-printhub-tray.command` as a one-click launcher.
It also includes direct panel shortcuts:

- `open-printhub-settings.sh`
- `open-printhub-printers.sh`
- `open-printhub-settings.ps1`
- `open-printhub-printers.ps1`
- `open-printhub-settings.command`
- `open-printhub-printers.command`

You can also override the startup URL manually:

```bash
PRINTHUB_URL=http://127.0.0.1:6060 ./run-printhub.sh
```

## Install for a regular user

The publish output now also contains install and uninstall scripts:

- macOS/Linux: `install-printhub.sh` and `uninstall-printhub.sh`
- Windows PowerShell: `install-printhub.ps1` and `uninstall-printhub.ps1`

Default install targets:

- macOS: `~/Applications/PrintHub.app`
- Linux: `~/.local/opt/PrintHub`
- Windows: `%LOCALAPPDATA%\Programs\PrintHub`

On macOS the publish output also includes double-clickable `.command` wrappers:

- `install-printhub.command`
- `run-printhub.command`
- `stop-printhub.command`

After install on macOS you get:

- `~/Applications/PrintHub.app`
- `~/Applications/PrintHub Tray.app` when the tray helper was built
- `~/Applications/Open PrintHub Tray.command` when the tray helper was built
- `~/Applications/Open PrintHub Settings.command`
- `~/Applications/Open PrintHub Printers.command`
- `~/Applications/Stop PrintHub.command`
- `~/Applications/Uninstall PrintHub.command`

On Windows the installer also creates Start Menu shortcuts for start, stop and uninstall.
It now also creates direct `PrintHub Settings` and `PrintHub Printers` shortcuts.

On Linux the installer creates desktop entries for:

- `PrintHub`
- `PrintHub Settings`
- `PrintHub Printers`
- `Stop PrintHub`

The installer works in user space and does not require admin rights.

## Release packaging

Distributable release artifacts are created in `output/release/<runtime>`.
Artifact names are versioned as `PrintHub-<version>-<runtime>`.

Each release package includes:

- self-contained payload
- end-user docs
- changelog
- release manifest
- checksum

On macOS the staged release also contains an installable `Applications/PrintHub.app`.

If you plan to distribute the macOS build outside local testing, follow:

- [Release guide](./docs/release.md)

That flow covers:

- signing with `codesign`
- notarization with `notarytool`
- stapling the notarization ticket back onto the app bundle

## Auto-start

After install, auto-start can be enabled from the local dashboard in `Settings`.
During first-run onboarding you can also enable it immediately with a single checkbox.

PrintHub configures user-level startup only:

- macOS: LaunchAgent in `~/Library/LaunchAgents`
- Linux: desktop autostart entry in `~/.config/autostart`
- Windows: Startup folder entry for the current user

Auto-start uses the packaged launcher scripts, so it keeps the saved port and runtime home behavior.

## Recommended first run

1. Install or publish PrintHub and launch the local dashboard.
2. Generate an API key in onboarding.
3. Leave `Enable auto-start` on if this machine should always be ready to print.
4. Open `Printers`, discover the OS printers and add the devices you actually want to expose.
5. Set one printer as default and send a test print.
6. Give the API key to the external system that will call `POST /print-jobs`.

## Release prep

PrintHub now keeps a repository-level release version in [VERSION](./VERSION).
For `0.1.0` the repo is prepared to:

- build versioned self-contained publish output
- produce versioned release archives and manifests
- include `README`, API docs, user guide and changelog in the staged release
- verify the staged package with captured diagnostics artifacts

Before shipping to real users, still do:

- one clean `publish` on the target runtime
- one real `verify-release` run against the fresh staged package
- one live printer smoke test on the target OS
- macOS `codesign/notarization` if you distribute outside local testing
