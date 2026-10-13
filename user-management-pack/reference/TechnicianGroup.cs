// Support-group + per-user-per-group permission models.

namespace HelpDesk.Models;

public class TechnicianGroup
{
    public int      GroupId     { get; set; }
    public string   GroupName   { get; set; } = "";
    public string   ShortCode   { get; set; } = "";    // 3-letter prefix, e.g. SAP / NET / HW
    public string?  Description { get; set; }
    public bool     IsActive    { get; set; } = true;
    public DateTime CreatedAt   { get; set; }
    public string?  ColorHex    { get; set; }          // optional brand color for the navbar, e.g. "#1e40af"
    // OPTIONAL — ticketing-only group settings. Keep them only if this group
    // model also drives a ticket system; drop them for pure user management.
    public string?  TicketPrefix        { get; set; }  // overrides ShortCode on ticket numbers
    public bool     CategoryRequired    { get; set; } = true;
    public bool     SubcategoryRequired { get; set; } = false;
}

public class TechnicianGroupMember
{
    public int      MemberId        { get; set; }
    public int      UserId          { get; set; }
    public int      GroupId         { get; set; }
    public bool     CanDelete       { get; set; }      // may delete tickets in this group
    public bool     CanAssign       { get; set; }      // may assign tickets to other members
    public bool     CanPickupOthers { get; set; }      // may take tickets already assigned to teammates
    public bool     CanMerge        { get; set; }      // may merge tickets in this group (SiteAdmin always can)
    public DateTime AssignedAt      { get; set; }

    // Navigation (populated via JOIN in queries)
    public string?  FullName        { get; set; }
    public string?  GroupName       { get; set; }
}
