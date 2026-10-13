using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;
using Saudization.Web.Models;

namespace Saudization.Web.Controllers;

public class EmployeesController : Controller
{
    private readonly AppDbContext _db;
    public EmployeesController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index(int facilityId, int? snapshotId, string? q, CancellationToken ct)
    {
        var fac = await _db.Facilities.Include(f => f.Record).FirstOrDefaultAsync(f => f.Id == facilityId, ct);
        if (fac is null) return NotFound();

        var sid = await _db.ResolveSnapshotAsync(HttpContext, snapshotId, ct);

        var query = _db.Employees
            .Include(e => e.Nationality).Include(e => e.Occupation)
            .Where(e => e.FacilityId == facilityId && e.SnapshotId == sid);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(e => e.Name.Contains(q) || (e.WorkerNo != null && e.WorkerNo.Contains(q)));

        ViewBag.Facility = fac;
        ViewBag.Query = q;
        ViewBag.SnapshotId = sid;
        ViewBag.Snapshots = await _db.Snapshots.OrderByDescending(s => s.WeekDate).ThenByDescending(s => s.Id).ToListAsync(ct);
        var list = await query.OrderBy(e => e.Name).Take(1000).ToListAsync(ct);
        return View(list);
    }

    public async Task<IActionResult> Create(int facilityId, int? snapshotId, CancellationToken ct)
    {
        var fac = await _db.Facilities.FindAsync([facilityId], ct);
        if (fac is null) return NotFound();
        await PopulateLookups(ct);
        ViewBag.Facility = fac;
        return View(new Employee { FacilityId = facilityId, SnapshotId = snapshotId ?? await LatestSnapshotIdAsync(ct) });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Employee model, string? nationalityName, CancellationToken ct)
    {
        await ResolveRelations(model, nationalityName, ct);
        if (!ModelState.IsValid)
        {
            await PopulateLookups(ct);
            ViewBag.Facility = await _db.Facilities.FindAsync([model.FacilityId], ct);
            return View(model);
        }
        if (model.SnapshotId == 0) model.SnapshotId = await LatestSnapshotIdAsync(ct);
        _db.Employees.Add(model);
        await _db.SaveChangesAsync(ct);
        TempData["ok"] = "Employee added.";
        return RedirectToAction(nameof(Index), new { facilityId = model.FacilityId, snapshotId = model.SnapshotId });
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var e = await _db.Employees.Include(x => x.Facility).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound();
        await PopulateLookups(ct);
        ViewBag.Facility = e.Facility;
        return View(e);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Employee model, string? nationalityName, CancellationToken ct)
    {
        if (id != model.Id) return BadRequest();
        var e = await _db.Employees.FindAsync([id], ct);
        if (e is null) return NotFound();

        await ResolveRelations(model, nationalityName, ct);
        if (!ModelState.IsValid)
        {
            await PopulateLookups(ct);
            ViewBag.Facility = await _db.Facilities.FindAsync([model.FacilityId], ct);
            return View(model);
        }

        e.WorkerNo = model.WorkerNo; e.Name = model.Name;
        e.NationalityId = model.NationalityId; e.Location = model.Location;
        e.BorderNo = model.BorderNo; e.IqamaNo = model.IqamaNo;
        e.OccupationId = model.OccupationId; e.OccupationRaw = model.OccupationRaw;
        e.IqamaExpiry = model.IqamaExpiry; e.EntryDate = model.EntryDate; e.WorkerType = model.WorkerType;
        // SnapshotId is intentionally not changed on edit — an employee stays in its version.
        await _db.SaveChangesAsync(ct);
        TempData["ok"] = "Employee saved.";
        return RedirectToAction(nameof(Index), new { facilityId = e.FacilityId, snapshotId = e.SnapshotId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var e = await _db.Employees.FindAsync([id], ct);
        if (e is null) return NotFound();
        var facilityId = e.FacilityId;
        var snapshotId = e.SnapshotId;
        _db.Employees.Remove(e);
        await _db.SaveChangesAsync(ct);
        TempData["ok"] = "Employee deleted.";
        return RedirectToAction(nameof(Index), new { facilityId, snapshotId });
    }

    // Resolve occupation (by typed name) and nationality (by typed name, creating if new).
    private async Task ResolveRelations(Employee model, string? nationalityName, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(model.OccupationRaw))
        {
            var occ = await _db.Occupations.FirstOrDefaultAsync(o => o.NameAr == model.OccupationRaw, ct);
            model.OccupationId = occ?.Id;
        }
        else model.OccupationId = null;

        if (!string.IsNullOrWhiteSpace(nationalityName))
        {
            var nat = await _db.Nationalities.FirstOrDefaultAsync(n => n.NameAr == nationalityName, ct);
            if (nat is null)
            {
                nat = new Nationality { NameAr = nationalityName.Trim(), IsSaudi = nationalityName.Contains("سعود") };
                _db.Nationalities.Add(nat);
                await _db.SaveChangesAsync(ct);
            }
            model.NationalityId = nat.Id;
        }
        model.Nationality = null;
        model.Occupation = null;
        model.Facility = null;
        model.Snapshot = null;
        ModelState.Remove(nameof(Employee.Nationality));
        ModelState.Remove(nameof(Employee.Occupation));
        ModelState.Remove(nameof(Employee.Facility));
        ModelState.Remove(nameof(Employee.Snapshot));
    }

    private Task<int> LatestSnapshotIdAsync(CancellationToken ct) => _db.ResolveSnapshotAsync(HttpContext, null, ct);

    private async Task PopulateLookups(CancellationToken ct)
    {
        ViewBag.Nationalities = await _db.Nationalities.OrderBy(n => n.NameAr).ToListAsync(ct);
        ViewBag.Occupations = await _db.Occupations.OrderBy(o => o.NameAr).Select(o => o.NameAr).ToListAsync(ct);
    }
}
