# PrintHub

PrintHub is a local HTTP service for printer discovery and PDF print jobs.

## Run in development

```bash
dotnet run --project src/PrintHub.Api
```

The local dashboard is served from:

- `http://localhost:5051`

On first launch the dashboard opens with a built-in onboarding flow:

- generate or enter the API key
- inspect detected local printers
- choose the default printer for jobs without `printerName`

## Runtime data location

By default PrintHub stores runtime data outside the build output:

- macOS: `~/Library/Application Support/PrintHub`
- Linux: `~/.local/share/PrintHub`
- Windows: `%LOCALAPPDATA%\PrintHub`

Stored data includes:

- `data/settings.json`
- `data/jobs.json`
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

Override self-contained mode only if you explicitly want a framework-dependent build:

```bash
SELF_CONTAINED=false ./scripts/publish.sh
```

```powershell
./scripts/publish.ps1 -SelfContained false
```

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
- `~/Applications/Stop PrintHub.command`
- `~/Applications/Uninstall PrintHub.command`

On Windows the installer also creates Start Menu shortcuts for start, stop and uninstall.

The installer works in user space and does not require admin rights.

## Auto-start

After install, auto-start can be enabled from the local dashboard in `Settings`.

PrintHub configures user-level startup only:

- macOS: LaunchAgent in `~/Library/LaunchAgents`
- Linux: desktop autostart entry in `~/.config/autostart`
- Windows: Startup folder entry for the current user

Auto-start uses the packaged launcher scripts, so it keeps the saved port and runtime home behavior.
