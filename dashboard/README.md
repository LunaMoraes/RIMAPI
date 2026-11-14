# RIMAPI AI Colony Monitor Dashboard

Zero-build-step local web dashboard for monitoring what the autonomous AI agent is seeing and doing in RimWorld 1.7, plus human-only `/api/v1/experiment/*` controls.

## Quick Start

1. Start RimWorld 1.7 with RIMAPI loaded and open/load a colony save.
2. Either open `dashboard/index.html` directly in your browser, or serve it locally:
   ```powershell
   python -m http.server 3000 -d dashboard
   ```
   Then open `http://localhost:3000`.
