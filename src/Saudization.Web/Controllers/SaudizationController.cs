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

    public async Task<IActionResult> Index(string mode = "current", int? recordId = null, int? snapshotId = null, int? planId = null, CancellationToken ct = default)
    {
        var snapshots = await _db.Snapshots.OrderByDescending(s => s.WeekDate).ThenByDescending(s => s.Id).ToListAsync(ct);
        if (snapshots.Count == 0)
        {
            ViewBag.Records = new List<Record>();
            ViewBag.Snapshots = snapshots;
            return View(new DashboardViewModel { Mode = DashboardMode.Current });
        }

        var sid = snapshotId ?? snapshots.First().Id;
        var m = string.Equals(mode, "plan", StringComparison.OrdinalIgnoreCase) ? DashboardMode.Plan : DashboardMode.Current;

        var recEntities = await _db.Records.OrderBy(r => r.RegistrationLabel).ToListAsync(ct);
        ViewBag.Records = recEntities;
        ViewBag.Snapshots = snapshots;

        var vm = await _calc.BuildAsync(m, recordId, sid, ct);                       // Grid 1 (respects the filter)
        var full = recordId == null ? vm : await _calc.BuildAsync(m, null, sid, ct); // Grid 2 always spans all records

        var plans = await _db.SaudizationPlans.Where(p => p.SnapshotId == sid).OrderByDescending(p => p.Id).ToListAsync(ct);
        ViewBag.Plans = plans;
        ViewBag.PlanId = planId;

        // optional scenario to pre-fill the editable grid
        List<SaudizationPlanCell>? planCells = null;
        if (planId is int pid)
            planCells = await _db.SaudizationPlanCells.Where(c => c.PlanId == pid).ToListAsync(ct);

        ViewBag.EditableModel = BuildEditableJson(full, recEntities, planCells, m);
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePlan([FromBody] SavePlanPayload payload, CancellationToken ct)
    {
        if (payload is null || payload.Cells.Count == 0) return BadRequest("Empty scenario.");
        var snap = await _db.Snapshots.FindAsync(new object[] { payload.SnapshotId }, ct);
        if (snap is null) return BadRequest("Unknown version.");

        var plan = new SaudizationPlan
        {
            SnapshotId = payload.SnapshotId,
            Label = string.IsNullOrWhiteSpace(payload.Label) ? "Scenario " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") : payload.Label!.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUser = User?.Identity?.Name ?? "web"
        };
        foreach (var c in payload.Cells)
            plan.Cells.Add(new SaudizationPlanCell { RecordId = c.RecordId, CategoryId = c.CategoryId, Saudi = Math.Max(0, c.Saudi), NonSaudi = Math.Max(0, c.NonSaudi) });

        _db.SaudizationPlans.Add(plan);
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
        var original = await _calc.BuildAsync(m, null, p.SnapshotId, ct);
        var enhanced = await _calc.BuildFromCountsAsync(m, p.SnapshotId, p.Cells, ct);

        bool isRtl = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
        var bytes = _export.Build(
            new[] { (_t["Dash_Original"].Value, original), (_t["Dash_Enhanced"].Value, enhanced) },
            key => _t[key].Value, isRtl);
        return File(bytes, Xlsx, "saudization-plan.xlsx");
    }

    // ---- builds the JSON model the editable grid (saudization-grid.js) consumes ----
    private string BuildEditableJson(DashboardViewModel full, List<Record> recEntities, List<SaudizationPlanCell>? planCells, DashboardMode mode)
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
                if (overrides != null && overrides.TryGetValue((rc.RecordId, row.CategoryId), out var ov)) { s = ov.s; n = ov.n; }
                return new { s, n };
            }).ToList()
        }).ToList();

        var settings = _db.Settings.FirstOrDefault() ?? new Settings();
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
            L = new
            {
                nonSaudi = T("Dash_NonSaudi"), saudi = T("Dash_Saudi"), total = T("Dash_Total"), pct = T("Dash_Pct"),
                action = T("Dash_Action"), category = T("Dash_Category"), minCalc = T("Dash_MinCalc"), required = T("Dash_Required"),
                grand = T("Dash_GrandTotal"), replace = T("Dash_Replace"), hire = T("Dash_Hire"), shed = T("Dash_Shed"),
                absorb = T("Dash_Absorb"), surplus = T("Dash_Surplus"), exempt = T("Dash_Exempt"), addBeforeCalc = T("Dash_AddBeforeCalc"),
                transfer = T("Dash_Transfer"), move = T("Dash_Move"), applyAll = T("Dash_ApplyAll"), apply = T("Dash_Apply"),
                noTransfers = T("Dash_NoTransfers"), transferHint = T("Dash_TransferHint"), resetCell = T("Dash_ResetCell")
            }
        };

        var opts = new JsonSerializerOptions { PropertyNamingPolicy = null, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        return JsonSerializer.Serialize(new { records, rows, cfg }, opts);
    }
}
