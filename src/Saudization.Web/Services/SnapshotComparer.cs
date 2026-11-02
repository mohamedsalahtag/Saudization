using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;
using Saudization.Web.Models;

namespace Saudization.Web.Services;

/// <summary>One employee of a version, described by names (not ids) so a not-yet-saved upload can be compared
/// with saved versions.</summary>
public record PersonRow(string Key, string Name, string? WorkerNo, string? IqamaNo, string? Facility, string? Registration,
    string? Nationality, bool Saudi, string? Occupation, string? Category, bool Special, int Disability);

/// <summary>A person present in both versions whose details differ.</summary>
public record PersonChange(PersonRow Before, PersonRow After, IReadOnlyList<string> Fields);

/// <summary>Who joined, left or changed between two versions (keyed by Iqama / ID number, else worker number).</summary>
public class SnapshotDiff
{
    public int OtherSnapshotId { get; set; }
    public string OtherLabel { get; set; } = "";
    public List<PersonRow> JoinerRows { get; } = new();
    public List<PersonRow> LeaverRows { get; } = new();
    public List<PersonChange> ChangedRows { get; } = new();
    public int Joiners => JoinerRows.Count;
    public int Leavers => LeaverRows.Count;
    public int Changed => ChangedRows.Count;
    public bool NoPeopleChange => Joiners == 0 && Leavers == 0;
    /// <summary>Saudi heads gained/lost (nationals + special) — the Saudization effect of the change.</summary>
    public int SaudiDelta => JoinerRows.Count(r => r.Saudi || r.Special) - LeaverRows.Count(r => r.Saudi || r.Special)
        + ChangedRows.Count(c => (c.After.Saudi || c.After.Special) && !(c.Before.Saudi || c.Before.Special))
        - ChangedRows.Count(c => (c.Before.Saudi || c.Before.Special) && !(c.After.Saudi || c.After.Special));
}

/// <summary>Fingerprints and compares versions person by person, so an upload that repeats existing data is caught
/// and the people who joined, left or changed can be listed.</summary>
public class SnapshotComparer
{
    private const string HashVersion = "v3:";   // bump when the fingerprint definition changes; old hashes are recomputed
    private readonly AppDbContext _db;
    public SnapshotComparer(AppDbContext db) => _db = db;

    /// <summary>Stable identity for a worker across weeks. Worker numbers can change for the same person,
    /// so the Iqama / national ID (digits only) is preferred.</summary>
    public static string WorkerKey(string? iqama, string? workerNo, string? name)
    {
        var digits = TextNormalizer.IdKey(iqama);
        if (digits is { Length: >= 6 } && digits.All(char.IsDigit)) return "id:" + digits;
        if (!string.IsNullOrWhiteSpace(workerNo)) return "wn:" + workerNo.Trim();
        return "nm:" + TextNormalizer.Key(name);
    }

    public async Task<List<PersonRow>> LoadAsync(int snapshotId, CancellationToken ct = default)
    {
        var raw = await _db.Employees.AsNoTracking().IgnoreQueryFilters()
            .Where(e => e.SnapshotId == snapshotId)
            .Select(e => new
            {
                e.Name, e.WorkerNo, e.IqamaNo,
                Facility = e.Facility!.Code, Registration = e.Facility!.Record!.RegistrationLabel,
                Nationality = e.Nationality != null ? e.Nationality.NameAr : null,
                Saudi = e.Nationality != null && e.Nationality.IsSaudi,
                Occupation = e.OccupationRaw,
                Category = e.Occupation != null && e.Occupation.Category != null ? e.Occupation.Category.NameAr : null,
                e.CountsAsSaudiSpecial, e.DisabilityCountAs
            })
            .ToListAsync(ct);
        return raw.Select(e => new PersonRow(WorkerKey(e.IqamaNo, e.WorkerNo, e.Name), e.Name, e.WorkerNo, e.IqamaNo, e.Facility,
            e.Registration, e.Nationality, e.Saudi, e.Occupation?.Trim(), e.Category, e.CountsAsSaudiSpecial, e.DisabilityCountAs)).ToList();
    }

    /// <summary>Rows of an upload that has been read but not saved (navigation properties are set by the importer).</summary>
    public static List<PersonRow> FromEmployees(IEnumerable<Employee> emps) =>
        emps.Select(e => new PersonRow(WorkerKey(e.IqamaNo, e.WorkerNo, e.Name), e.Name, e.WorkerNo, e.IqamaNo, e.Facility?.Code,
            e.Facility?.Record?.RegistrationLabel, e.Nationality?.NameAr, e.Nationality?.IsSaudi == true, e.OccupationRaw?.Trim(),
            e.Occupation?.Category?.NameAr, e.CountsAsSaudiSpecial, e.DisabilityCountAs)).ToList();

    private static string Sig(PersonRow r) =>
        $"{r.Facility}|{r.Nationality}|{r.Occupation}|{r.Special}|{r.Disability}";   // no worker number: the current template has none

    public static string Hash(IEnumerable<PersonRow> rows)
    {
        var lines = rows.Select(r => r.Key + "#" + Sig(r)).OrderBy(s => s, StringComparer.Ordinal);
        // Snapshot.ContentHash is nvarchar(64): the version prefix + the hash must fit
        var hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines))));
        return HashVersion + hex[..(64 - HashVersion.Length)];
    }

    /// <summary>Computes and stores the fingerprint of a saved version.</summary>
    public async Task<string> EnsureHashAsync(int snapshotId, CancellationToken ct = default)
    {
        var snap = await _db.Snapshots.IgnoreQueryFilters().FirstAsync(s => s.Id == snapshotId, ct);
        if (snap.ContentHash?.StartsWith(HashVersion) != true)
        {
            snap.ContentHash = Hash(await LoadAsync(snapshotId, ct));
            await _db.SaveChangesAsync(ct);
        }
        return snap.ContentHash!;
    }

    /// <summary>A non-deleted version whose employees are exactly <paramref name="hash"/>. Older versions' fingerprints are
    /// (re)computed on first use. Call only when the context holds no unsaved import (this saves).</summary>
    public async Task<Snapshot?> FindIdenticalAsync(string hash, int? excludeId = null, CancellationToken ct = default)
    {
        var stale = await _db.Snapshots.Where(s => s.ContentHash == null || !s.ContentHash.StartsWith(HashVersion)).Select(s => s.Id).ToListAsync(ct);
        foreach (var id in stale) await EnsureHashAsync(id, ct);
        return await _db.Snapshots.AsNoTracking().Where(s => s.Id != excludeId && s.ContentHash == hash)
            .OrderByDescending(s => s.Id).FirstOrDefaultAsync(ct);
    }

    public static SnapshotDiff Diff(List<PersonRow> newer, List<PersonRow> older, int otherId = 0, string otherLabel = "")
    {
        var a = newer.GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.First());
        var b = older.GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.First());
        var d = new SnapshotDiff { OtherSnapshotId = otherId, OtherLabel = otherLabel };
        d.JoinerRows.AddRange(a.Values.Where(r => !b.ContainsKey(r.Key)).OrderBy(r => r.Registration).ThenBy(r => r.Name));
        d.LeaverRows.AddRange(b.Values.Where(r => !a.ContainsKey(r.Key)).OrderBy(r => r.Registration).ThenBy(r => r.Name));
        foreach (var now in a.Values.OrderBy(r => r.Registration).ThenBy(r => r.Name))
        {
            if (!b.TryGetValue(now.Key, out var was)) continue;
            var f = new List<string>();
            // only when both sides have one — the current template no longer carries worker numbers
            if (!string.IsNullOrWhiteSpace(was.WorkerNo) && !string.IsNullOrWhiteSpace(now.WorkerNo) && !Same(was.WorkerNo, now.WorkerNo)) f.Add("WorkerNo");
            if (!Same(was.Facility, now.Facility)) f.Add(Same(was.Registration, now.Registration) ? "Facility" : "Registration");
            if (!Same(was.Nationality, now.Nationality)) f.Add("Nationality");
            if (!Same(was.Occupation, now.Occupation)) f.Add(Same(was.Category, now.Category) ? "Occupation" : "Category");
            if (was.Special != now.Special) f.Add("Special");
            if (was.Disability != now.Disability) f.Add("Disability");
            if (f.Count > 0) d.ChangedRows.Add(new PersonChange(was, now, f));
        }
        return d;
    }

    public async Task<SnapshotDiff> DiffAsync(int newId, int otherId, CancellationToken ct = default) =>
        Diff(await LoadAsync(newId, ct), await LoadAsync(otherId, ct), otherId,
            await _db.Snapshots.IgnoreQueryFilters().Where(s => s.Id == otherId).Select(s => s.Label).FirstOrDefaultAsync(ct) ?? "");

    private static bool Same(string? x, string? y) => TextNormalizer.Key(x) == TextNormalizer.Key(y);
}
