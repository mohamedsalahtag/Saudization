using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;
using Saudization.Web.Services;

namespace Saudization.Web.Controllers;

/// <summary>
/// Login / logout. Access is a <b>whitelist</b>: the user must exist in <see cref="AppUser"/> and be
/// active, AND their password must verify against Active Directory (bind-only). There are no roles and
/// no self-registration — new users are added on the /Users admin page. See user-management-pack SPEC.md.
/// </summary>
[AllowAnonymous]
public class AccountController : Controller
{
    private readonly AppDbContext _db;
    private readonly AdService _ad;
    private readonly ILogger<AccountController> _logger;

    public AccountController(AppDbContext db, AdService ad, ILogger<AccountController> logger)
    {
        _db = db; _ad = ad; _logger = logger;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Dashboard");
        ViewBag.ReturnUrl = returnUrl;
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        ViewBag.ReturnUrl = model.ReturnUrl;
        if (!ModelState.IsValid) return View(model);

        // Accept "user", "user@domain" (UPN) or "DOMAIN\user" — reduce to the bare sAMAccountName.
        var name = Normalize(model.Username);

        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.AdUsername == name);
        if (user == null)
        {
            _logger.LogWarning("Login denied: '{Name}' is not in the whitelist.", name);
            ModelState.AddModelError("", "Invalid credentials, or your account is not registered.");
            return View(model);
        }
        if (!user.IsActive)
        {
            ModelState.AddModelError("", "Your account has been disabled. Contact the IT admin.");
            return View(model);
        }

        // Password check is AD bind-only — there is no local password.
        var ad = _ad.Authenticate(name, model.Password);
        if (ad == null)
        {
            ModelState.AddModelError("", "Invalid credentials.");
            return View(model);
        }

        // Refresh cached profile from AD (best-effort; already have a row).
        if (!string.IsNullOrWhiteSpace(ad.FullName)) user.FullName = ad.FullName;
        if (!string.IsNullOrWhiteSpace(ad.Email)) user.Email = ad.Email;
        if (!string.IsNullOrWhiteSpace(ad.Department)) user.Department = ad.Department;
        user.LastLogin = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.AdUsername),
            new(ClaimTypes.GivenName, user.FullName),
            new(ClaimTypes.Email, user.Email ?? ""),
            new("Department", user.Department ?? "")
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        // Always keep users signed in for a long time (persistent cookie, renewed on each visit by sliding expiration).
        var props = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity), props);

        _logger.LogInformation("Login OK: {Name}", user.AdUsername);

        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return Redirect(model.ReturnUrl);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    private static string Normalize(string? raw)
    {
        var u = (raw ?? "").Trim();
        var at = u.IndexOf('@'); if (at > 0) u = u[..at];
        var bs = u.LastIndexOf('\\'); if (bs >= 0) u = u[(bs + 1)..];
        return u;
    }
}

public class LoginViewModel
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}
