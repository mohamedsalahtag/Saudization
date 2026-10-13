using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;
using Saudization.Web.Models;
using Saudization.Web.Services;

namespace Saudization.Web.Controllers;

/// <summary>The detailed Nationalization pivot (registrations × occupations), per version, plus the
/// editable "what-if" workbench (second grid), scenario save/load, and styled two-grid export.</summary>
public class SaudizationController : Controller
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private readonly NitaqatCalculator _calc;
    private readonly SaudizationExportService _export;
    private readonly IHtmlLocalizer<SharedResource> _t;
    private readonly AppDbContext _db;

    public SaudizationController(NitaqatCalculator calc, SaudizationExportService export, IHtmlLocalizer<SharedResource> t, AppDbContext db)
    { _calc = calc; _export = export; _t = t; _db = db; }

    public async Task<IActionResult> Index(string mode = "current", int? recordId = null, int? categoryId = null, int? snapshotId = null, int? planId = null, CancellationToken ct = default)
    {
        var snapshots = await _db.Snapshots.OrderByDescending(s => s.WeekDate).ThenByDescending(s => s.Id).ToListAsync(ct);
        if (snapshots.Count == 0)
        {
            ViewBag.Records = new List<Record>();
            ViewBag.Snapshots = snapshots;
            return View(new DashboardViewModel { Mode = DashboardMode.Current });
        }

        var sid = await _db.ResolveSnapshotAsync(HttpContext, snapshotId, ct);
        var m = string.Equals(mode, "plan", StringComparison.OrdinalIgnoreCase) ? DashboardMode.Plan : DashboardMode.Current;

        var recEntities = await _db.Records.OrderBy(r => r.RegistrationLabel).ToListAsync(ct);
        ViewBag.Records = recEntities;
        ViewBag.Snapshots = snapshots;

        var vm = await _calc.BuildAsync(m, recordId, sid, ct);                       // Grid 1 (respects the record filter)
        var full = recordId == null ? vm : await _calc.BuildAsync(m, null, sid, ct); // Grid 2 always spans all records

        // category filter — narrow Grid 1's rows only (Grid 2 stays whole so scenarios/transfers are complete)
        bool rtl = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
        ViewBag.Categories = full.Rows
            .Where(r => !r.IsUnclassified && r.CategoryId != null)
            .Select(r => new CatFilterOption
            {
                CategoryId = r.CategoryId!.Value,
                Name = rtl ? r.NameAr : (string.IsNullOrWhiteSpace(r.NameEn) ? r.NameAr : r.NameEn!)
            })
            .ToList();
        if (categoryId is int cid)
        {
            vm.Rows = vm.Rows.Where(r => r.CategoryId == cid).ToList();
            vm.FilterCategoryId = cid;
        }
        vm.FilterRecordId = recordId;

        var settings = await _calc.GetSettingsAsync(ct);
        var showIcons = settings.ShowNationalityIcons;
        ViewBag.ShowNatIcons = showIcons;

        var plans = await _db.SaudizationPlans.Where(p => p.SnapshotId == sid).OrderByDescending(p => p.Id).ToListAsync(ct);
        ViewBag.Plans = plans;
        ViewBag.PlanId = planId;

        // optional scenario to pre-fill the editable grid
        List<SaudizationPlanCell>? planCells = null;
        if (planId is int pid)
            planCells = await _db.SaudizationPlanCells.Where(c => c.PlanId == pid).ToListAsync(ct);

        ViewBag.EditableModel = BuildEditableJson(full, recEntities, planCells, m, settings);
        return View(vm);
    }

    // Read-only "Roadmap" tab: per registration, how to climb each Nitaqat level up to Platinum
    // (hire / swap / remove) with a red-categories-first per-category breakdown.
    public async Task<IActionResult> Roadmap(int? recordId, int? snapshotId, CancellationToken ct = default)
    {
        var snapshots = await _db.Snapshots.OrderByDescending(s => s.WeekDate).ThenByDescending(s => s.Id).ToListAsync(ct);
        ViewBag.Snapshots = snapshots;
        if (snapshots.Count == 0)
        {
            ViewBag.Records = new List<Record>();
            ViewBag.Roadmaps = new List<RecordRoadmap>();
            return View(new DashboardViewModel { Mode = DashboardMode.Current });
        }

        var sid = await _db.ResolveSnapshotAsync(HttpContext, snapshotId, ct);
        ViewBag.Records = await _db.Records.OrderBy(r => r.RegistrationLabel).ToListAsync(ct);

        var vm = await _calc.BuildAsync(DashboardMode.Current, recordId, sid, ct);   // roadmap uses current requirements
        bool rtl = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
        ViewBag.Roadmaps = NitaqatPlanner.Build(vm, rtl);
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePlan([FromBody] SavePlanPayload payload, CancellationToken ct)
    {
        // Save = create a NEW scenario. A name is required, and a duplicate name is rejected
        // (load it and use “Update” instead) — this is what prevents the same scenario piling up.
        if (payload is null || payload.Cells.Count == 0) return BadRequest("Nothing to save — make a change first.");
        if (string.IsNullOrWhiteSpace(payload.Label)) return BadRequest("Enter a name for the new scenario.");
        var snap = await _db.Snapshots.FindAsync(new object[] { payload.SnapshotId }, ct);
        if (snap is null) return BadRequest("Unknown version.");
        var label = payload.Label!.Trim();
        if (await _db.SaudizationPlans.AnyAsync(p => p.SnapshotId == payload.SnapshotId && p.Label == label, ct))
            return BadRequest($"A scenario named “{label}” already exists. Load it and press Update, or choose a different name.");

        var plan = new SaudizationPlan
        {
            SnapshotId = payload.SnapshotId,
            Label = label,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUser = User?.Identity?.Name ?? "web"
        };
        foreach (var c in payload.Cells)
            plan.Cells.Add(new SaudizationPlanCell { RecordId = c.RecordId, CategoryId = c.CategoryId, Saudi = Math.Max(0, c.Saudi), NonSaudi = Math.Max(0, c.NonSaudi) });
        _db.SaudizationPlans.Add(plan);
        await _db.SaveChangesAsync(ct);
        return Json(new { planId = plan.Id, label = plan.Label });
    }

    // Update = overwrite the cells of the currently-loaded scenario (keeps its id and name).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePlan([FromBody] SavePlanPayload payload, CancellationToken ct)
    {
        if (payload is null || payload.PlanId is not int pid) return BadRequest("No scenario is loaded to update.");
        if (payload.Cells.Count == 0) return BadRequest("Nothing to save — make a change first.");
        var plan = await _db.SaudizationPlans.Include(p => p.Cells).FirstOrDefaultAsync(p => p.Id == pid, ct);
        if (plan is null) return BadRequest("That scenario no longer exists — save it as new.");
        _db.SaudizationPlanCells.RemoveRange(plan.Cells);
        plan.Cells.Clear();
        plan.CreatedAtUtc = DateTime.UtcNow;
        plan.CreatedByUser = User?.Identity?.Name ?? "web";
        foreach (var c in payload.Cells)
            plan.Cells.Add(new SaudizationPlanCell { RecordId = c.RecordId, CategoryId = c.CategoryId, Saudi = Math.Max(0, c.Saudi), NonSaudi = Math.Max(0, c.NonSaudi) });
        await _db.SaveChangesAsync(ct);
        return Json(new { planId = plan.Id, label = plan.Label });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Export(string payload, CancellationToken ct)
    {
        ExportPayload? p;
        try { p = JsonSerializer.Deserialize<ExportPayload>(payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch { return BadRequest("Bad payload."); }
        if (p is null) return BadRequest("Bad payload.");

        var m = string.Equals(p.Mode, "plan", StringComparison.OrdinalIgnoreCase) ? DashboardMode.Plan : DashboardMode.Current;

        // Enhanced grid = EXACTLY what is on screen (the posted cells are the full merged grid, including a
        // loaded scenario + any edits). This is why registration %/zone now matches the page — the previous code
        // recomputed from a saved scenario's stored cells, which for a partial scenario gave a wrong headcount/zone.
        string fileName = "saudization-plan.xlsx";
        if (p.PlanId is int pid)
        {
            var label = await _db.SaudizationPlans.Where(x => x.Id == pid).Select(x => x.Label).FirstOrDefaultAsync(ct);
            if (label != null) fileName = $"saudization-{string.Join("_", label.Split(Path.GetInvalidFileNameChars()))}.xlsx";
        }

        var original = await _calc.BuildAsync(m, p.RecordId, p.SnapshotId, ct);           // original respects the record filter
        var enhanced = await _calc.BuildFromCountsAsync(m, p.SnapshotId, p.Cells, ct);     // enhanced = on-screen grid

        // Apply the on-screen filter to BOTH grids so the exported file matches what the user is looking at.
        ApplyExportFilter(enhanced, p.RecordId, p.CategoryId);
        ApplyExportFilter(original, null, p.CategoryId);   // original already limited to the record by BuildAsync

        bool isRtl = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
        var bytes = _export.Build(
            new[] { (_t["Dash_Original"].Value, original), (_t["Dash_Enhanced"].Value, enhanced) },
            key => _t[key].Value, isRtl);
        return File(bytes, Xlsx, fileName);
    }

    // Narrows a built grid to the selected registration and/or category so the export mirrors the on-screen filter.
    private static void ApplyExportFilter(DashboardViewModel vm, int? recordId, int? categoryId)
    {
        if (recordId is int rid)
        {
            vm.Records = vm.Records.Where(x => x.RecordId == rid).ToList();
            foreach (var row in vm.Rows)
                row.ByRecord = row.ByRecord.Where(kv => kv.Key == rid).ToDictionary(kv => kv.Key, kv => kv.Value);
        }
        if (categoryId is int cid)
            vm.Rows = vm.Rows.Where(r => r.CategoryId == cid).ToList();
    }

    // Delete a saved scenario (and its cells) for this version.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePlan(int planId, int snapshotId, string mode = "current", CancellationToken ct = default)
    {
        var plan = await _db.SaudizationPlans.Include(p => p.Cells).FirstOrDefaultAsync(p => p.Id == planId, ct);
        if (plan != null)
        {
            _db.SaudizationPlanCells.RemoveRange(plan.Cells);
            _db.SaudizationPlans.Remove(plan);
            await _db.SaveChangesAsync(ct);
            TempData["ok"] = _t["Dash_ScenarioDeleted"].Value;
        }
        return RedirectToAction(nameof(Index), new { mode, snapshotId });
    }

    // ---- builds the JSON model the editable grid (saudization-grid.js) consumes ----
    private string BuildEditableJson(DashboardViewModel full, List<Record> recEntities, List<SaudizationPlanCell>? planCells, DashboardMode mode, Settings settings)
    {
        bool isRtl = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
        string T(string k) => _t[k].Value;
        var recById = recEntities.ToDictionary(r => r.Id);

        var records = full.Records.Select(rc =>
        {
            recById.TryGetValue(rc.RecordId, out var re);
            return new
            {
                Id = rc.RecordId,
                Label = rc.Label,
                rc.DisabledExtraRaw,
                Low = re?.LowGreenMin ?? 0m,
                Medium = re?.MediumGreenMin ?? 0m,
                High = re?.HighGreenMin ?? 0m,
                Platinum = re?.PlatinumMin ?? 0m
            };
        }).ToList();

        // plan overrides keyed by (recordId, categoryId)
        Dictionary<(int, int?), (int s, int n)>? overrides = planCells?
            .GroupBy(c => (c.RecordId, c.CategoryId))
            .ToDictionary(g => g.Key, g => (g.Sum(x => x.Saudi), g.Sum(x => x.NonSaudi)));

        var rows = full.Rows.Select(row => new
        {
            CategoryId = row.CategoryId,
            Name = isRtl ? row.NameAr : (string.IsNullOrWhiteSpace(row.NameEn) ? row.NameAr : row.NameEn!),
            row.MinForCalc,
            Required = row.RequiredPct,
            row.IsUnclassified,
            Cells = full.Records.Select(rc =>
            {
                var c = row.ByRecord.TryGetValue(rc.RecordId, out var cell) ? cell : new Cell();
                int s = c.Saudi, n = c.NonSaudi;
                int bs = c.Saudi, bn = c.NonSaudi;   // current (pre-scenario) baseline — used to highlight & reset
                if (overrides != null && overrides.TryGetValue((rc.RecordId, row.CategoryId), out var ov)) { s = ov.s; n = ov.n; }
                return new { s, n, bs, bn };
            }).ToList()
        }).ToList();

        var cfg = new
        {
            capPct = settings.DisabilityCapPct,
            band = settings.LightGreenBand,
            snapshotId = full.SnapshotId,
            mode = mode == DashboardMode.Plan ? "plan" : "current",
            planLoaded = planCells != null,
            zones = new[] { T("Zone_Unknown"), T("Zone_Red"), T("Zone_LowGreen"), T("Zone_MediumGreen"), T("Zone_HighGreen"), T("Zone_Platinum") },
            zoneCss = new[] { "zone-unknown", "zone-red", "zone-lowgreen", "zone-medgreen", "zone-highgreen", "zone-platinum" },
            statusCss = new[] { "", "cell-red", "cell-lightgreen", "cell-highgreen", "cell-exempt" },
            useIcons = settings.ShowNationalityIcons,
            icons = new
            {
                non = NationalityIcons.Html("non", T("Dash_NonSaudi")),
                saudi = NationalityIcons.Html("saudi", T("Dash_Saudi")),
                total = NationalityIcons.Html("total", T("Dash_Total"))
            },
            L = new
            {
                nonSaudi = T("Dash_NonSaudi"), saudi = T("Dash_Saudi"), total = T("Dash_Total"), pct = T("Dash_Pct"),
                action = T("Dash_Action"), category = T("Dash_Category"), minCalc = T("Dash_MinCalc"), required = T("Dash_Required"),
                grand = T("Dash_GrandTotal"), replace = T("Dash_Replace"), hire = T("Dash_Hire"), shed = T("Dash_Shed"),
                absorb = T("Dash_Absorb"), surplus = T("Dash_Surplus"), exempt = T("Dash_Exempt"), addBeforeCalc = T("Dash_AddBeforeCalc"),
                replaceTip = T("Dash_ReplaceTip"), hireTip = T("Dash_HireTip"), shedTip = T("Dash_ShedTip"),
                shedExemptTip = T("Dash_ShedExemptTip"), absorbTip = T("Dash_AbsorbTip"), exemptTip = T("Dash_ExemptTip"),
                transfer = T("Dash_Transfer"), move = T("Dash_Move"), applyAll = T("Dash_ApplyAll"), apply = T("Dash_Apply"),
                noTransfers = T("Dash_NoTransfers"), transferHint = T("Dash_TransferHint"), resetCell = T("Dash_ResetCell"),
                scenarioChanges = T("Dash_ScenarioChanges")
            }
        };

        var opts = new JsonSerializerOptions { PropertyNamingPolicy = null, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        return JsonSerializer.Serialize(new { records, rows, cfg }, opts);
    }
}

/// <summary>One entry in the category filter dropdown on the Saudization page.</summary>
public class CatFilterOption
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = "";
}
