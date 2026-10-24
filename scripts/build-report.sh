#!/usr/bin/env bash
# Renders docs/report/report.html (Mermaid diagrams included) to docs/report/SlotHub-Design-Report.pdf with headless Chrome.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
chrome="${CHROME:-/Applications/Google Chrome.app/Contents/MacOS/Google Chrome}"
[[ -x "$chrome" ]] || chrome="$(command -v google-chrome || command -v chromium || true)"
[[ -n "$chrome" ]] || { echo "Chrome/Chromium not found; set CHROME=/path/to/chrome" >&2; exit 1; }
"$chrome" --headless=new --disable-gpu --no-pdf-header-footer --allow-file-access-from-files \
  --virtual-time-budget=20000 --run-all-compositor-stages-before-draw \
  --print-to-pdf="$root/docs/report/SlotHub-Design-Report.pdf" "file://$root/docs/report/report.html" 2>/dev/null
pages=$(python3 -I -c "import re,sys; d=open(sys.argv[1],'rb').read(); print(len(re.findall(rb'/Type\s*/Page[^s]', d)))" "$root/docs/report/SlotHub-Design-Report.pdf")
echo "docs/report/SlotHub-Design-Report.pdf ($pages pages)"
