"""Self-contained HTML report generator."""

from __future__ import annotations

import html
import json
from pathlib import Path
from typing import Any

from mission_live.report.coverage import build_coverage
from mission_live.report.model import default_out_dir, load_results


def write_html_report(
    out_dir: Path | None = None,
    results_doc: dict[str, Any] | None = None,
) -> Path:
    out_dir = out_dir or default_out_dir()
    out_dir.mkdir(parents=True, exist_ok=True)
    results_doc = results_doc if results_doc is not None else load_results()
    coverage = build_coverage(results_doc=results_doc)
    path = out_dir / "report.html"
    path.write_text(_render(results_doc, coverage), encoding="utf-8")
    return path


def _render(results: dict[str, Any], coverage: dict[str, Any]) -> str:
    summary = results.get("summary") or {}
    missions = results.get("missions") or []
    data_json = html.escape(json.dumps({"results": results, "coverage": coverage}, indent=2))

    cards = []
    for m in missions:
        status = html.escape(str(m.get("status") or ""))
        mid = m.get("missionId")
        title = html.escape(str(m.get("title") or ""))
        force = "yes" if m.get("forceGrant") else "no"
        locus = html.escape(str(m.get("failLocus") or ""))
        steps_html = "".join(
            f"<tr><td>{html.escape(s.get('key',''))}</td>"
            f"<td class='st-{html.escape(s.get('status',''))}'>{html.escape(s.get('status',''))}</td>"
            f"<td>{html.escape(str(s.get('detail') or ''))}</td></tr>"
            for s in (m.get("steps") or [])
        )
        cards.append(
            f"""
<details class="card st-{status}">
  <summary><strong>#{mid}</strong> {title} — <span class="st-{status}">{status}</span></summary>
  <p>forceGrant={force} policy={html.escape(str(m.get('policy') or ''))}
     duration={m.get('durationSec')}s failLocus={locus}</p>
  <p>seededPrereqs={html.escape(str(m.get('seededPrereqs') or []))}</p>
  <table><thead><tr><th>Step</th><th>Status</th><th>Detail</th></tr></thead>
  <tbody>{steps_html}</tbody></table>
</details>"""
        )

    cov_rows = "".join(
        f"<tr><td>{r['missionId']}</td><td>{r['inCatalog']}</td><td>{r['inRegistry']}</td>"
        f"<td class='st-{html.escape(str(r['lastStatus']))}'>{html.escape(str(r['lastStatus']))}</td>"
        f"<td>{html.escape(str(r.get('title') or ''))}</td></tr>"
        for r in (coverage.get("rows") or [])[:500]
    )

    return f"""<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8"/>
<title>Mission Live Report</title>
<style>
body {{ font-family: Segoe UI, sans-serif; margin: 1.5rem; background: #111; color: #eee; }}
h1,h2 {{ color: #fff; }}
.summary span {{ display: inline-block; margin-right: 1rem; padding: .25rem .5rem; border-radius: 4px; background: #222; }}
.st-PASS {{ color: #6f6; }} .st-PARTIAL {{ color: #fc6; }} .st-FAIL {{ color: #f66; }}
.st-SKIP {{ color: #9af; }} .st-ERROR {{ color: #f0f; }} .st-NEVER_RUN {{ color: #888; }}
.card {{ background: #1a1a1a; margin: .5rem 0; padding: .5rem .75rem; border: 1px solid #333; }}
table {{ border-collapse: collapse; width: 100%; font-size: 13px; }}
td, th {{ border: 1px solid #333; padding: 4px 6px; text-align: left; }}
input {{ margin: .5rem 0; padding: .4rem; width: 20rem; }}
</style></head><body>
<h1>Mission Live Report</h1>
<p>Generated: {html.escape(str(results.get('generatedAt') or ''))}</p>
<div class="summary">
  <span>total={summary.get('total', 0)}</span>
  <span class="st-PASS">PASS={summary.get('PASS', 0)}</span>
  <span class="st-PARTIAL">PARTIAL={summary.get('PARTIAL', 0)}</span>
  <span class="st-FAIL">FAIL={summary.get('FAIL', 0)}</span>
  <span class="st-SKIP">SKIP={summary.get('SKIP', 0)}</span>
  <span class="st-ERROR">ERROR={summary.get('ERROR', 0)}</span>
</div>
<h2>Coverage</h2>
<p>retail={coverage.get('retailCount')} registry={coverage.get('registryCount')}
 registry∩catalog={coverage.get('registryInCatalog')}
 catalogFullyAuto={coverage.get('catalogFullyAutoSupported')}
 catalogPartialAuto={coverage.get('catalogPartialAutoSupported')}</p>
<input id="filter" placeholder="Filter mission id / title / status" oninput="filterCards()"/>
<div id="cards">{''.join(cards)}</div>
<h2>Coverage matrix (first 500)</h2>
<table><thead><tr><th>Id</th><th>Catalog</th><th>Registry</th><th>Last</th><th>Title</th></tr></thead>
<tbody>{cov_rows}</tbody></table>
<details><summary>Raw JSON</summary><pre>{data_json}</pre></details>
<script>
function filterCards() {{
  const q = document.getElementById('filter').value.toLowerCase();
  for (const el of document.querySelectorAll('.card')) {{
    el.style.display = !q || el.innerText.toLowerCase().includes(q) ? '' : 'none';
  }}
}}
</script>
</body></html>
"""
