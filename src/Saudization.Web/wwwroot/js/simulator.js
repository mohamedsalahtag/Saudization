/* Ephemeral what-if simulator (§9). Two modes:
   • By registration — edit counts / transfer, watch the general Saudization % and Nitaqat zone.
   • By job — pick a category, edit or transfer per registration, see occupation % + status, and
     "Fill red to green" to compute the Saudis needed. Nothing is persisted (Reset restores actuals). */
(function () {
  "use strict";
  var recOrig = JSON.parse(document.getElementById("sim-records").textContent);
  var catOrig = JSON.parse(document.getElementById("sim-cats").textContent);
  var cfg = JSON.parse(document.getElementById("sim-cfg").textContent);

  var rec = recOrig.map(function (r) { return Object.assign({}, r); });
  var cats = deepCats(catOrig);
  var jIdx = 0;

  function deepCats(src) { return src.map(function (c) { var n = Object.assign({}, c); n.Cells = c.Cells.map(function (x) { return Object.assign({}, x); }); return n; }); }
  function esc(s) { return String(s == null ? "" : s).replace(/[&<>"]/g, function (c) { return ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]; }); }
  function fmt(p) { return (p * 100).toFixed(1).replace(/\.0$/, "") + "%"; }
  function pctOf(s, n) { var t = s + n; return t > 0 ? s / t : 0; }
  // general (registration) Saudi count = Saudi heads + disability bonus capped at capPct × Saudi heads (as NitaqatCalculator)
  function counted(r) { return r.Saudi + Math.min(r.DisabledExtraRaw || 0, Math.floor((cfg.capPct || 0) * r.Saudi + 1e-9)); }
  function regPct(r) { var t = r.Saudi + r.NonSaudi; return t > 0 ? counted(r) / t : 0; }

  function zoneIndex(r) {
    var p = regPct(r) + 1e-12, t = r.Saudi + r.NonSaudi;
    if (t <= 0) return 0;
    if (p >= r.Platinum) return 5; if (p >= r.High) return 4; if (p >= r.Medium) return 3; if (p >= r.Low) return 2; return 1;
  }

  /* ---------------- By registration ---------------- */
  function recRow(r, o) {
    var t = r.Saudi + r.NonSaudi, zi = zoneIndex(r), dp = regPct(r) - regPct(o);
    var cls = dp > 1e-4 ? "gap-neg" : dp < -1e-4 ? "gap-pos" : "";
    var delta = Math.abs(dp) < 1e-4 ? "—" : (dp > 0 ? "▲ +" : "▼ ") + (dp * 100).toFixed(1) + " pp";
    return "<tr><td><strong>" + esc(r.Label) + "</strong></td>" +
      "<td class='num'><input type='number' min='0' data-id='" + r.Id + "' data-f='NonSaudi' value='" + r.NonSaudi + "' style='width:70px'></td>" +
      "<td class='num'><input type='number' min='0' data-id='" + r.Id + "' data-f='Saudi' value='" + r.Saudi + "' style='width:70px'></td>" +
      "<td class='num'>" + t + "</td><td class='num'>" + fmt(regPct(r)) + "</td>" +
      "<td class='center'><span class='zonebadge " + cfg.zoneCss[zi] + "'>" + esc(cfg.zones[zi]) + "</span></td>" +
      "<td class='num'><span class='" + cls + "'>" + delta + "</span></td></tr>";
  }
  function renderReg() {
    document.getElementById("rBody").innerHTML = rec.map(function (r, i) { return recRow(r, recOrig[i]); }).join("");
    var s = 0, n = 0, c = 0, os = 0, on = 0, oc = 0;
    rec.forEach(function (r) { s += r.Saudi; n += r.NonSaudi; c += counted(r); });
    recOrig.forEach(function (r) { os += r.Saudi; on += r.NonSaudi; oc += counted(r); });
    var gp = s + n > 0 ? c / (s + n) : 0, ogp = os + on > 0 ? oc / (os + on) : 0;
    var dp = gp - ogp, cls = dp > 1e-4 ? "gap-neg" : dp < -1e-4 ? "gap-pos" : "";
    var delta = Math.abs(dp) < 1e-4 ? "—" : (dp > 0 ? "▲ +" : "▼ ") + (dp * 100).toFixed(1) + " pp";
    document.getElementById("rFoot").innerHTML = "<tr class='totalrow'><td>" + esc(cfg.overall) + "</td><td class='num'>" + n +
      "</td><td class='num'>" + s + "</td><td class='num'>" + (s + n) + "</td><td class='num'>" + fmt(gp) +
      "</td><td></td><td class='num'><span class='" + cls + "'>" + delta + "</span></td></tr>";
    bindNums("#rBody input", function (id, f, v) { byId(rec, id)[f] = v; renderReg(); });
  }

  /* ---------------- By job ---------------- */
  function needCell(saudi, total, req) {
    if (total <= 0 || req <= 0 || saudi / total >= req) return "";
    // the 1e-9 nudges undo float error (0.35 * 20 = 7.000000000000001) so results match the server's decimal maths
    var need = Math.ceil(req * total - 1e-9), rep = Math.max(0, need - saudi);
    var hire = req < 1 ? Math.ceil((req * total - saudi) / (1 - req) - 1e-9) : Math.max(0, total - saudi);
    return "<span class='act-rep'>↔ " + rep + "</span> <span class='act-hire'>＋ " + hire + "</span>";
  }
  function jobStatus(saudi, total, req, min) {
    if (total < min || req <= 0 || total <= 0) return 0;
    var p = saudi / total; if (p < req) return 1; if (p <= req + cfg.band) return 2; return 3;
  }
  function renderJob() {
    var cat = cats[jIdx];
    document.getElementById("jMeta").innerHTML = esc(cfg.needHint) + " · " + fmt(cat.Required) +
      (cat.MinForCalc > 0 ? " · min " + cat.MinForCalc : "");
    var body = cat.Cells.map(function (cell, k) {
      var r = rec[k], total = cell.Saudi + cell.NonSaudi, st = jobStatus(cell.Saudi, total, cat.Required, cat.MinForCalc);
      return "<tr><td><strong>" + esc(r.Label) + "</strong></td>" +
        "<td class='num'><input type='number' min='0' data-k='" + k + "' data-f='NonSaudi' value='" + cell.NonSaudi + "' style='width:70px'></td>" +
        "<td class='num'><input type='number' min='0' data-k='" + k + "' data-f='Saudi' value='" + cell.Saudi + "' style='width:70px'></td>" +
        "<td class='num'>" + total + "</td>" +
        "<td class='num " + cfg.statusCss[st] + "'>" + (total > 0 ? fmt(cell.Saudi / total) : "") + "</td>" +
        "<td class='num'>" + needCell(cell.Saudi, total, cat.Required) + "</td></tr>";
    }).join("");
    document.getElementById("jBody").innerHTML = body;
    var s = 0, n = 0; cat.Cells.forEach(function (c) { s += c.Saudi; n += c.NonSaudi; });
    document.getElementById("jFoot").innerHTML = "<tr class='totalrow'><td>" + esc(cfg.overall) + "</td><td class='num'>" + n +
      "</td><td class='num'>" + s + "</td><td class='num'>" + (s + n) + "</td><td class='num'>" + fmt(pctOf(s, n)) + "</td><td></td></tr>";
    bindNums("#jBody input", function (k, f, v) { cats[jIdx].Cells[+k][f] = v; renderJob(); }, "data-k");
  }

  /* ---------------- shared ---------------- */
  function byId(arr, id) { return arr.filter(function (r) { return r.Id === id; })[0]; }
  function recIndex(id) { for (var i = 0; i < rec.length; i++) if (rec[i].Id === id) return i; return -1; }
  function bindNums(sel, cb, attr) {
    attr = attr || "data-id";
    Array.prototype.forEach.call(document.querySelectorAll(sel), function (inp) {
      inp.addEventListener("input", function () {
        var v = parseInt(inp.value, 10); if (isNaN(v) || v < 0) v = 0;
        cb(inp.getAttribute(attr), inp.getAttribute("data-f"), v);
      });
    });
  }
  function fillSelect(el, arr, second) {
    el.innerHTML = arr.map(function (r) { return "<option value='" + r.Id + "'>" + esc(r.Label) + "</option>"; }).join("");
    if (second && arr.length > 1) el.selectedIndex = 1;
  }

  // registration transfer
  document.getElementById("rMove").addEventListener("click", function () {
    var from = byId(rec, +document.getElementById("rFrom").value), to = byId(rec, +document.getElementById("rTo").value);
    var f = document.getElementById("rKind").value === "saudi" ? "Saudi" : "NonSaudi";
    var c = Math.max(1, parseInt(document.getElementById("rCount").value, 10) || 1);
    if (!from || !to || from === to) return; c = Math.min(c, from[f]); from[f] -= c; to[f] += c; renderReg();
  });

  // job transfer + fill
  document.getElementById("jMove").addEventListener("click", function () {
    var cells = cats[jIdx].Cells;
    var fi = recIndex(+document.getElementById("jFrom").value), ti = recIndex(+document.getElementById("jTo").value);
    var f = document.getElementById("jKind").value === "saudi" ? "Saudi" : "NonSaudi";
    var c = Math.max(1, parseInt(document.getElementById("jCount").value, 10) || 1);
    if (fi < 0 || ti < 0 || fi === ti) return;
    c = Math.min(c, cells[fi][f]); cells[fi][f] -= c; cells[ti][f] += c; renderJob();
  });
  document.getElementById("jFill").addEventListener("click", function () {
    var cat = cats[jIdx];
    cat.Cells.forEach(function (cell) {
      var total = cell.Saudi + cell.NonSaudi;
      if (total >= cat.MinForCalc && total > 0 && cat.Required > 0 && cell.Saudi / total < cat.Required) {
        var need = Math.ceil(cat.Required * total - 1e-9);   // replace non-Saudis with Saudis (total unchanged)
        var add = Math.min(need - cell.Saudi, cell.NonSaudi);
        cell.Saudi += add; cell.NonSaudi -= add;
      }
    });
    renderJob();
  });
  document.getElementById("jCat").addEventListener("change", function () { jIdx = +this.value; fillSelect(document.getElementById("jFrom"), rec); fillSelect(document.getElementById("jTo"), rec, true); renderJob(); });

  document.getElementById("simReset").addEventListener("click", function () {
    rec = recOrig.map(function (r) { return Object.assign({}, r); }); cats = deepCats(catOrig);
    initSelects(); renderReg(); renderJob();
  });

  // tabs
  function showTab(which) {
    document.getElementById("paneReg").style.display = which === "reg" ? "" : "none";
    document.getElementById("paneJob").style.display = which === "job" ? "" : "none";
    document.getElementById("tabReg").className = which === "reg" ? "on" : "";
    document.getElementById("tabJob").className = which === "job" ? "on" : "";
  }
  document.getElementById("tabReg").addEventListener("click", function (e) { e.preventDefault(); showTab("reg"); });
  document.getElementById("tabJob").addEventListener("click", function (e) { e.preventDefault(); showTab("job"); });

  function initSelects() {
    fillSelect(document.getElementById("rFrom"), rec); fillSelect(document.getElementById("rTo"), rec, true);
    fillSelect(document.getElementById("jFrom"), rec); fillSelect(document.getElementById("jTo"), rec, true);
    document.getElementById("jCat").innerHTML = cats.map(function (c, i) { return "<option value='" + i + "'>" + esc(c.Name) + "</option>"; }).join("");
  }

  initSelects(); renderReg();
  if (cats.length) renderJob(); else document.getElementById("tabJob").style.display = "none";
})();
