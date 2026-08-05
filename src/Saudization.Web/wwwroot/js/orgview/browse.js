/* ==========================================================================
   Browse page — multi-dimensional hierarchy chart + org-structure tree.
   Dimensions: Manager (reporting) · Division · Department · Division›Department
               · Position (by inferred level).
   ========================================================================== */
(function () {
  "use strict";
  var D = window.EmpData.getActive();
  if (!D) { document.body.innerHTML = "<p style='padding:30px'>data.js not found — run refresh_data.bat</p>"; return; }

  var EMP = D.employees, BY = D.byCode, REPORTS = D.reports;

  var gen = document.getElementById("gen"); if (gen) gen.textContent = D.stats.total + " employees";
  // data is fed by the app (no in-browser Excel upload here)

  /* ---- filters ---- */
  function fillSelect(id, dist) {
    var sel = document.getElementById(id);
    dist.forEach(function (kv) {
      if (kv[0] === "(blank)") return;
      var o = document.createElement("option");
      o.value = kv[0]; o.textContent = kv[0] + " (" + kv[1] + ")";
      sel.appendChild(o);
    });
  }
  fillSelect("fBranch", D.stats.byBranch);
  fillSelect("fSite", D.stats.bySite);
  fillSelect("fDiv", D.stats.byDivision);
  fillSelect("fDept", D.stats.byDepartment);

  function currentFilter() {
    return {
      branchCode: document.getElementById("fBranch").value,
      site: document.getElementById("fSite").value,
      division: document.getElementById("fDiv").value,
      department: document.getElementById("fDept").value
    };
  }
  function passes(e, f) {
    return (!f.branchCode || e.branchCode === f.branchCode)
        && (!f.site || e.site === f.site)
        && (!f.division || e.division === f.division)
        && (!f.department || e.department === f.department);
  }
  function filterActive(f) { return f.branchCode || f.site || f.division || f.department; }

  function dimension() { return document.getElementById("dim").value; }

  /* ======================================================================
     TREE BUILDERS — every builder returns a plain node:
       { id, kind:'root'|'cat'|'emp', title, subtitle, tag, count, code, children }
     ====================================================================== */
  var nid = 0;
  function empNode(e) {
    return {
      id: "e" + (++nid), kind: "emp", code: e.code,
      title: e.nameEn || e.code,
      subtitle: "#" + e.code + (e.position ? "  ·  " + e.position : ""),
      tag: "", children: null
    };
  }
  function catNode(label, tag, children) {
    var count = children.reduce(function (s, c) { return s + (c.kind === "emp" ? 1 : c.count); }, 0);
    return { id: "c" + (++nid), kind: "cat", title: label, tag: tag, count: count, subtitle: count + " employees", children: children.length ? children : null };
  }
  function groupBy(list, keyFn) {
    var m = {}, order = [];
    list.forEach(function (e) { var k = keyFn(e); if (!(k in m)) { m[k] = []; order.push(k); } m[k].push(e); });
    order.sort(function (a, b) { return a.localeCompare(b); });
    return { map: m, keys: order };
  }
  function sortedEmps(list) { return list.slice().sort(function (a, b) { return (a.nameEn || "").localeCompare(b.nameEn || ""); }).map(empNode); }
  // leaf grouping: a list of employees -> Position nodes (or Manager nodes), each holding its employees.
  function posNodes(list) {
    var g = groupBy(list, function (e) { return e.position || "(No Position)"; });
    return g.keys.map(function (k) { return catNode(k, "POSITION", sortedEmps(g.map[k])); });
  }
  function mgrNodes(list) {
    var g = groupBy(list, function (e) { var m = e.managerCode && BY[e.managerCode]; return m ? (m.nameEn || e.managerCode) : "(No Manager)"; });
    return g.keys.map(function (k) { return catNode(k, "MANAGER", sortedEmps(g.map[k])); });
  }

  function buildManager(emps, f) {
    var active = filterActive(f);
    var keep = null;
    if (active) {
      keep = {};
      emps.forEach(function (e) { if (passes(e, f)) { keep[e.code] = 1; (e.path || []).forEach(function (c) { keep[c] = 1; }); } });
    }
    function inc(code) { return !active || keep[code]; }
    function build(code) {
      var e = BY[code];
      var kids = (REPORTS[code] || []).filter(inc).map(build);
      var n = empNode(e);
      n.children = kids.length ? kids : null;
      n.count = kids.length;
      return n;
    }
    // top roots = no managerCode, or manager code absent from the file (dangling), in kept set
    //             + pruned local roots (manager filtered out)
    var topRoots = EMP.filter(function (e) { return (!e.managerCode || !BY[e.managerCode]) && inc(e.code); }).map(function (e) { return e.code; });
    if (active) {
      EMP.forEach(function (e) { if (keep[e.code] && e.managerCode && !keep[e.managerCode] && topRoots.indexOf(e.code) < 0) topRoots.push(e.code); });
    }
    // split: real tops (have reports) stay top-level; unattached (no reports) -> "No Manager"
    var realTops = [], noMgr = [];
    topRoots.forEach(function (code) { (BY[code].directReports > 0 ? realTops : noMgr).push(code); });
    realTops.sort(function (a, b) { return BY[b].directReports - BY[a].directReports; });

    var children = realTops.map(build);
    if (noMgr.length) {
      var bucket = { id: "c" + (++nid), kind: "cat", tag: "GROUP", title: "No Manager",
        subtitle: noMgr.length + " unassigned employees", count: noMgr.length,
        children: noMgr.sort(function (a, b) { return (BY[a].nameEn || "").localeCompare(BY[b].nameEn || ""); }).map(build) };
      children.push(bucket);
    }
    return { id: "root", kind: "root", title: D.sourceName ? "All Employees" : "Company", tag: "COMPANY", subtitle: children.length + " top nodes", count: emps.length, children: children };
  }

  function buildDivision(emps) {
    var g = groupBy(emps, function (e) { return e.division || "(No Division)"; });
    var cats = g.keys.map(function (k) { return catNode(k, "DIVISION", posNodes(g.map[k])); });
    return { id: "root", kind: "root", title: "By Division › Position", tag: "COMPANY", count: emps.length, subtitle: g.keys.length + " divisions", children: cats };
  }
  function buildDepartment(emps) {
    var g = groupBy(emps, function (e) { return e.department || "(No Department)"; });
    var cats = g.keys.map(function (k) { return catNode(k, "DEPARTMENT", posNodes(g.map[k])); });
    return { id: "root", kind: "root", title: "By Department › Position", tag: "COMPANY", count: emps.length, subtitle: g.keys.length + " departments", children: cats };
  }
  function buildDivDept(emps) {
    var g = groupBy(emps, function (e) { return e.division || "(No Division)"; });
    var cats = g.keys.map(function (dk) {
      var gd = groupBy(g.map[dk], function (e) { return e.department || "(No Department)"; });
      var deps = gd.keys.map(function (pk) { return catNode(pk, "DEPARTMENT", posNodes(gd.map[pk])); });
      return catNode(dk, "DIVISION", deps);
    });
    return { id: "root", kind: "root", title: "By Division › Department › Position", tag: "COMPANY", count: emps.length, subtitle: g.keys.length + " divisions", children: cats };
  }
  function buildDivDeptMgr(emps) {
    var g = groupBy(emps, function (e) { return e.division || "(No Division)"; });
    var cats = g.keys.map(function (dk) {
      var gd = groupBy(g.map[dk], function (e) { return e.department || "(No Department)"; });
      var deps = gd.keys.map(function (pk) { return catNode(pk, "DEPARTMENT", mgrNodes(gd.map[pk])); });
      return catNode(dk, "DIVISION", deps);
    });
    return { id: "root", kind: "root", title: "By Division › Department › Manager", tag: "COMPANY", count: emps.length, subtitle: g.keys.length + " divisions", children: cats };
  }
  function buildPosition(emps) {
    var LEVEL_ORDER = ["Management", "Supervisory", "Professional / Technical", "Operations / Labor", "Other", "Unspecified"];
    var g = groupBy(emps, function (e) { return window.EmpData.positionLevel(e.position); });
    var cats = g.keys.slice().sort(function (a, b) { return LEVEL_ORDER.indexOf(a) - LEVEL_ORDER.indexOf(b); }).map(function (lvl) {
      var gp = groupBy(g.map[lvl], function (e) { return e.position || "(No Position)"; });
      var poss = gp.keys.map(function (pk) { return catNode(pk, "POSITION", sortedEmps(gp.map[pk])); });
      return catNode(lvl, "LEVEL", poss);
    });
    return { id: "root", kind: "root", title: "By Position level", tag: "COMPANY", count: emps.length, subtitle: g.keys.length + " levels", children: cats };
  }

  function buildTree() {
    nid = 0;
    var f = currentFilter();
    var emps = EMP.filter(function (e) { return passes(e, f); });
    switch (dimension()) {
      case "division": return buildDivision(emps);
      case "department": return buildDepartment(emps);
      case "divdept": return buildDivDept(emps);
      case "divdeptmgr": return buildDivDeptMgr(emps);
      case "position": return buildPosition(emps);
      default: return buildManager(EMP, f);
    }
  }

  /* ======================================================================
     D3 CHART
     ====================================================================== */
  var svg = d3.select("#chart"), gRoot, gLink, gNode, zoom;
  var NW = 226, NH = 50;
  // node-separation for each orientation: [ sibling gap , level gap ] in layout space
  // "Vertical" = left-to-right tree (root left, branches run down the page → tall).
  // "Horizontal" = top-down org chart (root top, levels are horizontal rows → wide).
  var SIZE = { vertical: [64, 300], horizontal: [250, 118] };
  var orient = (function () { try { return localStorage.getItem("emp_orient") || "vertical"; } catch (e) { return "vertical"; } })();
  var tree = d3.tree();
  var linkV = d3.linkVertical().x(function (d) { return d.x; }).y(function (d) { return d.y; });
  var linkH = d3.linkHorizontal().x(function (d) { return d.y; }).y(function (d) { return d.x; });
  var rootNode = null, uid = 0;

  function topDown() { return orient === "horizontal"; }   // horizontal label renders the top-down chart
  function nx(d) { return topDown() ? d.x : d.y; }        // layout -> screen X
  function ny(d) { return topDown() ? d.y : d.x; }        // layout -> screen Y
  function nx0(d) { return topDown() ? d.x0 : d.y0; }
  function ny0(d) { return topDown() ? d.y0 : d.x0; }
  function linkGen(o) { return topDown() ? linkV(o) : linkH(o); }

  function initSvg() {
    svg.selectAll("*").remove();
    gRoot = svg.append("g");
    gLink = gRoot.append("g");
    gNode = gRoot.append("g");
    zoom = d3.zoom().scaleExtent([0.05, 2.5]).on("zoom", function (ev) { gRoot.attr("transform", ev.transform); });
    svg.call(zoom);
  }

  function render(expandAll) {
    initSvg();
    tree.nodeSize(SIZE[orient]);
    rootNode = d3.hierarchy(buildTree(), function (d) { return d.children; });
    rootNode.x0 = 0; rootNode.y0 = 0;
    rootNode.descendants().forEach(function (d) {
      d.uid = ++uid; d._children = d.children;
      if (!expandAll && d.depth > 0) d.children = null;   // collapse below root
    });
    update(rootNode);
    setTimeout(fit, 30);
    updateHint();
  }

  function accordionOn() { var c = document.getElementById("accordion"); return c && c.checked; }

  function toggle(d) {
    if (d.children) {                       // collapsing
      d._children = d.children; d.children = null;
    } else {                                // expanding
      d.children = d._children;
      if (accordionOn() && d.parent && d.parent.children) {  // collapse the other open branches at this level
        d.parent.children.forEach(function (s) { if (s !== d && s.children) { s._children = s.children; s.children = null; } });
      }
    }
    update(d.parent || d);
  }

  function update(source) {
    tree(rootNode);
    var nodes = rootNode.descendants(), links = rootNode.links();
    var t = svg.transition().duration(220);

    var node = gNode.selectAll("g.node").data(nodes, function (d) { return d.uid; });

    var enter = node.enter().append("g").attr("class", "node")
      .attr("transform", function () { return "translate(" + nx0(source) + "," + ny0(source) + ")"; })
      .style("opacity", 0)
      .on("click", function (ev, d) {
        if (d.data.kind === "emp") selectEmp(d.data.code, false);
        toggle(d);
      });

    enter.append("rect").attr("x", -NW / 2).attr("y", -NH / 2).attr("width", NW).attr("height", NH);
    enter.append("text").attr("class", "n-tag").attr("x", -NW / 2 + 12).attr("y", -NH / 2 + 13);
    enter.append("text").attr("class", "n-name").attr("x", -NW / 2 + 12).attr("y", 2);
    enter.append("text").attr("class", "n-sub").attr("x", -NW / 2 + 12).attr("y", 16);
    var bx = topDown() ? 0 : NW / 2 - 2, byy = topDown() ? NH / 2 : 0;
    var badge = enter.append("g").attr("class", "badge");
    badge.append("circle").attr("class", "badge-c").attr("r", 11).attr("cx", bx).attr("cy", byy);
    badge.append("text").attr("class", "badge-t").attr("x", bx).attr("y", byy + 3.5).attr("text-anchor", "middle");

    var all = enter.merge(node);
    all.attr("class", function (d) {
      return "node kind-" + d.data.kind
        + (d._children && !d.children ? " collapsed" : "")
        + (d.data._match ? " match" : "") + (d.data._onpath ? " onpath" : "");
    });
    all.transition(t).attr("transform", function (d) { return "translate(" + nx(d) + "," + ny(d) + ")"; }).style("opacity", 1);
    all.select(".n-tag").text(function (d) { return d.data.tag || ""; });
    all.select(".n-name").text(function (d) { return trim(d.data.title, 26); });
    all.select(".n-sub").text(function (d) { return trim(d.data.subtitle || "", 30); });
    all.select(".badge").style("display", function (d) { return (!d.children && d._children) ? null : "none"; });
    all.select(".badge-t").text(function (d) { var n = countDesc(d); return n > 999 ? "999+" : n; });

    node.exit().transition(t).attr("transform", function () { return "translate(" + nx(source) + "," + ny(source) + ")"; }).style("opacity", 0).remove();

    var link = gLink.selectAll("path.link").data(links, function (d) { return d.target.uid; });
    var le = link.enter().append("path").attr("class", "link")
      .attr("d", function () { var o = { x: source.x0, y: source.y0 }; return linkGen({ source: o, target: o }); });
    le.merge(link).transition(t)
      .attr("class", function (d) { return "link" + (d.target.data._onpath ? " onpath" : ""); })
      .attr("d", function (d) { return linkGen(d); });
    link.exit().transition(t).attr("d", function () { var o = { x: source.x, y: source.y }; return linkGen({ source: o, target: o }); }).remove();

    rootNode.eachBefore(function (d) { d.x0 = d.x; d.y0 = d.y; });
  }

  function countDesc(d) { var n = 0; (function w(x) { var k = x.children || x._children; if (k) k.forEach(function (c) { n++; w(c); }); })(d); return n; }
  function trim(s, n) { s = s || ""; return s.length > n ? s.slice(0, n - 1) + "…" : s; }

  /* zoom */
  function fit() {
    var g = gRoot.node(); if (!g) return; var b = g.getBBox();
    if (!b.width || !b.height) return;
    var sz = svg.node().getBoundingClientRect();
    var scale = Math.max(Math.min(sz.width / (b.width + 140), sz.height / (b.height + 140), 1.3), 0.05);
    svg.transition().duration(300).call(zoom.transform, d3.zoomIdentity.translate(sz.width / 2 - scale * (b.x + b.width / 2), sz.height / 2 - scale * (b.y + b.height / 2)).scale(scale));
  }
  function centerOn(d, scale) {
    scale = scale || 0.9; var sz = svg.node().getBoundingClientRect();
    var ty = (topDown() ? sz.height / 3 : sz.height / 2.6) - scale * ny(d);
    svg.transition().duration(450).call(zoom.transform, d3.zoomIdentity.translate(sz.width / 2 - scale * nx(d), ty).scale(scale));
  }
  d3.select("#zin").on("click", function () { svg.transition().call(zoom.scaleBy, 1.3); });
  d3.select("#zout").on("click", function () { svg.transition().call(zoom.scaleBy, 1 / 1.3); });
  d3.select("#zfit").on("click", fit);

  /* focus a given employee code within the CURRENT dimension's tree */
  function focusCode(code, doSelect) {
    // in focus mode, collapse everything first so only the target's branch is shown
    if (accordionOn()) {
      (function collapse(d) { var k = d.children || d._children; if (k) k.forEach(collapse); if (d.children && d.parent) { d._children = d.children; d.children = null; } })(rootNode);
    }
    var target = null;
    (function expand(d) {
      if (d.data.code === code && d.data.kind === "emp") { target = d; return true; }
      var kids = d.children || d._children; if (!kids) return false;
      for (var i = 0; i < kids.length; i++) { if (expand(kids[i])) { if (!d.children) d.children = d._children; return true; } }
      return false;
    })(rootNode);
    (function clear(d) { d.data._match = false; d.data._onpath = false; var k = d.children || d._children; if (k) k.forEach(clear); })(rootNode);
    if (target) {
      target.data._match = true;
      target.ancestors().forEach(function (a) { a.data._onpath = true; });
      target.data._onpath = false;
      update(rootNode);
      setTimeout(function () { centerOn(target, 0.95); }, 260);
      if (doSelect !== false) selectEmp(code, false);
      return true;
    }
    return false;
  }

  /* ======================================================================
     DETAIL PANEL
     ====================================================================== */
  var detail = document.getElementById("detail");
  function selectEmp(code, alsoFocus) {
    var e = BY[code]; if (!e) return;
    if (alsoFocus) focusCode(code, false);
    var mgr = e.managerCode && BY[e.managerCode];
    var crumbs = (e.path || []).slice().reverse().map(function (c) { var x = BY[c]; return "<a href='#' data-code='" + c + "'>" + esc(x ? (x.nameEn || c) : c) + "</a>"; });
    crumbs.push("<b>" + esc(e.nameEn || e.code) + "</b>");
    var facts = [
      ["Department", e.department || "—"], ["Division", e.division || "—"],
      ["Section", e.section || "—"],
      ["Site", e.site || "—"], ["Branch", e.branchCode || "—"],
      ["Nationality", e.nationality || "—"], ["Gender", e.gender || "—"],
      ["Hire Date", e.hireDate || "—"], ["ID Number", e.idNumber || "—"],
      ["Cost Center", e.costCenter || "—"], ["Direct Reports", String(e.directReports)],
      ["Manager", mgr ? "<a href='#' data-code='" + mgr.code + "'>" + esc(mgr.nameEn) + "</a>" : "<span class='pill danger'>none</span>"]
    ];
    detail.innerHTML = "<div class='dt'>"
      + "<div class='dt-id'><div class='dt-name'>" + esc(e.nameEn || e.code) + "</div>"
      + "<div class='dt-sub'>#" + esc(e.code) + " · " + esc(e.position || "—") + "</div></div>"
      + "<div class='dt-body'><div class='dt-facts'>"
      + facts.map(function (r) { return "<div class='dt-fact'><span class='k'>" + r[0] + "</span><span class='v' title='" + esc(String(r[1]).replace(/<[^>]*>/g, "")) + "'>" + r[1] + "</span></div>"; }).join("")
      + "</div><div class='dt-crumb'><span class='k'>Reporting line</span> " + crumbs.join(" › ") + "</div></div>"
      + "<button class='dt-close' title='Close'>×</button></div>";
    detail.querySelectorAll("[data-code]").forEach(function (a) { a.addEventListener("click", function (ev) { ev.preventDefault(); selectEmp(a.getAttribute("data-code"), true); }); });
    detail.querySelector(".dt-close").addEventListener("click", function () {
      detail.innerHTML = "<div class='d-empty'>Select an employee to see their details, reporting chain, and direct reports.</div>";
    });
  }
  function esc(s) { return String(s == null ? "" : s).replace(/[&<>"]/g, function (c) { return ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]; }); }

  /* ======================================================================
     SEARCH + toolbar
     ====================================================================== */
  var searchBox = document.getElementById("search");
  function doSearch() {
    var raw = searchBox.value.trim(), q = raw.toLowerCase(); if (!q) return;
    var hit = BY[raw] || EMP.find(function (e) { return (e.nameEn || "").toLowerCase() === q; })
      || EMP.find(function (e) { return (e.nameEn || "").toLowerCase().indexOf(q) >= 0; });
    if (!hit) { alert("No employee matches: " + raw); return; }
    switchTab("chart");
    if (!focusCode(hit.code, true)) {
      // employee filtered out of current view — clear filters and retry
      ["fBranch", "fSite", "fDiv", "fDept"].forEach(function (id) { document.getElementById(id).value = ""; });
      render(false); setTimeout(function () { focusCode(hit.code, true); }, 60);
    }
  }
  searchBox.addEventListener("keydown", function (e) { if (e.key === "Enter") doSearch(); });

  function applyOrientClass() { document.body.classList.toggle("orient-vertical", orient === "vertical"); document.body.classList.toggle("orient-horizontal", orient === "horizontal"); }
  document.getElementById("orient").addEventListener("change", function () {
    orient = this.value;
    try { localStorage.setItem("emp_orient", orient); } catch (e) { }
    applyOrientClass(); render(false);
  });
  document.getElementById("accordion").addEventListener("change", function () {
    if (!this.checked || !rootNode) return;
    // enforce immediately: keep at most one open branch per level so the effect is visible right away
    (function prune(d) {
      var kids = d.children; if (!kids) return;
      var openKept = false;
      kids.forEach(function (c) {
        if (!c.children) return;
        if (openKept) { c._children = c.children; c.children = null; }
        else { openKept = true; prune(c); }
      });
    })(rootNode);
    update(rootNode); setTimeout(fit, 40);
  });
  document.getElementById("dim").addEventListener("change", function () { render(false); buildOrgTree(); });
  ["fBranch", "fSite", "fDiv", "fDept"].forEach(function (id) {
    document.getElementById(id).addEventListener("change", function () {
      var f = currentFilter(), active = filterActive(f);
      render(false);
      if (active) {
        var kept = EMP.filter(function (e) { return passes(e, f); }).length;
        if (kept <= 400) { rootNode.descendants().forEach(function (d) { if (d._children) d.children = d._children; }); update(rootNode); setTimeout(fit, 40); }
      }
      buildOrgTree();
    });
  });
  document.getElementById("btnExpand").addEventListener("click", function () { render(false); });

  function updateHint() {
    var f = currentFilter(), n = EMP.filter(function (e) { return passes(e, f); }).length;
    document.getElementById("dimHint").textContent = filterActive(f) ? (n + " employees match filters") : "";
  }

  /* legend */
  document.getElementById("legend").innerHTML =
    "<span class='lg'><i class='sw sw-cat'></i> group / category</span><span class='lg'><i class='sw sw-emp'></i> employee (#id · position)</span>";

  /* ======================================================================
     ORG STRUCTURE TREE (Division > Department > Section > Sub > employees)
     with explicit LEVEL LABELS on every row
     ====================================================================== */
  document.getElementById("orgLegend").innerHTML =
    "Levels: " + [["Division", 0], ["Department", 1], ["Section", 2], ["Employee", 4]].map(function (l) {
      return "<span class='lvl-tag lvl" + l[1] + "'>" + l[0] + "</span>";
    }).join(" ");

  function buildOrgTree() {
    var f = currentFilter();
    var host = document.getElementById("orgtree");
    var LEVELS = ["division", "department", "section"];
    var LABELS = ["Division", "Department", "Section"];
    var tree = {};
    EMP.forEach(function (e) {
      if (!passes(e, f)) return;
      var node = tree;
      LEVELS.forEach(function (lv, i) {
        var key = e[lv] || "(no " + LABELS[i].toLowerCase() + ")";
        node = (node[key] = node[key] || (i === LEVELS.length - 1 ? { __emps: [] } : {}));
      });
      node.__emps.push(e);
    });
    function count(obj) { if (obj.__emps) return obj.__emps.length; var n = 0; for (var k in obj) if (k !== "__emps") n += count(obj[k]); return n; }

    function makeRow(label, level, cnt, isEmp, emp) {
      var el = document.createElement("div"); el.className = "tnode";
      var row = document.createElement("div"); row.className = "row";
      var tag = isEmp ? "<span class='lvl-tag lvl4'>Employee</span>"
        : "<span class='lvl-tag lvl" + level + "'>" + LABELS[level] + "</span>";
      var caret = isEmp ? "" : "▸";
      var extra = isEmp ? "<span class='count-badge'>#" + esc(emp.code) + " · " + esc(emp.position || "") + "</span>"
        : "<span class='count-badge'>" + cnt + " employees</span>";
      row.innerHTML = "<span class='tw'>" + caret + "</span>" + tag + "<span class='tlabel'>" + esc(label) + "</span> " + extra;
      el.appendChild(row);
      return { el: el, row: row };
    }

    function build(obj, level) {
      var frag = document.createDocumentFragment();
      if (obj.__emps) {
        obj.__emps.slice().sort(function (a, b) { return (a.nameEn || "").localeCompare(b.nameEn || ""); }).forEach(function (e) {
          var m = makeRow(e.nameEn || e.code, 4, 0, true, e);
          m.row.addEventListener("click", function () { selectEmp(e.code, true); switchTab("chart"); });
          frag.appendChild(m.el);
        });
        return frag;
      }
      Object.keys(obj).sort().forEach(function (k) {
        var child = obj[k];
        var m = makeRow(k, level, count(child), false);
        var kids = document.createElement("div"); kids.style.display = "none"; kids.style.marginLeft = "16px";
        m.el.appendChild(kids);
        var built = false;
        m.row.addEventListener("click", function () {
          var open = kids.style.display !== "none";
          kids.style.display = open ? "none" : "block";
          m.row.querySelector(".tw").textContent = open ? "▸" : "▾";
          if (!built) { built = true; kids.appendChild(build(child, level + 1)); }
        });
        frag.appendChild(m.el);
      });
      return frag;
    }
    host.innerHTML = "";
    var top = build(tree, 0);
    if (!top.childNodes.length) host.innerHTML = "<div class='empty-note'>No employees match the current filters.</div>";
    else host.appendChild(top);
  }

  /* ======================================================================
     TABS + init + deep link
     ====================================================================== */
  function switchTab(which) {
    document.querySelectorAll(".tab").forEach(function (t) { t.classList.toggle("active", t.dataset.tab === which); });
    document.getElementById("panel-chart").style.display = which === "chart" ? "" : "none";
    document.getElementById("panel-org").style.display = which === "org" ? "" : "none";
    if (which === "chart") setTimeout(fit, 30);
  }
  document.querySelectorAll(".tab").forEach(function (t) { t.addEventListener("click", function () { switchTab(t.dataset.tab); }); });

  document.getElementById("orient").value = orient;   // keep select in sync with restored/persisted orientation
  applyOrientClass();
  render(false);
  buildOrgTree();

  function handleHash() {
    var om = /orient=(vertical|horizontal)/.exec(location.hash);
    if (om) { document.getElementById("orient").value = om[1]; orient = om[1]; applyOrientClass(); render(false); }
    var dm = /dim=([a-z]+)/.exec(location.hash);
    if (dm && document.querySelector("#dim option[value='" + dm[1] + "']")) { document.getElementById("dim").value = dm[1]; render(false); buildOrgTree(); }
    var m = /emp=([^&]+)/.exec(location.hash);
    if (m) { var code = decodeURIComponent(m[1]); if (BY[code]) { if (!dm) { document.getElementById("dim").value = "manager"; render(false); } switchTab("chart"); setTimeout(function () { focusCode(code, true); }, 60); } }
  }
  window.addEventListener("hashchange", handleHash);
  handleHash();
})();
