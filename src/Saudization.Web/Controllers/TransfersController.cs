using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;
using Saudization.Web.Models;
using Saudization.Web.Services;

namespace Saudization.Web.Controllers;

public class TransfersController : Controller
{
    private readonly TransferAdvisor _advisor;
    private readonly AppDbContext _db;
    public TransfersController(TransferAdvisor advisor, AppDbContext db) { _advisor = advisor; _db = db; }

    public async Task<IActionResult> Index(int? snapshotId, CancellationToken ct)
    {
        var sid = snapshotId ?? await _db.Snapshots
            .OrderByDescending(s => s.WeekDate).ThenByDescending(s => s.Id).Select(s => s.Id).FirstOrDefaultAsync(ct);
        var vm = new TransfersViewModel
        {
            SnapshotId = sid,
            Snapshot = await _db.Snapshots.FindAsync(new object[] { sid }, ct),
            Suggestions = await _advisor.SuggestAsync(sid, ct)
        };
        return View(vm);
    }
}
