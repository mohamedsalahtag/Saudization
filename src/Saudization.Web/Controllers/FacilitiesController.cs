using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;
using Saudization.Web.Models;

namespace Saudization.Web.Controllers;

public class FacilitiesController : Controller
{
    private readonly AppDbContext _db;
    public FacilitiesController(AppDbContext db) => _db = db;

    public IActionResult Create(int recordId) => View(new Facility { RecordId = recordId });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Facility model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);
        _db.Facilities.Add(model);
        await _db.SaveChangesAsync(ct);
        TempData["ok"] = "Facility created.";
        return RedirectToAction("Details", "Records", new { id = model.RecordId });
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var f = await _db.Facilities.FindAsync([id], ct);
        return f is null ? NotFound() : View(f);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Facility model, CancellationToken ct)
    {
        if (id != model.Id) return BadRequest();
        if (!ModelState.IsValid) return View(model);
        var f = await _db.Facilities.FindAsync([id], ct);
        if (f is null) return NotFound();
        f.Code = model.Code; f.NameAr = model.NameAr; f.Location = model.Location;
        await _db.SaveChangesAsync(ct);
        TempData["ok"] = "Facility saved.";
        return RedirectToAction("Details", "Records", new { id = f.RecordId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var f = await _db.Facilities.FindAsync([id], ct);
        if (f is null) return NotFound();
        var recordId = f.RecordId;
        var emps = await _db.Employees.IgnoreQueryFilters().Where(e => e.FacilityId == id)
            .GroupBy(e => e.SnapshotId).Select(g => g.Count()).ToListAsync(ct);
        if (emps.Count > 0)
        {
            TempData["err"] = $"Facility “{f.Code}” cannot be deleted: {emps.Sum()} employee rows in {emps.Count} version(s) belong to it.";
            return RedirectToAction("Details", "Records", new { id = recordId });
        }
        _db.Facilities.Remove(f);
        await _db.SaveChangesAsync(ct);
        TempData["ok"] = "Facility deleted.";
        return RedirectToAction("Details", "Records", new { id = recordId });
    }
}
