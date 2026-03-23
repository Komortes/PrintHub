# PrintHub

PrintHub is a local HTTP service for printer discovery and PDF print jobs.

## Run in development

```bash
dotnet run --project src/PrintHub.Api
```

The local dashboard is served from:

- `http://localhost:5051`

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

macOS/Linux:

```bash
./scripts/publish.sh
```

Windows PowerShell:

```powershell
./scripts/publish.ps1
```

Both scripts publish `src/PrintHub.Api` into `output/publish/<runtime>`.
