/* ==========================================================================
   Browse page — multi-dimensional hierarchy chart + org-structure tree.
   Dimensions: Manager (reporting) · Division · Department · Division›Department
               · Position (by inferred level).

   Dense-grid leaf clusters: in the grouping dimensions the final employee level
   is NOT laid out as one long line of nodes. Instead each terminal group renders
   its people as a wrapped grid of compact info-cards (with a department summary
   header) anchored at the group node — so a team of 100 fits on one screen.
   d3 node-separation reserves room for each open grid so clusters never overlap.
   ========================================================================== */
(function () {
  "use strict";
  var D = window.EmpData.getActive();
  if (!D) { document.body.innerHTML = "<p style='padding:30px'>data.js not found — run refresh_data.bat</p>"; return; }

  var EMP = D.employees, BY = D.byCode, REPORTS = D.reports;

  // Authoritative Head-of-Department reference (HOD sheet), keyed by normalized "division|department".
  function normKey(s) { return (s || "").toLowerCase().replace(/\s+/g, " ").trim(); }
  var HEADS = {};
  ((window.EMP_RAW && window.EMP_RAW.heads) || []).forEach(function (h) {
    HEADS[normKey(h.division) + "|" + normKey(h.department)] = { name: h.headName, code: h.headCode };
  });

  var gen = document.getElementById("gen"); if (gen) gen.textContent = D.stats.total + " employees";

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
     SUMMARY for a terminal group (department / position / manager team)
     ====================================================================== */
  function isSaudi(nat) {
    var s = (nat || "").toLowerCase();
    if (!s) return false;
    if (s.indexOf("non") >= 0 || s.indexOf("غير") >= 0) return false;
    return s.indexOf("saudi") >= 0 || s.indexOf("سعود") >= 0;
  }
  function topN(counts, n) {
    return Object.keys(counts).sort(function (a, b) { return counts[b] - counts[a]; })
      .slice(0, n).map(function (k) { return { k: k, v: counts[k] }; });
  }
  function shortName(name) { var p = (name || "").trim().split(/\s+/); return p.slice(0, 2).join(" "); }
  // the "head" of a group = the person others report up to: manager sits OUTSIDE the group,
  // and among those, the one with the most direct reports. Falls back to most-reports overall.
  function headOf(list) {
    var inGroup = {}; list.forEach(function (e) { inGroup[e.code] = 1; });
    var entry = list.filter(function (e) { return !(e.managerCode && inGroup[e.managerCode]); });
    var pool = entry.length ? entry : list, h = null;
    pool.forEach(function (e) { if (!h || (e.directReports || 0) > (h.directReports || 0)) h = e; });
    return (h && (h.directReports || 0) > 0) ? h : null;
  }
  // Use the authoritative HOD head only when the group is a single department; else fall back to the heuristic.
  function groupHead(list) {
    if (list.length) {
      var keys = {};
      list.forEach(function (e) { keys[normKey(e.division) + "|" + normKey(e.department)] = 1; });
      var uniq = Object.keys(keys);
      if (uniq.length === 1) {
        var hod = HEADS[uniq[0]];
        if (hod && hod.name) {
          var m = BY[hod.code];
          return { name: hod.name, position: m ? (m.position || "") : "", reports: m ? (m.directReports || 0) : 0, hod: true };
        }
      }
    }
    var h = headOf(list);
    return h ? { name: h.nameEn || h.code, position: h.position || "", reports: h.directReports || 0, hod: false } : null;
  }
  function summarize(list) {
    var sa = 0, sites = {}, branches = {}, nats = {}, poss = {};
    list.forEach(function (e) {
      if (isSaudi(e.nationality)) sa++;
      if (e.site) sites[e.site] = (sites[e.site] || 0) + 1;
      if (e.branchCode) branches[e.branchCode] = (branches[e.branchCode] || 0) + 1;
      nats[e.nationality || "—"] = (nats[e.nationality || "—"] || 0) + 1;
      poss[e.position || "—"] = (poss[e.position || "—"] || 0) + 1;
    });
    return {
      n: list.length, saudi: sa, nonSaudi: list.length - sa,
      sites: Object.keys(sites).length, branches: Object.keys(branches).length,
      nats: Object.keys(nats).length, topNat: topN(nats, 3), topPos: topN(poss, 3),
      head: groupHead(list)   // {name, position, reports, hod} — HOD reference preferred, heuristic fallback
    };
  }

  /* ======================================================================
     TREE BUILDERS — every builder returns a plain node:
       { id, kind:'root'|'cat'|'emp', title, subtitle, tag, count, code, children }
     A terminal group additionally carries { terminal:true, emps:[...], summary }.
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
  function empData(e) {
    return {
      code: e.code, name: e.nameEn || e.code, position: e.position || "",
      nationality: e.nationality || "", site: e.site || "", branch: e.branchCode || "",
      division: e.division || "", department: e.department || "", saudi: isSaudi(e.nationality)
    };
  }
  function catNode(label, tag, children) {
    var count = children.reduce(function (s, c) { return s + (c.kind === "emp" ? 1 : c.count); }, 0);
    return { id: "c" + (++nid), kind: "cat", title: label, tag: tag, count: count, subtitle: count + " employees", children: children.length ? children : null };
  }
  // category box (non-terminal) WITH a rollup summary line — used for divisions, departments,
  // managers-with-sub-teams, etc. so every group box carries a quick summary.
  function catGroup(label, tag, children, empList, extraSub) {
    var sm = empList ? summarize(empList) : null;
    var count = sm ? sm.n : children.reduce(function (s, c) { return s + (c.kind === "emp" ? 1 : c.count); }, 0);
    var sub = extraSub || (sm
      ? (sm.head ? "Head: " + shortName(sm.head.name) + " · " : "") + sm.n + " ppl" + (sm.saudi ? " · " + sm.saudi + " SA" : "")
      : count + " employees");
    return { id: "c" + (++nid), kind: "cat", title: label, tag: tag, count: count, subtitle: sub, summary: sm, children: children.length ? children : null };
  }
  // terminal group: a flat employee list rendered as a dense card grid (not child nodes)
  function catLeaf(label, tag, list) {
    var sm = summarize(list);
    var sub = sm.n + " ppl" + (sm.saudi ? " · " + sm.saudi + " SA" : "") + (sm.sites > 1 ? " · " + sm.sites + " sites" : "");
    return {
      id: "c" + (++nid), kind: "cat", terminal: true, tag: tag, title: label,
      count: sm.n, subtitle: sub, summary: sm,
      emps: list.slice().sort(function (a, b) { return (a.nameEn || "").localeCompare(b.nameEn || ""); }).map(empData),
      children: null
    };
  }
  function groupBy(list, keyFn) {
    var m = {}, order = [];
    list.forEach(function (e) { var k = keyFn(e); if (!(k in m)) { m[k] = []; order.push(k); } m[k].push(e); });
    order.sort(function (a, b) { return a.localeCompare(b); });
    return { map: m, keys: order };
  }
  // leaf grouping: a list of employees -> Position (or Manager) terminal groups.
  function posNodes(list) {
    var g = groupBy(list, function (e) { return e.position || "(No Position)"; });
    return g.keys.map(function (k) { return catLeaf(k, "POSITION", g.map[k]); });
  }
  function mgrNodes(list) {
    var g = groupBy(list, function (e) { var m = e.managerCode && BY[e.managerCode]; return m ? (m.nameEn || e.managerCode) : "(No Manager)"; });
    return g.keys.map(function (k) { return catLeaf(k, "MANAGER", g.map[k]); });
  }

  /* ---- Reporting forest primitives ------------------------------------------
     reportingMaps: builds an acyclic parent/children map over a population (self-references and
       reporting loops are cut so the offender becomes a seed → nobody is lost).
     forestUnit: renders a manager as a box whose ICs are a dense grid and whose sub-managers are
       nested boxes, recursively — driven by ANY childrenOf map.
     A manager's team is always expanded from the SAME childrenOf, so once a manager appears their
     FULL team is drillable even when reports sit in other departments/divisions. */
  function reportingMaps(list) {
    var poolSet = {}; list.forEach(function (e) { poolSet[e.code] = 1; });
    var parentOf = {};
    list.forEach(function (e) {
      var m = e.managerCode;
      parentOf[e.code] = (m && m !== e.code && BY[m] && poolSet[m]) ? m : null;
    });
    var state = {};   // 1 = on current path, 2 = done
    list.forEach(function seedResolve(e0) {
      var code = e0.code;
      while (code != null && !state[code]) {
        state[code] = 1;
        var p = parentOf[code];
        if (p == null || state[p] === 2) { break; }
        if (state[p] === 1) { parentOf[code] = null; break; }   // back-edge → cut → code becomes a seed
        code = p;
      }
      var c2 = e0.code, guard = 0;
      while (c2 != null && state[c2] === 1 && guard++ < 100000) { state[c2] = 2; c2 = parentOf[c2]; }
    });
    var childrenOf = {};
    list.forEach(function (e) { var p = parentOf[e.code]; if (p != null) (childrenOf[p] = childrenOf[p] || []).push(e.code); });
    return { poolSet: poolSet, parentOf: parentOf, childrenOf: childrenOf };
  }
  function forestUnit(childrenOf) {
    function isMgr(code) { return (childrenOf[code] || []).length > 0; }
    function team(code) {
      var out = [], st = [code];
      while (st.length) { var c = st.pop(); (childrenOf[c] || []).forEach(function (rc) { out.push(BY[rc]); st.push(rc); }); }
      return out;
    }
    function unit(code) {
      var me = BY[code];
      var repCodes = (childrenOf[code] || []);
      if (!repCodes.length) return empNode(me);              // no reports → plain person node (not a manager box)
      var subs = repCodes.filter(isMgr);
      var ics = repCodes.filter(function (c) { return !isMgr(c); }).map(function (c) { return BY[c]; });

      if (!subs.length) {                                    // pure team → one dense grid box
        var leaf = catLeaf(me.nameEn || code, "MANAGER", repCodes.map(function (c) { return BY[c]; }));
        leaf.mgrCode = code;
        leaf.subtitle = (me.position ? me.position + " · " : "") + repCodes.length + " report" + (repCodes.length === 1 ? "" : "s");
        return leaf;
      }
      var children = [];
      if (ics.length) children.push(catLeaf("Direct reports of " + (me.nameEn || code), "TEAM", ics));
      subs.sort(function (a, b) { return (childrenOf[b] || []).length - (childrenOf[a] || []).length; })
        .forEach(function (rc) { children.push(unit(rc)); });
      var tm = team(code);
      var node = catGroup(me.nameEn || code, "MANAGER", children, tm,
        (me.position ? me.position + " · " : "") + tm.length + " in team");
      node.mgrCode = code;
      return node;
    }
    return { unit: unit, team: team };
  }
  // Full reporting forest over an arbitrary population (roots = the population's own tops).
  function managerForest(list) {
    var M = reportingMaps(list), U = forestUnit(M.childrenOf);
    var roots = list.filter(function (e) { return M.parentOf[e.code] == null; }).map(function (e) { return e.code; });
    roots.sort(function (a, b) { return U.team(b).length - U.team(a).length; });
    return roots.map(U.unit);
  }
  // Grouped reporting forest: ONE tree over the whole (filtered) population, presented in buckets by
  // `keyFn` (division / department). A person starts a bucket sub-tree only when they have no ancestor
  // in the SAME group — so each manager's team shows in FULL (spanning departments) with no in-bucket
  // duplication. Returns { order:[key…], nodesByKey:{key:[node…]} }.
  function groupedForest(emps, keyFn) {
    var M = reportingMaps(emps), U = forestUnit(M.childrenOf);
    var buckets = {}, order = [];
    emps.forEach(function (e) {
      var mk = keyFn(e), p = M.parentOf[e.code], sameAnc = false, guard = 0;
      while (p != null && guard++ < 100000) { if (keyFn(BY[p]) === mk) { sameAnc = true; break; } p = M.parentOf[p]; }
      if (sameAnc) return;                       // covered inside a same-group ancestor's team
      if (!(mk in buckets)) { buckets[mk] = []; order.push(mk); }
      buckets[mk].push(e.code);
    });
    order.sort(function (a, b) { return a.localeCompare(b); });
    var nodesByKey = {};
    order.forEach(function (k) {
      buckets[k].sort(function (a, b) { return U.team(b).length - U.team(a).length; });
      nodesByKey[k] = buckets[k].map(U.unit);
    });
    return { order: order, nodesByKey: nodesByKey };
  }

  // Company-wide manager view = the forest over the (optionally filtered) whole population.
  function buildManager(emps, f) {
    var active = filterActive(f);
    var keep = null;
    if (active) {
      keep = {};
      EMP.forEach(function (e) { if (passes(e, f)) { keep[e.code] = 1; (e.path || []).forEach(function (c) { keep[c] = 1; }); } });
    }
    function inc(code) { return !active || !!keep[code]; }
    var pool = EMP.filter(function (e) { return inc(e.code); });
    var children = managerForest(pool);
    if (children.length === 1) return children[0];   // single top → open the org AT that person
    return { id: "root", kind: "root", title: "By Manager · all reporting lines", tag: "COMPANY", subtitle: children.length + " top nodes · " + pool.length + " people", count: pool.length, children: children };
  }

  function buildDivision(emps) {
    var gf = groupedForest(emps, function (e) { return e.division || "(No Division)"; });
    var cats = gf.order.map(function (k) {
      return catGroup(k, "DIVISION", gf.nodesByKey[k], emps.filter(function (e) { return (e.division || "(No Division)") === k; }));
    });
    return { id: "root", kind: "root", title: "By Division › Reporting line", tag: "COMPANY", count: emps.length, subtitle: gf.order.length + " divisions", children: cats };
  }
  function buildDepartment(emps) {
    var gf = groupedForest(emps, function (e) { return e.department || "(No Department)"; });
    var cats = gf.order.map(function (k) {
      return catGroup(k, "DEPARTMENT", gf.nodesByKey[k], emps.filter(function (e) { return (e.department || "(No Department)") === k; }));
    });
    return { id: "root", kind: "root", title: "By Department › Reporting line", tag: "COMPANY", count: emps.length, subtitle: gf.order.length + " departments", children: cats };
  }
  function buildDivDept(emps) {
    var g = groupBy(emps, function (e) { return e.division || "(No Division)"; });
    var cats = g.keys.map(function (dk) {
      var gd = groupBy(g.map[dk], function (e) { return e.department || "(No Department)"; });
      var deps = gd.keys.map(function (pk) { return catGroup(pk, "DEPARTMENT", posNodes(gd.map[pk]), gd.map[pk]); });
      return catGroup(dk, "DIVISION", deps, g.map[dk]);
    });
    return { id: "root", kind: "root", title: "By Division › Department › Position", tag: "COMPANY", count: emps.length, subtitle: g.keys.length + " divisions", children: cats };
  }
  function buildDivDeptMgr(emps) {
    // department-bucketed reporting forest, then nest each department box under its (root's) division
    var gf = groupedForest(emps, function (e) { return e.department || "(No Department)"; });
    var deptNode = {};
    gf.order.forEach(function (dept) {
      deptNode[dept] = catGroup(dept, "DEPARTMENT", gf.nodesByKey[dept], emps.filter(function (e) { return (e.department || "(No Department)") === dept; }));
    });
    var byDiv = {}, divOrder = [];
    gf.order.forEach(function (dept) {
      var members = emps.filter(function (e) { return (e.department || "(No Department)") === dept; });
      var div = (members[0] && members[0].division) || "(No Division)";
      if (!(div in byDiv)) { byDiv[div] = []; divOrder.push(div); }
      byDiv[div].push(deptNode[dept]);
    });
    divOrder.sort(function (a, b) { return a.localeCompare(b); });
    var cats = divOrder.map(function (div) {
      return catGroup(div, "DIVISION", byDiv[div], emps.filter(function (e) { return (e.division || "(No Division)") === div; }));
    });
    return { id: "root", kind: "root", title: "By Division › Department › Manager", tag: "COMPANY", count: emps.length, subtitle: divOrder.length + " divisions", children: cats };
  }
  // By Position — group by the ACTUAL job title from the file (accurate; no guessed levels).
  function buildPosition(emps) {
    return buildFlat(emps, function (e) { return e.position || "(No Position)"; }, "POSITION", "By Position (job title)", "job titles");
  }
  // Nationality / Branch — flat multi-seed: one dense-grid box per value.
  function buildFlat(emps, keyFn, tag, title, noun) {
    var g = groupBy(emps, keyFn);
    var cats = g.keys.map(function (k) { return catLeaf(k, tag, g.map[k]); });
    cats.sort(function (a, b) { return b.count - a.count; });   // biggest groups first
    return { id: "root", kind: "root", title: title, tag: "COMPANY", count: emps.length, subtitle: g.keys.length + " " + noun, children: cats };
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
      case "divflat": return buildFlat(emps, function (e) { return e.division || "(No Division)"; }, "DIVISION", "By Division — all employees", "divisions");
      case "deptflat": return buildFlat(emps, function (e) { return e.department || "(No Department)"; }, "DEPARTMENT", "By Department — all employees", "departments");
      case "nationality": return buildFlat(emps, function (e) { return e.nationality || "(No Nationality)"; }, "NATIONALITY", "By Nationality", "nationalities");
      case "branch": return buildFlat(emps, function (e) { return e.branchCode || "(No Branch)"; }, "BRANCH", "By Branch", "branches");
      case "managertree": return buildManager(EMP, f);   // same reporting forest, drawn as a tree (not the drill navigator)
      default: return buildManager(EMP, f);
    }
  }

  /* ======================================================================
     D3 CHART
     ====================================================================== */
  var svg = d3.select("#chart"), gRoot, gLink, gGrid, gNode, zoom;
  var NW = 178, NH = 50;
  // employee card metrics for the dense grid (kept compact so many fit on screen)
  var CW = 150, CH = 58, CG = 8, SUMH = 80, SUMGAP = 12;
  // [sibling gap, level gap] — kept tight so boxes pack close and lines between levels are short
  var SIZE = { vertical: [46, 176], horizontal: [150, 90] };
  var orient = (function () { try { return localStorage.getItem("emp_orient") || "vertical"; } catch (e) { return "vertical"; } })();
  var tree = d3.tree();
  var rootNode = null, uid = 0;
  var highlightCode = null;   // employee card to highlight after a search

  function topDown() { return orient === "horizontal"; }
  function nx(d) { return topDown() ? d.x : d.y; }
  function ny(d) { return topDown() ? d.y : d.x; }
  function nx0(d) { return topDown() ? d.x0 : d.y0; }
  function ny0(d) { return topDown() ? d.y0 : d.x0; }
  // straight, right-angle (elbow) connectors — not curved — like a classic org chart
  function linkGen(o) {
    var sx = nx(o.source), sy = ny(o.source), tx = nx(o.target), ty = ny(o.target);
    if (topDown()) { var my = sy + (ty - sy) / 2; return "M" + sx + "," + sy + "V" + my + "H" + tx + "V" + ty; }
    var mx = sx + (tx - sx) / 2; return "M" + sx + "," + sy + "H" + mx + "V" + ty + "H" + tx;
  }

  // grid geometry for a terminal group (cached per render on the d3 node)
  function clusterSize(d) {
    if (d.__cl) return d.__cl;
    var n = d.data.emps.length;
    var cols = Math.max(1, Math.min(10, Math.ceil(Math.sqrt(n * 1.4))));
    var rows = Math.ceil(n / cols);
    var gridW = cols * CW + (cols - 1) * CG, gridH = rows * CH + (rows - 1) * CG;
    var blockW = Math.max(gridW, 300), blockH = SUMH + SUMGAP + gridH;
    d.__cl = { n: n, cols: cols, rows: rows, gridW: gridW, gridH: gridH, blockW: blockW, blockH: blockH };
    return d.__cl;
  }
  function gridOpen(d) { return d.data.terminal && d.data._gridOpen; }
  // box width shrinks to fit its content — a box with little text takes less room
  function nodeW(d) {
    var t = (d.data.title || "").length, s = (d.data.subtitle || "").length;
    var need = Math.max(t * 6.5, s * 5.3) + 30;
    return Math.max(116, Math.min(NW, Math.round(need)));
  }
  // cross-axis extent a node occupies (used to reserve space between siblings)
  function crossSpan(d) {
    if (gridOpen(d)) { var s = clusterSize(d); return orient === "vertical" ? s.blockH : s.blockW; }
    return orient === "vertical" ? NH : nodeW(d);
  }

  function initSvg() {
    svg.selectAll("*").remove();
    gRoot = svg.append("g");
    gLink = gRoot.append("g");
    gGrid = gRoot.append("g");
    gNode = gRoot.append("g");
    zoom = d3.zoom().scaleExtent([0.03, 2.5]).on("zoom", function (ev) { gRoot.attr("transform", ev.transform); });
    svg.call(zoom);
  }

  function configureTree() {
    tree.nodeSize(SIZE[orient]);
    var step = SIZE[orient][0];
    tree.separation(function (a, b) {
      var need = (crossSpan(a) + crossSpan(b)) / 2 + 20;   // guaranteed gap so boxes never touch/overlap
      var base = a.parent === b.parent ? 1 : 1.06;
      return Math.max(base, need / step);
    });
  }

  function render(expandAll) {
    initSvg();
    configureTree();
    rootNode = d3.hierarchy(buildTree(), function (d) { return d.children; });
    rootNode.x0 = 0; rootNode.y0 = 0;
    rootNode.descendants().forEach(function (d) {
      d.uid = ++uid; d._children = d.children;
      if (d.data.terminal) d.data._gridOpen = !!expandAll;
      if (!expandAll && d.depth > 0) d.children = null;   // collapse below root
    });
    update(rootNode);
    setTimeout(fit, 30);
    updateHint();
  }

  function accordionOn() { var c = document.getElementById("accordion"); return c && c.checked; }

  function toggle(d) {
    if (d.data.terminal) {                       // open / close the dense employee grid
      d.data._gridOpen = !d.data._gridOpen;
      if (d.data._gridOpen && accordionOn() && d.parent && d.parent.children) {
        d.parent.children.forEach(function (s) {
          if (s === d) return;
          if (s.data.terminal) s.data._gridOpen = false;
          else if (s.children) { s._children = s.children; s.children = null; }
        });
      }
      update(d.parent || d);
      return;
    }
    if (d.children) {                            // collapsing
      d._children = d.children; d.children = null;
    } else {                                     // expanding
      d.children = d._children;
      if (accordionOn() && d.parent && d.parent.children) {
        d.parent.children.forEach(function (s) { if (s !== d && s.children) { s._children = s.children; s.children = null; } });
      }
    }
    update(d.parent || d);
  }

  function update(source) {
    rootNode.descendants().forEach(function (d) { d.__cl = null; });   // reset grid-size cache
    // Adaptive level gap: set the distance between levels to the widest visible box (+margin) so a wide
    // box never clips into the next level, while staying as tight as the content allows.
    if (orient === "vertical") {
      var maxW = NW; rootNode.each(function (d) { var w = nodeW(d); if (w > maxW) maxW = w; });
      tree.nodeSize([SIZE.vertical[0], maxW + 22]);
    } else {
      tree.nodeSize([SIZE.horizontal[0], NH + 44]);   // top-down: level gap = box height + room for the badge
    }
    tree(rootNode);
    var nodes = rootNode.descendants(), links = rootNode.links();
    var t = svg.transition().duration(220);

    /* ---- nodes ---- */
    var node = gNode.selectAll("g.node").data(nodes, function (d) { return d.uid; });

    var enter = node.enter().append("g").attr("class", "node")
      .attr("transform", function () { return "translate(" + nx0(source) + "," + ny0(source) + ")"; })
      .style("opacity", 0)
      .on("click", function (ev, d) {
        if (d.data.kind === "emp") selectEmp(d.data.code, false);
        else if (d.data.mgrCode) selectEmp(d.data.mgrCode, false);   // manager box → show their detail
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
      var collapsed = d.data.terminal ? !d.data._gridOpen : (d._children && !d.children);
      return "node kind-" + d.data.kind + (d.data.terminal ? " terminal" : "")
        + (d.data.mgrCode ? " mgr" : "")
        + (collapsed ? " collapsed" : "")
        + (d.data._match ? " match" : "") + (d.data._onpath ? " onpath" : "");
    });
    all.transition(t).attr("transform", function (d) { return "translate(" + nx(d) + "," + ny(d) + ")"; }).style("opacity", 1);
    // size each box to its content
    all.select("rect").attr("x", function (d) { return -nodeW(d) / 2; }).attr("width", nodeW);
    all.select(".n-tag").attr("x", function (d) { return -nodeW(d) / 2 + 11; });
    all.select(".n-name").attr("x", function (d) { return -nodeW(d) / 2 + 11; });
    all.select(".n-sub").attr("x", function (d) { return -nodeW(d) / 2 + 11; });
    all.select(".badge-c").attr("cx", function (d) { return topDown() ? 0 : nodeW(d) / 2 - 2; });
    all.select(".badge-t").attr("x", function (d) { return topDown() ? 0 : nodeW(d) / 2 - 2; });
    all.select(".n-tag").text(function (d) { return d.data.tag || ""; });
    all.select(".n-name").text(function (d) { var w = nodeW(d); return trim(d.data.title, Math.floor((w - 22) / 6.2)); });
    all.select(".n-sub").text(function (d) { var w = nodeW(d); return trim(d.data.subtitle || "", Math.floor((w - 22) / 5.1)); });
    all.select(".badge").style("display", function (d) {
      var show = d.data.terminal ? (!d.data._gridOpen && d.data.count > 0) : (!d.children && d._children);
      return show ? null : "none";
    });
    all.select(".badge-t").text(function (d) { var n = d.data.kind === "emp" ? countDesc(d) : d.data.count; return n > 999 ? "999+" : n; });

    node.exit().transition(t).attr("transform", function () { return "translate(" + nx(source) + "," + ny(source) + ")"; }).style("opacity", 0).remove();

    /* ---- links ---- */
    var link = gLink.selectAll("path.link").data(links, function (d) { return d.target.uid; });
    var le = link.enter().append("path").attr("class", "link")
      .attr("d", function () { var o = { x: source.x0, y: source.y0 }; return linkGen({ source: o, target: o }); });
    le.merge(link).transition(t)
      .attr("class", function (d) { return "link" + (d.target.data._onpath ? " onpath" : ""); })
      .attr("d", function (d) { return linkGen(d); });
    link.exit().transition(t).attr("d", function () { var o = { x: source.x, y: source.y }; return linkGen({ source: o, target: o }); }).remove();

    /* ---- dense employee grids (terminal groups that are open) ---- */
    renderClusters(nodes, t);

    rootNode.eachBefore(function (d) { d.x0 = d.x; d.y0 = d.y; });
  }

  /* ---- render one dense grid of employee cards per open terminal group ---- */
  function renderClusters(nodes, t) {
    var open = nodes.filter(gridOpen);
    var cl = gGrid.selectAll("g.cluster").data(open, function (d) { return d.data.id; });
    cl.exit().remove();
    var clEnter = cl.enter().append("g").attr("class", "cluster").style("opacity", 0);
    var clAll = clEnter.merge(cl);
    clAll.transition(t).attr("transform", function (d) { return "translate(" + nx(d) + "," + ny(d) + ")"; }).style("opacity", 1);
    clAll.each(function (d) { drawCluster(d3.select(this), d); });
  }

  function drawCluster(g, d) {
    g.selectAll("*").remove();
    var s = clusterSize(d), sm = d.data.summary;
    var vert = orient === "vertical";
    var nw = nodeW(d);
    var bx0, by0;   // block top-left, relative to node centre
    var gridX, gridY;
    if (vert) { bx0 = nw / 2 + 28; by0 = -s.blockH / 2; gridX = bx0; gridY = by0 + SUMH + SUMGAP; }
    else { by0 = NH / 2 + 26; bx0 = -s.blockW / 2; gridX = bx0 + (s.blockW - s.gridW) / 2; gridY = by0 + SUMH + SUMGAP; }

    // connector spine from the group node to the block (straight, kept short)
    var spine = vert
      ? "M " + (nw / 2) + " 0 H " + (bx0 - 12) + " M " + (bx0 - 12) + " " + by0 + " V " + (by0 + s.blockH)
      : "M 0 " + (NH / 2) + " V " + (by0 - 12) + " M " + bx0 + " " + (by0 - 12) + " H " + (bx0 + s.blockW);
    g.append("path").attr("class", "cl-spine").attr("d", spine);

    // faint container behind the whole block
    g.append("rect").attr("class", "cl-bg").attr("x", bx0 - 8).attr("y", by0 - 8)
      .attr("width", s.blockW + 16).attr("height", s.blockH + 16).attr("rx", 14).attr("ry", 14);

    /* ---- summary header ---- */
    drawSummary(g, d, bx0, by0, s.blockW, sm);

    /* ---- employee cards ---- */
    var cards = g.selectAll("g.ecard").data(d.data.emps).enter().append("g")
      .attr("class", function (c) { return "ecard" + (c.code === highlightCode ? " sel" : ""); })
      .attr("transform", function (c, i) {
        var col = i % s.cols, row = Math.floor(i / s.cols);
        return "translate(" + (gridX + col * (CW + CG)) + "," + (gridY + row * (CH + CG)) + ")";
      })
      .on("click", function (ev, c) {
        ev.stopPropagation();
        highlightCode = c.code;
        g.selectAll("g.ecard").classed("sel", function (x) { return x.code === c.code; });
        selectEmp(c.code, false);
      });
    cards.append("rect").attr("width", CW).attr("height", CH).attr("rx", 8).attr("ry", 8);
    cards.append("rect").attr("class", "ec-flag").attr("x", 0).attr("y", 0).attr("width", 4).attr("height", CH)
      .attr("fill", function (c) { return c.saudi ? "#3a9c78" : "#c7d0dc"; });
    cards.append("text").attr("class", "ec-name").attr("x", 10).attr("y", 16).text(function (c) { return trim(c.name, 20); });
    cards.append("text").attr("class", "ec-pos").attr("x", 10).attr("y", 29).text(function (c) { return trim(c.position || "—", 23); });
    cards.append("text").attr("class", "ec-meta").attr("x", 10).attr("y", 41).text(function (c) { return "#" + c.code; });
    cards.append("text").attr("class", "ec-nat").attr("x", 10).attr("y", 52).text(function (c) { return trim((c.nationality || "—") + (c.site ? " · " + c.site : ""), 25); });
    cards.append("title").text(function (c) {
      return c.name + "\n" + (c.position || "—") + "\n#" + c.code + " · " + (c.nationality || "—")
        + "\n" + (c.department || "") + (c.site ? " · " + c.site : "") + (c.branch ? " · " + c.branch : "");
    });
  }

  function drawSummary(g, d, bx0, by0, w, sm) {
    var sum = g.append("g").attr("class", "cl-sum").attr("transform", "translate(" + bx0 + "," + by0 + ")");
    sum.append("rect").attr("class", "cl-sum-bg").attr("width", w).attr("height", SUMH).attr("rx", 10).attr("ry", 10);
    sum.append("text").attr("class", "cl-sum-title").attr("x", 12).attr("y", 17).text(trim((d.data.tag ? d.data.tag + " · " : "") + d.data.title, Math.floor(w / 8)));

    // line 2: the group's head if we can identify one, else the top roles
    if (sm.head) {
      sum.append("text").attr("class", "cl-sum-head").attr("x", 12).attr("y", 32)
        .text(trim("★ Head: " + sm.head.name + (sm.head.position ? " — " + sm.head.position : "") + " (" + sm.head.reports + " reports)", Math.floor(w / 6)));
    } else {
      var pos = sm.topPos.map(function (p) { return p.k + " (" + p.v + ")"; }).join("   ·   ");
      sum.append("text").attr("class", "cl-sum-foot").attr("x", 12).attr("y", 32).text(trim("Top roles:  " + pos, Math.floor(w / 6)));
    }

    var stats = [["HEADCOUNT", sm.n], ["SAUDI (nat.)", sm.saudi], ["NON-SAUDI", sm.nonSaudi], ["SITES", sm.sites], ["NATIONALITIES", sm.nats]];
    var colW = w / stats.length;
    stats.forEach(function (st, i) {
      var gx = 12 + i * colW;
      sum.append("text").attr("class", "cl-sum-k").attr("x", gx).attr("y", 48).text(st[0]);
      sum.append("text").attr("class", "cl-sum-v").attr("x", gx).attr("y", 61).text(st[1]);
    });

    // Saudi / non-Saudi mix bar
    var barW = w - 24, sfrac = sm.n ? sm.saudi / sm.n : 0;
    sum.append("rect").attr("class", "cl-sum-bar-bg").attr("x", 12).attr("y", 68).attr("width", barW).attr("height", 6).attr("rx", 3).attr("ry", 3);
    sum.append("rect").attr("class", "cl-sum-bar-sa").attr("x", 12).attr("y", 68).attr("width", Math.max(0, barW * sfrac)).attr("height", 6).attr("rx", 3).attr("ry", 3);
  }

  function countDesc(d) { var n = 0; (function w(x) { var k = x.children || x._children; if (k) k.forEach(function (c) { n++; w(c); }); })(d); return n; }
  function trim(s, n) { s = s || ""; return s.length > n ? s.slice(0, n - 1) + "…" : s; }

  /* zoom */
  function fit() {
    if (!gRoot) return;
    var g = gRoot.node(); if (!g) return; var b = g.getBBox();
    if (!b.width || !b.height) return;
    var sz = svg.node().getBoundingClientRect();
    var scale = Math.max(Math.min(sz.width / (b.width + 140), sz.height / (b.height + 140), 1.3), 0.03);
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

  /* maximize the chart to fill the whole screen (any monitor size), and back */
  function setFull(on) {
    var p = document.getElementById("panel-chart");
    p.classList.toggle("fs", on);
    document.body.classList.toggle("chart-fs", on);
    var b = document.getElementById("zfull"); if (b) b.textContent = on ? "🗕" : "⛶";
    setTimeout(fit, 60);
  }
  d3.select("#zfull").on("click", function () { setFull(!document.getElementById("panel-chart").classList.contains("fs")); });
  document.addEventListener("keydown", function (e) {
    if (e.key === "Escape" && document.getElementById("panel-chart").classList.contains("fs")) setFull(false);
  });
  window.addEventListener("resize", function () { if (rootNode) setTimeout(fit, 80); });

  /* focus a given employee code within the CURRENT dimension's tree */
  function focusCode(code, doSelect) {
    // 1) manager dimension — the employee is a real node
    if (accordionOn()) {
      (function collapse(d) { var k = d.children || d._children; if (k) k.forEach(collapse); if (d.children && d.parent) { d._children = d.children; d.children = null; } })(rootNode);
    }
    (function clear(d) { d.data._match = false; d.data._onpath = false; var k = d.children || d._children; if (k) k.forEach(clear); })(rootNode);

    var target = null;
    (function expand(d) {
      if (d.data.code === code && d.data.kind === "emp") { target = d; return true; }
      var kids = d.children || d._children; if (!kids) return false;
      for (var i = 0; i < kids.length; i++) { if (expand(kids[i])) { if (!d.children) d.children = d._children; return true; } }
      return false;
    })(rootNode);
    if (target) {
      target.data._match = true;
      target.ancestors().forEach(function (a) { a.data._onpath = true; });
      target.data._onpath = false; highlightCode = null;
      update(rootNode);
      setTimeout(function () { centerOn(target, 0.95); }, 260);
      if (doSelect !== false) selectEmp(code, false);
      return true;
    }

    // 2) grouping dimension — the employee lives inside a terminal group's grid
    var grp = null;
    (function find(d) {
      if (grp) return;
      if (d.data.terminal && d.data.emps && d.data.emps.some(function (e) { return e.code === code; })) { grp = d; return; }
      var kids = d.children || d._children; if (kids) kids.forEach(find);
    })(rootNode);
    if (grp) {
      grp.ancestors().reverse().forEach(function (a) {   // reveal the path down to the group
        if (a._children && !a.children) a.children = a._children;
        if (accordionOn() && a.children) a.children.forEach(function (s) {
          if (grp.ancestors().indexOf(s) >= 0 || s === grp) return;
          if (s.data.terminal) s.data._gridOpen = false;
          else if (s.children) { s._children = s.children; s.children = null; }
        });
      });
      grp.data._gridOpen = true; grp.data._match = true;
      grp.ancestors().forEach(function (a) { a.data._onpath = true; });
      grp.data._onpath = false; highlightCode = code;
      update(rootNode);
      setTimeout(function () { centerOn(grp, 0.85); }, 300);
      if (doSelect !== false) selectEmp(code, false);
      return true;
    }

    // 3) the person is a manager — focus their own box
    var box = null;
    (function find(d) { if (box) return; if (d.data.mgrCode === code) { box = d; return; } var kids = d.children || d._children; if (kids) kids.forEach(find); })(rootNode);
    if (box) {
      box.ancestors().reverse().forEach(function (a) { if (a._children && !a.children) a.children = a._children; });
      if (box.data.terminal) box.data._gridOpen = true;
      box.data._match = true;
      box.ancestors().forEach(function (a) { a.data._onpath = true; });
      box.data._onpath = false; highlightCode = null;
      update(rootNode);
      setTimeout(function () { centerOn(box, 0.9); }, 300);
      if (doSelect !== false) selectEmp(code, false);
      return true;
    }
    return false;
  }

  /* ======================================================================
     MANAGER DRILL-DOWN NAVIGATOR (HTML) — one level fills the screen as a
     wrapped grid; managers are boxes you click to go deeper, staff are cards,
     a breadcrumb bar walks back up. Handles any number of managers per level.
     ====================================================================== */
  var navHost = document.getElementById("navigator");
  var NAV = null, navPath = [];   // navPath = the codes drilled into; [] = top ("All managers")

  function isNavMode() { return dimension() === "manager"; }

  // cycle-safe forest over the (optionally filtered) population + team-size memo
  function buildForestData(inc) {
    var pool = EMP.filter(function (e) { return inc(e.code); });
    var poolSet = {}; pool.forEach(function (e) { poolSet[e.code] = 1; });
    var parentOf = {};
    pool.forEach(function (e) { var m = e.managerCode; parentOf[e.code] = (m && m !== e.code && poolSet[m]) ? m : null; });
    var state = {};
    pool.forEach(function (e0) {
      var code = e0.code;
      while (code != null && !state[code]) { state[code] = 1; var p = parentOf[code]; if (p == null || state[p] === 2) break; if (state[p] === 1) { parentOf[code] = null; break; } code = p; }
      var c2 = e0.code, g = 0; while (c2 != null && state[c2] === 1 && g++ < 100000) { state[c2] = 2; c2 = parentOf[c2]; }
    });
    var childrenOf = {};
    pool.forEach(function (e) { var p = parentOf[e.code]; if (p != null) (childrenOf[p] = childrenOf[p] || []).push(e.code); });
    var roots = pool.filter(function (e) { return parentOf[e.code] == null; }).map(function (e) { return e.code; });
    var memo = {};
    function ts(c) { if (memo[c] != null) return memo[c]; var n = 0; (childrenOf[c] || []).forEach(function (rc) { n += 1 + ts(rc); }); return memo[c] = n; }
    return { parentOf: parentOf, childrenOf: childrenOf, roots: roots, poolSet: poolSet, poolCount: pool.length, ts: ts };
  }

  function renderNav() {
    var f = currentFilter(), active = filterActive(f), keep = null;
    if (active) { keep = {}; EMP.forEach(function (e) { if (passes(e, f)) { keep[e.code] = 1; (e.path || []).forEach(function (c) { keep[c] = 1; }); } }); }
    NAV = buildForestData(function (code) { return !active || !!keep[code]; });
    NAV.tsMemo = {};
    // drop any drilled steps that are no longer in the (filtered) population
    navPath = navPath.filter(function (c) { return NAV.poolSet[c]; });
    paintNav();
  }

  // Navigate the RAW reporting links (managerCode), not the cycle-cut forest — so EVERY manager
  // stays drillable to the last level. A path stack gives breadcrumbs and blocks re-entering a
  // node already on the current path (which is how reporting cycles are made safe).
  function rawKids(code) {
    return (REPORTS[code] || []).filter(function (rc) { return NAV.poolSet[rc] && rc !== code; });
  }
  function navIsMgr(code) { return rawKids(code).some(function (rc) { return navPath.indexOf(rc) < 0; }); }
  function navKids() {
    if (!navPath.length) return NAV.roots.slice();
    return rawKids(navPath[navPath.length - 1]).filter(function (rc) { return navPath.indexOf(rc) < 0; });
  }
  // total distinct people reachable below `code` via raw links (cycle-safe)
  function teamSize(code) {
    if (NAV.tsMemo[code] != null) return NAV.tsMemo[code];
    var seen = {}, st = [code], n = 0; seen[code] = 1;
    while (st.length) { var c = st.pop(); rawKids(c).forEach(function (rc) { if (!seen[rc]) { seen[rc] = 1; n++; st.push(rc); } }); }
    return NAV.tsMemo[code] = n;
  }

  function paintNav() {
    if (!NAV || !navHost) return;
    var focus = navPath.length ? navPath[navPath.length - 1] : null;
    var kids = navKids();
    var mgrs = kids.filter(navIsMgr), staff = kids.filter(function (c) { return !navIsMgr(c); });
    mgrs.sort(function (a, b) { return teamSize(b) - teamSize(a); });
    staff.sort(function (a, b) { return (BY[a].nameEn || "").localeCompare(BY[b].nameEn || ""); });

    var h = "<div class='nav-bar'><div class='nav-crumbs'><a href='#' data-idx='-1'>🏠 All managers</a>";
    navPath.forEach(function (c, i) { h += " <span class='sep'>›</span> <a href='#' data-idx='" + i + "'>" + esc(BY[c].nameEn || c) + " #" + esc(c) + "</a>"; });
    h += "</div></div>";

    if (focus == null) {
      h += "<div class='nav-head'><div class='nh-title'>All managers</div><div class='nh-sub'>" + NAV.roots.length + " top nodes · " + NAV.poolCount + " people</div></div>";
    } else {
      var me = BY[focus];
      // upward reporting chain (this manager → their manager → … → top), each step showing the employee ID
      var up = (me.path || []);
      var upHtml = up.length
        ? up.map(function (c) { var x = BY[c]; return "<a href='#' data-navto='" + esc(c) + "'>" + esc((x ? (x.nameEn || c) : c)) + " #" + esc(c) + "</a>"; }).join(" <span class='sep'>▲</span> ")
        : "<span class='muted'>top of the organization</span>";
      h += "<div class='nav-head'><div class='nh-title'>" + esc(me.nameEn || focus) + " <button class='nh-det' data-emp='" + esc(focus) + "'>details</button></div>"
        + "<div class='nh-sub'>" + esc(me.position || "—") + " · <b>Employee ID #" + esc(focus) + "</b> · <b>" + teamSize(focus) + "</b> in team · " + kids.length + " direct</div>"
        + "<div class='nh-chain'><span class='k'>Reports to:</span> " + upHtml + "</div></div>";
    }
    if (mgrs.length) {
      h += "<div class='nav-sec'>Managers reporting here (" + mgrs.length + ") — click to open</div><div class='nav-grid'>";
      mgrs.forEach(function (c) {
        var m = BY[c];
        h += "<div class='nav-box' data-drill='" + esc(c) + "' title='" + esc((m.nameEn || c) + " — " + (m.position || "")) + "'>"
          + "<div class='nb-tag'>MANAGER · click to open ▸</div>"
          + "<div class='nb-name'>" + esc(trim(m.nameEn || c, 22)) + "</div>"
          + "<div class='nb-pos'>" + esc(trim(m.position || "—", 24)) + "</div>"
          + "<div class='nb-meta'><b>" + teamSize(c) + "</b> in team · " + rawKids(c).length + " direct</div></div>";
      });
      h += "</div>";
    }
    if (staff.length) {
      h += "<div class='nav-sec'>Direct staff (" + staff.length + ")</div><div class='nav-grid'>";
      staff.forEach(function (c) {
        var m = BY[c];
        h += "<div class='nav-card' data-emp='" + esc(c) + "'>"
          + "<span class='nc-flag' style='background:" + (isSaudi(m.nationality) ? "#3a9c78" : "#c7d0dc") + "'></span>"
          + "<div class='nc-name'>" + esc(trim(m.nameEn || c, 20)) + "</div>"
          + "<div class='nc-pos'>" + esc(trim(m.position || "—", 22)) + "</div>"
          + "<div class='nc-meta'>#" + esc(c) + " · " + esc(trim((m.nationality || "—") + (m.site ? " · " + m.site : ""), 24)) + "</div></div>";
      });
      h += "</div>";
    }
    if (!mgrs.length && !staff.length) h += "<div class='nav-empty'>No reports under this person.</div>";
    navHost.innerHTML = h;
    navHost.querySelectorAll("[data-drill]").forEach(function (el) { el.addEventListener("click", function (ev) { ev.preventDefault(); ev.stopPropagation(); navPath.push(el.getAttribute("data-drill")); navHost.scrollTop = 0; paintNav(); }); });
    navHost.querySelectorAll("[data-idx]").forEach(function (el) { el.addEventListener("click", function (ev) { ev.preventDefault(); ev.stopPropagation(); navPath = navPath.slice(0, parseInt(el.getAttribute("data-idx"), 10) + 1); navHost.scrollTop = 0; paintNav(); }); });
    navHost.querySelectorAll("[data-emp]").forEach(function (el) { el.addEventListener("click", function (ev) { ev.preventDefault(); ev.stopPropagation(); selectEmp(el.getAttribute("data-emp"), false); }); });
    navHost.querySelectorAll("[data-navto]").forEach(function (el) { el.addEventListener("click", function (ev) { ev.preventDefault(); ev.stopPropagation(); navSearch(el.getAttribute("data-navto")); }); });
  }

  // walk UP the managerCode chain to build a drill path from a top node down to `code`
  function navPathTo(code) {
    var chain = [], cur = code, seen = {}, g = 0;
    while (cur != null && !seen[cur] && g++ < 1000) {
      seen[cur] = 1; chain.unshift(cur);
      if (NAV.roots.indexOf(cur) >= 0) break;
      var p = (BY[cur] || {}).managerCode;
      cur = (p && NAV.poolSet[p] && p !== cur) ? p : null;
    }
    return chain;
  }
  function navSearch(code) {
    if (!NAV) renderNav();
    if (!NAV.poolSet[code]) { alert("That person is filtered out of the current view."); return; }
    var path = navPathTo(code);
    navPath = navIsMgr(code) ? path : path.slice(0, path.length - 1);   // managers focus themselves; staff show as a card under their manager
    paintNav();
    var sel = navHost.querySelector("[data-emp='" + code + "']") || navHost.querySelector("[data-drill='" + code + "']");
    if (sel) { sel.classList.add("nav-hit"); try { sel.scrollIntoView({ block: "center" }); } catch (e) { } }
    selectEmp(code, false);
  }

  // route: manager dimension → navigator; everything else → the SVG chart
  function draw() {
    var nav = isNavMode();
    document.getElementById("panel-chart").classList.toggle("navmode", nav);
    if (nav) renderNav(); else render(false);
  }

  /* ======================================================================
     DETAIL PANEL
     ====================================================================== */
  var detail = document.getElementById("detail");

  // Full "reports to" chain from a person UP to the top of the org, each step labelled with the
  // employee ID — so e.g. 100022 → 103933 → 103903 is unmistakable. chain[0] is the person themself.
  function reportingChainHtml(e) {
    var chain = [e.code].concat(e.path || []);   // self, then manager, manager's manager, … top
    var rows = chain.map(function (c, i) {
      var x = BY[c], nm = x ? (x.nameEn || c) : c, po = x ? (x.position || "—") : "";
      var clickable = i > 0;
      return "<div class='rl-row" + (i === 0 ? " me" : "") + "'" + (clickable ? " data-code='" + esc(c) + "'" : "") + ">"
        + "<span class='rl-id'>#" + esc(c) + "</span>"
        + "<span class='rl-nm'>" + esc(nm) + "</span>"
        + "<span class='rl-po'>" + esc(po) + "</span>"
        + (i === 0 ? "<span class='rl-tag'>this employee</span>" : (i === 1 ? "<span class='rl-tag mgr'>direct manager</span>" : ""))
        + "</div>";
    });
    var body = rows.join("<div class='rl-up'>▼ reports to</div>");
    if (chain.length === 1) body += "<div class='rl-none'>Top of the organization — reports to no one.</div>";
    return "<div class='dt-chain'><span class='k'>Reporting line — who reports to whom</span><div class='rl'>" + body + "</div></div>";
  }

  function selectEmp(code, alsoFocus) {
    var e = BY[code]; if (!e) return;
    if (alsoFocus) focusCode(code, false);
    var mgr = e.managerCode && BY[e.managerCode];
    var facts = [
      ["Employee ID", "<b class='id-big'>" + esc(e.code) + "</b>"],
      ["Reports to (ID)", mgr ? "<a href='#' data-code='" + mgr.code + "'>" + esc(mgr.nameEn || mgr.code) + " · #" + esc(mgr.code) + "</a>"
        : (e.managerCode ? "<span class='pill danger'>#" + esc(e.managerCode) + " (not in file)</span>" : "<span class='pill danger'>none</span>")],
      ["Direct Reports", String(e.directReports)],
      ["Department", e.department || "—"], ["Division", e.division || "—"],
      ["Section", e.section || "—"],
      ["Site", e.site || "—"], ["Branch", e.branchCode || "—"],
      ["Nationality", e.nationality || "—"], ["Gender", e.gender || "—"],
      ["Hire Date", e.hireDate || "—"], ["ID Number", e.idNumber || "—"],
      ["Cost Center", e.costCenter || "—"]
    ];
    detail.innerHTML = "<div class='dt'>"
      + "<div class='dt-id'><div class='dt-name'>" + esc(e.nameEn || e.code) + "</div>"
      + "<div class='dt-sub'>Employee ID <b>#" + esc(e.code) + "</b> · " + esc(e.position || "—") + "</div></div>"
      + "<div class='dt-body'><div class='dt-facts'>"
      + facts.map(function (r) { return "<div class='dt-fact'><span class='k'>" + r[0] + "</span><span class='v' title='" + esc(String(r[1]).replace(/<[^>]*>/g, "")) + "'>" + r[1] + "</span></div>"; }).join("")
      + "</div>" + reportingChainHtml(e) + "</div>"
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
    if (isNavMode()) { navSearch(hit.code); return; }
    if (!focusCode(hit.code, true)) {
      ["fBranch", "fSite", "fDiv", "fDept"].forEach(function (id) { document.getElementById(id).value = ""; });
      render(false); setTimeout(function () { focusCode(hit.code, true); }, 60);
    }
  }
  searchBox.addEventListener("keydown", function (e) { if (e.key === "Enter") doSearch(); });

  function applyOrientClass() { document.body.classList.toggle("orient-vertical", orient === "vertical"); document.body.classList.toggle("orient-horizontal", orient === "horizontal"); }
  document.getElementById("orient").addEventListener("change", function () {
    orient = this.value;
    try { localStorage.setItem("emp_orient", orient); } catch (e) { }
    applyOrientClass(); draw();
  });
  document.getElementById("accordion").addEventListener("change", function () {
    if (!this.checked || !rootNode) return;
    (function prune(d) {
      var kids = d.children; if (!kids) return;
      var openKept = false;
      kids.forEach(function (c) {
        var isOpen = c.data.terminal ? c.data._gridOpen : !!c.children;
        if (!isOpen) return;
        if (openKept) { if (c.data.terminal) c.data._gridOpen = false; else { c._children = c.children; c.children = null; } }
        else { openKept = true; prune(c); }
      });
    })(rootNode);
    update(rootNode); setTimeout(fit, 40);
  });
  document.getElementById("dim").addEventListener("change", function () { navPath = []; draw(); buildOrgTree(); });
  ["fBranch", "fSite", "fDiv", "fDept"].forEach(function (id) {
    document.getElementById(id).addEventListener("change", function () {
      if (isNavMode()) { navPath = []; draw(); buildOrgTree(); return; }
      var f = currentFilter(), active = filterActive(f);
      render(false);
      if (active) {
        var kept = EMP.filter(function (e) { return passes(e, f); }).length;
        if (kept <= 400) {
          rootNode.descendants().forEach(function (d) { if (d._children) d.children = d._children; if (d.data.terminal) d.data._gridOpen = true; });
          update(rootNode); setTimeout(fit, 40);
        }
      }
      buildOrgTree();
    });
  });
  document.getElementById("btnExpand").addEventListener("click", function () { draw(); });

  function updateHint() {
    var f = currentFilter(), n = EMP.filter(function (e) { return passes(e, f); }).length;
    document.getElementById("dimHint").textContent = filterActive(f) ? (n + " employees match filters") : "";
  }

  /* (legend removed — boxes are self-explanatory; corner space reclaimed) */

  /* ======================================================================
     ORG STRUCTURE TREE (Division > Department > Section > employees)
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

  /* ======================================================================
     VIEW VISIBILITY — hide any "View by" options the user turned off on the
     Settings ▸ Manage views page (stored per browser in localStorage).
     ====================================================================== */
  var VIEWS_KEY = "orgv_hidden_views";
  function getHiddenViews() { try { return JSON.parse(localStorage.getItem(VIEWS_KEY) || "[]"); } catch (e) { return []; } }
  function applyHiddenViews() {
    var sel = document.getElementById("dim"); if (!sel) return;
    var hidden = getHiddenViews(), opts = Array.prototype.slice.call(sel.options);
    var firstVisible = null, visible = 0;
    opts.forEach(function (o) {
      var h = hidden.indexOf(o.value) >= 0;
      o.hidden = h; o.disabled = h;
      if (!h) { visible++; if (!firstVisible) firstVisible = o.value; }
    });
    if (!visible && opts.length) { opts[0].hidden = false; opts[0].disabled = false; firstVisible = opts[0].value; }
    var cur = sel.selectedOptions[0];
    if (cur && cur.hidden && firstVisible) { sel.value = firstVisible; navPath = []; draw(); buildOrgTree(); }
  }
  applyHiddenViews();

  document.getElementById("orient").value = orient;
  applyOrientClass();
  draw();
  buildOrgTree();

  function handleHash() {
    var om = /orient=(vertical|horizontal)/.exec(location.hash);
    if (om) { document.getElementById("orient").value = om[1]; orient = om[1]; applyOrientClass(); draw(); }
    var dm = /dim=([a-z]+)/.exec(location.hash);
    if (dm && document.querySelector("#dim option[value='" + dm[1] + "']")) { document.getElementById("dim").value = dm[1]; navPath = []; draw(); buildOrgTree(); }
    var m = /emp=([^&]+)/.exec(location.hash);
    if (m) {
      var code = decodeURIComponent(m[1]);
      if (BY[code]) {
        if (!dm) { document.getElementById("dim").value = "manager"; draw(); }
        switchTab("chart");
        setTimeout(function () { if (isNavMode()) navSearch(code); else focusCode(code, true); }, 60);
      }
    }
  }
  window.addEventListener("hashchange", handleHash);
  handleHash();
})();
