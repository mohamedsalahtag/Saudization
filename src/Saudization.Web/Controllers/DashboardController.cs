using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using Saudization.Web.Services;

namespace Saudization.Web.Controllers;

/// <summary>Landing overview: KPIs, findings/issues, suggestions and charts. The detailed pivot lives in SaudizationController.</summary>
public class DashboardController : Controller
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private readonly OverviewBuilder _overview;
    private readonly DashboardExportService _export;
    private readonly IHtmlLocalizer<SharedResource> _t;
    public DashboardController(OverviewBuilder overview, DashboardExportService export, IHtmlLocalizer<SharedResource> t)
    { _overview = overview; _export = export; _t = t; }

    public async Task<IActionResult> Index(int? snapshotId, CancellationToken ct = default)
    {
        var vm = await _overview.BuildAsync(snapshotId, ct);
        return View(vm);
    }

    public async Task<IActionResult> Export(int? snapshotId, CancellationToken ct = default)
    {
        var vm = await _overview.BuildAsync(snapshotId, ct);
        bool isRtl = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
        var bytes = _export.Build(vm, key => _t[key].Value, isRtl);
        var name = "dashboard-" + (vm.Snapshot?.Label ?? "export") + ".xlsx";
        return File(bytes, Xlsx, name);
    }
}
