# Changelog

## 0.1.0

First release candidate of PrintHub.

Highlights:

- local HTTP API for printer discovery and PDF print jobs
- onboarding dashboard with API key setup and auto-start toggle
- printer registry, default printer selection, test print, queue controls
- JSON and multipart print job intake, background worker, retry/cancel/history
- SQLite-backed job history, support bundle export, diagnostics report
- self-contained publish, user installers, tray helper, release packaging scripts

Known limits for this release:

- production printer QA still has to be completed on real Windows and macOS machines
- PDF is the only supported document format
- release signing and notarization remain optional post-build steps for macOS distribution
