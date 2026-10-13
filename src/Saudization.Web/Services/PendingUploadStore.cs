using System.Text.Json;
using Saudization.Web.Models;

namespace Saudization.Web.Services;

/// <summary>What the user entered on the upload form, kept with the file until they confirm or cancel the preview.</summary>
public class PendingUploadMeta
{
    public string Label { get; set; } = "";
    public DateTime WeekDate { get; set; }
    public string? Note { get; set; }
    public ImportSheetRule Rule { get; set; }
    public bool UpdateMaster { get; set; }
    public string User { get; set; } = "";
    public string FileName { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}

/// <summary>Holds an uploaded workbook on the server between the preview and the confirm step
/// (folder "uploads" under the app, kept across deploys). Abandoned uploads are removed after a day.</summary>
public class PendingUploadStore
{
    private readonly string _dir;
    public PendingUploadStore(IConfiguration config, IWebHostEnvironment env)
    {
        _dir = config["Uploads:Path"] is { Length: > 0 } p ? p : Path.Combine(env.ContentRootPath, "uploads");
        Directory.CreateDirectory(_dir);
    }

    private string FilePath(string token) => Path.Combine(_dir, token + ".xlsx");
    private string MetaPath(string token) => Path.Combine(_dir, token + ".json");
    private static bool ValidToken(string? t) => Guid.TryParseExact(t, "N", out _);

    public async Task<string> SaveAsync(IFormFile file, PendingUploadMeta meta, CancellationToken ct)
    {
        Cleanup();
        var token = Guid.NewGuid().ToString("N");
        await using (var fs = File.Create(FilePath(token))) await file.CopyToAsync(fs, ct);
        await File.WriteAllTextAsync(MetaPath(token), JsonSerializer.Serialize(meta), ct);
        return token;
    }

    public PendingUploadMeta? Load(string? token)
    {
        if (!ValidToken(token) || !File.Exists(MetaPath(token!)) || !File.Exists(FilePath(token!))) return null;
        return JsonSerializer.Deserialize<PendingUploadMeta>(File.ReadAllText(MetaPath(token!)));
    }

    public Stream Open(string token) => File.OpenRead(FilePath(token));

    public void Delete(string? token)
    {
        if (!ValidToken(token)) return;
        try { File.Delete(FilePath(token!)); File.Delete(MetaPath(token!)); } catch (IOException) { }
    }

    private void Cleanup()
    {
        foreach (var f in Directory.EnumerateFiles(_dir))
            try { if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-1)) File.Delete(f); } catch (IOException) { }
    }
}
