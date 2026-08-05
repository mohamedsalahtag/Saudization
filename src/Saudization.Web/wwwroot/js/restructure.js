/* Org restructuring workbench — block-board GUI.
   Colored Division swimlanes ▸ Department cards ▸ Job cards ▸ Employee chips, with drag-drop, inline
   rename (department asks this-block/all), and an employee modal (details + manager + transfer).
   Everything is in-browser until "Save as version" persists a new version. */
(function () {
  "use strict";
  var model = JSON.parse(document.getElementById("org-model").textContent);
  var cfg = JSON.parse(document.getElementById("org-cfg").textContent);
  var T = cfg.t;
  var dirty = false, expanded = {}, tempSeq = -1, paletteFilter = 0;   // 0 = all divisions

  // baseline snapshot of the loaded version, to flag unsaved changes until Save
  var base = JSON.parse(document.getElementById("org-model").textContent);
  var oEmp = {}, oDept = {}, oJob = {}, oDiv = {};
  base.Employees.forEach(function (e) { oEmp[e.Id] = { d: e.DepartmentId, j: e.JobId || 0 }; });
  base.Departments.forEach(function (d) { oDept[d.Id] = { v: d.DivisionId, n: d.Name }; });
  base.Jobs.forEach(function (j) { oJob[j.Id] = { d: j.DepartmentId, n: j.Name }; });
  base.Divisions.forEach(function (d) { oDiv[d.Id] = { n: d.Name }; });
  function empChanged(e) { var o = oEmp[e.Id]; return !o || o.d !== e.DepartmentId || o.j !== (e.JobId || 0); }
  function deptChanged(d) { var o = oDept[d.Id]; return !o || o.v !== d.DivisionId || o.n !== d.Name; }
  function jobChanged(j) { var o = oJob[j.Id]; return !o || o.d !== j.DepartmentId || o.n !== j.Name; }
  function divChanged(d) { var o = oDiv[d.Id]; return !o || o.n !== d.Name; }
  function deptSubtreeChanged(id) { return deptChanged(deptById[id]) || empsOfDept(id).some(empChanged) || jobsOfDept(id).some(jobChanged); }
  function divSubtreeChanged(id) { return divChanged(divById[id]) || deptsOfDiv(id).some(function (dp) { return deptSubtreeChanged(dp.Id); }); }

  var divById = {}, deptById = {}, jobById = {}, byCode = {};
  function reindex() {
    divById = {}; deptById = {}; jobById = {}; byCode = {};
    model.Divisions.forEach(function (d) { divById[d.Id] = d; });
    model.Departments.forEach(function (d) { deptById[d.Id] = d; });
    model.Jobs.forEach(function (j) { jobById[j.Id] = j; });
    model.Employees.forEach(function (e) { if (e.Code) byCode[String(e.Code).trim()] = e; });
  }
  function deptsOfDiv(id) { return model.Departments.filter(function (d) { return d.DivisionId === id; }); }
  function jobsOfDept(id) { return model.Jobs.filter(function (j) { return j.DepartmentId === id; }); }
  function empsOfDept(id) { return model.Employees.filter(function (e) { return e.DepartmentId === id; }); }
  function empsOfJob(id) { return model.Employees.filter(function (e) { return e.JobId === id; }); }
  function empsOfDeptNoJob(id) { return model.Employees.filter(function (e) { return e.DepartmentId === id && !e.JobId; }); }
  function divOfDept(id) { var d = deptById[id]; return d ? divById[d.DivisionId] : null; }
  function emp(id) { return model.Employees.filter(function (e) { return e.Id === id; })[0]; }

  function consistency(e) {
    var mc = (e.ManagerCode || "").trim();
    if (!mc || mc.toUpperCase() === "NO_MANAGER") return { s: "none" };
    var mgr = byCode[mc]; if (!mgr) return { s: "dangling" };
    var eDiv = (deptById[e.DepartmentId] || {}).DivisionId, mDiv = (deptById[mgr.DepartmentId] || {}).DivisionId;
    var sameDiv = eDiv === mDiv, sameDept = e.DepartmentId === mgr.DepartmentId;
    var ok = cfg.divisionOnly ? sameDiv : (sameDiv && sameDept);
    return { s: ok ? "ok" : "bad", mgr: mgr, reason: (!sameDiv && !sameDept) ? "cross-both" : (!sameDiv ? "cross-division" : "cross-department") };
  }

  // ---------- colours ----------
  var PALETTE = ["#3b6fb0", "#8a6d3b", "#b0603b", "#5a7d9a", "#6b7a3b", "#8e4b6e", "#3b8a7d", "#a06a2c", "#4b5a8e", "#7a3b3b", "#3b8a4b", "#7a5a8e"];
  function colorFor(name) { var h = 0, s = String(name || ""); for (var i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) >>> 0; return PALETTE[h % PALETTE.length]; }

  // ---------- mutations ----------
  function markDirty() { dirty = true; var b = document.getElementById("saveBtn"); if (b) { b.disabled = false; b.textContent = "● " + b.dataset.label; } document.getElementById("saveLabel").style.display = ""; }
  function moveDeptToDiv(deptId, divId) { var d = deptById[deptId]; if (d && d.DivisionId !== divId) { d.DivisionId = divId; markDirty(); } }
  function moveJobToDept(jobId, deptId) { var j = jobById[jobId]; if (!j || j.DepartmentId === deptId) return; j.DepartmentId = deptId; model.Employees.forEach(function (e) { if (e.JobId === jobId) e.DepartmentId = deptId; }); markDirty(); }
  function moveEmpToDept(empId, deptId) { var e = emp(empId); if (e) { e.DepartmentId = deptId; e.JobId = null; markDirty(); } }
  function moveEmpToJob(empId, jobId) { var e = emp(empId), j = jobById[jobId]; if (e && j) { e.JobId = jobId; e.DepartmentId = j.DepartmentId; markDirty(); } }
  function deleteDept(deptId) {
    if (empsOfDept(deptId).length) return;               // only empty departments
    model.Departments = model.Departments.filter(function (d) { return d.Id !== deptId; });
    model.Jobs = model.Jobs.filter(function (j) { return j.DepartmentId !== deptId; });
    markDirty(); renderAll();
  }

  // ---------- render ----------
  function renderAll() { reindex(); renderKpis(); renderBoard(); renderPalette(); renderTable(); fillDivSelect(); }

  function renderKpis() {
    var inc = 0, nomgr = 0;
    model.Employees.forEach(function (e) { var c = consistency(e); if (c.s === "bad" || c.s === "dangling") inc++; else if (c.s === "none") nomgr++; });
    var tiles = [[model.Employees.length, T.employees], [model.Divisions.length, T.division], [model.Departments.length, T.department], [model.Jobs.length, T.job], [inc, T.inconsistent, inc > 0 ? "bad" : "ok"], [nomgr, T.noManager]];
    document.getElementById("orgKpis").innerHTML = tiles.map(function (t) { return "<div class='kpi " + (t[2] || "") + "'><div class='k-val'>" + t[0] + "</div><div class='k-lbl'>" + esc(t[1]) + "</div></div>"; }).join("");
  }

  function nameEditor(type, id, name) {
    return "<span class='nm'>" + esc(name) + "</span> <span class='ic-edit' data-rename='" + type + ":" + id + "'>✎</span>";
  }

  function renderBoard() {
    var host = document.getElementById("orgBoard");
    host.innerHTML = model.Divisions.slice().sort(byName).map(divLane).join("");
    wireBoard(host);
  }
  function divLane(d) {
    var key = "div:" + d.Id, open = expanded[key], col = colorFor(d.Name);
    var cnt = deptsOfDiv(d.Id).reduce(function (s, dp) { return s + empsOfDept(dp.Id).length; }, 0);
    var chg = divSubtreeChanged(d.Id) ? " changed" : "";
    var block = "<div class='divblock" + chg + "' style='background:" + col + "' data-toggle='" + key + "' data-drop='div:" + d.Id + "'>" +
      "<span class='dl-tw'>" + (open ? "▾" : "▸") + "</span>" +
      "<span class='dl-name'>" + nameEditor("div", d.Id, d.Name) + (chg ? " <span class='chg-dot' title='" + esc(T.changed) + "'></span>" : "") + "</span>" +
      "<span class='dl-cnt'>" + cnt + " " + esc(T.employees) + " · " + deptsOfDiv(d.Id).length + " " + esc(T.department) + "</span></div>";
    var body = "";
    if (open) {
      var deps = deptsOfDiv(d.Id).sort(byName);
      body = deps.length ? deps.map(deptCard).join("") : "<span>—</span>";
      body = "<div class='depts" + (deps.length ? "" : " empty") + "'>" + body + "</div>";
    }
    return "<div class='divlane" + (open ? " open" : "") + "' data-drop='div:" + d.Id + "'>" + block + body + "</div>";
  }
  function deptCard(dp) {
    var key = "dept:" + dp.Id, open = expanded[key], n = empsOfDept(dp.Id).length, chg = deptSubtreeChanged(dp.Id);
    var del = n === 0 ? " <span class='ic-del' data-del='" + dp.Id + "' title='" + esc(T.deleteDept) + "'>🗑</span>" : "";
    var h = "<div class='deptcard-h" + (chg ? " changed" : "") + "' data-toggle='" + key + "'><span>" + (open ? "▾" : "▸") + "</span><span class='dc-name'>" +
      nameEditor("dept", dp.Id, dp.Name) + (chg ? " <span class='chg-dot' title='" + esc(T.changed) + "'></span>" : "") + "</span>" + del + "<span class='cnt'>" + n + "</span></div>";
    var body = "";
    if (open) {
      var inner = jobsOfDept(dp.Id).sort(byName).map(jobCard).join("");
      var loose = empsOfDeptNoJob(dp.Id);
      if (loose.length || jobsOfDept(dp.Id).length === 0) {
        inner += "<div class='jobcard'><div class='jobcard-h muted'>" + esc(T.unassignedJob) + " <span class='cnt'>" + loose.length + "</span></div>" +
          "<div class='chips'>" + loose.slice().sort(byName).map(empChip).join("") + "</div></div>";
      }
      body = "<div class='deptbody'>" + inner + "</div>";
    }
    return "<div class='deptcard' draggable='true' data-drag='dept:" + dp.Id + "' data-drop='dept:" + dp.Id + "'>" + h + body + "</div>";
  }
  function jobCard(j) {
    var key = "job:" + j.Id, open = expanded[key], chg = jobChanged(j) || empsOfJob(j.Id).some(empChanged);
    return "<div class='jobcard" + (chg ? " changed" : "") + "' draggable='true' data-drag='job:" + j.Id + "' data-drop='job:" + j.Id + "' title='" + esc(T.dragJobHint) + "'>" +
      "<div class='jobcard-h' data-toggle='" + key + "'><span>" + (open ? "▾" : "▸") + "</span>" + nameEditor("job", j.Id, j.Name) +
      (chg ? " <span class='chg-dot' title='" + esc(T.changed) + "'></span>" : "") + " <span class='cnt'>" + empsOfJob(j.Id).length + "</span></div>" +
      (open ? "<div class='chips'>" + empsOfJob(j.Id).slice().sort(byName).map(empChip).join("") + "</div>" : "") + "</div>";
  }
  function empChip(e) {
    var c = consistency(e), cls = (c.s === "bad" || c.s === "dangling") ? " bad" : "";
    var dot = c.s === "ok" ? "ok" : (c.s === "bad" || c.s === "dangling") ? "bad" : "none";
    var chg = empChanged(e) ? " changed" : "";
    return "<span class='empchip" + cls + chg + "' draggable='true' data-drag='emp:" + e.Id + "' data-emp='" + e.Id + "'><span class='edot " + dot + "'></span>" + esc(e.Name) + (chg ? " <span class='chg-dot' title='" + esc(T.changed) + "'></span>" : "") + "</span>";
  }

  function wireBoard(host) {
    host.querySelectorAll("[data-toggle]").forEach(function (n) {
      n.addEventListener("click", function (ev) { if (ev.target.closest(".ic-edit") || ev.target.closest(".renaming")) return; toggle(n.dataset.toggle); });
    });
    host.querySelectorAll("[data-drag]").forEach(dragSource);
    host.querySelectorAll("[data-drop]").forEach(dropTarget);
    host.querySelectorAll(".empchip").forEach(function (ch) { ch.addEventListener("click", function () { openEmp(+ch.dataset.emp); }); });
    host.querySelectorAll(".ic-edit").forEach(function (ic) { ic.addEventListener("click", function (ev) { ev.stopPropagation(); startRename(ic); }); });
    host.querySelectorAll(".ic-del").forEach(function (ic) { ic.addEventListener("click", function (ev) { ev.stopPropagation(); if (confirm(T.deleteDept + "?")) deleteDept(+ic.dataset.del); }); });
  }
  function toggle(key) { expanded[key] = !expanded[key]; renderBoard(); }

  // ---------- inline rename ----------
  function startRename(ic) {
    var spec = ic.dataset.rename.split(":"), type = spec[0], id = +spec[1];
    var node = type === "div" ? divById[id] : type === "dept" ? deptById[id] : jobById[id];
    if (!node) return;
    var span = ic.parentElement; // .nm sibling
    var nm = span.querySelector(".nm");
    var old = node.Name;
    span.innerHTML = "<span class='renaming'><input value='" + esc(old) + "' /></span>";
    var inp = span.querySelector("input"); inp.focus(); inp.select();
    var done = false;
    function commit(save) {
      if (done) return; done = true;
      var val = inp.value.trim();
      if (!save || !val || val === old) { renderAll(); return; }
      if (type === "dept") chooseRenameScope(old, function (all) { applyRename(type, id, old, val, all); });
      else { node.Name = val; markDirty(); renderAll(); }
    }
    inp.addEventListener("keydown", function (e) { if (e.key === "Enter") { e.preventDefault(); commit(true); } else if (e.key === "Escape") commit(false); });
    inp.addEventListener("blur", function () { commit(true); });
  }
  function applyRename(type, id, oldName, val, all) {
    if (all) model.Departments.forEach(function (d) { if (d.Name === oldName) d.Name = val; });
    else deptById[id].Name = val;
    markDirty(); renderAll();
  }
  function chooseRenameScope(oldName, cb) {
    var host = document.getElementById("empModalHost");
    host.innerHTML = "<div class='modal-backdrop'><div class='modal' style='width:420px'>" +
      "<div class='modal-h'><h3>" + esc(T.renamePrompt) + "</h3></div>" +
      "<div class='modal-b'><p class='subtle'>“" + esc(oldName) + "”</p></div>" +
      "<div class='modal-f'><button class='btn btn-soft' id='rnThis'>" + esc(T.renameThis) + "</button><button class='btn' id='rnAll'>" + esc(T.renameAll) + "</button></div></div></div>";
    document.getElementById("rnThis").addEventListener("click", function () { host.innerHTML = ""; cb(false); });
    document.getElementById("rnAll").addEventListener("click", function () { host.innerHTML = ""; cb(true); });
  }

  // ---------- drag & drop ----------
  var drag = null;
  function dragSource(n) {
    n.addEventListener("dragstart", function (e) { e.stopPropagation(); drag = n.dataset.drag; n.classList.add("dragging"); e.dataTransfer.effectAllowed = "move"; });
    n.addEventListener("dragend", function () { drag = null; n.classList.remove("dragging"); clearDrop(); });
  }
  function dropTarget(n) {
    n.addEventListener("dragover", function (e) { if (accepts(n.dataset.drop, drag)) { e.preventDefault(); e.stopPropagation(); n.classList.add("drop-ok"); } });
    n.addEventListener("dragleave", function () { n.classList.remove("drop-ok"); });
    n.addEventListener("drop", function (e) {
      e.preventDefault(); e.stopPropagation(); n.classList.remove("drop-ok");
      if (!accepts(n.dataset.drop, drag)) return;
      var s = parse(drag), t = parse(n.dataset.drop);
      if (s.type === "dept" && t.type === "div") moveDeptToDiv(s.id, t.id);
      else if (s.type === "job" && t.type === "dept") moveJobToDept(s.id, t.id);
      else if (s.type === "emp" && t.type === "dept") moveEmpToDept(s.id, t.id);
      else if (s.type === "emp" && t.type === "job") moveEmpToJob(s.id, t.id);
      renderAll();
    });
  }
  function accepts(dropSpec, dragSpec) {
    if (!dragSpec || !dropSpec) return false; var s = parse(dragSpec), t = parse(dropSpec);
    return (s.type === "dept" && t.type === "div") || (s.type === "job" && t.type === "dept") || (s.type === "emp" && (t.type === "dept" || t.type === "job"));
  }
  function parse(spec) { var p = spec.split(":"); return { type: p[0], id: +p[1] }; }
  function clearDrop() { document.querySelectorAll(".drop-ok").forEach(function (n) { n.classList.remove("drop-ok"); }); }

  // ---------- employee modal ----------
  function openEmp(id) {
    var e = emp(id); if (!e) return;
    var c = consistency(e), dep = deptById[e.DepartmentId] || {}, div = divOfDept(e.DepartmentId) || {}, job = e.JobId ? (jobById[e.JobId] || {}) : {};
    var mgr = c.mgr;
    var mgrDep = mgr ? (deptById[mgr.DepartmentId] || {}) : {}, mgrDiv = mgr ? (divOfDept(mgr.DepartmentId) || {}) : {};
    var badge = (c.s === "bad" || c.s === "dangling") ? "<span class='ic-bad'>⚠ " + esc(c.reason || "dangling") + "</span>" : c.s === "ok" ? "<span class='ic-ok'>✓ " + esc(T.consistent) + "</span>" : "<span class='muted'>" + esc(T.noManager) + "</span>";
    function kv(k, v) { return "<div class='kv'><span class='k'>" + esc(k) + "</span><span>" + esc(v || "—") + "</span></div>"; }

    var mgrBox = mgr
      ? "<div class='mgrbox " + ((c.s === "bad") ? "bad" : "") + "'><strong>" + esc(T.managerTitle) + "</strong> " + badge +
        kv(T.emp, mgr.Name) + kv(T.code, mgr.Code) + kv(T.division, mgrDiv.Name) + kv(T.department, mgrDep.Name) +
        "<button class='btn btn-sm btn-soft' id='goMgr' data-id='" + mgr.Id + "'>" + esc(T.goToManager + " →") + "</button></div>"
      : "<div class='mgrbox'>" + badge + (e.ManagerCode ? kv(T.code, e.ManagerCode) : "") + "</div>";

    var divOpts = model.Divisions.slice().sort(byName).map(function (d) { return "<option value='" + d.Id + "'" + (d.Id === dep.DivisionId ? " selected" : "") + ">" + esc(d.Name) + "</option>"; }).join("");

    var host = document.getElementById("empModalHost");
    host.innerHTML = "<div class='modal-backdrop' id='empBack'><div class='modal'>" +
      "<div class='modal-h'><h3>" + esc(e.Name) + " " + badge + "</h3><button class='modal-x' id='empX'>×</button></div>" +
      "<div class='modal-b'>" +
        "<div style='font-weight:700;margin-bottom:6px'>" + esc(T.empDetails) + "</div>" +
        kv(T.code, e.Code) + kv(T.site, e.Site) + kv(T.branch, e.BranchCode) +
        kv(T.division, div.Name) + kv(T.department, dep.Name) + kv(T.job, job.Name) +
        mgrBox +
        "<div style='margin-top:12px'><strong>" + esc(T.transfer) + "</strong></div>" +
        "<div class='transfer'>" +
          "<div class='field'><label class='muted small'>" + esc(T.division) + "</label><select id='tDiv' class='btn-soft'>" + divOpts + "</select></div>" +
          "<div class='field'><label class='muted small'>" + esc(T.department) + "</label><select id='tDept' class='btn-soft'></select></div>" +
          "<div class='field'><label class='muted small'>" + esc(T.job) + "</label><select id='tJob' class='btn-soft'></select></div>" +
        "</div>" +
      "</div>" +
      "<div class='modal-f'><button class='btn btn-soft' id='empClose'>" + esc(T.close) + "</button><button class='btn' id='empApply'>" + esc(T.apply) + "</button></div>" +
      "</div></div>";

    function fillDept() {
      var dv = +document.getElementById("tDiv").value;
      var opts = deptsOfDiv(dv).sort(byName).map(function (d) { return "<option value='" + d.Id + "'" + (d.Id === e.DepartmentId ? " selected" : "") + ">" + esc(d.Name) + "</option>"; }).join("");
      document.getElementById("tDept").innerHTML = opts || "<option value=''>—</option>"; fillJob();
    }
    function fillJob() {
      var dp = +document.getElementById("tDept").value;
      var opts = "<option value=''>" + esc(T.noneLabel) + "</option>" + jobsOfDept(dp).sort(byName).map(function (j) { return "<option value='" + j.Id + "'" + (j.Id === e.JobId ? " selected" : "") + ">" + esc(j.Name) + "</option>"; }).join("");
      document.getElementById("tJob").innerHTML = opts;
    }
    document.getElementById("tDiv").addEventListener("change", fillDept);
    document.getElementById("tDept").addEventListener("change", fillJob);
    fillDept();

    function closeM() { host.innerHTML = ""; }
    document.getElementById("empX").addEventListener("click", closeM);
    document.getElementById("empClose").addEventListener("click", closeM);
    document.getElementById("empBack").addEventListener("click", function (ev) { if (ev.target.id === "empBack") closeM(); });
    var gm = document.getElementById("goMgr"); if (gm) gm.addEventListener("click", function () { openEmp(+gm.dataset.id); });
    document.getElementById("empApply").addEventListener("click", function () {
      var jv = document.getElementById("tJob").value, dv = +document.getElementById("tDept").value;
      if (jv) moveEmpToJob(id, +jv); else moveEmpToDept(id, dv);
      renderAll(); openEmp(id);   // refresh modal with updated data
    });
  }

  // ---------- palette (grouped by division, filterable, editable) ----------
  function palItem(d, counts) {
    var n = counts[d.Id] || 0, chg = deptSubtreeChanged(d.Id);
    var del = n === 0 ? " <span class='ic-del' data-del='" + d.Id + "' title='" + esc(T.deleteDept) + "'>🗑</span>" : "";
    return "<div class='pitem" + (chg ? " changed" : "") + "' draggable='true' data-drag='dept:" + d.Id + "'><span>" + nameEditor("dept", d.Id, d.Name) +
      (chg ? " <span class='chg-dot' title='" + esc(T.changed) + "'></span>" : "") + "</span><span>" + del + " <span class='cnt'>" + n + "</span></span></div>";
  }
  function renderPalette() {
    var counts = {}; model.Departments.forEach(function (d) { counts[d.Id] = 0; });
    model.Employees.forEach(function (e) { counts[e.DepartmentId] = (counts[e.DepartmentId] || 0) + 1; });
    var divs = model.Divisions.slice().sort(byName).filter(function (v) { return !paletteFilter || v.Id === paletteFilter; });
    document.getElementById("orgPalette").innerHTML = divs.map(function (v) {
      var deps = deptsOfDiv(v.Id).sort(byName);
      var head = empsOfDivCount(v.Id);
      return "<div class='pgroup' data-drop='div:" + v.Id + "'><div class='pgroup-h'>" + esc(v.Name) +
        " <span class='cnt'>" + deps.length + " · " + head + "</span></div>" +
        (deps.length ? deps.map(function (d) { return palItem(d, counts); }).join("") : "<div class='subtle small' style='padding:4px 6px'>—</div>") + "</div>";
    }).join("");
    var pal = document.getElementById("orgPalette");
    pal.querySelectorAll(".pitem").forEach(dragSource);
    pal.querySelectorAll(".pgroup").forEach(dropTarget);   // drop a department onto a division group
    pal.querySelectorAll(".ic-edit").forEach(function (ic) { ic.addEventListener("click", function (ev) { ev.stopPropagation(); startRename(ic); }); });
    pal.querySelectorAll(".ic-del").forEach(function (ic) { ic.addEventListener("click", function (ev) { ev.stopPropagation(); if (confirm(T.deleteDept + "?")) deleteDept(+ic.dataset.del); }); });
  }
  function empsOfDivCount(divId) { return deptsOfDiv(divId).reduce(function (s, d) { return s + empsOfDept(d.Id).length; }, 0); }

  // ---------- result table ----------
  function renderTable() {
    var rows = model.Employees.slice().sort(byName).map(function (e) {
      var dep = deptById[e.DepartmentId] || {}, div = divOfDept(e.DepartmentId) || {}, job = e.JobId ? (jobById[e.JobId] || {}) : {}, c = consistency(e);
      var st = (c.s === "bad" || c.s === "dangling") ? "<span class='ic-bad'>⚠ " + esc(c.reason || "dangling") + "</span>" : c.s === "ok" ? "<span class='ic-ok'>✓</span>" : "<span class='muted'>—</span>";
      return "<tr data-emp='" + e.Id + "' class='" + (empChanged(e) ? "rowchg" : "") + "' style='cursor:pointer'><td>" + esc(e.Name) + (empChanged(e) ? " <span class='chg-dot'></span>" : "") + "</td><td>" + esc(div.Name || "") + "</td><td>" + esc(dep.Name || "") + "</td><td>" + esc(job.Name || "") + "</td><td>" + esc(c.mgr ? c.mgr.Name : (e.ManagerCode || "")) + "</td><td>" + st + "</td></tr>";
    }).join("");
    document.getElementById("orgTable").innerHTML = "<table class='grid'><thead><tr><th>" + esc(T.emp) + "</th><th>" + esc(T.division) + "</th><th>" + esc(T.department) + "</th><th>" + esc(T.job) + "</th><th>" + esc(T.mgr) + "</th><th>" + esc(T.status) + "</th></tr></thead><tbody>" + rows + "</tbody></table>";
    var q = (document.getElementById("orgSearch").value || "").toLowerCase();
    if (q) document.getElementById("orgSearch").dispatchEvent(new Event("input"));
    document.querySelectorAll("#orgTable tbody tr").forEach(function (tr) { tr.addEventListener("click", function () { openEmp(+tr.dataset.emp); }); });
  }
  document.getElementById("orgSearch").addEventListener("input", function () {
    var q = this.value.toLowerCase();
    document.querySelectorAll("#orgTable tbody tr").forEach(function (tr) { tr.style.display = tr.textContent.toLowerCase().indexOf(q) >= 0 ? "" : "none"; });
  });

  // ---------- add department ----------
  function fillDivSelect() {
    var opts = model.Divisions.slice().sort(byName).map(function (d) { return "<option value='" + d.Id + "'>" + esc(d.Name) + "</option>"; }).join("");
    document.getElementById("newDeptDiv").innerHTML = opts;
    var f = document.getElementById("paletteDivFilter");
    f.innerHTML = "<option value='0'>" + esc(T.allDivisions) + "</option>" + opts;
    f.value = String(paletteFilter);
  }
  document.getElementById("paletteDivFilter").addEventListener("change", function () { paletteFilter = +this.value; renderPalette(); });
  document.getElementById("addDeptBtn").addEventListener("click", function () {
    var name = document.getElementById("newDeptName").value.trim(); if (!name) return;
    model.Departments.push({ Id: tempSeq--, Name: name, DivisionId: +document.getElementById("newDeptDiv").value });
    document.getElementById("newDeptName").value = ""; markDirty(); renderAll();
  });

  // ---------- expand/collapse all ----------
  document.getElementById("expandAll").addEventListener("click", function (e) { e.preventDefault(); model.Divisions.forEach(function (d) { expanded["div:" + d.Id] = true; }); model.Departments.forEach(function (d) { expanded["dept:" + d.Id] = true; }); renderBoard(); });
  document.getElementById("collapseAll").addEventListener("click", function (e) { e.preventDefault(); expanded = {}; renderBoard(); });

  // ---------- save ----------
  var saveBtn = document.getElementById("saveBtn"); saveBtn.dataset.label = saveBtn.textContent;
  saveBtn.addEventListener("click", function () {
    var fd = new FormData();
    fd.append("__RequestVerificationToken", document.querySelector('input[name="__RequestVerificationToken"]').value);
    fd.append("payload", JSON.stringify(model));
    fd.append("label", document.getElementById("saveLabel").value || "");
    saveBtn.disabled = true; saveBtn.textContent = "…";
    fetch(saveBtn.dataset.saveUrl, { method: "POST", body: fd })
      .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
      .then(function (j) { dirty = false; window.location = saveBtn.dataset.indexUrl + "?versionId=" + j.versionId; })
      .catch(function (e) { alert("Save failed: " + e); saveBtn.disabled = false; saveBtn.textContent = saveBtn.dataset.label; });
  });
  window.addEventListener("beforeunload", function (e) { if (dirty) { e.preventDefault(); e.returnValue = ""; } });

  function byName(a, b) { return String(a.Name || "").localeCompare(String(b.Name || "")); }
  function esc(s) { return String(s == null ? "" : s).replace(/[&<>"]/g, function (c) { return ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]; }); }

  model.Divisions.forEach(function (d) { expanded["div:" + d.Id] = true; });   // divisions expanded by default
  reindex(); renderAll();
})();
