// User domain model + role constants.

namespace HelpDesk.Models;

public class User
{
    public int      UserId         { get; set; }
    public string   EmployeeId     { get; set; } = "";
    // OPTIONAL — only if you keep the Reset-Password "confirm your Employee ID"
    // popup. True once the user has confirmed their Employee ID; drives whether
    // that popup shows again. Omit this field (and its column) if you don't use it.
    public bool     SapEmployeeIdConfirmed { get; set; }
    public string   AdUsername     { get; set; } = "";   // login key
    public string   FullName       { get; set; } = "";
    public string   Email          { get; set; } = "";
    public string?  Department     { get; set; }
    public string?  ProfilePicture { get; set; }         // /avatars/{userId}.{ext}
    public string   PasswordHash   { get; set; } = "";   // BCrypt local fallback
    public string   Role           { get; set; } = UserRoles.Requester;
    // OPTIONAL — only if you also ship a Projects module. Orthogonal to the
    // system Role above (Owner/Contributor/Reader). Drop it otherwise.
    public string   ProjectRole    { get; set; } = "Reader";
    public bool     IsActive       { get; set; } = true;
    public bool     IsOnline       { get; set; }
    public DateTime? LastLogin     { get; set; }
    public DateTime? LastSeen      { get; set; }
    public DateTime  CreatedAt     { get; set; }
    public int?      CreatedBy     { get; set; }
    public DateTime? DisabledAt    { get; set; }
    public int?      DisabledBy    { get; set; }
}

public static class UserRoles
{
    public const string Requester         = "Requester";
    public const string Technician        = "Technician";
    public const string FirstLevelSupport = "FirstLevelSupport";
    public const string SiteAdmin         = "SiteAdmin";
}
