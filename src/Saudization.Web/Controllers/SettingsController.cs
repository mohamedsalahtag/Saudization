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
        var s = await _db.Settings.FirstOrDefaultAsync(ct) ?? new Settings { Id = 1 };
        return View(s);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(Settings model, CancellationToken ct)
    {
        var s = await _db.Settings.FirstOrDefaultAsync(ct);
        if (s is null) { s = new Settings { Id = 1 }; _db.Settings.Add(s); }
        s.DisabilityCapPct = model.DisabilityCapPct;
        s.LightGreenBand = model.LightGreenBand;
        await _db.SaveChangesAsync(ct);
        TempData["ok"] = "Settings saved.";
        return RedirectToAction(nameof(Index));
    }
}
