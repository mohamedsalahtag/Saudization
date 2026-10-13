using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace Saudization.Web.Controllers;

public class HomeController : Controller
{
    // Only the language switch is anonymous — the login page's toggle needs it before sign-in.
    // Everything else (incl. Error) inherits the global "must be authenticated" policy, so an
    // unauthenticated request to any page is redirected to /Account/Login.
    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SetLanguage(string culture, string returnUrl)
    {
        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true });
        return LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
    }

    public IActionResult Error() => View();
}
