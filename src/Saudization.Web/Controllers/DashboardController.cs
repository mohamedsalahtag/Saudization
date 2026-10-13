using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;
using Saudization.Web.Models;
using Saudization.Web.Services;

namespace Saudization.Web.Controllers;

/// <summary>Landing overview: KPIs, findings/issues, suggestions and charts. The detailed pivot lives in SaudizationController.</summary>
public class DashboardController : Controller
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private readonly OverviewBuilder _overview;
    private readonly DashboardExportService _export;
    private readonly AppDbContext _db;
    private readonly IHtmlLocalizer<SharedResource> _t;
    public DashboardController(OverviewBuilder overview, DashboardExportService export, AppDbContext db, IHtmlLocalizer<SharedResource> t)
    { _overview = overview; _export = export; _db = db; _t = t; }

    public async Task<IActionResult> Index(int? snapshotId, CancellationToken ct = default)
    {
        var vm = await _overview.BuildAsync(await _db.ResolveSnapshotAsync(HttpContext, snapshotId, ct), ct);
        return View(vm);
    }

    public async Task<IActionResult> Export(int? snapshotId, CancellationToken ct = default)
    {
        var vm = await _overview.BuildAsync(await _db.ResolveSnapshotAsync(HttpContext, snapshotId, ct), ct);
        bool isRtl = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
        var bytes = _export.Build(vm, key => _t[key].Value, isRtl);
        var name = "dashboard-" + (vm.Snapshot?.Label ?? "export") + ".xlsx";
        return File(bytes, Xlsx, name);
    }

    // Breakdown: who the special-category & disabled employees are, and how much they add to the Saudi count.
    public async Task<IActionResult> Weighting(int? snapshotId, CancellationToken ct = default)
    {
        var vm = await _overview.BuildAsync(await _db.ResolveSnapshotAsync(HttpContext, snapshotId, ct), ct, full: false);
        int sid = vm.SnapshotId;

        var special = await _db.Employees
            .Where(e => e.SnapshotId == sid && e.CountsAsSaudiSpecial)
            .Select(e => new WeightRow
            {
                Name = e.Name,
                IqamaNo = e.IqamaNo,
                Info = e.SpecialCategory,
                Registration = e.Facility!.Record!.RegistrationLabel,
                Occupation = e.OccupationRaw
            })
            .OrderBy(x => x.Registration).ThenBy(x => x.Name).ToListAsync(ct);

        var disabled = await _db.Employees
            .Where(e => e.SnapshotId == sid && e.DisabilityCountAs > 1)
            .Select(e => new WeightRow
            {
                Name = e.Name,
                IqamaNo = e.IqamaNo,
                Weight = e.DisabilityCountAs,
                Registration = e.Facility!.Record!.RegistrationLabel,
                Occupation = e.OccupationRaw
            })
            .OrderBy(x => x.Registration).ThenBy(x => x.Name).ToListAsync(ct);

        var cap = await _db.Settings.Where(s => s.Id == 1).Select(s => (decimal?)s.DisabilityCapPct).FirstOrDefaultAsync(ct) ?? 0.10m;

        return View(new WeightingViewModel
        {
            SnapshotId = sid,
            Snapshot = vm.Snapshot,
            Snapshots = vm.Snapshots,
            Records = vm.Records,
            Special = special,
            Disabled = disabled,
            DisabilityCapPct = cap
        });
    }
}

public class WeightRow
{
    public string Name { get; set; } = "";
    public string? IqamaNo { get; set; }
    public string? Info { get; set; }          // special category label
    public int Weight { get; set; }            // disability CountAs (×N)
    public string? Registration { get; set; }
    public string? Occupation { get; set; }
}

public class WeightingViewModel
{
    public int SnapshotId { get; set; }
    public Snapshot? Snapshot { get; set; }
    public List<Snapshot> Snapshots { get; set; } = new();
    public List<RecordColumn> Records { get; set; } = new();
    public List<WeightRow> Special { get; set; } = new();
    public List<WeightRow> Disabled { get; set; } = new();
    public decimal DisabilityCapPct { get; set; }
}
