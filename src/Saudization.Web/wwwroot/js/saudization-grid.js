/* Editable "what-if" Saudization grid (Grid 2). Builds the table ONCE (inputs stay put while you type)
   and only refreshes the derived cells on edit — so typing is stable. Actions are click-to-apply,
   transfers are quantity-matched within a category, and the whole thing can be saved / exported. */
(function () {
  "use strict";
  var el = document.getElementById("sg-model");
  if (!el) return;
  var model = JSON.parse(el.textContent);
  var RECS = model.records, CFG = model.cfg, L = CFG.L;
  var origRows = model.rows;
  var rows = clone(origRows);
  // tally column headers: icons (raw HTML) or plain text, per the Settings toggle
  var HN = CFG.useIcons ? CFG.icons.non : esc(L.nonSaudi),
      HS = CFG.useIcons ? CFG.icons.saudi : esc(L.saudi),
      HT = CFG.useIcons ? CFG.icons.total : esc(L.total);

  function clone(src) { return src.map(function (r) { var n = Object.assign({}, r); n.Cells = r.Cells.map(function (c) { return { s: c.s, n: c.n, bs: c.bs, bn: c.bn }; }); return n; }); }
  function cellChanged(c) { return c.s !== c.bs || c.n !== c.bn; }   // differs from the current data
  function esc(s) { return String(s == null ? "" : s).replace(/[&<>"]/g, function (c) { return ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]; }); }
  function fmt(p) { return (p * 100).toFixed(1).replace(/\.0$/, "") + "%"; }

  /* ---- calc (mirrors NitaqatCalculator.Classify) ---- */
  function classify(s, n, req, min) {
    var t = s + n;
    if (req <= 0 || t === 0) return { st: 0 };
    if (t < min) return { st: 4, exemptAdd: Math.max(0, min - 1 - t) };
    // the 1e-9 nudges undo float error (0.35 * 20 = 7.000000000000001) so results match NitaqatCalculator's decimal maths
    var need = Math.ceil(req * t - 1e-9), cap = Math.floor(s / req + 1e-9), p = s / t;
    if (p < req) {
        var shed = Math.max(0, t - cap);
        // cheaper escape: shed non-Saudi to fall below min → exempt (only when reachable & cheaper)
        var shedEx = 0;
        if (min > 0 && s < min) { var e = Math.max(0, t - (min - 1)); if (e >= 1 && e < shed) shedEx = e; }
        return { st: 1, replace: Math.max(0, need - s), hire: req < 1 ? Math.ceil((req * t - s) / (1 - req) - 1e-9) : Math.max(0, t - s), shed: shed, shedExempt: shedEx };
    }
    return { st: p <= req + CFG.band ? 2 : 3, absorb: Math.max(0, cap - t), surplus: Math.max(0, s - need) };
  }
  function genExtra(rec, base) { return Math.min(rec.DisabledExtraRaw, Math.floor(CFG.capPct * base + 1e-9)); }
  function genPct(rec, base, head) { return head <= 0 ? 0 : (base + genExtra(rec, base)) / head; }
  function zoneIdx(rec, base, head) {
    if (head <= 0) return 0;
    var p = genPct(rec, base, head) + 1e-12;
    if (p >= rec.Platinum) return 5; if (p >= rec.High) return 4; if (p >= rec.Medium) return 3; if (p >= rec.Low) return 2; return 1;
  }

  function seg(ri, k, doType, v, icon, title, cls) {
    return "<span class='act-btn " + cls + "' data-r='" + ri + "' data-k='" + k + "' data-do='" + doType + "' data-v='" + v +
      "' title='" + esc(title) + "'>" + icon + " " + v + "</span>";
  }
  function actionHtml(cl, ri, k) {
    if (cl.st === 1) {
      var h = seg(ri, k, "rep", cl.replace, "↔", L.replaceTip, "act-rep") +
        seg(ri, k, "hire", cl.hire, "＋", L.hireTip, "act-hire");
      // one removal recommendation only: the cheaper "→ exempt" shed dominates the "→ ratio" shed
      if (cl.shedExempt > 0) h += seg(ri, k, "shed", cl.shedExempt, "⇩", L.shedExemptTip, "act-exshed");
      else h += seg(ri, k, "shed", cl.shed, "⤓", L.shedTip, "act-shed");
      return "<div class='act'>" + h + "</div>";
    }
    if (cl.st === 2 || cl.st === 3) {
      var g = "";
      if (cl.absorb > 0) g += seg(ri, k, "absorb", cl.absorb, "⤒ +", L.absorbTip, "act-absorb");
      if (cl.surplus > 0) g += seg(ri, k, "surplus", cl.surplus, "▽", L.surplus, "act-surplus");
      return "<div class='act'>" + g + "</div>";
    }
    if (cl.st === 4) return "<div class='act'>" + seg(ri, k, "exempt", cl.exemptAdd, "✓ +", L.exemptTip, "act-exempt") + "</div>";
    return "";
  }

  /* ---- build the table ONCE (inputs get stable ids) ---- */
  function buildOnce() {
    var thead = "<tr><th rowspan='2'>" + esc(L.category) + "</th><th rowspan='2' class='center'>" + esc(L.minCalc) +
      "</th><th rowspan='2' class='center'>" + esc(L.required) + "</th>";
    RECS.forEach(function (rec, k) {
      thead += "<th colspan='5' class='grp rec-sep' id='hz_" + k + "'></th>";
    });
    thead += "<th colspan='4' class='grp rec-sep'>" + esc(L.grand) + "</th></tr><tr>";
    RECS.forEach(function (rec, k) {
      thead += "<th class='num rec-sep'>" + HN + "</th><th class='num'>" + HS + "</th><th class='num'>" +
        HT + "</th><th class='num'>" + esc(L.pct) + "</th><th class='num'>" + esc(L.action) + "</th>";
    });
    thead += "<th class='num rec-sep'>" + HN + "</th><th class='num'>" + HS + "</th><th class='num'>" +
      HT + "</th><th class='num'>" + esc(L.pct) + "</th></tr>";
    document.querySelector("#sgGrid thead").innerHTML = thead;

    var body = rows.map(function (row, ri) {
      var tds = RECS.map(function (rec, k) {
        var c = row.Cells[k];
        var sc = cellChanged(c) ? " sc-chg" : "";   // differs from current data → highlight amber
        return "<td class='num rec-sep" + sc + "'><input type='text' inputmode='numeric' data-r='" + ri + "' data-k='" + k + "' data-f='n' value='" + c.n + "'></td>" +
          "<td class='num" + sc + "'><input type='text' inputmode='numeric' data-r='" + ri + "' data-k='" + k + "' data-f='s' value='" + c.s + "'></td>" +
          "<td class='num' id='tot_" + ri + "_" + k + "'></td>" +
          "<td class='num' id='pct_" + ri + "_" + k + "'></td>" +
          "<td class='num' id='act_" + ri + "_" + k + "'></td>";
      }).join("");
      return "<tr><td>" + esc(row.Name) + "</td><td class='center'>" + (row.IsUnclassified ? "" : row.MinForCalc) +
        "</td><td class='center'>" + (row.IsUnclassified ? "" : fmt(row.Required)) + "</td>" + tds +
        "<td class='num rec-sep' id='gn_" + ri + "'></td><td class='num' id='gs_" + ri + "'></td><td class='num' id='gt_" + ri +
        "'></td><td class='num' id='gp_" + ri + "'></td></tr>";
    }).join("");
    document.querySelector("#sgGrid tbody").innerHTML = body;

    var foot = "<tr class='totalrow'><td>" + esc(L.grand) + "</td><td></td><td></td>";
    RECS.forEach(function (rec, k) {
      foot += "<td class='num rec-sep' id='fn_" + k + "'></td><td class='num' id='fs_" + k + "'></td><td class='num' id='ft_" + k +
        "'></td><td class='num' id='fp_" + k + "'></td><td></td>";
    });
    foot += "<td class='num rec-sep' id='gfn'></td><td class='num' id='gfs'></td><td class='num' id='gft'></td><td class='num' id='gfp'></td></tr>";
    document.querySelector("#sgGrid tfoot").innerHTML = foot;

    bindInputs();
    refresh();
  }

  // Amber note above the grid counting how many cells differ from the current data (scenario + any edits).
  function markScenarioBanner() {
    var count = 0;
    rows.forEach(function (r) { r.Cells.forEach(function (c) { if (cellChanged(c)) count++; }); });
    var grid = document.getElementById("sgGrid");
    var host = document.getElementById("sgChgNote");
    if (!host) { host = document.createElement("div"); host.id = "sgChgNote"; host.className = "sg-chg-note"; grid.parentNode.insertBefore(host, grid); }
    if (count > 0) { host.style.display = ""; host.textContent = "★ " + (L.scenarioChanges || "{0} cell(s) differ from the current data — highlighted. Use ↺ on a cell, or Reset, to restore.").replace("{0}", count); }
    else host.style.display = "none";
  }

  /* ---- refresh derived cells only (inputs untouched → stable typing) ---- */
  function refresh() {
    var base = RECS.map(function () { return 0; }), head = RECS.map(function () { return 0; });
    var GB = 0, GH = 0;
    rows.forEach(function (row, ri) {
      var gs = 0, gn = 0;
      RECS.forEach(function (rec, k) {
        var c = row.Cells[k], t = c.s + c.n; base[k] += c.s; head[k] += t; gs += c.s; gn += c.n;
        var cl = row.IsUnclassified ? { st: 0 } : classify(c.s, c.n, row.Required, row.MinForCalc);
        var changed = cellChanged(c);
        var totEl = document.getElementById("tot_" + ri + "_" + k);
        totEl.textContent = t; totEl.className = changed ? "num chg" : "num";
        var pc = document.getElementById("pct_" + ri + "_" + k);
        pc.textContent = fmt(t > 0 ? c.s / t : 0); pc.className = "num " + CFG.statusCss[cl.st];
        document.getElementById("act_" + ri + "_" + k).innerHTML = actionHtml(cl, ri, k) +
          (changed ? "<span class='cell-reset' data-r='" + ri + "' data-k='" + k + "' title='" + esc(L.resetCell) + "'>↺</span>" : "");
      });
      set("gn_" + ri, gn); set("gs_" + ri, gs); set("gt_" + ri, gs + gn); set("gp_" + ri, fmt(gs + gn > 0 ? gs / (gs + gn) : 0));
    });
    var GC = 0;   // counted Saudis incl. each registration's capped disability bonus (matches Grid 1's grand %)
    RECS.forEach(function (rec, k) {
      GB += base[k]; GH += head[k]; GC += head[k] > 0 ? base[k] + genExtra(rec, base[k]) : 0;
      var zi = zoneIdx(rec, base[k], head[k]);
      document.getElementById("hz_" + k).innerHTML = "<span class='grp-lbl'>" + esc(rec.Label) + "</span> <span class='zonebadge " +
        CFG.zoneCss[zi] + "'>" + esc(CFG.zones[zi]) + " " + fmt(genPct(rec, base[k], head[k])) + "</span>";
      set("fn_" + k, head[k] - base[k]); set("fs_" + k, base[k]); set("ft_" + k, head[k]); set("fp_" + k, fmt(genPct(rec, base[k], head[k])));
    });
    set("gfn", GH - GB); set("gfs", GB); set("gft", GH); set("gfp", fmt(GH > 0 ? GC / GH : 0));
    renderTransfers();
    markScenarioBanner();
  }
  function set(id, v) { var e = document.getElementById(id); if (e) e.textContent = v; }

  function bindInputs() {
    Array.prototype.forEach.call(document.querySelectorAll("#sgGrid input"), function (inp) {
      inp.addEventListener("focus", function () { inp.select(); });
      inp.addEventListener("input", function () {
        var raw = inp.value.replace(/[^\d]/g, "");
        if (raw !== inp.value) inp.value = raw;                    // keep it digits-only, don't reformat/pad
        var v = raw === "" ? 0 : parseInt(raw, 10);
        rows[+inp.getAttribute("data-r")].Cells[+inp.getAttribute("data-k")][inp.getAttribute("data-f")] = v;
        refresh();                                                 // NB: never rebuilds inputs
      });
      inp.addEventListener("blur", function () { if (inp.value === "") inp.value = "0"; });
    });
  }
  // ONE delegated listener on the persistent tbody (never re-bound → no stacking).
  function bindActions() {
    document.querySelector("#sgGrid tbody").addEventListener("click", function (e) {
      var rb = e.target.closest(".cell-reset");
      if (rb) { resetCell(+rb.getAttribute("data-r"), +rb.getAttribute("data-k")); buildOnce(); return; }
      var b = e.target.closest(".act-btn");
      if (!b) return;
      applyAction(+b.getAttribute("data-r"), +b.getAttribute("data-k"), b.getAttribute("data-do"), +b.getAttribute("data-v"));
      buildOnce();
    });
  }
  function applyAction(ri, k, doType, v) {
    var c = rows[ri].Cells[k];
    if (doType === "rep") { var r = Math.min(v, c.n); c.n -= r; c.s += r; }
    else if (doType === "hire") { c.s += v; }
    else if (doType === "shed") { c.n = Math.max(0, c.n - v); }
    else if (doType === "absorb") { c.n += v; }
    else if (doType === "surplus") { c.s = Math.max(0, c.s - v); }
    else if (doType === "exempt") { c.n += v; }
  }
  function resetCell(ri, k) { var c = rows[ri].Cells[k]; c.s = c.bs; c.n = c.bn; }   // reset this cell to the current value

  /* ---- transfer planner (same-category deficit ↔ surplus tolerance), with per-move detail ---- */
  function computeTransfers() {
    var out = [];
    rows.forEach(function (row, ri) {
      if (row.IsUnclassified || row.Required <= 0) return;
      var def = [], sur = [];
      row.Cells.forEach(function (c, k) {
        var t = c.s + c.n; if (t < row.MinForCalc || t === 0) return;
        var cap = Math.floor(c.s / row.Required);
        if (c.s / t < row.Required) { var ex = Math.max(0, t - cap); if (ex > 0) def.push({ k: k, left: ex }); }
        else { var ab = Math.max(0, cap - t); if (ab > 0) sur.push({ k: k, left: ab }); }
      });
      var i = 0, j = 0;
      while (i < def.length && j < sur.length) {
        var move = Math.min(def[i].left, sur[j].left);
        if (move > 0) out.push({ ri: ri, from: def[i].k, to: sur[j].k, count: move });
        def[i].left -= move; sur[j].left -= move;
        if (def[i].left === 0) i++; if (sur[j].left === 0) j++;
      }
    });
    return out;
  }
  function renderTransfers() {
    var list = computeTransfers(), box = document.getElementById("sgTransfers");
    if (!list.length) { box.innerHTML = "<div class='subtle'>" + esc(L.noTransfers) + "</div>"; return; }
    var html = list.map(function (m, idx) {
      var row = rows[m.ri], fc = row.Cells[m.from], tc = row.Cells[m.to];
      var ft = fc.s + fc.n, tt = tc.s + tc.n, cnt = Math.min(m.count, fc.n);
      var fAfter = ft - cnt > 0 ? fc.s / (ft - cnt) : 0, tAfter = tc.s / (tt + cnt);
      return "<div class='sg-tr'><span class='sg-tr-txt'><strong>" + esc(L.move) + " " + cnt + " " + esc(L.nonSaudi) + "</strong> · " +
        esc(row.Name) + "<span class='sg-tr-detail'>" +
        esc(RECS[m.from].Label) + " " + fmt(fc.s / ft) + "→<b>" + fmt(fAfter) + "</b> &nbsp;→&nbsp; " +
        esc(RECS[m.to].Label) + " " + fmt(tc.s / tt) + "→<b>" + fmt(tAfter) + "</b></span></span>" +
        "<button class='btn btn-sm btn-soft' data-tr='" + idx + "'>" + esc(L.apply) + "</button></div>";
    }).join("");
    box.innerHTML = "<div class='sg-tr-actions'><button id='sgApplyAll' class='btn btn-sm'>" + esc(L.applyAll) + "</button></div>" + html;
    Array.prototype.forEach.call(box.querySelectorAll("button[data-tr]"), function (b) {
      b.addEventListener("click", function () { applyMove(list[+b.getAttribute("data-tr")]); buildOnce(); });
    });
    document.getElementById("sgApplyAll").addEventListener("click", function () { list.forEach(applyMove); buildOnce(); });
  }
  function applyMove(m) { var cells = rows[m.ri].Cells, c = Math.min(m.count, cells[m.from].n); cells[m.from].n -= c; cells[m.to].n += c; }

  /* ---- payload + controls ---- */
  function collectCells() {
    var out = [];
    rows.forEach(function (row) {
      // include cells edited down to 0/0 — otherwise a reloaded scenario shows the original counts again
      row.Cells.forEach(function (c, k) { if (c.s > 0 || c.n > 0 || cellChanged(c)) out.push({ RecordId: RECS[k].Id, CategoryId: row.CategoryId, Saudi: c.s, NonSaudi: c.n }); });
    });
    return out;
  }
  function token() { var i = document.querySelector('input[name="__RequestVerificationToken"]'); return i ? i.value : ""; }
  function msg(t) { var m = document.getElementById("sgMsg"); if (m) m.textContent = t; }

  var saveBtn = document.getElementById("sgSave");
  var updateBtn = document.getElementById("sgUpdate");
  var nameInput = document.getElementById("sgName");
  var loadSelEl = document.getElementById("sgLoad");

  function refreshScenarioButtons() { if (updateBtn) updateBtn.style.display = window.SG_PLAN_ID ? "" : "none"; }
  refreshScenarioButtons();

  function postPlan(url, body) {
    return fetch(url, { method: "POST", headers: { "Content-Type": "application/json", "RequestVerificationToken": token() }, body: JSON.stringify(body) })
      .then(function (r) { return r.ok ? r.json() : r.text().then(function (t) { throw new Error(t); }); });
  }

  // Save = always a NEW scenario; requires a name; appears in the list immediately.
  if (saveBtn) saveBtn.addEventListener("click", function () {
    if (saveBtn.disabled) return;
    var name = (nameInput.value || "").trim();
    if (!name) { msg("✗ Enter a name for the new scenario"); if (nameInput) nameInput.focus(); return; }
    saveBtn.disabled = true;
    postPlan(window.SG_SAVE_URL, { SnapshotId: window.SG_SNAPSHOT, Label: name, Cells: collectCells() })
      .then(function (res) {
        if (loadSelEl && !loadSelEl.querySelector("option[value='" + res.planId + "']")) {
          var o = document.createElement("option"); o.value = res.planId; o.textContent = res.label; loadSelEl.appendChild(o);
        }
        if (loadSelEl) loadSelEl.value = res.planId;   // select it (no navigation — setting value doesn't fire change)
        window.SG_PLAN_ID = res.planId; refreshScenarioButtons();
        msg("✓ Saved “" + res.label + "”");
      })
      .catch(function (e) { msg("✗ " + e.message); })
      .finally(function () { saveBtn.disabled = false; });
  });

  // Update = overwrite the currently-loaded scenario (no new row).
  if (updateBtn) updateBtn.addEventListener("click", function () {
    if (updateBtn.disabled) return;
    if (!window.SG_PLAN_ID) { msg("✗ Load a scenario first, or use Save for a new one"); return; }
    updateBtn.disabled = true;
    postPlan(window.SG_UPDATE_URL, { PlanId: window.SG_PLAN_ID, SnapshotId: window.SG_SNAPSHOT, Cells: collectCells() })
      .then(function (res) { msg("✓ Updated “" + res.label + "”"); })
      .catch(function (e) { msg("✗ " + e.message); })
      .finally(function () { updateBtn.disabled = false; });
  });
  var exportBtn = document.getElementById("sgExport");
  if (exportBtn) exportBtn.addEventListener("click", function () {
    // Export EXACTLY what is on screen (full merged grid) + the current filter, so % / zone and the
    // shown registrations/categories match the page.
    document.getElementById("sgExportPayload").value = JSON.stringify({
      SnapshotId: window.SG_SNAPSHOT, Mode: window.SG_MODE, PlanId: window.SG_PLAN_ID,
      RecordId: window.SG_RECORD_ID, CategoryId: window.SG_CATEGORY_ID, Cells: collectCells()
    });
    document.getElementById("sgExportForm").submit();
  });
  var resetBtn = document.getElementById("sgReset");
  if (resetBtn) resetBtn.addEventListener("click", function () {
    rows.forEach(function (r) { r.Cells.forEach(function (c) { c.s = c.bs; c.n = c.bn; }); });   // reset ALL cells to current
    buildOnce();
  });
  var loadSel = document.getElementById("sgLoad");
  if (loadSel) loadSel.addEventListener("change", function () {
    try { sessionStorage.setItem("sgScroll", String(window.pageYOffset || window.scrollY || 0)); } catch (e) { }
    var q = "?snapshotId=" + window.SG_SNAPSHOT + "&mode=" + window.SG_MODE;
    if (window.SG_RECORD_ID != null) q += "&recordId=" + window.SG_RECORD_ID;     // keep the current filter
    if (window.SG_CATEGORY_ID != null) q += "&categoryId=" + window.SG_CATEGORY_ID;
    if (this.value) q += "&planId=" + this.value;
    window.location = window.SG_LOAD_URL + q;
  });

  bindActions();   // attach the delegated click handler exactly once
  buildOnce();

  // restore the scroll position after a scenario-load reload, so the page doesn't jump to the top
  try {
    var _sy = sessionStorage.getItem("sgScroll");
    if (_sy !== null) { sessionStorage.removeItem("sgScroll"); window.scrollTo(0, parseInt(_sy, 10) || 0); }
  } catch (e) { }
})();
