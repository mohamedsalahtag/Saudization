using System.DirectoryServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Caching.Memory;
using Saudization.Web.Models;

namespace Saudization.Web.Services;

/// <summary>
/// Active Directory authentication for the login page — <b>bind-only</b> (SPEC.md §2.2):
/// we never issue an LDAP search to authenticate; we simply try to bind as the user with the
/// password they typed. A successful bind IS the proof of identity. A profile lookup afterwards
/// (display name / mail / department) is best-effort and never fails the login.
///
/// The user binds with their OWN credentials, so no service-account password is stored anywhere.
/// Config comes from appsettings: <c>Ad:Domain</c> and <c>Ad:LdapPath</c> (neither is secret).
/// System.DirectoryServices is Windows-only; the app only ever runs on Windows/IIS.
/// </summary>
[SupportedOSPlatform("windows")]
public class AdService
{
    private readonly ILogger<AdService> _logger;
    private readonly IMemoryCache _cache;
    private readonly string _domain;
    private readonly string _ldapPath;
    private const string CacheKey = "AdUsers_All";

    public AdService(IConfiguration config, IMemoryCache cache, ILogger<AdService> logger)
    {
        _logger = logger;
        _cache = cache;
        _domain = config["Ad:Domain"] ?? "";
        _ldapPath = config["Ad:LdapPath"] ?? "";
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_domain) && !string.IsNullOrWhiteSpace(_ldapPath);

    /// <summary>Try to authenticate <paramref name="username"/> against AD by binding with the password.
    /// Returns the AD profile on success, or <c>null</c> on a wrong password / unreachable server.</summary>
    public AdUser? Authenticate(string username, string password)
    {
        if (!IsConfigured) return null;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) return null;

        var (server, baseDn) = ParseLdap(_ldapPath, _domain);
        var sam = username.Contains('@') ? username.Split('@')[0] : username;

        // Bind formats, most-reliable first. The domain FQDN and the first DC label can differ
        // (e.g. sharbatlyfruit.com vs DC=sharbatly), so try NetBIOS from both.
        var netbiosFromDomain = _domain.Split('.')[0];
        var firstDc = baseDn.Split(',')
            .Select(p => p.Trim())
            .FirstOrDefault(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            ?.Substring(3);

        var formats = new List<(string bindAs, string label)>
        {
            ($"{sam}@{_domain}", "UPN"),
            ($"{netbiosFromDomain}\\{sam}", "NetBIOS(domain)")
        };
        if (!string.IsNullOrEmpty(firstDc) &&
            !firstDc.Equals(netbiosFromDomain, StringComparison.OrdinalIgnoreCase))
            formats.Add(($"{firstDc}\\{sam}", "NetBIOS(dc)"));

        var ldapUrl = $"LDAP://{server}";

        foreach (var (bindAs, label) in formats)
        {
            try
            {
                using var entry = new DirectoryEntry(ldapUrl, bindAs, password, AuthenticationTypes.Secure);
                _ = entry.NativeObject;   // forces the bind — throws on bad credentials

                _logger.LogInformation("AD auth SUCCESS for {Sam} via [{Label}]", sam, label);

                // Best-effort profile fetch — a failure here must NOT fail the login.
                string fullName = sam, email = $"{sam}@{_domain}", dept = "";
                try
                {
                    using var searcher = new DirectorySearcher(entry)
                    {
                        Filter = $"(sAMAccountName={sam})",
                        SearchScope = SearchScope.Subtree
                    };
                    searcher.PropertiesToLoad.AddRange(new[] { "displayName", "cn", "mail", "department" });
                    var r = searcher.FindOne();
                    if (r != null)
                    {
                        fullName = Prop(r, "displayName") ?? Prop(r, "cn") ?? sam;
                        email = Prop(r, "mail") ?? email;
                        dept = Prop(r, "department") ?? "";
                    }
                }
                catch (Exception sx)
                {
                    _logger.LogWarning("AD profile lookup failed for {Sam} (login still OK): {Msg}", sam, sx.Message);
                }

                return new AdUser
                {
                    Username = sam,
                    FullName = string.IsNullOrWhiteSpace(fullName) ? sam : fullName,
                    Email = email,
                    Department = dept
                };
            }
            // Definitive wrong password / access denied — stop, do NOT try other formats.
            catch (COMException ex) when (
                ex.ErrorCode == unchecked((int)0x8007052E) ||
                ex.ErrorCode == unchecked((int)0x80070005))
            {
                _logger.LogWarning("AD auth WRONG PASSWORD for {Sam} [{Label}]", sam, label);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("AD auth [{Label}] failed ({Type}): {Msg} — trying next format",
                    label, ex.GetType().Name, ex.Message);
            }
        }

        _logger.LogError("All AD bind formats failed for {Sam} — check connectivity to {Server}", sam, server);
        return null;
    }

    /// <summary>
    /// List enabled AD accounts under the configured base DN, for the "Add from Active Directory" browse.
    /// Binds with the app's own Windows identity (the domain machine account) — no service-account
    /// password needed. Cached for 10 minutes; pass <paramref name="forceRefresh"/> to re-fetch.
    /// </summary>
    public List<AdUser> GetAllUsers(bool forceRefresh = false)
    {
        if (!IsConfigured) return new();
        if (!forceRefresh && _cache.TryGetValue(CacheKey, out List<AdUser>? cached) && cached != null)
            return cached;

        var (server, baseDn) = ParseLdap(_ldapPath, _domain);

        // Bind with the process identity (Secure). On a domain-joined server the app pool's machine
        // account can read the directory, so no credentials are supplied.
        var urls = new[]
        {
            $"LDAP://{server}/{baseDn}",
            $"LDAP://{server}",
            $"LDAP://{_domain}/{baseDn}"
        };

        foreach (var url in urls)
        {
            try
            {
                // Single-arg constructor => bind with the process's own Windows identity (the domain
                // machine account on the server) using secure/integrated auth. The 4-arg null-credential
                // form does NOT do integrated auth reliably, so don't use it here.
                using var root = new DirectoryEntry(url);
                _ = root.NativeObject;   // force the bind

                using var searcher = new DirectorySearcher(root)
                {
                    Filter = "(&(objectCategory=person)(objectClass=user)" +
                             "(|(userAccountControl=512)(userAccountControl=514)" +
                             "(userAccountControl=66050)(userAccountControl=66048)))",
                    SearchScope = SearchScope.Subtree,
                    PageSize = 1000
                };
                searcher.PropertiesToLoad.AddRange(new[]
                {
                    "sAMAccountName", "displayName", "cn", "givenName", "sn",
                    "mail", "department", "userAccountControl"
                });

                using var results = searcher.FindAll();
                if (results.Count == 0) continue;

                var users = new List<AdUser>();
                foreach (SearchResult r in results)
                {
                    var sam = Prop(r, "sAMAccountName");
                    if (string.IsNullOrEmpty(sam) || sam.EndsWith("$")) continue;   // skip machine accounts

                    int.TryParse(Prop(r, "userAccountControl") ?? "0", out var uac);
                    var enabled = uac == 512 || uac == 66048;

                    var fn = Prop(r, "displayName") ?? Prop(r, "cn")
                             ?? $"{Prop(r, "givenName")} {Prop(r, "sn")}".Trim();

                    users.Add(new AdUser
                    {
                        Username = sam,
                        FullName = string.IsNullOrWhiteSpace(fn) ? sam : fn,
                        Email = Prop(r, "mail") ?? "",
                        Department = Prop(r, "department") ?? "",
                        Enabled = enabled
                    });
                }

                var ordered = users.OrderBy(u => u.FullName).ToList();
                _cache.Set(CacheKey, ordered, TimeSpan.FromMinutes(10));
                _logger.LogInformation("AD browse returned {Count} users via {Url}", ordered.Count, url);
                return ordered;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("AD browse [{Url}] failed: {Msg}", url, ex.Message);
            }
        }

        _logger.LogError("AD browse: all strategies failed (server unreachable or no read access).");
        return new();
    }

    public void InvalidateUserCache() => _cache.Remove(CacheKey);

    /// <summary>Connectivity probe for diagnostics (anonymous bind + 1-result search).</summary>
    public (bool ok, string message) TestConnection()
    {
        if (!IsConfigured) return (false, "AD is not configured (Ad:Domain / Ad:LdapPath).");
        var (server, baseDn) = ParseLdap(_ldapPath, _domain);
        foreach (var url in new[] { $"LDAP://{server}", $"LDAP://{_domain}", $"LDAP://{server}/{baseDn}" })
        {
            try
            {
                using var entry = new DirectoryEntry(url);
                _ = entry.NativeObject;
                using var searcher = new DirectorySearcher(entry) { Filter = "(objectClass=user)", SizeLimit = 1 };
                searcher.PropertiesToLoad.Add("sAMAccountName");
                searcher.FindOne();
                return (true, $"Connected to {url} (BaseDN: {baseDn}).");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("AD test [{Url}] failed: {Msg}", url, ex.Message);
            }
        }
        return (false, $"Could not reach AD. Server: {server}, BaseDN: {baseDn}.");
    }

    // Accepts LDAP://server/baseDn, LDAP://server, LDAP://DC=..,DC=.. — returns (server, baseDn). SPEC.md §3.3.
    private static (string server, string baseDn) ParseLdap(string ldapPath, string domain)
    {
        var clean = ldapPath.Replace("LDAP://", "", StringComparison.OrdinalIgnoreCase).TrimStart('/');
        var slash = clean.IndexOf('/');
        string server, baseDn;
        if (slash > 0) { server = clean[..slash]; baseDn = clean[(slash + 1)..]; }
        else if (clean.StartsWith("DC=", StringComparison.OrdinalIgnoreCase)) { server = domain; baseDn = clean; }
        else { server = clean; baseDn = string.Join(",", domain.Split('.').Select(p => $"DC={p}")); }

        var colon = server.LastIndexOf(':');
        if (colon > 0 && int.TryParse(server[(colon + 1)..], out _)) server = server[..colon];
        return (server, baseDn);
    }

    private static string? Prop(SearchResult r, string name)
    {
        try { var c = r.Properties[name]; return c?.Count > 0 ? c[0]?.ToString() : null; }
        catch { return null; }
    }
}
