using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;
using Saudization.Web.Models;
using Saudization.Web.Services;

namespace Saudization.Web.Controllers;

public class SimulatorController : Controller
{
    private readonly NitaqatCalculator _calc;
    private readonly AppDbContext _db;
    public SimulatorController(NitaqatCalculator calc, AppDbContext db) { _calc = calc; _db = db; }

    public async Task<IActionResult> Index(int? snapshotId, CancellationToken ct)
    {
        var sid = snapshotId ?? await _db.Snapshots
            .OrderByDescending(s => s.WeekDate).ThenByDescending(s => s.Id).Select(s => s.Id).FirstOrDefaultAsync(ct);

        var vm = await _calc.BuildAsync(DashboardMode.Current, null, sid, ct);
        var recEntities = await _db.Records.ToDictionaryAsync(r => r.Id, ct);
        var settings = await _calc.GetSettingsAsync(ct);

        var model = new SimulatorViewModel
        {
            SnapshotId = sid,
            Snapshot = await _db.Snapshots.FindAsync(new object[] { sid }, ct),
            LightGreenBand = settings.LightGreenBand,
            Records = vm.Records.Select(rc =>
            {
                var r = recEntities[rc.RecordId];
                return new SimRecord
                {
                    Id = rc.RecordId,
                    Label = rc.Label,
                    Saudi = rc.SaudiCountedGeneral,                    // weighted (incl. special + capped disability)
                    NonSaudi = rc.Headcount - rc.SaudiCountedGeneral,  // keeps Total == actual headcount
                    Low = r.LowGreenMin,
                    Medium = r.MediumGreenMin,
                    High = r.HighGreenMin,
                    Platinum = r.PlatinumMin
                };
            }).ToList()
        };

        // per (category × registration) occupation cells, for the "by job" simulation
        foreach (var row in vm.Rows)
        {
            if (row.IsUnclassified) continue;
            var jc = new SimCategory { Id = row.CategoryId ?? 0, Name = row.NameAr, Required = row.RequiredPct, MinForCalc = row.MinForCalc };
            foreach (var rc in vm.Records)
            {
                var cell = row.ByRecord.TryGetValue(rc.RecordId, out var c) ? c : new Cell();
                jc.Cells.Add(new SimJobCell { RecordId = rc.RecordId, Saudi = cell.Saudi, NonSaudi = cell.NonSaudi });
            }
            model.Categories.Add(jc);
        }

        return View(model);
    }
}
