using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;
using Saudization.Web.Models;

namespace Saudization.Web.Services;

public class ImportResult
{
    public int Records, Facilities, Employees, Categories, Occupations, Nationalities, Disabilities, SpecialCases;
    public int OccupationsUnmatched;
    public int OccupationsUpdated, EmployeesRelinked, GuideRows;
    public int SpecialMatched, SpecialUnmatched, DisabilityMatched, DisabilityUnmatched;
    public readonly List<string> Warnings = new();
    /// <summary>Employees read per worksheet (for the upload preview).</summary>
    public readonly Dictionary<string, int> SheetEmployees = new();
    /// <summary>Occupation names in the file that match nothing in the occupation guide → employee count.</summary>
    public readonly Dictionary<string, int> UnmatchedOccupations = new();
    public bool Success => Warnings.All(w => !w.StartsWith("ERROR"));
}

/// <summary>
/// Reads a dashboard.xlsx-shaped workbook (reference sheets + one sheet per facility code)
/// and upserts everything into the database. Matching is by Arabic header text so a
/// re-exported file with reordered columns still loads.
/// </summary>
public class ExcelImporter
{
    private readonly AppDbContext _db;
    public ExcelImporter(AppDbContext db) => _db = db;

    // Sheet names in the source workbook (Arabic). The registrations sheet is "Records" in older files
    // and "اسماء السجلات" in the current ministry export — both are accepted.
    private const string ShRecords = "Records";
    private const string ShRecordsAr = "اسماء السجلات";
    private const string ShCategories = "فئات التوطين";
    private const string ShOccupations = "دليل المهن";
    private const string ShSpecial = "الفئة الخاصة";
    private const string ShDisability = "ذوي الاحتياجات الخاصة";

    /// <summary>Imports a weekly file into <paramref name="snapshot"/>. When <paramref name="protectMaster"/> is true
    /// (the default for regular uploads), existing master data — Saudization categories, the occupation guide, and
    /// each registration's Nitaqat settings — is treated as read-only: it is reused to place employees but never
    /// created or edited from the file. Master data is maintained only through its own screens.</summary>
    public async Task<ImportResult> ImportAsync(Stream xlsx, Snapshot snapshot,
        ImportSheetRule rule = ImportSheetRule.PerFacility, bool protectMaster = true, CancellationToken ct = default, bool save = true)
    {
        var res = new ImportResult();
        // ClosedXML evaluates formulas with the current culture; under "ar" it writes R1C1 offsets with an Arabic
        // letter mark (RC[؜-1]) and then fails to parse them. Excel content is culture-neutral, so read it invariant.
        using var _ = new InvariantCultureScope();
        using var wb = new XLWorkbook(xlsx);

        // Default "يُحتسب بعدد" weight for disabled rows that leave the count blank (Settings, Nitaqat default 4).
        var defWeight = (await _db.Settings.Where(s => s.Id == 1).Select(s => (int?)s.DisabilityDefaultWeight).FirstOrDefaultAsync(ct)) ?? 4;
        if (defWeight < 1) defWeight = 4;

        var catByName = await ImportCategoriesAsync(wb, res, protectMaster, ct);
        var occByName = await ImportOccupationsAsync(wb, res, catByName, protectMaster, ct);
        var recByLabel = await ImportRecordsAsync(wb, res, protectMaster, ct);   // also creates facilities
        await ImportReferenceListsAsync(wb, res, defWeight, ct);       // disability + special (raw lists)
        await ImportEmployeesAsync(wb, res, occByName, snapshot, rule, recByLabel, ct);
        ApplyWeighting(wb, res, snapshot, defWeight);                 // §4/§5: match special/disabled → employees

        if (res.Employees == 0)
        {
            // Nothing is saved: an empty version must never be created (or become the default).
            res.Warnings.Add("ERROR: no employees were read from the file. Check the sheet rule (by facility / by registration) and that the sheet names match facility codes or registration labels.");
            return res;
        }
        if (!save) return res;   // dry run (upload preview): the caller inspects the tracked entities, then discards them
        await _db.SaveChangesAsync(ct);
        return res;
    }

    // ---------- categories (فئات التوطين) ----------
    private async Task<Dictionary<string, SaudizationCategory>> ImportCategoriesAsync(
        XLWorkbook wb, ImportResult res, bool protectMaster, CancellationToken ct)
    {
        var map = (await _db.Categories.ToListAsync(ct)).GroupBy(c => Normalize(c.NameAr)).ToDictionary(g => g.Key, g => g.First());
        if (protectMaster) return map;   // weekly upload: existing categories are authoritative and untouched
        var ws = Find(wb, ShCategories);
        if (ws is null) { res.Warnings.Add($"Sheet '{ShCategories}' not found — categories skipped."); return map; }

        var h = HeaderIndex(ws);
        int cName = Col(h, "الفئة");
        int cMin = Col(h, "الحد الادنى لحساب التوطين", "الحد الأدنى لحساب التوطين");
        int cCur = Col(h, "النسبة الحالية");
        int cCurD = Col(h, "تاريخ تطبيق القرار");
        int cFut = Col(h, "نسبة التوطين المستقبلية", "النسبة المستقبلية");
        int cFutD = Col(h, "تاريخ تطبيق القرار الممستقبلي", "تاريخ تطبيق القرار المستقبلي");
        int cWage = Col(h, "الحد الادنى للأجور", "الحد الأدنى للأجور");

        foreach (var row in DataRows(ws))
        {
            var name = Str(row, cName);
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!map.TryGetValue(Normalize(name), out var cat))
            {
                cat = new SaudizationCategory { NameAr = name };
                _db.Categories.Add(cat); map[Normalize(name)] = cat; res.Categories++;
            }
            cat.MinForCalc = (int)(Num(row, cMin) ?? 0);
            // Frac normalizes a whole-number percentage (e.g. 30 → 0.30) so it fits decimal(5,4); true fractions pass through.
            cat.CurrentRequiredPct = Frac(row, cCur) ?? 0;
            cat.CurrentDecisionDate = Str(row, cCurD);
            cat.FutureRequiredPct = Frac(row, cFut) ?? cat.CurrentRequiredPct;
            cat.FutureDecisionDate = Str(row, cFutD);
            cat.MinWage = Num(row, cWage);
        }
        return map;
    }

    // ---------- occupation guide (دليل المهن) ----------
    private async Task<Dictionary<string, Occupation>> ImportOccupationsAsync(
        XLWorkbook wb, ImportResult res, Dictionary<string, SaudizationCategory> cats, bool protectMaster, CancellationToken ct)
    {
        // keyed by normalized name (so "مهندس مدنى" == "مهندس مدني"); duplicates in the guide must not crash the import
        var map = (await _db.Occupations.ToListAsync(ct)).GroupBy(o => Normalize(o.NameAr)).ToDictionary(g => g.Key, g => g.First());
        if (protectMaster) return map;   // weekly upload: existing occupation guide is authoritative and untouched
        var ws = Find(wb, ShOccupations);
        if (ws is null) { res.Warnings.Add($"Sheet '{ShOccupations}' not found — occupation guide skipped."); return map; }

        var h = HeaderIndex(ws);
        int cCode = Col(h, "الرمز");
        int cName = Col(h, "المهنة");
        int cCat = Col(h, "الفئة");

        foreach (var row in DataRows(ws))
        {
            var name = Str(row, cName);
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!map.TryGetValue(Normalize(name), out var occ))
            {
                occ = new Occupation { NameAr = name };
                _db.Occupations.Add(occ); map[Normalize(name)] = occ; res.Occupations++;
            }
            occ.Code = Str(row, cCode);
            var catName = Str(row, cCat);
            if (!string.IsNullOrWhiteSpace(catName) && cats.TryGetValue(Normalize(catName!), out var cat))
                occ.Category = cat;
        }
        return map;
    }

    /// <summary>Applies a standalone occupation-guide workbook (columns الرمز / المهنة / الفئة, any sheet) to the
    /// shared reference data: creates missing categories, upserts occupations + their category, and back-fills the
    /// occupation link on every employee (all versions) whose raw occupation now matches the guide.</summary>
    public async Task<ImportResult> ApplyOccupationGuideAsync(Stream xlsx, CancellationToken ct = default)
    {
        var res = new ImportResult();
        using var _ = new InvariantCultureScope();
        using var wb = new XLWorkbook(xlsx);
        var ws = Find(wb, ShOccupations) ?? FindByHeaders(wb, "المهنة", "الفئة") ?? wb.Worksheets.FirstOrDefault();
        if (ws is null) { res.Warnings.Add("ERROR: no worksheet found in the file."); return res; }

        var h = HeaderIndex(ws);
        int cCode = Col(h, "الرمز"), cName = Col(h, "المهنة"), cCat = Col(h, "الفئة");
        if (cName < 1 || cCat < 1) { res.Warnings.Add("ERROR: the sheet must have المهنة (occupation) and الفئة (category) columns."); return res; }

        var catByNorm = (await _db.Categories.ToListAsync(ct))
            .GroupBy(c => Normalize(c.NameAr)).ToDictionary(g => g.Key, g => g.First());
        var occByNorm = (await _db.Occupations.ToListAsync(ct))
            .GroupBy(o => Normalize(o.NameAr)).ToDictionary(g => g.Key, g => g.First());

        foreach (var row in DataRows(ws))
        {
            var name = Str(row, cName);
            if (string.IsNullOrWhiteSpace(name)) continue;
            res.GuideRows++;

            SaudizationCategory? cat = null;
            var catName = Str(row, cCat);
            if (!string.IsNullOrWhiteSpace(catName))
            {
                var cnorm = Normalize(catName!);
                if (!catByNorm.TryGetValue(cnorm, out cat))
                { cat = new SaudizationCategory { NameAr = catName!.Trim() }; _db.Categories.Add(cat); catByNorm[cnorm] = cat; res.Categories++; }
            }

            var nnorm = Normalize(name!);
            if (!occByNorm.TryGetValue(nnorm, out var occ))
            { occ = new Occupation { NameAr = name!.Trim() }; _db.Occupations.Add(occ); occByNorm[nnorm] = occ; res.Occupations++; }
            else res.OccupationsUpdated++;

            var code = Str(row, cCode);
            if (!string.IsNullOrWhiteSpace(code)) occ.Code = code;
            if (cat != null) occ.Category = cat;
        }
        await _db.SaveChangesAsync(ct);   // assign new occupation ids

        // back-fill the occupation link on employees that had no mapping before
        var emps = await _db.Employees.Where(e => e.OccupationRaw != null && e.OccupationId == null).ToListAsync(ct);
        foreach (var e in emps)
            if (occByNorm.TryGetValue(Normalize(e.OccupationRaw!), out var occ)) { e.Occupation = occ; res.EmployeesRelinked++; }
        await _db.SaveChangesAsync(ct);

        return res;
    }

    private static IXLWorksheet? FindByHeaders(XLWorkbook wb, params string[] required)
    {
        foreach (var w in wb.Worksheets)
        {
            var h = HeaderIndex(w);
            if (required.All(r => Col(h, r) > 0)) return w;
        }
        return null;
    }

    // ---------- records + facilities (Records sheet) ----------
    private async Task<Dictionary<string, Record>> ImportRecordsAsync(
        XLWorkbook wb, ImportResult res, bool protectMaster, CancellationToken ct)
    {
        var recByLabel = (await _db.Records.ToListAsync(ct)).GroupBy(r => Normalize(r.RegistrationLabel)).ToDictionary(g => g.Key, g => g.First());
        var facByCode = (await _db.Facilities.ToListAsync(ct)).GroupBy(f => Normalize(f.Code)).ToDictionary(g => g.Key, g => g.First());

        var ws = Find(wb, ShRecords) ?? Find(wb, ShRecordsAr);
        if (ws is null) { res.Warnings.Add($"Sheet '{ShRecords}'/'{ShRecordsAr}' not found — records skipped."); return recByLabel; }

        var h = HeaderIndex(ws);
        int cFac = Col(h, "رقم المنشأة", "إسم المنشأة", "اسم المنشأة");   // code column first: a sheet with both must not read the name as the code
        int cName = Col(h, "اسم السجل");
        int cLabel = Col(h, "السجل التجاري");
        int cRed = Col(h, "احمر", "أحمر");
        int cMed = Col(h, "أخضر متوسط", "اخضر متوسط");   // 5th zone — usually absent in ministry files
        int cHigh = Col(h, "أخضر مرتفع", "اخضر مرتفع");
        int cPlat = Col(h, "بلاتيني");

        foreach (var row in DataRows(ws))
        {
            var facCode = Str(row, cFac);
            var label = Str(row, cLabel);
            if (string.IsNullOrWhiteSpace(label)) continue;

            var isNewRec = false;
            if (!recByLabel.TryGetValue(Normalize(label!), out var rec))
            {
                if (protectMaster)
                {
                    // a weekly upload never creates registrations (a typo would otherwise move facilities and rewrite history)
                    res.Warnings.Add($"Registrations sheet: unknown registration “{label}” ignored — add it on the Records page, or tick “update master data”.");
                    continue;
                }
                rec = new Record { RegistrationLabel = label! };
                _db.Records.Add(rec); recByLabel[Normalize(label!)] = rec; res.Records++; isNewRec = true;
            }
            var recName = Str(row, cName);
            // Never overwrite an existing registration's name/Nitaqat thresholds on a weekly upload — only set them
            // for a brand-new registration (or when master editing is explicitly allowed).
            if (isNewRec || !protectMaster)
            {
                if (!string.IsNullOrWhiteSpace(recName)) rec.NameAr = recName;
                var red = Frac(row, cRed); if (red is not null) rec.LowGreenMin = red.Value;
                var high = Frac(row, cHigh); if (high is not null) rec.HighGreenMin = high.Value;
                var plat = Frac(row, cPlat); if (plat is not null) rec.PlatinumMin = plat.Value;
                // Keep the (spec-added) medium-green threshold between low and high so all five zones stay ascending.
                var med = Frac(row, cMed);
                rec.MediumGreenMin = med ?? (rec.LowGreenMin + rec.HighGreenMin) / 2m;
            }

            if (!string.IsNullOrWhiteSpace(facCode))
            {
                if (!facByCode.TryGetValue(Normalize(facCode!), out var fac))
                {
                    fac = new Facility { Code = facCode!, Record = rec };
                    _db.Facilities.Add(fac); facByCode[Normalize(facCode!)] = fac; res.Facilities++;
                    if (protectMaster) res.Warnings.Add($"New facility {facCode} added under registration “{rec.RegistrationLabel}”.");
                }
                else if (fac.Record != rec && fac.RecordId != rec.Id)
                {
                    // moving a facility changes every past version's registration totals — only with master editing
                    if (protectMaster)
                        res.Warnings.Add($"Facility {fac.Code} is listed under “{rec.RegistrationLabel}” in the file but belongs to another registration — not moved (tick “update master data” to move it).");
                    else fac.Record = rec;
                }
                if (string.IsNullOrWhiteSpace(fac.NameAr)) fac.NameAr = recName;
            }
        }
        return recByLabel;
    }

    private async Task ImportReferenceListsAsync(XLWorkbook wb, ImportResult res, int defaultWeight, CancellationToken ct)
    {
        var disWs = Find(wb, ShDisability);
        if (disWs is not null)
        {
            var h = HeaderIndex(disWs);
            int cId = Col(h, "رقم الهوية");
            int cName = Col(h, "الاسم");
            int cCount = Col(h, "ليُحتسب بعدد", "ليحتسب بعدد");
            foreach (var row in DataRows(disWs))
            {
                var id = Str(row, cId); var nm = Str(row, cName);
                if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(nm)) continue;
                res.Disabilities++;   // the weighting itself is applied per employee in ApplyWeighting
            }
        }

        var spWs = Find(wb, ShSpecial);
        if (spWs is not null)
        {
            var h = HeaderIndex(spWs);
            int cId = Col(h, "رقم الهوية");
            int cName = Col(h, "الاسم");
            int cCat = Col(h, "الفئة الخاصة");
            int cOcc = Col(h, "المهنة");
            foreach (var row in DataRows(spWs))
            {
                var id = Str(row, cId); var nm = Str(row, cName);
                if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(nm)) continue;
                res.SpecialCases++;   // applied per employee in ApplyWeighting
            }
        }
        await Task.CompletedTask;
    }

    // ---------- §4/§5: attach special-category & disability weighting to this snapshot's employees ----------
    private void ApplyWeighting(XLWorkbook wb, ImportResult res, Snapshot snapshot, int defaultWeight)
    {
        // Build IqamaNo → employee(s) for the employees just added to this snapshot.
        var byId = new Dictionary<string, List<Employee>>();
        foreach (var e in _db.ChangeTracker.Entries<Employee>().Select(x => x.Entity))
        {
            if (!ReferenceEquals(e.Snapshot, snapshot)) continue;
            var key = IdKey(e.IqamaNo);
            if (key is null) continue;
            (byId.TryGetValue(key, out var l) ? l : byId[key] = new List<Employee>()).Add(e);
        }

        // Special categories (الفئة الخاصة) — count as Saudi in general AND occupation %.
        var sp = Find(wb, ShSpecial);
        if (sp is not null)
        {
            var h = HeaderIndex(sp);
            int cId = Col(h, "رقم الهوية");
            int cCat = Col(h, "الفئة الخاصة");
            foreach (var row in DataRows(sp))
            {
                var key = IdKey(Str(row, cId));
                if (key is null) continue;
                if (byId.TryGetValue(key, out var emps))
                {
                    foreach (var e in emps) { e.CountsAsSaudiSpecial = true; e.SpecialCategory = Str(row, cCat); }
                    res.SpecialMatched++;
                }
                else res.SpecialUnmatched++;
            }
        }

        // People with special needs (ذوو الاحتياجات الخاصة) — يُحتسب بعدد weight, general % only.
        var dis = Find(wb, ShDisability);
        if (dis is not null)
        {
            var h = HeaderIndex(dis);
            int cId = Col(h, "رقم الهوية");
            int cCount = Col(h, "ليُحتسب بعدد", "ليحتسب بعدد");
            foreach (var row in DataRows(dis))
            {
                var key = IdKey(Str(row, cId));
                if (key is null) continue;
                var countAs = (int)(Num(row, cCount) ?? defaultWeight);
                if (countAs < 1) countAs = 1;
                if (byId.TryGetValue(key, out var emps))
                {
                    foreach (var e in emps)
                    {
                        // the يُحتسب بعدد bonus applies to Saudi employees only
                        if (e.Nationality?.IsSaudi == true || e.CountsAsSaudiSpecial) e.DisabilityCountAs = countAs;
                        else res.Warnings.Add($"Disability list: {e.Name} ({e.IqamaNo}) is not Saudi — weighting not applied.");
                    }
                    res.DisabilityMatched++;
                }
                else res.DisabilityUnmatched++;
            }
        }

        if (res.SpecialUnmatched > 0 || res.DisabilityUnmatched > 0)
            res.Warnings.Add($"Unmatched IDs — special: {res.SpecialUnmatched}, disability: {res.DisabilityUnmatched} (no employee with that ID in this version).");
    }

    private static string? IdKey(string? id) => TextNormalizer.IdKey(id);

    // ---------- employees — one sheet per facility (default) OR one sheet per registration/company ----------
    private async Task ImportEmployeesAsync(
        XLWorkbook wb, ImportResult res, Dictionary<string, Occupation> occByName, Snapshot snapshot,
        ImportSheetRule rule, Dictionary<string, Record> recByLabel, CancellationToken ct)
    {
        var facByCode = (await _db.Facilities.ToListAsync(ct)).GroupBy(f => Normalize(f.Code)).ToDictionary(g => g.Key, g => g.First());
        foreach (var f in _db.ChangeTracker.Entries<Facility>().Select(e => e.Entity))
            facByCode[Normalize(f.Code)] = f;   // include facilities added in this same session
        var natByName = (await _db.Nationalities.ToListAsync(ct)).GroupBy(n => Normalize(n.NameAr)).ToDictionary(g => g.Key, g => g.First());
        var seen = new Dictionary<string, string>();   // worker identity → sheet, to drop duplicates within the file
        int dupes = 0;
        var recByNorm = recByLabel.ToDictionary(kv => Normalize(kv.Key), kv => kv.Value);

        // reference sheets are never employee sheets
        var reserved = new[] { ShRecords, ShRecordsAr, ShCategories, ShOccupations, ShSpecial, ShDisability }.Select(Normalize).ToHashSet();

        foreach (var ws in wb.Worksheets)
        {
            var sheetNorm = Normalize(ws.Name);
            if (reserved.Contains(sheetNorm)) continue;

            Facility? sheetFac = null;   // fixed facility (PerFacility rule)
            Record? sheetReg = null;     // registration (PerRegistration rule)
            if (rule == ImportSheetRule.PerRegistration)
            {
                if (!recByNorm.TryGetValue(sheetNorm, out sheetReg))   // only registration-named sheets
                {
                    res.Warnings.Add($"Sheet \"{ws.Name}\" was skipped: its name does not match any registration label.");
                    continue;
                }
            }
            else
            {
                if (!facByCode.TryGetValue(Normalize(ws.Name), out sheetFac))   // only facility-code sheets
                {
                    res.Warnings.Add($"Sheet \"{ws.Name}\" was skipped: its name does not match any facility code (رقم المنشأة).");
                    continue;
                }
            }
            int sheetRows = 0, noName = 0;

            var h = HeaderIndex(ws);
            int cWorker = Col(h, "رقم العامل");
            int cName = Col(h, "اسم العامل");
            int cNat = Col(h, "الجنسية");
            int cLoc = Col(h, "الشركة");
            int cFacNo = Col(h, "رقم المنشأة");
            int cFacName = Col(h, "إسم المنشأة", "اسم المنشأة");
            int cBorder = Col(h, "رقم الحدود");
            int cIqama = Col(h, "الإقامة - البطاقة", "الاقامة - البطاقة", "الإقامة", "الاقامة");
            int cOcc = Col(h, "المهنة");
            int cExp = Col(h, "تاريخ انتهاء الاقامة", "تاريخ انتهاء الإقامة");
            int cEntry = Col(h, "تاريخ دخول المملكة");
            int cType = Col(h, "نوع العامل");

            foreach (var row in DataRows(ws))
            {
                var name = Str(row, cName);
                if (string.IsNullOrWhiteSpace(name)) { noName++; continue; }

                // resolve the target facility for this row
                Facility fac;
                if (rule == ImportSheetRule.PerRegistration)
                {
                    var facCode = Str(row, cFacNo) ?? sheetReg!.RegistrationLabel;   // fall back to one facility per registration
                    if (!facByCode.TryGetValue(Normalize(facCode), out fac!))
                    {
                        fac = new Facility { Code = facCode, NameAr = Str(row, cFacName), Record = sheetReg };
                        _db.Facilities.Add(fac); facByCode[Normalize(facCode)] = fac; res.Facilities++;
                    }
                    else if (fac.RecordId != 0 && fac.RecordId != sheetReg!.Id && fac.Record != sheetReg)
                        res.Warnings.Add($"Sheet \"{ws.Name}\": facility {facCode} belongs to another registration — its employees are counted there.");
                }
                else fac = sheetFac!;

                // the same person twice in one file (pasted twice / listed on two facility sheets) is counted once
                var who = SnapshotComparer.WorkerKey(Str(row, cIqama), Str(row, cWorker), name);
                if (seen.TryGetValue(who, out var firstSheet))
                {
                    dupes++;
                    if (dupes <= 20) res.Warnings.Add($"Duplicate skipped: {name} ({Str(row, cIqama) ?? Str(row, cWorker)}) on sheet \"{ws.Name}\" — already read from sheet \"{firstSheet}\".");
                    continue;
                }
                seen[who] = ws.Name;

                var natName = Str(row, cNat);
                Nationality? nat = null;
                if (!string.IsNullOrWhiteSpace(natName))
                {
                    if (!natByName.TryGetValue(Normalize(natName!), out nat))
                    {
                        nat = new Nationality { NameAr = natName!, IsSaudi = IsSaudi(natName!) };
                        _db.Nationalities.Add(nat); natByName[Normalize(natName!)] = nat; res.Nationalities++;
                        res.Warnings.Add($"New nationality “{natName}” added (counted as {(nat.IsSaudi ? "Saudi" : "non-Saudi")}).");
                    }
                }

                var occRaw = Str(row, cOcc);
                Occupation? occ = null;
                if (!string.IsNullOrWhiteSpace(occRaw))
                {
                    if (!occByName.TryGetValue(Normalize(occRaw!), out occ))
                    {
                        res.OccupationsUnmatched++;
                        res.UnmatchedOccupations[occRaw!] = res.UnmatchedOccupations.GetValueOrDefault(occRaw!) + 1;
                    }
                }

                _db.Employees.Add(new Employee
                {
                    Snapshot = snapshot,
                    WorkerNo = Str(row, cWorker),
                    Name = name!,
                    Nationality = nat,
                    Location = Str(row, cLoc),
                    Facility = fac,
                    BorderNo = Str(row, cBorder),
                    IqamaNo = Str(row, cIqama),
                    Occupation = occ,
                    OccupationRaw = occRaw,
                    IqamaExpiry = Str(row, cExp),
                    EntryDate = Str(row, cEntry),
                    WorkerType = Str(row, cType)
                });
                res.Employees++; sheetRows++;
            }
            res.SheetEmployees[ws.Name] = sheetRows;
            if (cName < 0)
                res.Warnings.Add($"Sheet \"{ws.Name}\": no \"اسم العامل\" column found in the header row — no employees read.");
            else if (noName > 0)
                res.Warnings.Add($"Sheet \"{ws.Name}\": {sheetRows} employees read, {noName} row(s) skipped because the worker name was empty.");
        }
        if (dupes > 20) res.Warnings.Add($"… {dupes} duplicate rows skipped in total.");
    }

    // ===================== helpers =====================

    private static bool IsSaudi(string nat) => TextNormalizer.IsSaudiNationality(nat);

    private static IXLWorksheet? Find(XLWorkbook wb, string name) =>
        wb.Worksheets.FirstOrDefault(w => Normalize(w.Name) == Normalize(name));

    /// <summary>The header row: among the first 10 used rows, the first one with the most filled cells
    /// (a title row above the header has only one or two).</summary>
    private static IXLRow? HeaderRow(IXLWorksheet ws)
    {
        IXLRow? best = null; int bestCount = 0;
        foreach (var r in ws.RowsUsed().Take(10))
        {
            int count = r.CellsUsed().Count(c => c.HasFormula || !string.IsNullOrWhiteSpace(c.GetString()));   // no formula evaluation
            if (count > bestCount) { best = r; bestCount = count; }
        }
        return best;
    }

    /// <summary>Map normalized header text → worksheet column number. Absolute, like Str/Num which read worksheet
    /// rows, so a sheet whose data starts at column B is read correctly.</summary>
    private static Dictionary<string, int> HeaderIndex(IXLWorksheet ws)
    {
        var dict = new Dictionary<string, int>();
        var headerRow = HeaderRow(ws);
        if (headerRow is null) return dict;
        foreach (var cell in headerRow.CellsUsed())
        {
            var key = Normalize(cell.GetString());
            if (key.Length > 0 && !dict.ContainsKey(key)) dict[key] = cell.Address.ColumnNumber;
        }
        return dict;
    }

    private static int Col(Dictionary<string, int> h, params string[] candidates)
    {
        // 1) exact header match for any candidate, in candidate order
        foreach (var c in candidates)
            if (h.TryGetValue(Normalize(c), out var idx)) return idx;
        // 2) a header that contains the candidate; the shortest such header wins, so "الإقامة" picks
        //    "رقم الإقامة" rather than "تاريخ انتهاء الإقامة". Very short candidates are never fuzzy-matched.
        foreach (var c in candidates)
        {
            var key = Normalize(c);
            if (key.Length < 4) continue;
            var hit = h.Where(kv => kv.Key.Contains(key)).OrderBy(kv => kv.Key.Length).Select(kv => (int?)kv.Value).FirstOrDefault();
            if (hit is int found) return found;
        }
        return -1;
    }

    private static IEnumerable<IXLRow> DataRows(IXLWorksheet ws)
    {
        var header = HeaderRow(ws);
        if (header is null) yield break;
        int headerNo = header.RowNumber();
        foreach (var row in ws.RowsUsed())
            if (row.RowNumber() > headerNo) yield return row;
    }

    private static string? Str(IXLRow row, int col)
    {
        if (col < 1) return null;
        var v = row.Cell(col).GetString().Trim();
        if (string.IsNullOrWhiteSpace(v)) return null;
        // when an employee row is also an Excel table's header row, its empty cells read back as the table's
        // automatic column names ("Column7") — those are blanks, not data
        return AutoTableHeader.IsMatch(v) ? null : v;
    }
    private static readonly Regex AutoTableHeader = new(@"^Column\d+$", RegexOptions.Compiled);

    private static decimal? Num(IXLRow row, int col)
    {
        if (col < 1) return null;
        var cell = row.Cell(col);
        if (cell.TryGetValue<double>(out var d)) return (decimal)d;
        var s = cell.GetString();
        var m = Regex.Match(s, @"-?\d+(?:[.,]\d+)?");
        if (m.Success && decimal.TryParse(m.Value.Replace(',', '.'),
            NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return v;
        return null;
    }

    /// <summary>Parse a threshold cell into a 0..1 fraction; a value &gt;1 (e.g. "20-40%") is treated as a percentage.</summary>
    private static decimal? Frac(IXLRow row, int col)
    {
        var n = Num(row, col);
        if (n is null) return null;
        return n.Value > 1m ? n.Value / 100m : n.Value;
    }

    private static string Normalize(string s) => TextNormalizer.Key(s);
}
