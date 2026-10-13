using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Saudization.Web.Data;

/// <summary>
/// Resolving and setting the "default version" (snapshot) shown across the app. The default is the
/// snapshot flagged <c>IsDefault</c> (normally the most recently uploaded); if none is flagged the app
/// falls back to the most recent by week then id, so behaviour never breaks on a fresh database.
/// </summary>
public static class SnapshotExtensions
{
    /// <summary>Id of the default version, or 0 when there are no versions.</summary>
    public static async Task<int> DefaultSnapshotIdAsync(this AppDbContext db, CancellationToken ct = default)
    {
        var flagged = await db.Snapshots.Where(s => s.IsDefault).OrderByDescending(s => s.Id)
            .Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);
        return flagged ?? await db.Snapshots.OrderByDescending(s => s.WeekDate).ThenByDescending(s => s.Id)
            .Select(s => s.Id).FirstOrDefaultAsync(ct);
    }

    private const string SelCookie = "selSnapshot";

    /// <summary>The version a page should show, shared across all version-based pages: an explicit selection
    /// wins and is remembered (session cookie) so it carries to the next page; otherwise the last remembered
    /// selection is used; otherwise the global default. Returns 0 when there are no versions.</summary>
    public static async Task<int> ResolveSnapshotAsync(this AppDbContext db, HttpContext http, int? requested, CancellationToken ct = default)
    {
        if (requested is int r && r > 0 && await db.Snapshots.AnyAsync(s => s.Id == r, ct))
        {
            http.Response.Cookies.Append(SelCookie, r.ToString(),
                new CookieOptions { HttpOnly = true, IsEssential = true, SameSite = SameSiteMode.Lax });
            return r;
        }
        if (int.TryParse(http.Request.Cookies[SelCookie], out var c) && c > 0
            && await db.Snapshots.AnyAsync(s => s.Id == c, ct))
            return c;
        return await db.DefaultSnapshotIdAsync(ct);
    }

    /// <summary>Forget the shared selection so the caller falls back to the global default (used after an upload).</summary>
    public static void ClearSelectedSnapshot(this HttpContext http) => http.Response.Cookies.Delete(SelCookie);

    /// <summary>Make <paramref name="id"/> the single default version (clears the flag on all others).</summary>
    public static async Task SetDefaultSnapshotAsync(this AppDbContext db, int id, CancellationToken ct = default)
    {
        await db.Snapshots.Where(s => s.IsDefault && s.Id != id)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.IsDefault, false), ct);
        await db.Snapshots.Where(s => s.Id == id)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.IsDefault, true), ct);
    }
}
