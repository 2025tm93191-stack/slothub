#!/usr/bin/env bash
# Renders an HTML report (Mermaid diagrams included) to PDF with headless Chrome.
#   ./scripts/build-report.sh                                     Assignment 1 design report
#   ./scripts/build-report.sh docs/report-a2/report.html docs/report-a2/SlotHub-Deployment-Report.pdf
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
html="$root/${1:-docs/report/report.html}"
pdf="$root/${2:-docs/report/SlotHub-Design-Report.pdf}"
chrome="${CHROME:-/Applications/Google Chrome.app/Contents/MacOS/Google Chrome}"
[[ -x "$chrome" ]] || chrome="$(command -v google-chrome || command -v chromium || true)"
[[ -n "$chrome" ]] || { echo "Chrome/Chromium not found; set CHROME=/path/to/chrome" >&2; exit 1; }
"$chrome" --headless=new --disable-gpu --no-pdf-header-footer --allow-file-access-from-files \
  --virtual-time-budget=20000 --run-all-compositor-stages-before-draw \
  --print-to-pdf="$pdf" "file://$html" 2>/dev/null
pages=$(python3 -I -c "import re,sys; d=open(sys.argv[1],'rb').read(); print(len(re.findall(rb'/Type\s*/Page[^s]', d)))" "$pdf")
echo "${pdf#"$root"/} ($pages pages)"
