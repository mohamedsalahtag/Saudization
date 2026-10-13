using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.Extensions.WebEncoders;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;
using Saudization.Web.Services;
using Saudization.Web.Models;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Emit Arabic (and all Unicode) as real characters in HTML rather than numeric entities (&#x634;).
// Best practice for a non-Latin app: cleaner output, smaller pages, and robust round-tripping of Arabic.
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

// ---- Data Protection: persist the encryption keys to disk (default: <content root>\dp-keys) so the
// auth cookie and antiforgery tokens survive app restarts, deploys and IIS worker recycles. Without
// this the keys live only in memory, so every restart invalidates existing cookies and forces every
// user to sign in again. The folder is kept out of the deploy mirror so keys are never wiped.
var keysPath = config["DataProtection:KeysPath"];
if (string.IsNullOrWhiteSpace(keysPath))
    keysPath = Path.Combine(builder.Environment.ContentRootPath, "dp-keys");
Directory.CreateDirectory(keysPath);
builder.Services.AddDataProtection()
    .SetApplicationName("Saudization.Web")                       // stable name so keys are shared across restarts
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

// ---- Database (provider switchable: Sqlite for dev, SqlServer for production) ----
var provider = config["Database:Provider"] ?? "Sqlite";
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    var audit = sp.GetRequiredService<AuditSaveChangesInterceptor>();
    if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        options.UseSqlServer(config.GetConnectionString("SqlServer"),
            sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));
    else
        options.UseSqlite(config.GetConnectionString("Sqlite") ?? "Data Source=saudization.db");
    options.AddInterceptors(audit);
});

builder.Services.AddScoped<ExcelImporter>();
builder.Services.AddScoped<SnapshotComparer>();
builder.Services.AddSingleton<PendingUploadStore>();
builder.Services.AddScoped<ExcelTemplateService>();
builder.Services.AddScoped<NitaqatCalculator>();
builder.Services.AddScoped<TransferAdvisor>();
builder.Services.AddScoped<SaudizationExportService>();
builder.Services.AddScoped<DashboardExportService>();
builder.Services.AddScoped<OverviewBuilder>();
builder.Services.AddScoped<OrgImporter>();
builder.Services.AddScoped<OrgTemplateService>();
builder.Services.AddScoped<OrgAnalysisService>();
builder.Services.AddMemoryCache();          // caches the AD browse list (10 min)
builder.Services.AddScoped<AdService>();   // AD login + directory browse (user-management pack)

// ---- Localization (Arabic + English) ----
// No ResourcesPath: SharedResource.resx sits next to SharedResource.cs, so the SDK names the
// embedded resource after the class (Saudization.Web.SharedResource) — which is what the localizer
// then resolves. Setting ResourcesPath="Resources" would make it look for the wrong base name.
builder.Services.AddLocalization();
builder.Services.AddControllersWithViews()
    .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
    .AddDataAnnotationsLocalization();
builder.Services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");   // allow token via fetch header

var supportedCultures = new[] { new CultureInfo("en"), new CultureInfo("ar") };
builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    o.DefaultRequestCulture = new RequestCulture("ar");
    o.SupportedCultures = supportedCultures;
    o.SupportedUICultures = supportedCultures;
    o.RequestCultureProviders = new IRequestCultureProvider[]
    {
        new CookieRequestCultureProvider(),
        new QueryStringRequestCultureProvider(),
        new AcceptLanguageHeaderRequestCultureProvider()
    };
});

// ---- Authentication: cookie sign-in backed by an AD bind at login (user-management pack). ----
// The whole app requires a signed-in user (FallbackPolicy); [AllowAnonymous] opens only the login
// page and the language switch. There are no roles — every active whitelisted user has full access.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
        options.Cookie.Name = "Saudization.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.None;   // reached over HTTP on the intranet
        options.Cookie.MaxAge = TimeSpan.FromDays(30);           // persistent cookie survives browser restarts
        options.ExpireTimeSpan = TimeSpan.FromDays(30);          // stay signed in up to 30 days
        options.SlidingExpiration = true;                        // ...renewed on every visit, so active users stay in
    });
builder.Services.AddAuthorization(o =>
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// gzip/brotli for HTML, JSON, JS and CSS (the org-chart and xlsx libraries are ~1 MB uncompressed)
builder.Services.AddResponseCompression(o =>
{
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseStaticFiles(new StaticFileOptions
{
    // asp-append-version URLs (?v=hash) change whenever the file changes → safe to cache for a year;
    // unversioned third-party libraries (/lib) for a week
    OnPrepareResponse = ctx =>
    {
        var h = ctx.Context.Response.Headers;
        if (ctx.Context.Request.Query.ContainsKey("v")) h.CacheControl = "public,max-age=31536000,immutable";
        else if (ctx.Context.Request.Path.StartsWithSegments("/lib")) h.CacheControl = "public,max-age=604800";
    }
});
app.UseRouting();

app.UseRequestLocalization();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(name: "default", pattern: "{controller=Dashboard}/{action=Index}/{id?}");

// ---- Create/seed the database on startup ----
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await DbInitializer.InitAsync(app.Services, app.Configuration, logger, app.Environment.ContentRootPath);
}

// ---- One-off admin import from the console (no web server): reuses the exact same importer as the UI.
//   dotnet Saudization.Web.dll import "<file.xlsx>" [--update-master] [--label X] [--week yyyy-MM-dd] [--rule facility|registration]
if (args.Length > 0 && string.Equals(args[0], "import", StringComparison.OrdinalIgnoreCase))
{
    await RunImportCliAsync(app, args);
    return;
}

app.Run();

static async Task RunImportCliAsync(WebApplication app, string[] args)
{
    string? path = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : null;
    bool updateMaster = args.Any(a => a.Equals("--update-master", StringComparison.OrdinalIgnoreCase));
    string? Opt(string name)
    {
        var i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
    if (path is null || !File.Exists(path)) { Console.Error.WriteLine($"import: file not found: {path ?? "(none)"}"); Environment.ExitCode = 2; return; }
    var week = DateTime.TryParse(Opt("--week"), out var w) ? w.Date : DateTime.UtcNow.Date;
    var rule = string.Equals(Opt("--rule"), "registration", StringComparison.OrdinalIgnoreCase)
        ? ImportSheetRule.PerRegistration : ImportSheetRule.PerFacility;
    var label = Opt("--label");

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var importer = scope.ServiceProvider.GetRequiredService<ExcelImporter>();
    var comparer = scope.ServiceProvider.GetRequiredService<SnapshotComparer>();
    var previousDefault = await db.DefaultSnapshotIdAsync();
    var snap = new Snapshot
    {
        Label = string.IsNullOrWhiteSpace(label) ? $"{week:yyyy-MM-dd} (import)" : label!,
        WeekDate = week, Note = "CLI import", CreatedAtUtc = DateTime.UtcNow, CreatedByUser = "cli"
    };
    db.Snapshots.Add(snap);
    await using var fs = File.OpenRead(path);
    var res = await importer.ImportAsync(fs, snap, rule, protectMaster: !updateMaster);
    if (!res.Success)
    {
        foreach (var wn in res.Warnings) Console.Error.WriteLine("  " + wn);
        Console.Error.WriteLine("IMPORT FAILED — nothing saved."); Environment.ExitCode = 3; return;
    }
    // same guards as the web upload (ImportController): identical data is removed, no-joiner/no-leaver uploads don't become default
    var identical = await comparer.FindIdenticalAsync(await comparer.EnsureHashAsync(snap.Id), snap.Id);
    if (identical is not null)
    {
        await db.Snapshots.IgnoreQueryFilters().Where(x => x.Id == snap.Id).ExecuteDeleteAsync();
        Console.Error.WriteLine($"IMPORT NOT SAVED — identical to version \"{identical.Label}\" (id {identical.Id})."); Environment.ExitCode = 4; return;
    }
    var diff = previousDefault > 0 ? await comparer.DiffAsync(snap.Id, previousDefault) : null;
    bool makeDefault = diff is null || !diff.NoPeopleChange;
    if (makeDefault) await db.SetDefaultSnapshotAsync(snap.Id);
    Console.WriteLine($"IMPORT OK  snapshotId={snap.Id}  label=\"{snap.Label}\"  week={week:yyyy-MM-dd}  rule={rule}  updateMaster={updateMaster}  default={makeDefault}");
    if (diff is not null) Console.WriteLine($"  vs \"{diff.OtherLabel}\": +{diff.Joiners} joined, -{diff.Leavers} left, {diff.Changed} changed");
    Console.WriteLine($"  Records={res.Records} Facilities={res.Facilities} Employees={res.Employees} Categories={res.Categories} Occupations={res.Occupations} Nationalities={res.Nationalities}");
    Console.WriteLine($"  Disabilities={res.Disabilities} SpecialCases={res.SpecialCases} OccUnmatched={res.OccupationsUnmatched} SpecialMatched={res.SpecialMatched} DisabilityMatched={res.DisabilityMatched}");
    foreach (var wn in res.Warnings) Console.WriteLine("  WARN: " + wn);
}
