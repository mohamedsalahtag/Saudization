/* Structure editor — a fast table to reassign each employee's Division / Department / Direct Manager,
   then Save as a new version (reuses Restructure/Save + OrgModelDto). Division & Department cells edit
   lazily (click → dropdown) so 2000+ rows stay responsive. */
(function () {
  "use strict";
  var model = JSON.parse(document.getElementById("asg-model").textContent);
  if (!model) return;

  var divById = {}, deptById = {}, byCode = {}, empById = {}, dirty = false;
  function reindex() {
    divById = {}; deptById = {}; byCode = {}; empById = {};
    model.Divisions.forEach(function (d) { divById[d.Id] = d; });
    model.Departments.forEach(function (d) { deptById[d.Id] = d; });
    model.Employees.forEach(function (e) { empById[e.Id] = e; if (e.Code) byCode[String(e.Code).trim()] = e; });
  }
  reindex();
  function byName(a, b) { return String(a.Name || "").localeCompare(String(b.Name || "")); }
  function esc(s) { return String(s == null ? "" : s).replace(/[&<>"]/g, function (c) { return ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]; }); }
  function deptsOfDiv(id) { return model.Departments.filter(function (d) { return d.DivisionId === id; }).sort(byName); }
  function divOfDept(id) { var d = deptById[id]; return d ? divById[d.DivisionId] : null; }
  function markDirty() { dirty = true; }

  // shared manager picker list (one datalist referenced by every row → cheap)
  document.getElementById("asgMgrList").innerHTML =
    model.Employees.slice().sort(byName).map(function (e) { return "<option value='" + esc((e.Code || "") + " — " + e.Name) + "'></option>"; }).join("");

  var tbody = document.querySelector("#asgTable tbody");

  function rowHtml(e) {
    var dep = deptById[e.DepartmentId] || {}, div = divOfDept(e.DepartmentId) || {};
    var mgr = byCode[(e.ManagerCode || "").trim()];
    var mgrVal = e.ManagerCode ? (e.ManagerCode + (mgr ? " — " + mgr.Name : "")) : "";
    return "<td>" + esc(e.Name) + "</td>"
      + "<td>" + esc(e.Code || "") + "</td>"
      + "<td class='asg-cell asg-div' title='" + esc(T.clickEdit) + "'>" + esc(div.Name || "—") + "</td>"
      + "<td class='asg-cell asg-dept' title='" + esc(T.clickEdit) + "'>" + esc(dep.Name || "—") + "</td>"
      + "<td><input class='asg-mgr' list='asgMgrList' value='" + esc(mgrVal) + "' placeholder='" + esc(T.mgrPlaceholder) + "' /></td>";
  }
  function paintRow(tr) { var e = empById[+tr.dataset.id]; tr.innerHTML = rowHtml(e); wireRow(tr); }

  function render() {
    tbody.innerHTML = model.Employees.slice().sort(byName).map(function (e) {
      return "<tr data-id='" + e.Id + "'>" + rowHtml(e) + "</tr>";
    }).join("");
    tbody.querySelectorAll("tr").forEach(wireRow);
  }

  function wireRow(tr) {
    var e = empById[+tr.dataset.id];
    var mgr = tr.querySelector(".asg-mgr");
    if (mgr) {
      // The field is pre-filled with the current manager, which makes the datalist show only that one match.
      // Clear it on focus so the full employee list appears; restore it on blur if nothing was picked.
      mgr.addEventListener("focus", function () { mgr.dataset.prev = mgr.value; mgr.value = ""; });
      mgr.addEventListener("blur", function () { if ((mgr.value || "").trim() === "") mgr.value = mgr.dataset.prev || ""; });
      mgr.addEventListener("change", function () {
        var v = (mgr.value || "").trim();
        e.ManagerCode = v ? v.split("—")[0].trim() : "";   // accept "code — name" or a bare code
        markDirty();
      });
    }
    var divCell = tr.querySelector(".asg-div");
    if (divCell) divCell.addEventListener("click", function () { editDiv(tr, e, divCell); });
    var deptCell = tr.querySelector(".asg-dept");
    if (deptCell) deptCell.addEventListener("click", function () { editDept(tr, e, deptCell); });
  }

  function editDiv(tr, e, cell) {
    if (cell.querySelector("select")) return;
    var cur = (divOfDept(e.DepartmentId) || {}).Id;
    var sel = document.createElement("select"); sel.className = "asg-inline";
    sel.innerHTML = model.Divisions.slice().sort(byName).map(function (d) { return "<option value='" + d.Id + "'" + (d.Id === cur ? " selected" : "") + ">" + esc(d.Name) + "</option>"; }).join("");
    cell.innerHTML = ""; cell.appendChild(sel); sel.focus();
    sel.addEventListener("change", function () {
      var first = deptsOfDiv(+sel.value)[0];        // move into the first department of the chosen division
      if (first) { e.DepartmentId = first.Id; e.JobId = null; markDirty(); }
      paintRow(tr);
    });
    sel.addEventListener("blur", function () { paintRow(tr); });
  }
  function editDept(tr, e, cell) {
    if (cell.querySelector("select")) return;
    var div = divOfDept(e.DepartmentId) || {};
    var deps = deptsOfDiv(div.Id);
    if (!deps.length) return;
    var sel = document.createElement("select"); sel.className = "asg-inline";
    sel.innerHTML = deps.map(function (d) { return "<option value='" + d.Id + "'" + (d.Id === e.DepartmentId ? " selected" : "") + ">" + esc(d.Name) + "</option>"; }).join("");
    cell.innerHTML = ""; cell.appendChild(sel); sel.focus();
    sel.addEventListener("change", function () { e.DepartmentId = +sel.value; e.JobId = null; markDirty(); paintRow(tr); });
    sel.addEventListener("blur", function () { paintRow(tr); });
  }

  var search = document.getElementById("asgSearch");
  if (search) search.addEventListener("input", function () {
    var q = this.value.toLowerCase();
    tbody.querySelectorAll("tr").forEach(function (tr) { tr.style.display = tr.textContent.toLowerCase().indexOf(q) >= 0 ? "" : "none"; });
  });

  function msg(t) { var m = document.getElementById("asgMsg"); if (m) m.textContent = t; }
  function token() { return document.querySelector('input[name="__RequestVerificationToken"]').value; }
  function post(url, extra, btn) {
    if (btn.disabled) return;
    var fd = new FormData();
    fd.append("__RequestVerificationToken", token());
    fd.append("payload", JSON.stringify(model));
    if (extra) Object.keys(extra).forEach(function (k) { fd.append(k, extra[k]); });
    btn.disabled = true; var old = btn.textContent; btn.textContent = "…";
    fetch(url, { method: "POST", body: fd })
      .then(function (r) { return r.ok ? r.json() : r.text().then(function (t) { throw new Error(t); }); })
      .then(function (j) { dirty = false; window.location = window.ASG_BACK_URL + "?versionId=" + j.versionId; })
      .catch(function (e) { msg("✗ " + e.message); btn.disabled = false; btn.textContent = old; });
  }

  // Save as a NEW version
  var saveBtn = document.getElementById("asgSave");
  if (saveBtn) saveBtn.addEventListener("click", function () {
    post(window.ASG_SAVE_URL, { label: (document.getElementById("asgLabel").value || "").trim() }, saveBtn);
  });
  // Update the CURRENT version in place
  var updateBtn = document.getElementById("asgUpdate");
  if (updateBtn) updateBtn.addEventListener("click", function () {
    if (!confirm(window.ASG_UPDATE_CONFIRM || "Update the current version in place?")) return;
    post(window.ASG_UPDATE_URL, null, updateBtn);
  });
  window.addEventListener("beforeunload", function (e) { if (dirty) { e.preventDefault(); e.returnValue = ""; } });

  var T = {
    clickEdit: "Click to change",
    mgrPlaceholder: "manager code or name",
    saved: "Saved"
  };
  render();
})();
