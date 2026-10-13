// AccountController - login, logout, setup, profile picture upload.
// Reference: ASP.NET Core 9 cookie auth + AD bind + BCrypt local fallback.
//
// Hard rules from SPEC.md §6:
//   - No self-registration UI; never link to /Account/Register from the login page.
//   - Login uses AD bind-only (no LDAP search). Fall back to BCrypt local hash.
//   - Auto-create-on-login is gated by SiteConfiguration.AutoCreateAdUsers.
//   - Login accepts a bare username, a UPN (user@domain), or DOMAIN\user — all
//     normalized to the bare sAMAccountName before lookup/bind.
//   - There is NO seeded admin. The first SiteAdmin is created via the
//     loopback-only /Account/Setup below, or by a one-row DB insert.

using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HelpDesk.Models;
using HelpDesk.Services;

namespace HelpDesk.Controllers;

public class AccountController : Controller
{
    private readonly IDbService _db;
    private readonly IAdService _ad;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AccountController> _logger;

    public AccountController(IDbService db, IAdService ad,
        IWebHostEnvironment env, ILogger<AccountController> logger)
    {
        _db = db; _ad = ad; _env = env; _logger = logger;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    // -------------------------------------------------------------------
    // POST /Account/Login - the algorithm in SPEC.md §2.1.
    // -------------------------------------------------------------------
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        // Accept bare username, UPN (user@domain), or DOMAIN\user — normalize first.
        var loginUser = NormalizeUsername(model.Username);
        var user = await _db.GetUserByUsernameAsync(loginUser);

        // Auto-create unknown AD user (gated by SiteAdmin toggle)
        if (user == null)
        {
            var autoCreate = (await _db.GetConfigAsync("AutoCreateAdUsers") ?? "false") == "true";
            var adDomain   = await _db.GetConfigAsync("AdDomain") ?? "";
            var adLdapPath = await _db.GetConfigAsync("AdLdapPath") ?? "";
            var adReady    = !string.IsNullOrWhiteSpace(adDomain) && !string.IsNullOrWhiteSpace(adLdapPath);

            if (autoCreate && adReady)
            {
                var adUser = await _ad.AuthenticateAsync(loginUser, model.Password);
                if (adUser != null)
                {
                    var sam = loginUser;   // already normalized to the bare sAMAccountName
                    var newUser = new User
                    {
                        EmployeeId   = "",
                        AdUsername   = sam,
                        FullName     = string.IsNullOrWhiteSpace(adUser.FullName) ? sam : adUser.FullName,
                        Email        = adUser.Email ?? "",
                        Department   = adUser.Department ?? "",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password),
                        Role         = UserRoles.Requester,
                        IsActive     = true
                    };
                    var newId = await _db.CreateUserAsync(newUser);
                    newUser.UserId = newId;
                    user = newUser;
                    _logger.LogInformation("Auto-created AD user '{Sam}' as Requester (UserId={Id})", sam, newId);
                }
            }

            if (user == null)
            {
                ModelState.AddModelError("", "Invalid credentials or account not registered.");
                return View(model);
            }
        }

        if (!user.IsActive)
        {
            ModelState.AddModelError("", "Your account has been disabled. Contact the IT admin.");
            return View(model);
        }

        // Verify password - AD first, then BCrypt local hash.
        bool authenticated = false;
        if (_ad.IsConfigured)
        {
            var adUser = await _ad.AuthenticateAsync(loginUser, model.Password);
            authenticated = adUser != null;
        }
        if (!authenticated)
            authenticated = BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash);

        if (!authenticated)
        {
            ModelState.AddModelError("", "Invalid credentials.");
            return View(model);
        }

        // Update last-login + online flag
        user.LastLogin = DateTime.UtcNow;
        await _db.UpdateLastSeenAsync(user.UserId);
        await _db.SetUserOnlineAsync(user.UserId, true);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name,           user.AdUsername),
            new(ClaimTypes.GivenName,      user.FullName),
            new(ClaimTypes.Email,          user.Email),
            new(ClaimTypes.Role,           user.Role),
            new("Department",              user.Department ?? ""),
            new("EmployeeId",              user.EmployeeId)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var props = new AuthenticationProperties
        {
            IsPersistent = model.RememberMe,
            ExpiresUtc   = model.RememberMe
                ? DateTimeOffset.UtcNow.AddDays(30)
                : DateTimeOffset.UtcNow.AddHours(8)
        };

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity), props);

        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return Redirect(model.ReturnUrl);
        return RedirectToAction("Index", "Home");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var userId = GetUserId();
        if (userId > 0) await _db.SetUserOnlineAsync(userId, false);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    // -------------------------------------------------------------------
    // First-admin bootstrap. There is NO seeded admin and NO well-known hash.
    // Hit this ONCE from the server box to mint the first SiteAdmin with a
    // random temp password. It is hardened two ways: it refuses any request
    // that isn't from loopback, and it refuses once a SiteAdmin already exists —
    // so it can never act as a remote password-reset/takeover backdoor.
    // Alternative: insert one SiteAdmin row by hand, then sign in via AD:
    //   UPDATE Users SET Role='SiteAdmin' WHERE AdUsername='your.adname';
    // -------------------------------------------------------------------
    [HttpGet]
    public async Task<IActionResult> Setup()
    {
        if (!IsLoopback()) return NotFound();
        // Refuse once a SiteAdmin exists (still allows bootstrapping the FIRST admin
        // even if auto-create has already inserted some Requesters).
        if ((await _db.GetAllUsersAsync()).Any(u => u.Role == UserRoles.SiteAdmin))
            return Content("Setup already completed — an admin exists. This endpoint is disabled.");

        var tempPassword = GenerateTempPassword();
        await _db.CreateUserAsync(new User
        {
            EmployeeId = "", AdUsername = "admin", FullName = "System Administrator",
            Email = "admin@localhost", Department = "IT",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword),
            Role = UserRoles.SiteAdmin, IsActive = true
        });
        _logger.LogWarning("First admin created via loopback /Account/Setup.");
        return Content($"First admin created.\nUsername: admin\nTemporary password: {tempPassword}\n" +
                       "Sign in and change it immediately. This endpoint is now disabled.");
    }

    private bool IsLoopback()
    {
        var ip = HttpContext.Connection.RemoteIpAddress;
        return ip != null && IPAddress.IsLoopback(ip);
    }

    private static string GenerateTempPassword()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
                  .Replace("+", "A").Replace("/", "B").Replace("=", "");

    // -------------------------------------------------------------------
    // Profile picture upload (multipart). SPEC.md §5.
    // -------------------------------------------------------------------
    [HttpPost, Authorize]
    public async Task<IActionResult> UploadProfilePicture(IFormFile picture)
    {
        var userId = GetUserId();
        if (userId == 0) return Json(new { ok = false, error = "Not authenticated" });
        if (picture == null || picture.Length == 0)
            return Json(new { ok = false, error = "No file selected" });

        var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        var ext = Path.GetExtension(picture.FileName).ToLower();
        if (!allowed.Contains(ext))
            return Json(new { ok = false, error = "Only JPG, PNG, GIF or WebP allowed" });
        if (picture.Length > 2 * 1024 * 1024)
            return Json(new { ok = false, error = "Image must be under 2MB" });

        var avatarDir = Path.Combine(_env.WebRootPath, "avatars");
        Directory.CreateDirectory(avatarDir);
        // Delete any prior avatar for this user (handles ext changes)
        foreach (var old in Directory.GetFiles(avatarDir, $"{userId}.*"))
            System.IO.File.Delete(old);

        var fileName = $"{userId}{ext}";
        var filePath = Path.Combine(avatarDir, fileName);
        using (var fs = System.IO.File.Create(filePath))
            await picture.CopyToAsync(fs);

        var webPath = $"/avatars/{fileName}?v={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        await _db.UpdateProfilePictureAsync(userId, $"/avatars/{fileName}");
        return Json(new { ok = true, path = webPath });
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> RemoveProfilePicture()
    {
        var userId = GetUserId();
        var avatarDir = Path.Combine(_env.WebRootPath, "avatars");
        if (Directory.Exists(avatarDir))
            foreach (var old in Directory.GetFiles(avatarDir, $"{userId}.*"))
                System.IO.File.Delete(old);
        await _db.UpdateProfilePictureAsync(userId, "");
        return Json(new { ok = true });
    }

    // Accept "user", "user@domain" (UPN), or "DOMAIN\user" — reduce to the bare
    // sAMAccountName we store as AdUsername and bind AD with.
    private static string NormalizeUsername(string? raw)
    {
        var u = (raw ?? "").Trim();
        var at = u.IndexOf('@');      if (at > 0)  u = u.Substring(0, at);
        var bs = u.LastIndexOf('\\'); if (bs >= 0) u = u.Substring(bs + 1);
        return u;
    }

    private int GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return int.TryParse(claim?.Value, out var id) ? id : 0;
    }
}

public class LoginViewModel
{
    public string Username   { get; set; } = "";
    public string Password   { get; set; } = "";
    public bool   RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}
