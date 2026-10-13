using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.EntityFrameworkCore;
using Saudization.Web;
using Saudization.Web.Data;
using Saudization.Web.Models;
using Saudization.Web.Services;

namespace Saudization.Web.Controllers;

/// <summary>
/// The login whitelist. Any signed-in user may manage it (there are no roles). Adding a user here
/// lets that AD account sign in; their password is always checked against AD at login time.
/// Self-protection: you cannot disable or delete your own account (SPEC.md §6).
/// </summary>
[Authorize]
public class UsersController : Controller
{
    private readonly AppDbContext _db;
    private readonly AdService _ad;
    private readonly IHtmlLocalizer<SharedResource> T;
    public UsersController(AppDbContext db, AdService ad, IHtmlLocalizer<SharedResource> t) { _db = db; _ad = ad; T = t; }

    private string CurrentUsername => User.FindFirstValue(ClaimTypes.Name) ?? "";

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var users = await _db.AppUsers.OrderBy(u => u.FullName).ToListAsync();
        ViewBag.Me = CurrentUsername;
        return View(users);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var u = await _db.AppUsers.FindAsync(id);
        if (u == null) return RedirectToAction(nameof(Index));
        if (u.AdUsername == CurrentUsername)
        {
            TempData["err"] = T["Users_ErrSelf"].Value;
            return RedirectToAction(nameof(Index));
        }
        u.IsActive = !u.IsActive;   // flip server-side; never trust a posted bool (SPEC.md §6)
        await _db.SaveChangesAsync();
        TempData["ok"] = string.Format(u.IsActive ? T["Users_Enabled"].Value : T["Users_Disabled"].Value, u.AdUsername);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var u = await _db.AppUsers.FindAsync(id);
        if (u == null) return RedirectToAction(nameof(Index));
        if (u.AdUsername == CurrentUsername)
        {
            TempData["err"] = T["Users_ErrSelf"].Value;
            return RedirectToAction(nameof(Index));
        }
        _db.AppUsers.Remove(u);
        await _db.SaveChangesAsync();
        TempData["ok"] = string.Format(T["Users_Deleted"].Value, u.AdUsername);
        return RedirectToAction(nameof(Index));
    }

    // ---- Add from Active Directory (browse + one-click / bulk import) ---------------------------

    [HttpGet]
    public async Task<IActionResult> AdBrowse(bool refresh = false, string? q = null)
    {
        if (!_ad.IsConfigured)
        {
            TempData["err"] = T["AdBrowse_NotConfigured"].Value;
            return RedirectToAction(nameof(Index));
        }

        var adUsers = FilterAd(_ad.GetAllUsers(refresh), q);

        var registered = await _db.AppUsers
            .Select(u => u.AdUsername)
            .ToListAsync();
        ViewBag.Registered = registered
            .Select(n => n.ToLowerInvariant())
            .ToHashSet();
        ViewBag.Query = q;
        ViewBag.TotalFetched = _ad.GetAllUsers(false).Count;
        return View(adUsers);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFromAd(string adUsername)
    {
        var name = (adUsername ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return RedirectToAction(nameof(AdBrowse));

        if (await _db.AppUsers.AnyAsync(u => u.AdUsername == name))
        {
            TempData["err"] = string.Format(T["Users_ErrExists"].Value, name);
            return RedirectToAction(nameof(AdBrowse));
        }

        var ad = _ad.GetAllUsers(false).FirstOrDefault(u =>
            u.Username.Equals(name, StringComparison.OrdinalIgnoreCase));
        _db.AppUsers.Add(new AppUser
        {
            AdUsername = name,
            FullName = string.IsNullOrWhiteSpace(ad?.FullName) ? name : ad!.FullName,
            Email = ad?.Email,
            Department = ad?.Department,
            IsActive = true,
            CreatedByUser = CurrentUsername
        });
        await _db.SaveChangesAsync();
        TempData["ok"] = string.Format(T["Users_Added"].Value, name);
        return RedirectToAction(nameof(AdBrowse));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MassCreate(string? q)
    {
        // Same filter as the page the button is on — never add more than the list the admin is looking at.
        var adUsers = FilterAd(_ad.GetAllUsers(false), q).Where(u => u.Enabled).ToList();
        if (adUsers.Count == 0)
        {
            TempData["err"] = T["AdBrowse_NoneFetched"].Value;
            return RedirectToAction(nameof(AdBrowse), new { q });
        }

        var existing = (await _db.AppUsers.Select(u => u.AdUsername).ToListAsync())
            .Select(n => n.ToLowerInvariant()).ToHashSet();

        int added = 0;
        foreach (var u in adUsers)
        {
            if (existing.Contains(u.Username.ToLowerInvariant())) continue;
            _db.AppUsers.Add(new AppUser
            {
                AdUsername = u.Username,
                FullName = string.IsNullOrWhiteSpace(u.FullName) ? u.Username : u.FullName,
                Email = u.Email,
                Department = u.Department,
                IsActive = true,
                CreatedByUser = CurrentUsername
            });
            existing.Add(u.Username.ToLowerInvariant());
            added++;
        }
        if (added > 0) await _db.SaveChangesAsync();
        TempData["ok"] = string.Format(T["AdBrowse_MassResult"].Value, added);
        return RedirectToAction(nameof(AdBrowse), new { q });
    }

    private static List<AdUser> FilterAd(List<AdUser> users, string? q)
    {
        if (string.IsNullOrWhiteSpace(q)) return users;
        var needle = q.Trim();
        return users.Where(u =>
            u.Username.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
            u.FullName.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
            (u.Email ?? "").Contains(needle, StringComparison.OrdinalIgnoreCase) ||
            (u.Department ?? "").Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
