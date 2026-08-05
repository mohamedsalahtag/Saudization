/* ==========================================================================
   dataloader.js — single source of truth for analysis.
   - maps raw rows -> employee objects (bundled AND uploaded files)
   - build(): computes reporting lines, spans, depth, anomalies, stats
   - getActive(): returns the built dataset (uploaded file if any, else bundled)
   - mountLoader(): "Load Excel file" button + current-file banner (in-browser)
   Both pages use this, so any file you load is analysed by the exact same code.
   ========================================================================== */
window.EmpData = (function () {
  "use strict";

  var SPAN_THRESHOLD = 15;
  var GENDER = { "1": "Male", "2": "Female", "M": "Male", "F": "Female" };
  var JUNIOR_TITLE = ["laborer", "labourer", "driver", "helper", "cleaner", "house keeper",
    "housekeeper", "packer", "loader", "guard", "worker", "operator", "checker",
    "picker", "waiter", "office boy", "messenger"];

  /* ---- normalisation ---- */
  function norm(v) {
    if (v == null) return "";
    if (v instanceof Date && !isNaN(v)) {
      var d = ("0" + v.getDate()).slice(-2), m = ("0" + (v.getMonth() + 1)).slice(-2);
      return d + "/" + m + "/" + v.getFullYear();
    }
    var s = String(v).trim();
    return s.toUpperCase() === "NA" ? "" : s;
  }

  /* ---- column mapping (header-text match, with fixed-index fallback) ---- */
  var FALLBACK = {
    branchCode: 0, code: 1, nameEn: 2, hireDate: 3, birthDate: 4,
    idNumber: 5, site: 6, division: 7, department: 8, section: 9,
    costCenter: 10, costCenterCode: 11, position: 12, nationality: 13,
    gender: 14, managerCode: 15
  };
  // Ordered specific-first so "cost center code" beats "cost center", "code" beats "name".
  var MATCHERS = [
    ["section", function (h) { return h.indexOf("section") >= 0; }],
    ["costCenterCode", function (h) { return h.indexOf("cost center") >= 0 && h.indexOf("code") >= 0; }],
    ["costCenter", function (h) { return h.indexOf("cost center") >= 0; }],
    ["site", function (h) { return h.indexOf("site name") >= 0 || h.indexOf("plant name") >= 0; }],
    ["managerCode", function (h) { return h.indexOf("direct manager") >= 0 || (h.indexOf("manager") >= 0 && h.indexOf("code") >= 0); }],
    ["nameEn", function (h) { return h.indexOf("english name") >= 0 || h.indexOf("name of employee") >= 0; }],
    ["code", function (h) { return (h.indexOf("employees code") >= 0 || h.indexOf("employee code") >= 0); }],
    ["branchCode", function (h) { return h.indexOf("branch") >= 0; }],
    ["hireDate", function (h) { return h.indexOf("hiring") >= 0; }],
    ["birthDate", function (h) { return h.indexOf("date of birth") >= 0; }],
    ["idNumber", function (h) { return h.indexOf("id number") >= 0; }],
    ["division", function (h) { return h.indexOf("division") >= 0; }],
    ["department", function (h) { return h.indexOf("department") >= 0; }],
    ["position", function (h) { return h.indexOf("postion") >= 0 || h.indexOf("position") >= 0; }],
    ["nationality", function (h) { return h.indexOf("nationality") >= 0; }],
    ["gender", function (h) { return h.indexOf("gender") >= 0; }]
  ];

  function resolveColumns(headerRow) {
    var heads = headerRow.map(function (h) { return String(h == null ? "" : h).toLowerCase().replace(/\s+/g, " ").trim(); });
    var used = {}, idx = {};
    MATCHERS.forEach(function (m) {
      var field = m[0], test = m[1];
      for (var i = 0; i < heads.length; i++) {
        if (!used[i] && heads[i] && test(heads[i])) { idx[field] = i; used[i] = true; break; }
      }
    });
    // fallback to fixed index for anything unmatched
    Object.keys(FALLBACK).forEach(function (f) {
      if (idx[f] == null && FALLBACK[f] < headerRow.length) idx[f] = FALLBACK[f];
    });
    return idx;
  }

  // Parse an uploaded .xlsx (ArrayBuffer) -> array of raw employee objects.
  function mapWorkbook(arrayBuffer) {
    if (typeof XLSX === "undefined") throw new Error("SheetJS (xlsx) not loaded");
    var wb = XLSX.read(arrayBuffer, { type: "array", cellDates: true });
    var sheet = wb.SheetNames.indexOf("Sheet2") >= 0 ? "Sheet2" : wb.SheetNames[0];
    var aoa = XLSX.utils.sheet_to_json(wb.Sheets[sheet], { header: 1, raw: true, defval: null, blankrows: false });
    if (!aoa.length) return [];
    var idx = resolveColumns(aoa[0]);
    var emps = [];
    for (var r = 1; r < aoa.length; r++) {
      var row = aoa[r];
      var code = norm(row[idx.code]);
      if (!code) continue;
      var e = {};
      Object.keys(idx).forEach(function (f) {
        var v = norm(row[idx[f]]);
        if (f === "gender") v = GENDER[v.toUpperCase()] || v;
        else if (f === "managerCode" && v.toUpperCase().replace(/\s+/g, "_") === "NO_MANAGER") v = "";
        e[f] = v;
      });
      emps.push(e);
    }
    return emps;
  }

  /* ---- position level (for the Position dimension) ---- */
  function positionLevel(pos) {
    var p = (pos || "").toLowerCase();
    if (!p) return "Unspecified";
    if (/manager|director|head|chief|\bgm\b|president|owner|general manager/.test(p)) return "Management";
    if (/supervisor|foreman|incharge|in charge|coordinator|team lead|\blead\b|captain/.test(p)) return "Supervisory";
    if (/officer|executive|specialist|engineer|accountant|analyst|technician|admin|clerk|nurse|designer|auditor/.test(p)) return "Professional / Technical";
    if (JUNIOR_TITLE.some(function (k) { return p.indexOf(k) >= 0; })) return "Operations / Labor";
    return "Other";
  }
  // seniority ranking of a position level (higher = more senior) — for the inversion check
  var LEVEL_RANK = { "Management": 4, "Supervisory": 3, "Professional / Technical": 2, "Operations / Labor": 1, "Other": 0, "Unspecified": 0 };
  function levelRank(pos) { return LEVEL_RANK[positionLevel(pos)] || 0; }

  /* ======================================================================
     build() — derive everything (mirrors the original Python analysis)
     ====================================================================== */
  function build(employees, meta) {
    meta = meta || {};
    var by = {};
    employees.forEach(function (e) { by[e.code] = e; });

    var reports = {};
    employees.forEach(function (e) {
      var m = e.managerCode;
      if (m && by[m]) { (reports[m] = reports[m] || []).push(e.code); }
    });
    var roots = employees.filter(function (e) { return !e.managerCode; }).map(function (e) { return e.code; });
    var dangling = employees.filter(function (e) { return e.managerCode && !by[e.managerCode]; }).map(function (e) { return e.code; });

    function upChain(code) {
      var chain = [], seen = {}, cur = by[code].managerCode;
      while (cur && by[cur] && !seen[cur]) { seen[cur] = 1; chain.push(cur); cur = by[cur].managerCode; }
      return chain;
    }
    employees.forEach(function (e) {
      e.directReports = (reports[e.code] || []).length;
      e.path = upChain(e.code);
      e.depth = e.path.length;
    });

    function slim(e, extra) {
      var row = {
        code: e.code, nameEn: e.nameEn, position: e.position,
        department: e.department, division: e.division, site: e.site,
        branchCode: e.branchCode, managerCode: e.managerCode, directReports: e.directReports
      };
      if (extra) Object.keys(extra).forEach(function (k) { row[k] = extra[k]; });
      return row;
    }

    // 1. orphans
    var orphans = employees.filter(function (e) { return !e.managerCode; }).map(function (e) { return slim(e); });

    // 2. cross-line
    var crossLine = [];
    employees.forEach(function (e) {
      var m = by[e.managerCode];
      if (!m) return;
      var dBranch = e.branchCode && m.branchCode && e.branchCode !== m.branchCode;
      var dDept = e.department && m.department && e.department !== m.department;
      var dDiv = e.division && m.division && e.division !== m.division;
      var dSite = e.site && m.site && e.site !== m.site;
      if (dBranch || dDept || dDiv || dSite) {
        crossLine.push(slim(e, {
          managerName: m.nameEn, managerDept: m.department, managerDivision: m.division,
          managerSite: m.site, managerBranch: m.branchCode,
          severity: dBranch ? "cross-branch" : dSite ? "cross-site" : dDiv ? "cross-division" : "cross-department"
        }));
      }
    });

    // 3. oversized span
    var oversizedSpan = [];
    Object.keys(reports).forEach(function (code) {
      if (reports[code].length > SPAN_THRESHOLD && by[code]) oversizedSpan.push(slim(by[code], { reportsCount: reports[code].length }));
    });
    oversizedSpan.sort(function (a, b) { return b.reportsCount - a.reportsCount; });

    // 4. junior-titled managers
    var juniorManagers = [];
    Object.keys(reports).forEach(function (code) {
      var e = by[code]; if (!e) return;
      var p = (e.position || "").toLowerCase();
      if (JUNIOR_TITLE.some(function (k) { return p.indexOf(k) >= 0; }))
        juniorManagers.push(slim(e, { reportsCount: reports[code].length }));
    });
    juniorManagers.sort(function (a, b) { return b.reportsCount - a.reportsCount; });

    // 5. missing critical data
    // managerCode is intentionally absent — the orphans anomaly already lists exactly those rows
    // current org data only carries Division/Department/Site — only flag those to avoid noise
    var missingSpecs = [["department", "Department"], ["site", "Site"]];
    var missing = {};
    missingSpecs.forEach(function (s) {
      var rows = employees.filter(function (e) { return !e[s[0]]; }).map(function (e) { return slim(e); });
      missing[s[0]] = { label: s[1], count: rows.length, rows: rows };
    });

    // 6. duplicates & self-reference
    var selfReference = employees.filter(function (e) { return e.managerCode === e.code; }).map(function (e) { return slim(e); });
    var idCounts = {};
    employees.forEach(function (e) { if (e.idNumber) idCounts[e.idNumber] = (idCounts[e.idNumber] || 0) + 1; });
    var duplicateIds = employees.filter(function (e) { return e.idNumber && idCounts[e.idNumber] > 1; })
      .map(function (e) { return slim(e, { idNumber: e.idNumber }); })
      .sort(function (a, b) { return String(a.idNumber).localeCompare(String(b.idNumber)); });
    var singleChild = [];
    Object.keys(reports).forEach(function (code) {
      if (reports[code].length === 1 && by[code]) singleChild.push(slim(by[code], { onlyReport: reports[code][0] }));
    });

    // 7. seniority inversion — a report is more senior (higher level) than their own manager
    var seniorityInversion = [];
    employees.forEach(function (e) {
      var m = by[e.managerCode]; if (!m) return;
      var re = levelRank(e.position), rm = levelRank(m.position);
      if (re > 0 && rm > 0 && re > rm)
        seniorityInversion.push(slim(e, { level: positionLevel(e.position), managerName: m.nameEn, managerPosition: m.position, managerLevel: positionLevel(m.position) }));
    });

    // 8. managerial title but no direct reports
    var titleWithoutTeam = employees.filter(function (e) {
      var lv = positionLevel(e.position);
      return (lv === "Management" || lv === "Supervisory") && e.directReports === 0;
    }).map(function (e) { return slim(e, { level: positionLevel(e.position) }); });

    // 9. reporting cycles — the manager chain loops back to the employee (includes self-manager)
    var reportingCycle = [];
    employees.forEach(function (e) {
      var seen = {}, cur = e.code, looped = false, hops = 0;
      while (cur && by[cur] && by[cur].managerCode && hops++ < 5000) {
        var nxt = by[cur].managerCode;
        if (nxt === e.code) { looped = true; break; }
        if (seen[nxt]) break;                 // a cycle that doesn't include e
        seen[nxt] = 1; cur = nxt;
      }
      if (looped) reportingCycle.push(slim(e, { managerName: (by[e.managerCode] || {}).nameEn || "" }));
    });

    // 10. very deep reporting chains
    var DEPTH_LIMIT = 8;
    var deepChain = employees.filter(function (e) { return e.depth > DEPTH_LIMIT; })
      .map(function (e) { return slim(e, { depth: e.depth }); })
      .sort(function (a, b) { return b.depth - a.depth; });

    // 11. duplicate employee codes (data integrity — same code on >1 row)
    var codeCounts = {};
    employees.forEach(function (e) { codeCounts[e.code] = (codeCounts[e.code] || 0) + 1; });
    var duplicateCodes = employees.filter(function (e) { return codeCounts[e.code] > 1; })
      .map(function (e) { return slim(e); })
      .sort(function (a, b) { return String(a.code).localeCompare(String(b.code)); });

    var anomalies = {
      orphans: orphans, crossLine: crossLine, oversizedSpan: oversizedSpan,
      juniorManagers: juniorManagers, missing: missing, selfReference: selfReference,
      duplicateIds: duplicateIds, singleChild: singleChild,
      dangling: dangling.map(function (c) { return slim(by[c]); }),
      seniorityInversion: seniorityInversion, titleWithoutTeam: titleWithoutTeam,
      reportingCycle: reportingCycle, deepChain: deepChain, duplicateCodes: duplicateCodes
    };

    /* ---- stats ---- */
    function dist(field) {
      var c = {};
      employees.forEach(function (e) { var k = e[field] || "(blank)"; c[k] = (c[k] || 0) + 1; });
      return Object.keys(c).map(function (k) { return [k, c[k]]; }).sort(function (a, b) { return b[1] - a[1]; });
    }
    function uniq(field) { var s = {}; employees.forEach(function (e) { if (e[field]) s[e[field]] = 1; }); return Object.keys(s).length; }
    var spanSizes = Object.keys(reports).map(function (k) { return reports[k].length; });
    var withMgr = employees.filter(function (e) { return e.managerCode; }).length;
    var depthHist = {}; employees.forEach(function (e) { depthHist[e.depth] = (depthHist[e.depth] || 0) + 1; });

    var totalIssues = orphans.length + crossLine.length + oversizedSpan.length + juniorManagers.length
      + Object.keys(missing).reduce(function (s, k) { return s + missing[k].count; }, 0)
      + selfReference.length + duplicateIds.length + singleChild.length + dangling.length
      + seniorityInversion.length + titleWithoutTeam.length + reportingCycle.length + deepChain.length + duplicateCodes.length;

    var byLevel = (function () {
      var c = {}; employees.forEach(function (e) { var k = positionLevel(e.position); c[k] = (c[k] || 0) + 1; });
      return Object.keys(c).map(function (k) { return [k, c[k]]; }).sort(function (a, b) { return b[1] - a[1]; });
    })();
    function levelCount(name) { var r = byLevel.filter(function (x) { return x[0] === name; })[0]; return r ? r[1] : 0; }

    var stats = {
      total: employees.length, sites: uniq("site"), divisions: uniq("division"),
      departments: uniq("department"), positions: uniq("position"), nationalities: uniq("nationality"),
      branches: uniq("branchCode"), managers: Object.keys(reports).length, withManager: withMgr,
      pctWithManager: employees.length ? Math.round(1000 * withMgr / employees.length) / 10 : 0,
      avgSpan: spanSizes.length ? Math.round(10 * spanSizes.reduce(function (a, b) { return a + b; }, 0) / spanSizes.length) / 10 : 0,
      maxSpan: spanSizes.length ? Math.max.apply(null, spanSizes) : 0,
      maxDepth: Object.keys(depthHist).length ? Math.max.apply(null, Object.keys(depthHist).map(Number)) : 0,
      byDivision: dist("division"), bySite: dist("site"), byDepartment: dist("department"),
      byNationality: dist("nationality"), byGender: dist("gender"),
      byBranch: dist("branchCode"), byPosition: dist("position"), byLevel: byLevel,
      levelManagement: levelCount("Management"), levelSupervisory: levelCount("Supervisory"),
      levelProfessional: levelCount("Professional / Technical"), levelLabor: levelCount("Operations / Labor"),
      totalIssues: totalIssues
    };

    return {
      generatedAt: meta.generatedAt || nowStr(),
      sourceName: meta.name || "data",
      uploaded: !!meta.uploaded,
      employees: employees, byCode: by, roots: roots, reports: reports,
      anomalies: anomalies, stats: stats
    };
  }

  function nowStr() {
    try { return new Date().toISOString().slice(0, 16).replace("T", " "); } catch (e) { return ""; }
  }

  /* ======================================================================
     Active dataset — uploaded (sessionStorage) or bundled window.EMP_RAW
     ====================================================================== */
  var KEY_B64 = "empUploadB64", KEY_NAME = "empUploadName";

  function getActive() {
    try {
      var b64 = sessionStorage.getItem(KEY_B64);
      if (b64) {
        var buf = base64ToBuf(b64);
        var emps = mapWorkbook(buf);
        if (emps.length) return build(emps, { name: sessionStorage.getItem(KEY_NAME), uploaded: true });
      }
    } catch (e) { console.warn("Uploaded file could not be read, using bundled data:", e); }
    var raw = window.EMP_RAW;
    if (!raw) return null;
    return build(raw.employees, { name: raw.sourceFile, generatedAt: raw.generatedAt });
  }

  function bufToBase64(buf) {
    var bytes = new Uint8Array(buf), CHUNK = 0x8000, out = "";
    for (var i = 0; i < bytes.length; i += CHUNK) out += String.fromCharCode.apply(null, bytes.subarray(i, i + CHUNK));
    return btoa(out);
  }
  function base64ToBuf(b64) {
    var bin = atob(b64), len = bin.length, bytes = new Uint8Array(len);
    for (var i = 0; i < len; i++) bytes[i] = bin.charCodeAt(i);
    return bytes.buffer;
  }

  /* ---- upload UI ---- */
  function mountLoader(container) {
    var uploadedName = null;
    try { uploadedName = sessionStorage.getItem(KEY_NAME); } catch (e) { }

    var input = document.createElement("input");
    input.type = "file"; input.accept = ".xlsx,.xls"; input.style.display = "none";

    var btn = document.createElement("button");
    btn.className = "btn btn-sm"; btn.innerHTML = "📁 Load Excel file…";
    btn.title = "Analyse a different .xlsx right here in the browser — no rebuild needed";
    btn.addEventListener("click", function () { input.click(); });

    var banner = document.createElement("span");
    banner.className = "loadbanner";
    function paint() {
      if (uploadedName) {
        banner.innerHTML = "<span class='pill warn'>Loaded file</span> " + escapeHtml(uploadedName)
          + " <a href='#' class='resetlink'>✕ reset to bundled</a>";
        banner.querySelector(".resetlink").addEventListener("click", function (ev) {
          ev.preventDefault();
          try { sessionStorage.removeItem(KEY_B64); sessionStorage.removeItem(KEY_NAME); } catch (e) { }
          location.reload();
        });
      } else {
        banner.innerHTML = "<span class='muted'>Using bundled data (" + escapeHtml((window.EMP_RAW && window.EMP_RAW.sourceFile) || "merge data.xlsx") + ")</span>";
      }
    }
    paint();

    input.addEventListener("change", function () {
      var file = input.files && input.files[0];
      if (!file) return;
      var reader = new FileReader();
      reader.onload = function () {
        try {
          var b64 = bufToBase64(reader.result);
          // sanity parse before committing
          var test = mapWorkbook(reader.result);
          if (!test.length) { alert("No employee rows found in that file. Expected an export with an 'Employees code' column."); return; }
          sessionStorage.setItem(KEY_B64, b64);
          sessionStorage.setItem(KEY_NAME, file.name);
          location.reload();
        } catch (e) {
          alert("Could not read that Excel file:\n" + e.message);
        }
      };
      reader.readAsArrayBuffer(file);
    });

    container.appendChild(btn);
    container.appendChild(input);
    container.appendChild(banner);
  }

  function escapeHtml(s) { return String(s == null ? "" : s).replace(/[&<>"]/g, function (c) { return ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]; }); }

  return {
    build: build, getActive: getActive, mountLoader: mountLoader,
    mapWorkbook: mapWorkbook, positionLevel: positionLevel
  };
})();
