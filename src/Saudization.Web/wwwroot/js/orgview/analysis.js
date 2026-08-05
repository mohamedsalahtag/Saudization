/* ==========================================================================
   HR Analysis page — KPIs, distribution charts, anomaly tables + exports.
   Everything is recomputed for the selected scope (Branch / Site / Division):
   the full dataset is rebuilt on the filtered employee list, so all counts,
   charts, issue lists and exports are per-scope.
   ========================================================================== */
(function () {
  "use strict";
  var FULL = window.EmpData.getActive();
  if (!FULL) { document.body.innerHTML = "<p style='padding:30px'>data.js not found — run refresh_data.bat</p>"; return; }
  var fullBy = FULL.byCode;

  // data is fed by the app (no in-browser Excel upload here)

  function fmt(n) { return String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ","); }
  function esc(s) { return String(s == null ? "" : s).replace(/[&<>"]/g, function (c) { return ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]; }); }
  var CHART_URL = window.ORGV_CHART_URL || "";
  function empLink(code, label) { return "<a href='" + CHART_URL + "#emp=" + encodeURIComponent(code) + "'>" + esc(label) + "</a>"; }

  /* generic expandable nested-group tree: `levels` = array of key functions; leaves = employees.
     Each group lazily builds its children on first open; clicking an employee opens them in the chart. */
  function groupTree(hostId, emps, levels) {
    var host = document.getElementById(hostId); if (!host) return;
    function build(list, depth) {
      var frag = document.createDocumentFragment();
      if (depth >= levels.length) {
        list.slice().sort(function (a, b) { return (a.nameEn || "").localeCompare(b.nameEn || ""); }).forEach(function (e) {
          var row = document.createElement("div"); row.className = "gt-emp";
          row.innerHTML = empLink(e.code, e.nameEn || e.code) + " <span class='muted'>#" + esc(e.code) + (e.position ? " · " + esc(e.position) : "") + "</span>";
          frag.appendChild(row);
        });
        return frag;
      }
      var m = {}, order = [];
      list.forEach(function (e) { var k = levels[depth](e) || "(none)"; if (!(k in m)) { m[k] = []; order.push(k); } m[k].push(e); });
      order.sort(function (a, b) { return a.localeCompare(b); });
      order.forEach(function (k) {
        var node = document.createElement("div"); node.className = "gt-node gt-l" + depth;
        var head = document.createElement("div"); head.className = "gt-head";
        head.innerHTML = "<span class='gt-caret'>▸</span><span class='gt-label'>" + esc(k) + "</span><span class='gt-count'>" + fmt(m[k].length) + "</span>";
        var kids = document.createElement("div"); kids.className = "gt-kids"; kids.style.display = "none";
        node.appendChild(head); node.appendChild(kids);
        var built = false;
        head.addEventListener("click", function () {
          var open = kids.style.display !== "none";
          kids.style.display = open ? "none" : "block";
          head.querySelector(".gt-caret").textContent = open ? "▸" : "▾";
          if (!built) { built = true; kids.appendChild(build(m[k], depth + 1)); }
        });
        frag.appendChild(node);
      });
      return frag;
    }
    host.innerHTML = "";
    var top = build(emps, 0);
    if (!top.childNodes.length) host.innerHTML = "<div class='empty-note'>No data in this scope.</div>";
    else host.appendChild(top);
  }

  /* ---- scope selects (options come from the FULL dataset) ---- */
  function fillScope(id, dist) {
    var sel = document.getElementById(id);
    dist.forEach(function (kv) {
      if (kv[0] === "(blank)") return;
      var o = document.createElement("option");
      o.value = kv[0]; o.textContent = kv[0] + " (" + fmt(kv[1]) + ")";
      sel.appendChild(o);
    });
    sel.addEventListener("change", renderAll);
  }
  fillScope("scopeBranch", FULL.stats.byBranch);
  fillScope("scopeSite", FULL.stats.bySite);
  fillScope("scopeDiv", FULL.stats.byDivision);

  function scope() {
    return {
      branchCode: document.getElementById("scopeBranch").value,
      site: document.getElementById("scopeSite").value,
      division: document.getElementById("scopeDiv").value
    };
  }
  function scopeActive(f) { return !!(f.branchCode || f.site || f.division); }
  function scopeLabel(f) {
    var p = [];
    if (f.branchCode) p.push("Branch: " + f.branchCode);
    if (f.site) p.push("Site: " + f.site);
    if (f.division) p.push("Division: " + f.division);
    return p.join(" · ");
  }

  // current view, used by the export-all button
  var CUR = { specs: [], D: FULL, S: FULL.stats, scopeText: "All data", slug: "" };

  document.getElementById("exportAll").addEventListener("click", exportAllFindings);

  /* ======================================================================
     RENDER EVERYTHING for the current scope
     ====================================================================== */
  function renderAll() {
    var f = scope(), active = scopeActive(f);
    var emps = !active ? FULL.employees : FULL.employees.filter(function (e) {
      return (!f.branchCode || e.branchCode === f.branchCode)
          && (!f.site || e.site === f.site)
          && (!f.division || e.division === f.division);
    });
    var D = window.EmpData.build(emps, { name: FULL.sourceName, generatedAt: FULL.generatedAt, uploaded: FULL.uploaded });
    var S = D.stats, A = D.anomalies;

    var gen = document.getElementById("gen"); if (gen) gen.textContent = D.stats.total + " employees";
    document.getElementById("scopeHint").textContent = active ? (scopeLabel(f) + " — " + fmt(S.total) + " employees") : "";
    document.getElementById("issuePill").textContent = fmt(S.totalIssues) + " flagged";

    /* ---- KPI cards ---- */
    var kpis = [
      { num: fmt(S.total), lbl: "Employees" },
      { num: S.divisions, lbl: "Divisions" },
      { num: S.departments, lbl: "Departments" },
      { num: fmt(S.positions), lbl: "Positions" },
      { num: fmt(S.levelManagement), lbl: "Management" },
      { num: fmt(S.levelSupervisory), lbl: "Supervisory" },
      { num: fmt(S.levelLabor), lbl: "Labor" },
      { num: S.pctWithManager + "%", lbl: "Have a manager", alert: S.pctWithManager < 100 },
      { num: S.avgSpan, lbl: "Avg direct reports" },
      { num: fmt(S.maxSpan), lbl: "Largest team", alert: S.maxSpan > 30 },
      { num: fmt(S.totalIssues), lbl: "Flagged issues", alert: true }
    ];
    document.getElementById("kpis").innerHTML = kpis.map(function (k) {
      return "<div class='card kpi" + (k.alert ? " alert" : "") + "'><div class='num'>" + k.num + "</div><div class='lbl'>" + k.lbl + "</div></div>";
    }).join("");

    /* ---- bar charts ---- */
    function barChart(hostId, dist, topN) {
      var host = document.getElementById(hostId); if (!host) return;
      var rows = dist.filter(function (kv) { return kv[0] !== "(blank)"; }).slice(0, topN || 10);
      // hide the whole card when the field carries no data (e.g. Site/Nationality not in this import)
      var card = host.closest(".card"); if (card) card.style.display = rows.length ? "" : "none";
      var max = rows.reduce(function (m, kv) { return Math.max(m, kv[1]); }, 1);
      host.innerHTML = rows.map(function (kv) {
        var w = (100 * kv[1] / max).toFixed(1);
        return "<div class='bar-row'><span class='lab' title='" + esc(kv[0]) + "'>" + esc(kv[0]) + "</span>"
          + "<span class='bar-track'><span class='bar-fill' style='width:" + w + "%'></span></span>"
          + "<span class='val'>" + fmt(kv[1]) + "</span></div>";
      }).join("");
    }
    barChart("cDiv", S.byDivision, 10);
    barChart("cSite", S.bySite, 12);
    barChart("cPos", S.byPosition, 12);
    barChart("cDept", S.byDepartment, 12);
    barChart("cNat", S.byNationality, 10);

    /* ---- expandable breakdowns: Division › Department › Position, and Employees by manager ---- */
    groupTree("byPosTree", emps, [
      function (e) { return e.division || "(No Division)"; },
      function (e) { return e.department || "(No Department)"; },
      function (e) { return e.position || "(No Position)"; }
    ]);
    groupTree("byMgrTree", emps, [
      function (e) { var m = fullBy[e.managerCode]; return m ? (m.nameEn || e.managerCode) : "(No Manager)"; }
    ]);

    /* ---- anomaly specs ---- */
    var COLS = {
      code: { key: "code", label: "Code" },
      name: { key: "nameEn", label: "Name", render: function (r) { return empLink(r.code, r.nameEn || r.code); } },
      pos: { key: "position", label: "Position" },
      dept: { key: "department", label: "Department" },
      div: { key: "division", label: "Division" },
      site: { key: "site", label: "Site" },
      branch: { key: "branchCode", label: "Branch" },
      mgr: { key: "managerCode", label: "Mgr Code" },
      reports: { key: "directReports", label: "Reports", num: true }
    };

    // split dangling: manager missing from the FILE vs manager outside the current scope
    var inScope = {}; emps.forEach(function (e) { inScope[e.code] = 1; });
    var notFound = [], outsideScope = [];
    A.dangling.forEach(function (r) {
      if (active && fullBy[r.managerCode] && !inScope[r.managerCode]) outsideScope.push(r);
      else notFound.push(r);
    });

    var specs = [
      {
        id: "orphans", title: "No manager (orphans)", tone: "danger",
        desc: "Employees with no Direct Manager Code. Confirm the true company tops; assign a manager to the rest.",
        rows: A.orphans,
        cols: [COLS.code, COLS.name, COLS.pos, COLS.dept, COLS.div, COLS.site, COLS.branch, COLS.reports]
      },
      {
        id: "dangling", title: "Manager code not found", tone: "danger",
        desc: "The Direct Manager Code does not match any employee in the file — the manager may have left or the code is a typo. These employees are shown as extra tops in the chart.",
        rows: notFound,
        cols: [COLS.code, COLS.name, COLS.pos, COLS.dept, COLS.site, COLS.branch, { key: "managerCode", label: "Missing Mgr Code" }, COLS.reports]
      }
    ];
    if (outsideScope.length) {
      specs.push({
        id: "outsideScope", title: "Manager outside this scope", tone: "warn",
        desc: "The manager exists in the file but sits outside the selected " + (f.branchCode ? "branch" : f.site ? "site" : "division") + " — a reporting line that crosses the selection.",
        rows: outsideScope.map(function (r) {
          var m = fullBy[r.managerCode];
          return Object.assign({}, r, { managerName: m.nameEn, managerSite: m.site, managerDivision: m.division, managerBranch: m.branchCode });
        }),
        cols: [COLS.code, COLS.name, COLS.pos, COLS.dept, COLS.site, COLS.branch,
          { key: "managerName", label: "Manager", render: function (r) { return empLink(r.managerCode, r.managerName || r.managerCode); } },
          { key: "managerBranch", label: "Mgr Branch" },
          { key: "managerSite", label: "Mgr Site" }, { key: "managerDivision", label: "Mgr Division" }]
      });
    }
    specs.push(
      {
        id: "crossLine", title: "Reports across org lines", tone: "warn",
        desc: "Employee sits in a different branch / department / division / site than their manager. cross-branch and cross-site are the most disruptive.",
        rows: A.crossLine,
        cols: [
          COLS.code, COLS.name, COLS.pos, COLS.dept, COLS.site, COLS.branch,
          { key: "managerName", label: "Manager", render: function (r) { return empLink(r.managerCode, r.managerName || r.managerCode); } },
          { key: "managerDept", label: "Mgr Dept" },
          { key: "managerSite", label: "Mgr Site" },
          { key: "managerBranch", label: "Mgr Branch" },
          { key: "severity", label: "Severity", render: function (r) { return "<span class='sev-" + r.severity + "'>" + r.severity + "</span>"; } }
        ]
      },
      {
        id: "oversizedSpan", title: "Oversized span of control", tone: "warn",
        desc: "Managers with more than 15 direct reports — candidates for adding a supervisory layer.",
        rows: A.oversizedSpan,
        cols: [COLS.code, COLS.name, COLS.pos, COLS.dept, COLS.site, { key: "reportsCount", label: "Direct reports", num: true }]
      },
      {
        id: "juniorManagers", title: "Junior-titled people with reports", tone: "warn",
        desc: "Someone has direct reports but their own position looks like a labor/operative title — possible mis-mapped manager code.",
        rows: A.juniorManagers,
        cols: [COLS.code, COLS.name, COLS.pos, COLS.dept, COLS.site, { key: "reportsCount", label: "Direct reports", num: true }]
      },
      {
        id: "duplicateIds", title: "Duplicate ID numbers", tone: "danger",
        desc: "The same national/ID number appears on more than one employee record.",
        rows: A.duplicateIds,
        cols: [COLS.code, COLS.name, { key: "idNumber", label: "ID Number" }, COLS.pos, COLS.dept, COLS.site]
      },
      {
        id: "singleChild", title: "Thin management layers", tone: "grey",
        desc: "Manager with exactly one direct report — a possible redundant layer to flatten.",
        rows: A.singleChild,
        cols: [COLS.code, COLS.name, COLS.pos, COLS.dept, COLS.site, { key: "onlyReport", label: "Only report", render: function (r) { return empLink(r.onlyReport, r.onlyReport); } }]
      },
      {
        id: "seniorityInversion", title: "Seniority inversion (report more senior than manager)", tone: "warn",
        desc: "The employee's position level is more senior than their own manager's — a likely mis-set manager code.",
        rows: A.seniorityInversion,
        cols: [COLS.code, COLS.name, COLS.pos, { key: "level", label: "Level" }, COLS.dept,
          { key: "managerName", label: "Manager", render: function (r) { return empLink(r.managerCode, r.managerName || r.managerCode); } },
          { key: "managerPosition", label: "Mgr Position" }, { key: "managerLevel", label: "Mgr Level" }]
      },
      {
        id: "titleWithoutTeam", title: "Manager/Supervisor title with no team", tone: "grey",
        desc: "A management- or supervisory-level title but zero direct reports — a vacant team, an individual contributor, or a mis-title.",
        rows: A.titleWithoutTeam,
        cols: [COLS.code, COLS.name, COLS.pos, { key: "level", label: "Level" }, COLS.dept, COLS.div, COLS.site]
      },
      {
        id: "reportingCycle", title: "Reporting cycle / self-manager", tone: "danger",
        desc: "The manager chain loops back to the employee (including anyone set as their own manager) — an impossible reporting line.",
        rows: A.reportingCycle,
        cols: [COLS.code, COLS.name, COLS.pos, COLS.dept, { key: "managerCode", label: "Mgr Code" },
          { key: "managerName", label: "Manager", render: function (r) { return empLink(r.managerCode, r.managerName || r.managerCode); } }]
      },
      {
        id: "deepChain", title: "Very deep reporting chain", tone: "grey",
        desc: "Employees more than 8 levels below the top — the hierarchy may be unnecessarily tall.",
        rows: A.deepChain,
        cols: [COLS.code, COLS.name, COLS.pos, COLS.dept, { key: "depth", label: "Levels deep", num: true }]
      },
      {
        id: "duplicateCodes", title: "Duplicate employee codes", tone: "danger",
        desc: "The same employee code appears on more than one row — reporting links for that code are ambiguous.",
        rows: A.duplicateCodes,
        cols: [COLS.code, COLS.name, COLS.pos, COLS.dept, COLS.div, COLS.site]
      }
    );

    // missing-data — one spec per field
    Object.keys(A.missing).forEach(function (field) {
      var m = A.missing[field];
      if (!m.count) return;
      specs.push({
        id: "missing_" + field, title: "Missing " + m.label, tone: "grey",
        desc: m.count + " employees have no " + m.label + " recorded.",
        rows: m.rows,
        cols: [COLS.code, COLS.name, COLS.pos, COLS.dept, COLS.div, COLS.site]
      });
    });

    var host = document.getElementById("anomalies");
    host.innerHTML = "";
    specs.forEach(function (sp) { host.appendChild(renderAnom(sp)); });

    CUR = {
      specs: specs, D: D, S: S,
      scopeText: active ? scopeLabel(f) : "All data",
      slug: [f.branchCode, f.site, f.division].filter(Boolean).join("_").replace(/[^\w؀-ۿ-]+/g, "-")
    };
  }

  /* ======================================================================
     anomaly table renderer (unchanged behaviour)
     ====================================================================== */
  function renderAnom(sp) {
    var wrap = document.createElement("div");
    wrap.className = "card anom";
    var tone = sp.tone || "grey";
    var count = sp.rows.length;

    var head = document.createElement("div");
    head.className = "ahead";
    head.innerHTML = "<span class='caret'>▸</span>"
      + "<span class='atitle'>" + esc(sp.title) + "</span>"
      + "<span class='adesc'>— " + esc(sp.desc) + "</span>"
      + "<span class='acount'><span class='pill " + tone + "'>" + fmt(count) + "</span></span>";
    wrap.appendChild(head);

    var body = document.createElement("div");
    body.className = "abody";
    wrap.appendChild(body);

    var built = false;
    head.addEventListener("click", function () {
      wrap.classList.toggle("open");
      if (!built && wrap.classList.contains("open")) { built = true; buildBody(); }
    });

    function buildBody() {
      if (!count) { body.innerHTML = "<div class='empty-note'>None found. 🎉</div>"; return; }

      var bar = document.createElement("div"); bar.className = "abar";
      var filterInp = document.createElement("input");
      filterInp.type = "text"; filterInp.placeholder = "Filter these rows…"; filterInp.className = "grow";
      var xlsBtn = document.createElement("button"); xlsBtn.className = "btn btn-sm btn-primary"; xlsBtn.textContent = "⭳ Download Excel";
      var csvBtn = document.createElement("button"); csvBtn.className = "btn btn-sm"; csvBtn.textContent = "⭳ Download CSV";
      bar.appendChild(filterInp); bar.appendChild(xlsBtn); bar.appendChild(csvBtn);
      body.appendChild(bar);

      var scroll = document.createElement("div"); scroll.className = "tbl-scroll";
      var table = document.createElement("table"); table.className = "data";
      scroll.appendChild(table); body.appendChild(scroll);

      var sortState = { key: null, dir: 1 };
      var view = sp.rows.slice();

      function draw() {
        var thead = "<thead><tr>" + sp.cols.map(function (c) {
          var arrow = sortState.key === c.key ? (sortState.dir > 0 ? " ▲" : " ▼") : "";
          return "<th data-k='" + c.key + "'>" + esc(c.label) + arrow + "</th>";
        }).join("") + "</tr></thead>";
        var tbody = "<tbody>" + view.map(function (r) {
          return "<tr>" + sp.cols.map(function (c) {
            var v = c.render ? c.render(r) : esc(r[c.key] == null ? "" : r[c.key]);
            return "<td>" + v + "</td>";
          }).join("") + "</tr>";
        }).join("") + "</tbody>";
        table.innerHTML = thead + tbody;
        table.querySelectorAll("th").forEach(function (th) {
          th.addEventListener("click", function () {
            var k = th.getAttribute("data-k");
            sortState.dir = sortState.key === k ? -sortState.dir : 1;
            sortState.key = k;
            view.sort(function (a, b) {
              var x = a[k], y = b[k];
              var nx = parseFloat(x), ny = parseFloat(y);
              if (!isNaN(nx) && !isNaN(ny)) return (nx - ny) * sortState.dir;
              return String(x == null ? "" : x).localeCompare(String(y == null ? "" : y)) * sortState.dir;
            });
            draw();
          });
        });
      }
      draw();

      filterInp.addEventListener("input", function () {
        var q = filterInp.value.trim().toLowerCase();
        view = !q ? sp.rows.slice() : sp.rows.filter(function (r) {
          return sp.cols.some(function (c) { return String(r[c.key] == null ? "" : r[c.key]).toLowerCase().indexOf(q) >= 0; });
        });
        draw();
      });

      xlsBtn.addEventListener("click", function () { exportRows(sp, "xlsx"); });
      csvBtn.addEventListener("click", function () { exportRows(sp, "csv"); });
    }
    return wrap;
  }

  /* ---- exports (SheetJS) — Arabic-safe ---- */
  function exportRows(sp, kind) {
    var headers = sp.cols.map(function (c) { return c.label; });
    var aoa = [headers];
    sp.rows.forEach(function (r) {
      aoa.push(sp.cols.map(function (c) {
        // export raw values (not HTML) — for render cols use the underlying key
        var v = r[c.key];
        return v == null ? "" : v;
      }));
    });
    var ws = XLSX.utils.aoa_to_sheet(aoa);
    var name = sp.id.slice(0, 28);
    if (kind === "csv") {
      var csv = XLSX.utils.sheet_to_csv(ws);
      // BOM so Excel opens UTF-8 (Arabic) correctly
      downloadBlob("﻿" + csv, name + ".csv", "text/csv;charset=utf-8");
    } else {
      var wb = XLSX.utils.book_new();
      XLSX.utils.book_append_sheet(wb, ws, "Issues");
      XLSX.writeFile(wb, name + ".xlsx");
    }
  }

  /* ---- export ALL findings as one workbook: Summary + one sheet per issue ---- */
  function exportAllFindings() {
    var specs = CUR.specs, D = CUR.D, S = CUR.S;
    var wb = XLSX.utils.book_new();
    var summary = [
      ["Employee Hierarchy — All findings"],
      ["Data", D.generatedAt + (D.uploaded ? " (loaded file: " + D.sourceName + ")" : "")],
      ["Scope", CUR.scopeText],
      ["Employees", S.total],
      [],
      ["Finding", "Count", "Description"]
    ];
    specs.forEach(function (sp) { summary.push([sp.title, sp.rows.length, sp.desc]); });
    summary.push([]);
    summary.push(["Total flagged issues", S.totalIssues]);
    var wsSum = XLSX.utils.aoa_to_sheet(summary);
    wsSum["!cols"] = [{ wch: 36 }, { wch: 10 }, { wch: 100 }];
    XLSX.utils.book_append_sheet(wb, wsSum, "Summary");

    specs.forEach(function (sp) {
      var aoa = [sp.cols.map(function (c) { return c.label; })];
      sp.rows.forEach(function (r) {
        aoa.push(sp.cols.map(function (c) { var v = r[c.key]; return v == null ? "" : v; }));
      });
      var ws = XLSX.utils.aoa_to_sheet(aoa);
      ws["!cols"] = sp.cols.map(function (c) { return { wch: c.key === "nameEn" || c.key === "managerName" ? 32 : 16 }; });
      // sheet names: max 31 chars, must be unique
      XLSX.utils.book_append_sheet(wb, ws, sp.id.slice(0, 31));
    });

    var stamp = new Date().toISOString().slice(0, 10);
    XLSX.writeFile(wb, "hierarchy_findings" + (CUR.slug ? "_" + CUR.slug : "") + "_" + stamp + ".xlsx");
  }

  renderAll();
})();
