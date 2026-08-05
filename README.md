# Employee Hierarchy Web App

A zero-install web app to **browse the organization graphically** and **analyze the
reporting hierarchy for issues** HR can fix. Reads `merge data.xlsx` (Sheet2, 16 columns:
Branch, Employee Code, English Name, Hire Date, Birth Date, ID Number, Site, Division,
Department, Section, Cost Center Name/Code, Position, Nationality, Gender, Direct Manager Code).

## How to open it

1. **Double-click** `webapp\index.html` — it opens in your browser. No install, works offline.
2. Two pages, linked in the top nav:
   - **Browse** — an interactive reporting org chart plus a Division › Department ›
     Section browser, with a searchable detail panel.
   - **HR Analysis** — KPIs, distribution charts, and downloadable lists of hierarchy issues.

## Analysing a new / updated file — two ways

**Will an updated file be read automatically?** No — but you have two easy options:

1. **Load it in the browser (no rebuild, recommended).** On either page click
   **📁 Load Excel file…**, pick any `.xlsx`, and *both* pages instantly recompute every
   chart and every issue from that file — no Python, no restart. A banner shows which file is
   loaded; **✕ reset to bundled** returns to the built-in data. The loaded file lasts for the
   browser session only (it is never uploaded anywhere — all analysis runs locally in your browser).
2. **Rebuild the bundled snapshot.** Replace `merge data.xlsx`, then **double-click
   `refresh_data.bat`** (runs `python build_data.py`) and reload. This makes the new file the
   default every time the app opens. Requires Python with `openpyxl` (`pip install openpyxl`).

Both paths run the **same analysis code** (`dataloader.js`), so issues are always discovered
dynamically from whatever data is present. Column mapping matches by header text, so a
re-exported file with slightly reordered columns still loads correctly.

## Using the Browse page

- **View by** (top-left): switch the hierarchy dimension —
  **Manager** (reporting line) · **Division** · **Department** · **Division › Department** ·
  **Position (by level)**. Employees appear at the leaves showing **#ID · position**.
- **Manager view**: people with no manager (blank or `NO_MANAGER`) or whose manager code is
  not in the file stay at the top if they lead teams; everyone unattached *and* without
  reports is grouped under a **No Manager** node.
- **Chart**: click a node to expand/collapse; drag to pan; scroll to zoom; the badge shows how
  many people are hidden under a collapsed node.
- **Search**: type a name or employee code, press Enter — the chart expands to that person,
  highlights their chain, and opens their details.
- **Filters** — Branch, Site, Division, Department — prune the chart
  and the Org Structure tab.
- **Org Structure tab**: Division › Department › Section › Employee, with a
  colour-coded **level label** on every row.

## Using the HR Analysis page

**Analyze per branch / site / division:** the selectors at the top of the page re-run the
entire analysis on just that slice — every KPI, chart, issue list, and export reflects the
selection. When a scope is active, an extra list appears — **Manager outside this scope** —
for employees whose manager exists in the file but sits in a different branch/site/division.

Each issue list expands to a sortable, filterable table. Click a row to jump to that person in
the chart. Use **Download Excel** / **Download CSV** on a single list, or **⭳ Export all
findings** (next to the issues heading) to download every list in one workbook — a Summary
sheet with counts plus one sheet per issue.

Issues detected:

| List | What it flags |
|---|---|
| No manager (orphans) | Employees with no Direct Manager Code (blank or `NO_MANAGER`) |
| Manager code not found | Direct Manager Code doesn't match any employee in the file |
| Reports across org lines | Employee in a different department / division / site than their manager |
| Oversized span of control | Managers with > 15 direct reports |
| Junior-titled people with reports | A labor/operative title that nonetheless has direct reports |
| Duplicate ID numbers | Same ID on more than one record |
| Thin management layers | Manager with exactly one direct report |
| Missing … | Missing Department / Section / Position / Site / ID / Cost Center / Nationality |

## Files

```
merge data.xlsx        source data (never modified by the app)
build_data.py          Excel -> webapp/data.js (+ computes all anomalies)
refresh_data.bat       double-click to rebuild data
webapp/
  index.html           Browse page          analysis.html   HR Analysis page
  browse.js            analysis.js          app.css
  data.js              generated snapshot
  vendor/              d3.v7.min.js, xlsx.full.min.js (offline copies)
```
