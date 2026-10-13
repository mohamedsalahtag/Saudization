using Microsoft.EntityFrameworkCore;
using Saudization.Web.Models;
using Saudization.Web.Services;

namespace Saudization.Web.Data;

/// <summary>Creates the schema and, on an empty database, seeds it from the bundled dashboard workbook.</summary>
public static class DbInitializer
{
    public static async Task InitAsync(IServiceProvider services, IConfiguration config, ILogger logger, string contentRoot)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var provider = config["Database:Provider"] ?? "Sqlite";
        if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            await db.Database.MigrateAsync();
        else
            await db.Database.EnsureCreatedAsync();

        if (!await db.Settings.AnyAsync())          // ensure the single settings row exists
        {
            db.Settings.Add(new Settings { Id = 1 });
            await db.SaveChangesAsync();
        }

        // Login whitelist: guarantee the initial admin (mohamed.tag) can always sign in.
        // Idempotent — only inserts when missing, and re-enables the row if it was disabled.
        const string bootstrapUser = "mohamed.tag";
        var seedUser = await db.AppUsers.FirstOrDefaultAsync(u => u.AdUsername == bootstrapUser);
        if (seedUser == null)
        {
            db.AppUsers.Add(new AppUser
            {
                AdUsername = bootstrapUser,
                FullName = "Mohamed Tag",
                Email = "itc-mgm@sharbatlyfruit.com",
                Department = "IT",
                IsActive = true,
                CreatedByUser = "seed"
            });
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded login whitelist with bootstrap user '{User}'.", bootstrapUser);
        }
        else if (!seedUser.IsActive)
        {
            seedUser.IsActive = true;
            await db.SaveChangesAsync();
            logger.LogWarning("Re-enabled bootstrap user '{User}'.", bootstrapUser);
        }

        if (!await db.Records.AnyAsync())
        {
            var seedPath = config["Seed:FilePath"];
            if (string.IsNullOrWhiteSpace(seedPath)) { logger.LogInformation("No Seed:FilePath configured; starting empty."); return; }
            if (!Path.IsPathRooted(seedPath)) seedPath = Path.GetFullPath(Path.Combine(contentRoot, seedPath));
            if (!File.Exists(seedPath)) { logger.LogWarning("Seed file not found at {Path}; starting empty.", seedPath); return; }

            logger.LogInformation("Seeding database from {Path} …", seedPath);
            var importer = new ExcelImporter(db);
            var baseline = new Snapshot
            {
                Label = config["Seed:VersionLabel"] ?? "Baseline",
                WeekDate = DateTime.UtcNow.Date,
                Note = "Initial import from dashboard.xlsx",
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUser = "seed"
            };
            db.Snapshots.Add(baseline);
            await using var fs = File.OpenRead(seedPath);
            var res = await importer.ImportAsync(fs, baseline);
            logger.LogInformation("Seed complete: {Rec} records, {Fac} facilities, {Emp} employees, {Cat} categories, {Occ} occupations ({Un} unmatched occupations).",
                res.Records, res.Facilities, res.Employees, res.Categories, res.Occupations, res.OccupationsUnmatched);
        }

        await SeedZoneThresholdsAsync(db, logger);
    }

    /// <summary>Fills each registration's Nitaqat zone thresholds from the Qiwa screenshots in /Levels.
    /// Only touches records still on the model defaults (10/15/20/40%), so thresholds edited by users
    /// on the Records page survive restarts and deploys.</summary>
    private static async Task SeedZoneThresholdsAsync(AppDbContext db, ILogger logger)
    {
        // (distinctive Arabic keyword, LowGreenMin, MediumGreenMin, HighGreenMin, PlatinumMin) — specific first
        var map = new (string kw, decimal low, decimal med, decimal high, decimal plat)[]
        {
            ("factory",    0.2316m, 0.3087m, 0.3398m, 0.3988m),  // Riyadh Factory (مصنع الرياض)
            ("مصنع",       0.2316m, 0.3087m, 0.3398m, 0.3988m),  // مصنع الرياض
            ("صفا",        0.1212m, 0.1412m, 0.1750m, 0.2250m),  // صفاء جدة
            ("المهارات",   0.1757m, 0.2157m, 0.2651m, 0.3154m),  // المهارات للتشغيل والصيانة
            ("العربية",    0.1840m, 0.2251m, 0.2605m, 0.3712m),  // النقليات العربية (ATCL)
            ("للنقليات",   0.1993m, 0.2404m, 0.2804m, 0.3939m),  // فرع … للنقليات
            ("ثلاجة",      0.3818m, 0.4265m, 0.4655m, 0.5608m),  // ثلاجة الدمام (Branch)
            ("masco",      0.3818m, 0.4265m, 0.4655m, 0.5608m),  // MASCO Main (شركة محمد عبدالله شربتلي)
            ("الريسي",     0.3818m, 0.4265m, 0.4655m, 0.5608m),  // الرئيسي (normalized)
            ("فرع الرياض", 0.3818m, 0.4265m, 0.4655m, 0.5608m),  // فرع الرياض
        };

        var records = await db.Records.ToListAsync();
        int changed = 0; var unmatched = new List<string>();
        foreach (var r in records)
        {
            var hay = Normalize($"{r.NameAr} {r.RegistrationLabel} {r.NameEn}");
            var hit = map.FirstOrDefault(m => hay.Contains(Normalize(m.kw)));
            if (hit.kw is null) { unmatched.Add(r.RegistrationLabel); continue; }
            bool atDefaults = r.LowGreenMin == 0.10m && r.MediumGreenMin == 0.15m && r.HighGreenMin == 0.20m && r.PlatinumMin == 0.40m;
            if (!atDefaults) continue;
            r.LowGreenMin = hit.low; r.MediumGreenMin = hit.med; r.HighGreenMin = hit.high; r.PlatinumMin = hit.plat;
            changed++;
        }
        if (changed > 0) { await db.SaveChangesAsync(); logger.LogInformation("Zone thresholds updated for {N} registration(s) from /Levels images.", changed); }
        if (unmatched.Count > 0) logger.LogWarning("Zone-threshold seeding could not match {N} registration(s): {Labels}", unmatched.Count, string.Join(", ", unmatched));
    }

    private static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s.Trim().ToLowerInvariant())
        {
            char c = ch switch
            {
                'أ' or 'إ' or 'آ' => 'ا',
                'ى' => 'ي',
                'ئ' => 'ي',
                'ؤ' => 'و',
                'ة' => 'ه',
                _ => ch
            };
            if (c is 'ء' or 'ً' or 'ٌ' or 'ٍ' or 'َ' or 'ُ' or 'ِ' or 'ّ' or 'ْ') continue; // hamza + tashkeel
            sb.Append(c);
        }
        return sb.ToString();
    }
}
