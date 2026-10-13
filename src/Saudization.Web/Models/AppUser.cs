using System.ComponentModel.DataAnnotations;

namespace Saudization.Web.Models;

/// <summary>
/// A person allowed to sign in. This is a <b>whitelist</b>: only rows here can log in, and only after
/// their password is verified against Active Directory (bind-only). There are no roles — every active
/// user has the same full access. Password is never stored: AD is the sole authenticator.
/// </summary>
public class AppUser
{
    public int Id { get; set; }

    /// <summary>The AD sAMAccountName — the canonical login key (e.g. <c>mohamed.tag</c>).</summary>
    [Required, MaxLength(100)]
    public string AdUsername { get; set; } = "";

    [Required, MaxLength(200)]
    public string FullName { get; set; } = "";

    [MaxLength(200)]
    public string? Email { get; set; }

    [MaxLength(200)]
    public string? Department { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? LastLogin { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string? CreatedByUser { get; set; }
}

/// <summary>Profile returned from AD — by a successful login bind, or by the directory browse.</summary>
public class AdUser
{
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Department { get; set; } = "";
    public bool Enabled { get; set; } = true;   // userAccountControl says the AD account is enabled
}
