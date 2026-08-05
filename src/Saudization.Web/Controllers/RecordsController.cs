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

    private async Task<int> LatestSnapshotIdAsync(CancellationToken ct) =>
        await _db.Snapshots.OrderByDescending(s => s.WeekDate).ThenByDescending(s => s.Id)
            .Select(s => s.Id).FirstOrDefaultAsync(ct);

    public IActionResult Create() => View(new Record());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Record model, CancellationToken ct)
    {
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

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var rec = await _db.Records.FindAsync([id], ct);
        if (rec is not null) { _db.Records.Remove(rec); await _db.SaveChangesAsync(ct); TempData["ok"] = "Record deleted."; }
        return RedirectToAction(nameof(Index));
    }
}

public class RecordListItem
{
    public Record Record { get; set; } = null!;
    public int Facilities { get; set; }
    public int Employees { get; set; }
}
