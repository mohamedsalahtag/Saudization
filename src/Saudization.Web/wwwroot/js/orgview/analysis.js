/* ==========================================================================
   HR Analysis page — renders the server-computed analysis (OrgAnalysisService):
   summary tiles, department / manager scorecards, grouped findings with
   filter / sort / export / accept, and the changes since the previous version.
   ========================================================================== */
(function () {
  "use strict";
  var el = function (id) { return document.getElementById(id); };
  var src = el("org-analysis"); if (!src) return;
  var D = JSON.parse(src.textContent), S = D.summary, T = D.strings, C = D.changes;
  var CHART_URL = window.ORGV_CHART_URL || "", EXPORT_URL = window.ORGV_EXPORT_URL || "", ACCEPT_URL = window.ORGV_ACCEPT_URL || "";

  function esc(s) { return String(s == null ? "" : s).replace(/[&<>"']/g, function (c) { return ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]; }); }
  function fmt(n) { return String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ","); }
  function f(key) { var s = T[key] || key, a = arguments; return s.replace(/\{(\d+)\}/g, function (_, i) { return a[+i + 1]; }); }
  function pct(a, b) { return b ? Math.round(100 * a / b) : 0; }
  function link(code, label) { return code ? "<a href='" + CHART_URL + "#emp=" + encodeURIComponent(code) + "' title='" + esc(T.OpenInChart) + "'>" + esc(label || code) + "</a>" : esc(label); }
  function exportUrl(kind, key, format) { return EXPORT_URL + "&kind=" + kind + (key ? "&key=" + encodeURIComponent(key) : "") + (format ? "&format=" + format : ""); }
  function token() { var i = document.querySelector('input[name="__RequestVerificationToken"]'); return i ? i.value : ""; }

  /* ---- generic sortable / filterable table ---- */
  function render(col, row) {
    if (col.render) return col.render(row);
    var v = row[col.key];
    if (col.key === "name" && row.code) return link(row.code, v);
    if (col.key === "mgrName" && row.mgrCode) return link(row.mgrCode, v);
    if (col.key === "severity") return "<span class='pill " + (row._sev === 3 ? "danger" : "warn") + "'>" + esc(v) + "</span>";
    if (col.key === "case") return "<span class='pill " + (v === T.Case_noMgr ? "grey" : "warn") + "'>" + esc(v) + "</span>";
    return esc(v);
  }
  function dataTable(host, columns, rows, opts) {
    opts = opts || {}; var sortKey = opts.sortKey || null, sortDir = opts.sortDir || -1, q = "";
    function view() {
      var v = !q ? rows.slice() : rows.filter(function (r) { return columns.some(function (c) { return String(r[c.key] == null ? "" : r[c.key]).toLowerCase().indexOf(q) >= 0; }); });
      if (sortKey) v.sort(function (a, b) { var x = a[sortKey], y = b[sortKey]; if (typeof x === "number" && typeof y === "number") return (x - y) * sortDir; return String(x == null ? "" : x).localeCompare(String(y == null ? "" : y)) * sortDir; });
      if (opts.acceptedLast) v.sort(function (a, b) { return (a._accepted ? 1 : 0) - (b._accepted ? 1 : 0); });
      return v;
    }
    function draw() {
      var v = view();
      host.innerHTML = "<thead><tr>" + columns.map(function (c) { return "<th class='" + (c.num ? "num" : "") + "' data-k='" + c.key + "'>" + esc(c.label) + (sortKey === c.key ? (sortDir > 0 ? " ▲" : " ▼") : "") + "</th>"; }).join("")
        + (opts.extraHead ? opts.extraHead() : "") + "</tr></thead><tbody>"
        + v.map(function (r) { return "<tr" + (opts.rowAttr ? opts.rowAttr(r) : "") + ">" + columns.map(function (c) { return "<td class='" + (c.num ? "num" : "") + "'>" + render(c, r) + "</td>"; }).join("") + (opts.extraCell ? opts.extraCell(r) : "") + "</tr>"; }).join("") + "</tbody>";
      host.querySelectorAll("th[data-k]").forEach(function (th) { th.addEventListener("click", function () { var k = th.getAttribute("data-k"); var col = columns.filter(function (c) { return c.key === k; })[0]; sortDir = sortKey === k ? -sortDir : (col && col.num ? -1 : 1); sortKey = k; draw(); }); });
      if (opts.onDraw) opts.onDraw(v);
    }
    draw();
    return { filter: function (s) { q = (s || "").trim().toLowerCase(); draw(); }, redraw: draw };
  }

  /* ---- summary ---- */
  var kpis = [
    { n: fmt(S.employees), l: T.K_Employees, s: f("K_EmployeesSub", S.divisions, S.departments) },
    { n: fmt(S.managers), l: T.K_Managers, s: f("K_ManagersSub", S.avgSpan) },
    { n: pct(S.shallow, S.employees) + "%", l: T.K_Shallow, s: f("K_ShallowSub", fmt(S.shallow)), cls: "warn" },
    { n: fmt(S.cross), l: T.K_Cross, s: f("K_CrossSub", pct(S.cross, S.employees)), cls: "alert" },
    { n: fmt(S.notUnderHead), l: T.K_NotUnder, s: T.K_NotUnderSub, cls: "alert" },
    { n: fmt(S.hodIssues), l: T.K_Hod, s: T.K_HodSub, cls: "warn" },
    { n: fmt(S.issuePeople), l: T.K_Issue, s: f("K_IssueSub", pct(S.issuePeople, S.employees)), cls: "alert" }
  ];
  el("kpis").innerHTML = kpis.map(function (k) { return "<div class='card kpi " + (k.cls || "") + "'><div class='num'>" + k.n + "</div><div class='lbl'>" + esc(k.l) + "</div><div class='sub'>" + esc(k.s) + "</div></div>"; }).join("");
  function hist(id, pairs, labelFn) { var max = pairs.reduce(function (m, p) { return Math.max(m, p[1]); }, 1); el(id).innerHTML = pairs.map(function (p) { return "<div class='col'><div class='b' style='height:" + Math.max(2, 66 * p[1] / max) + "px'><span>" + fmt(p[1]) + "</span></div><div class='x'>" + esc(labelFn(p[0])) + "</div></div>"; }).join(""); }
  hist("depthHist", S.depthHist, function (k) { return k === 0 ? T.Top : k + " " + (k === 1 ? T.Level : T.Levels); });
  el("depthNote").textContent = f("DepthNote", pct(S.shallow, S.employees));
  hist("spanHist", S.spanHist, function (k) { return k; });
  el("spanNote").textContent = f("SpanNote", S.managers, S.avgSpan, S.over30);

  /* ---- department scorecard ---- */
  var selected = D.depts[0] || null;
  function meter(p, invert) { var cls = invert ? (p >= 50 ? "bad" : p >= 20 ? "mid" : "") : (p < 60 ? "bad" : p < 85 ? "mid" : ""); return "<span class='meter " + cls + "'><span class='num'>" + p + "%</span><span class='bar'><i style='width:" + p + "%'></i></span></span>"; }
  var deptCols = [
    { key: "division", label: T.D_division }, { key: "dept", label: T.D_dept }, { key: "headcount", label: T.D_people, num: 1 },
    { key: "head", label: T.D_head, render: function (d) { return d.head ? esc(d.head) + (d.headInChart ? "" : " <span class='pill danger'>" + esc(T.NotInChart) + "</span>") : "<span class='pill warn'>" + esc(T.NoHead) + "</span>"; } },
    { key: "underPct", label: T.D_under, num: 1, render: function (d) { return d.underPct < 0 ? "<span class='muted'>—</span>" : meter(d.underPct); } },
    { key: "directPct", label: T.D_direct, num: 1, render: function (d) { return d.directPct < 0 ? "<span class='muted'>—</span>" : meter(d.directPct, true); } },
    { key: "external", label: T.D_external, num: 1, render: function (d) { return d.external ? "<span class='pill " + (pct(d.external, d.headcount) >= 50 ? "danger" : "warn") + "'>" + d.external + "</span>" : "<span class='muted'>0</span>"; } },
    { key: "supsNoTeamN", label: T.D_sups, num: 1, render: function (d) { return d.supsNoTeamN || "<span class='muted'>0</span>"; } },
    { key: "issueEmployees", label: T.D_issues, num: 1, render: function (d) { return "<b>" + d.issueEmployees + "</b> <span class='muted'>(" + pct(d.issueEmployees, d.headcount) + "%)</span>"; } },
    { key: "verdict", label: T.D_cause, render: function (d) { return "<div class='verdict " + d.verdictCls + "'>" + esc(d.verdict) + "</div>"; } }
  ];
  var deptTbl = dataTable(el("deptTable"), deptCols, D.depts, { sortKey: "issueEmployees",
    rowAttr: function (d) { return " class='clickable" + (d === selected ? " sel" : "") + "' data-i='" + D.depts.indexOf(d) + "'"; },
    onDraw: function (v) { el("deptCount").textContent = f("D_count", v.length, D.depts.length); el("deptTable").querySelectorAll("tr.clickable").forEach(function (tr) { tr.addEventListener("click", function () { selected = D.depts[+tr.getAttribute("data-i")]; deptTbl.redraw(); drawDetail(); el("deptDetail").scrollIntoView({ behavior: "smooth", block: "nearest" }); }); }); } });
  el("deptFilter").addEventListener("input", function () { deptTbl.filter(this.value); });
  function barList(pairs) { var max = pairs.reduce(function (m, p) { return Math.max(m, p[1]); }, 1); return "<div class='bars'>" + pairs.map(function (p) { return "<div class='row'><span title='" + esc(p[0]) + "'>" + esc(p[0]) + "</span><span class='track'><span class='fill' style='width:" + pct(p[1], max) + "%'></span></span><span class='n'>" + p[1] + "</span></div>"; }).join("") + "</div>"; }
  function drawDetail() {
    var d = selected, host = el("deptDetail"); if (!d) { host.innerHTML = ""; return; }
    host.innerHTML = "<div class='card detail'><div class='head'><div><h3>" + esc(d.division) + " › " + esc(d.dept) + "</h3><span class='note'>" + d.headcount + " " + esc(T.People) + " · " + esc(T.SitesLbl) + ": " + d.sites.map(function (s) { return esc(s[0]) + " (" + s[1] + ")"; }).join(", ") + "</span></div>"
      + "<div><a class='btn btn-sm btn-primary' href='" + exportUrl("fixlist", d.key) + "'>⭳ " + esc(T.FixList) + "</a></div></div>"
      + "<div class='cause'><b>" + esc(T.LikelyCause) + ":</b> " + esc(d.verdict) + "</div><div class='grid3'>"
      + "<div><h4>" + esc(T.Head) + "</h4>" + (d.head ? "<div>" + link(d.headCode, d.head) + " <span class='code muted'>" + esc(d.headCode) + "</span></div>" + (d.headInChart ? "<div class='note'>" + f("SitsIn", d.headDeptInChart) + " · " + f("UnderOf", d.underHead, d.headcount) + " · " + f("DirectOf", d.directToHead) + "</div>" : "<div class='pill danger'>" + esc(T.NotFoundVersion) + "</div>") : "<span class='pill warn'>" + esc(T.NoHodEntry) + "</span>")
      + (d.internalMgrs.length ? "<h4>" + esc(T.ManagersInside) + "</h4>" + barList(d.internalMgrs) : "") + "</div>"
      + "<div><h4>" + f("IntoOther", d.external) + "</h4>" + (d.externalDepts.length ? barList(d.externalDepts) : "<span class='note'>" + esc(T.None) + "</span>")
      + (d.externalRows.length ? "<ul class='plain note'>" + d.externalRows.slice(0, 4).map(function (e) { return "<li>" + link(e.code, e.name) + " <span class='muted'>(" + esc(e.job) + ")</span> → " + esc(e.mgrName) + " <span class='muted'>· " + esc(e.mgrDept) + "</span></li>"; }).join("") + (d.externalRows.length > 4 ? "<li class='muted'>" + f("More", d.externalRows.length - 4) + "</li>" : "") + "</ul>" : "") + "</div>"
      + "<div><h4>" + f("SupsNoTeam", d.supsNoTeam.length, d.supervisors) + "</h4>" + (d.supsNoTeam.length ? "<ul class='plain'>" + d.supsNoTeam.slice(0, 6).map(function (s) { return "<li>" + link(s.code, s.name) + " <span class='muted'>" + esc(s.job) + " · " + esc(s.site) + "</span></li>"; }).join("") + (d.supsNoTeam.length > 6 ? "<li class='muted'>" + f("More", d.supsNoTeam.length - 6) + "</li>" : "") + "</ul>" : "<span class='note'>" + esc(T.None) + "</span>")
      + (d.bigSpans.length ? "<h4>" + esc(T.TeamsOver15) + "</h4><ul class='plain'>" + d.bigSpans.map(function (b) { return "<li>" + esc(b[0]) + " <span class='pill warn'>" + b[1] + "</span></li>"; }).join("") + "</ul>" : "") + "</div></div></div>";
  }
  drawDetail();

  /* ---- manager scorecard ---- */
  var mgrCols = [
    { key: "name", label: T.M_name }, { key: "job", label: T.M_job }, { key: "dept", label: T.M_dept },
    { key: "reports", label: T.M_reports, num: 1, render: function (m) { return "<span class='pill " + (m.reports > 30 ? "danger" : m.reports > 15 ? "warn" : "grey") + "'>" + m.reports + "</span>"; } },
    { key: "depts", label: T.M_depts, num: 1 }, { key: "sites", label: T.M_sites, num: 1 }, { key: "subSups", label: T.M_subSups, num: 1 },
    { key: "topDepts", label: T.M_from, render: function (m) { return "<span class='note'>" + esc(m.topDepts) + "</span>"; } },
    { key: "why", label: T.M_cause, render: function (m) { return "<div class='verdict " + m.whyCls + "'>" + esc(m.why) + "</div>"; } }
  ];
  var mgrTbl = dataTable(el("mgrTable"), mgrCols, D.managers, { sortKey: "reports", onDraw: function (v) { el("mgrCount").textContent = f("M_count", v.length, D.managers.length); } });
  el("mgrFilter").addEventListener("input", function () { mgrTbl.filter(this.value); });

  /* ---- findings ---- */
  var fh = el("findings"), allFindings = [];
  D.groups.forEach(function (g) {
    var gt = document.createElement("div"); gt.className = "group-title"; gt.textContent = g.title; fh.appendChild(gt);
    g.findings.forEach(function (sp) { allFindings.push(sp); fh.appendChild(renderFinding(sp, true)); });
  });
  function count(sp) { return sp.rows.filter(function (r) { return !r._accepted; }).length; }
  function renderFinding(sp, canAccept) {
    var wrap = document.createElement("div"); wrap.className = "card anom"; var n = count(sp), acc = sp.rows.length - n;
    wrap.innerHTML = "<div class='ahead'><span class='caret'>▸</span><div class='atext'><span class='atitle'>" + esc(sp.title) + "</span><div class='adesc'>" + (sp.pending ? esc(sp.pending) : sp.cause) + "</div></div>"
      + "<span class='acount'><span class='pill " + (sp.pending ? "grey" : n ? sp.tone : "ok") + "'>" + (sp.pending ? "—" : fmt(n)) + "</span>" + (acc ? " <span class='pill grey'>" + f("AcceptedN", acc) + "</span>" : "") + "</span></div><div class='abody'></div>";
    var head = wrap.querySelector(".ahead"), body = wrap.querySelector(".abody"), built = false;
    head.addEventListener("click", function () { wrap.classList.toggle("open"); if (!built && wrap.classList.contains("open")) { built = true; buildBody(); } });
    function refreshCount() { var n2 = count(sp), acc2 = sp.rows.length - n2; wrap.querySelector(".acount").innerHTML = "<span class='pill " + (n2 ? sp.tone : "ok") + "'>" + fmt(n2) + "</span>" + (acc2 ? " <span class='pill grey'>" + f("AcceptedN", acc2) + "</span>" : ""); }
    function buildBody() {
      if (sp.pending) { body.innerHTML = "<div class='empty-note'>" + esc(f("AvailableWhen", sp.pending)) + "</div>"; return; }
      if (!sp.rows.length) { body.innerHTML = "<div class='empty-note'>" + esc(T.NoneFound) + "</div>"; return; }
      body.innerHTML = "<div class='abar'><input type='text' class='grow' placeholder='" + esc(T.Filter) + "'><a class='btn btn-sm btn-primary' href='" + exportUrl("finding", sp.id) + "'>⭳ " + esc(T.Excel) + "</a><a class='btn btn-sm' href='" + exportUrl("finding", sp.id, "csv") + "'>⭳ " + esc(T.Csv) + "</a><span class='note'></span></div><div class='tbl-scroll'><table class='data'></table></div>";
      var useAccept = canAccept && sp.canAccept;
      var t = dataTable(body.querySelector("table"), sp.columns, sp.rows, { sortKey: sp.sortKey || null, acceptedLast: useAccept,
        rowAttr: function (r) { return r._accepted ? " class='accepted'" : ""; },
        extraHead: useAccept ? function () { return "<th></th>"; } : null,
        extraCell: useAccept ? function (r) { return "<td class='acc'><button type='button' class='btn btn-sm' data-key='" + esc(r._key) + "'>" + esc(r._accepted ? T.Undo : T.Accept) + "</button></td>"; } : null,
        onDraw: function (v) {
          body.querySelector(".note").textContent = f("RowsOf", v.length, sp.rows.length);
          body.querySelectorAll("button[data-key]").forEach(function (b) { b.addEventListener("click", function () { toggleAccept(sp, b.getAttribute("data-key"), b); }); });
        } });
      body.querySelector("input").addEventListener("input", function () { t.filter(this.value); });
      function toggleAccept(spec, key, btn) {
        var row = spec.rows.filter(function (r) { return r._key === key; })[0]; if (!row) return;
        var accept = !row._accepted; btn.disabled = true;
        fetch(ACCEPT_URL, { method: "POST", headers: { "Content-Type": "application/json", "RequestVerificationToken": token() },
          body: JSON.stringify({ findingId: spec.id, rowKey: key, rowLabel: row.name || row.head || row.dept || key, accept: accept }) })
          .then(function (r) { if (!r.ok) throw new Error(r.status); row._accepted = accept; refreshCount(); t.redraw(); })
          .catch(function () { btn.disabled = false; btn.textContent = T.AcceptFailed; });
      }
    }
    return wrap;
  }
  el("findingPill").textContent = f("ChecksWith", allFindings.filter(function (s) { return count(s) > 0; }).length) + " · " + f("Affected", fmt(S.issuePeople));

  /* ---- changes since the previous version ---- */
  if (!C.previousVersionId) {
    el("changeFacts").innerHTML = "<span class='note'>" + esc(T.C_none) + "</span>";
  } else {
    el("changeFacts").innerHTML = [["C_joined", C.joined, "ok"], ["C_left", C.left, "grey"], ["C_mgr", C.mgr, "danger"], ["C_dept", C.dept, "warn"], ["C_job", C.job, "grey"]]
      .map(function (x) { return "<span><b class='pill " + x[2] + "'>" + fmt(x[1]) + "</b> " + esc(T[x[0]]) + "</span>"; }).join("");
    el("changeNote").textContent = f("C_note", pct(C.mgr, S.employees), C.mgrByDept.slice(0, 3).map(function (x) { return x[0] + " (" + x[1] + ")"; }).join(", "));
    var ch = el("changeList");
    C.lists.forEach(function (sp) { ch.appendChild(renderFinding(sp, false)); });
  }

  /* ---- section nav highlight ---- */
  var links = Array.prototype.slice.call(el("sectnav").querySelectorAll("a"));
  window.addEventListener("scroll", function () { var y = window.scrollY + 110, on = links[0]; links.forEach(function (a) { var s = document.querySelector(a.getAttribute("href")); if (s && s.offsetTop <= y) on = a; }); links.forEach(function (a) { a.classList.toggle("on", a === on); }); }, { passive: true });
})();
