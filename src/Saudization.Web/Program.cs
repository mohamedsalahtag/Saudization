using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.Extensions.WebEncoders;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;
using Saudization.Web.Services;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Emit Arabic (and all Unicode) as real characters in HTML rather than numeric entities (&#x634;).
// Best practice for a non-Latin app: cleaner output, smaller pages, and robust round-tripping of Arabic.
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

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
builder.Services.AddScoped<ExcelTemplateService>();
builder.Services.AddScoped<NitaqatCalculator>();
builder.Services.AddScoped<TransferAdvisor>();
builder.Services.AddScoped<SaudizationExportService>();
builder.Services.AddScoped<DashboardExportService>();
builder.Services.AddScoped<OverviewBuilder>();
builder.Services.AddScoped<OrgImporter>();
builder.Services.AddScoped<OrgTemplateService>();

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

// ---- Authentication (Windows / AD). Enabled only when configured — dev on Kestrel runs anonymous. ----
var useWindowsAuth = config.GetValue<bool>("Auth:UseWindows");
if (useWindowsAuth)
{
    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
    builder.Services.AddAuthorization(o => o.FallbackPolicy = o.DefaultPolicy);
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseRequestLocalization();
if (useWindowsAuth) { app.UseAuthentication(); app.UseAuthorization(); }

app.MapControllerRoute(name: "default", pattern: "{controller=Dashboard}/{action=Index}/{id?}");

// ---- Create/seed the database on startup ----
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await DbInitializer.InitAsync(app.Services, app.Configuration, logger, app.Environment.ContentRootPath);
}

app.Run();
