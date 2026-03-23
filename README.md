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
