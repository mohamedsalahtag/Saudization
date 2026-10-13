using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;
using Saudization.Web.Models;

namespace Saudization.Web.Controllers;

public class RecordsController : Controller
{
    private readonly AppDbContext _db;
    public RecordsController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var latest = await LatestSnapshotIdAsync(ct);   // counts reflect the most recent version, not every version combined
        var records = await _db.Records
            .OrderBy(r => r.RegistrationLabel)
            .Select(r => new RecordListItem
            {
                Record = r,
                Facilities = r.Facilities.Count,
                Employees = r.Facilities.SelectMany(f => f.Employees).Count(e => e.SnapshotId == latest)
            })
            .ToListAsync(ct);
        return View(records);
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var rec = await _db.Records.Include(r => r.Facilities).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rec is null) return NotFound();

        var latest = await LatestSnapshotIdAsync(ct);
        ViewBag.EmpCounts = await _db.Employees
            .Where(e => e.SnapshotId == latest && e.Facility!.RecordId == id)
            .GroupBy(e => e.FacilityId).Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C, ct);
        return View(rec);
    }

    private Task<int> LatestSnapshotIdAsync(CancellationToken ct) => _db.ResolveSnapshotAsync(HttpContext, null, ct);

    public IActionResult Create() => View(new Record());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Record model, CancellationToken ct)
    {
        ValidateThresholds(model);
        if (!ModelState.IsValid) return View(model);
        _db.Records.Add(model);
        await _db.SaveChangesAsync(ct);
        TempData["ok"] = "Record created.";
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var rec = await _db.Records.FindAsync([id], ct);
        return rec is null ? NotFound() : View(rec);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Record model, CancellationToken ct)
    {
        if (id != model.Id) return BadRequest();
        ValidateThresholds(model);
        if (!ModelState.IsValid) return View(model);
        var rec = await _db.Records.FindAsync([id], ct);
        if (rec is null) return NotFound();
        rec.RegistrationLabel = model.RegistrationLabel;
        rec.RegistrationNumber = model.RegistrationNumber;
        rec.NameAr = model.NameAr;
        rec.NameEn = model.NameEn;
        rec.RequiredPct = model.RequiredPct;
        rec.LowGreenMin = model.LowGreenMin;
        rec.MediumGreenMin = model.MediumGreenMin;
        rec.HighGreenMin = model.HighGreenMin;
        rec.PlatinumMin = model.PlatinumMin;
        rec.RedColor = model.RedColor;
        rec.LowGreenColor = model.LowGreenColor;
        rec.MediumGreenColor = model.MediumGreenColor;
        rec.HighGreenColor = model.HighGreenColor;
        rec.PlatinumColor = model.PlatinumColor;
        await _db.SaveChangesAsync(ct);
        TempData["ok"] = "Record saved.";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Thresholds are Saudi-ratio fractions. A value typed as a percentage (20 instead of 0.20) is converted,
    /// and the zones must ascend (Low ≤ Medium ≤ High ≤ Platinum ≤ 1) or ZoneFor would silently skip zones.</summary>
    private void ValidateThresholds(Record m)
    {
        static decimal Frac(decimal v) => v > 1m && v <= 100m ? v / 100m : v;
        m.RequiredPct = Frac(m.RequiredPct);
        m.LowGreenMin = Frac(m.LowGreenMin); m.MediumGreenMin = Frac(m.MediumGreenMin);
        m.HighGreenMin = Frac(m.HighGreenMin); m.PlatinumMin = Frac(m.PlatinumMin);
        foreach (var (name, v) in new[] { (nameof(Record.RequiredPct), m.RequiredPct), (nameof(Record.LowGreenMin), m.LowGreenMin),
                     (nameof(Record.MediumGreenMin), m.MediumGreenMin), (nameof(Record.HighGreenMin), m.HighGreenMin), (nameof(Record.PlatinumMin), m.PlatinumMin) })
            if (v < 0m || v > 1m) ModelState.AddModelError(name, "Enter a fraction between 0 and 1 (e.g. 0.20 for 20%).");
        if (!(m.LowGreenMin <= m.MediumGreenMin && m.MediumGreenMin <= m.HighGreenMin && m.HighGreenMin <= m.PlatinumMin))
            ModelState.AddModelError(nameof(Record.LowGreenMin), "Zone thresholds must ascend: Low green ≤ Medium green ≤ High green ≤ Platinum.");
        // the converted values must be the ones re-displayed / saved
        foreach (var k in new[] { nameof(Record.RequiredPct), nameof(Record.LowGreenMin), nameof(Record.MediumGreenMin), nameof(Record.HighGreenMin), nameof(Record.PlatinumMin) })
            if (ModelState[k]?.Errors.Count == 0) ModelState.Remove(k);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var rec = await _db.Records.Include(r => r.Facilities).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rec is null) return RedirectToAction(nameof(Index));
        // Never erase history: refuse while any version (including deleted ones) still has employees here.
        var emps = await _db.Employees.IgnoreQueryFilters().Where(e => e.Facility!.RecordId == id)
            .GroupBy(e => e.SnapshotId).Select(g => g.Count()).ToListAsync(ct);
        if (emps.Count > 0)
        {
            TempData["err"] = $"“{rec.RegistrationLabel}” cannot be deleted: {emps.Sum()} employee rows in {emps.Count} version(s) belong to it. " +
                              "Move its facilities to another registration, or delete those versions permanently first.";
            return RedirectToAction(nameof(Details), new { id });
        }
        _db.Facilities.RemoveRange(rec.Facilities);
        _db.Records.Remove(rec);
        await _db.SaveChangesAsync(ct);
        TempData["ok"] = "Record deleted.";
        return RedirectToAction(nameof(Index));
    }
}

public class RecordListItem
{
    public Record Record { get; set; } = null!;
    public int Facilities { get; set; }
    public int Employees { get; set; }
}
