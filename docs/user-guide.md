# PrintHub User Guide

This guide is for the normal end user who wants to install PrintHub on one computer and make local printers available over HTTP.

## What PrintHub does

PrintHub runs on the same machine as your printers and exposes a local HTTP API.

Typical flow:

1. Your website, ERP or CRM sends a request to PrintHub.
2. PrintHub downloads or receives the PDF.
3. PrintHub sends the file to a local printer.

## Install options

### Option 1: Run from source

Use this only for development:

```bash
dotnet run --project src/PrintHub.Api
```

The dashboard opens at:

- `http://localhost:5051`

### Option 2: Self-contained publish

Recommended when you want a portable folder:

```bash
./scripts/publish.sh
```

This creates a self-contained build in:

- `output/publish/<runtime>`

Then start PrintHub with:

```bash
output/publish/<runtime>/run-printhub.sh
```

Optional direct launchers in the publish folder:

- `open-printhub-settings.sh`
- `open-printhub-printers.sh`
- `open-printhub-settings.ps1`
- `open-printhub-printers.ps1`

### Option 3: Install for the current user

Recommended for a normal user:

```bash
./scripts/publish.sh
output/publish/<runtime>/install-printhub.sh
```

On macOS this installs:

- `~/Applications/PrintHub.app`
- `~/Applications/PrintHub Tray.app` if the tray helper is available
- `~/Applications/Open PrintHub Settings.command`
- `~/Applications/Open PrintHub Printers.command`
- `~/Applications/Stop PrintHub.command`
- `~/Applications/Uninstall PrintHub.command`

No admin rights are required.

On Linux the installer also creates desktop shortcuts for:

- `PrintHub`
- `PrintHub Settings`
- `PrintHub Printers`
- `Stop PrintHub`

On Windows the installer creates Start Menu shortcuts for:

- `PrintHub`
- `PrintHub Settings`
- `PrintHub Printers`
- `Stop PrintHub`
- `Uninstall PrintHub`

## First launch

On first launch PrintHub shows a small onboarding flow.

Do this in order:

1. Generate or enter an API key.
2. Leave `Enable auto-start` turned on if this computer should always be ready to print after login.
3. Finish onboarding.

After that, open the local dashboard sections:

- `Printers` to discover printers from the OS and add them to PrintHub
- `Settings` to change the port, storage path, API key and auto-start
- `Print Jobs` to inspect queue and history

## Add printers

PrintHub does not have to expose every printer installed in the OS.

Recommended flow:

1. Open `Printers`.
2. Click discovery/refresh.
3. Add only the printers that external systems should use.
4. Mark one printer as default.
5. Send a test print.

If no printer is marked as default, jobs without `printerName` may not go where you expect.

## Auto-start

Auto-start is user-level only.

PrintHub uses:

- macOS: `~/Library/LaunchAgents`
- Linux: `~/.config/autostart`
- Windows: current user Startup folder

You can enable it in two places:

- during first-run onboarding
- later in `Settings`

Use auto-start when:

- the computer should always be ready for incoming print jobs
- the user should not open Terminal or manually launch PrintHub each day

## Tray mode on macOS

If `PrintHub Tray.app` is present, you can keep PrintHub available from the macOS menu bar.

The tray helper can:

- open the dashboard
- open the `Printers` panel directly
- open the `Settings` panel directly
- start PrintHub in the background
- stop PrintHub
- open the runtime folder

This is useful when the user does not want to keep a terminal window open.

## Runtime data

By default PrintHub stores its runtime files outside the build output.

- macOS: `~/Library/Application Support/PrintHub`
- Linux: `~/.local/share/PrintHub`
- Windows: `%LOCALAPPDATA%\PrintHub`

Important files:

- `data/settings.json`
- `data/jobs.db`
- `data/documents/`
- `data/logs/`
- `runtime/launcher.log`

You can override the root with:

```bash
export PRINTHUB_HOME=/absolute/path
```

## Quick integration checklist

Before handing PrintHub over to another system, make sure all of this is true:

- PrintHub starts locally
- `/health` returns `200`
- at least one printer is registered in PrintHub
- the default printer is set
- a test print completes successfully
- the external system has the correct API key

## Troubleshooting

### Printers do not appear

Check the OS first:

```bash
lpstat -e
lpstat -d
```

If the printer exists in macOS/Linux but not in PrintHub:

1. refresh the `Printers` panel
2. make sure you added the printer to the PrintHub registry
3. check the dashboard after a hard reload

### PrintHub starts but the browser does not open

Open the dashboard manually:

- `http://127.0.0.1:5051`

If the port was changed in settings, use that port instead.

### Auto-start does not work

Check:

- PrintHub was installed from the packaged output, not just run from a random dev folder
- the auto-start toggle is enabled in `Settings`
- the launcher scripts still exist in the installed app payload

### Need logs

Look in:

- `data/logs/printhub.log`
- `runtime/launcher.log`
