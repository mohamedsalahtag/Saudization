using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;

namespace Saudization.Web.Controllers;

public class AuditController : Controller
{
    private readonly AppDbContext _db;
    public AuditController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index(string? entity, int take = 300, CancellationToken ct = default)
    {
        var q = _db.AuditLogs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(entity)) q = q.Where(a => a.EntityName == entity);
        ViewBag.Entities = await _db.AuditLogs.Select(a => a.EntityName).Distinct().OrderBy(x => x).ToListAsync(ct);
        ViewBag.Entity = entity;
        var rows = await q.OrderByDescending(a => a.Id).Take(Math.Clamp(take, 1, 2000)).ToListAsync(ct);
        return View(rows);
    }
}
