using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;
using Saudization.Web.Models;

namespace Saudization.Web.Controllers;

public class SettingsController : Controller
{
    private readonly AppDbContext _db;
    public SettingsController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var s = await _db.Settings.FirstOrDefaultAsync(x => x.Id == 1, ct) ?? new Settings { Id = 1 };
        return View(s);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(Settings model, CancellationToken ct)
    {
        // A value that failed to parse (e.g. "0.10" under the Arabic culture) binds as 0 — never save that.
        if (!ModelState.IsValid || model.DisabilityCapPct is < 0 or > 1 || model.LightGreenBand is < 0 or > 1)
        {
            TempData["err"] = "Settings not saved: enter the percentages as fractions between 0 and 1 (e.g. 0.10).";
            return RedirectToAction(nameof(Index));
        }
        var s = await _db.Settings.FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (s is null) { s = new Settings { Id = 1 }; _db.Settings.Add(s); }
        s.DisabilityCapPct = model.DisabilityCapPct;
        s.LightGreenBand = model.LightGreenBand;
        s.DisabilityDefaultWeight = model.DisabilityDefaultWeight < 1 ? 1 : model.DisabilityDefaultWeight;
        s.ShowNationalityIcons = model.ShowNationalityIcons;
        await _db.SaveChangesAsync(ct);
        TempData["ok"] = "Settings saved.";
        return RedirectToAction(nameof(Index));
    }
}
