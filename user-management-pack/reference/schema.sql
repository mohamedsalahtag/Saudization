-- ============================================================
-- User Management Pack - Storage
-- ============================================================
-- Three tables + AD config keys in SiteConfiguration.
-- Roles enforced as a string column (no enum) so the set is configurable.
-- ============================================================

-- 1) Users
CREATE TABLE Users (
    UserId          INT IDENTITY(1,1) PRIMARY KEY,
    EmployeeId      NVARCHAR(50),
    SapEmployeeIdConfirmed BIT NOT NULL DEFAULT 0,    -- optional: Reset-Password Employee-ID confirmation popup
    AdUsername      NVARCHAR(100) NOT NULL UNIQUE,    -- login key
    FullName        NVARCHAR(200) NOT NULL,
    Email           NVARCHAR(200),
    Department      NVARCHAR(200),
    ProfilePicture  NVARCHAR(300) NULL,               -- /avatars/{userId}.{ext}
    PasswordHash    NVARCHAR(500) NOT NULL,           -- BCrypt local fallback
    Role            NVARCHAR(50)  NOT NULL DEFAULT 'Requester',
        -- Roles: Requester | Technician | FirstLevelSupport | SiteAdmin
    ProjectRole     NVARCHAR(50) NULL,                -- optional: only with a Projects module (Owner/Contributor/Reader)
    IsActive        BIT NOT NULL DEFAULT 1,
    IsOnline        BIT NOT NULL DEFAULT 0,            -- set on login; swept back to 0 after inactivity (see presence)
    LastLogin       DATETIME NULL,
    LastSeen        DATETIME NULL,                     -- bumped by the client heartbeat; drives presence
    CreatedAt       DATETIME NOT NULL DEFAULT GETDATE(),
    CreatedBy       INT NULL REFERENCES Users(UserId),
    DisabledAt      DATETIME NULL,
    DisabledBy      INT NULL REFERENCES Users(UserId)
);

-- 2) Support groups (kept for tickets & permissions; remove if not needed)
CREATE TABLE TechnicianGroups (
    GroupId     INT IDENTITY(1,1) PRIMARY KEY,
    GroupName   NVARCHAR(100) NOT NULL,
    ShortCode   NVARCHAR(10)  NOT NULL,             -- 3-letter prefix
    Description NVARCHAR(500),
    IsActive    BIT NOT NULL DEFAULT 1,
    CreatedAt   DATETIME NOT NULL DEFAULT GETDATE(),
    ColorHex    NVARCHAR(9) NULL,                   -- optional navbar brand color, e.g. '#1e40af'
    -- OPTIONAL — ticketing-only columns; drop them for pure user management:
    TicketPrefix        NVARCHAR(20) NULL,          -- overrides ShortCode on ticket numbers
    CategoryRequired    BIT NOT NULL DEFAULT 1,
    SubcategoryRequired BIT NOT NULL DEFAULT 0
);

-- 3) Per-user-per-group permissions
CREATE TABLE TechnicianGroupMembers (
    MemberId        INT IDENTITY(1,1) PRIMARY KEY,
    UserId          INT NOT NULL REFERENCES Users(UserId),
    GroupId         INT NOT NULL REFERENCES TechnicianGroups(GroupId),
    CanDelete       BIT NOT NULL DEFAULT 0,
    CanAssign       BIT NOT NULL DEFAULT 0,
    CanPickupOthers BIT NOT NULL DEFAULT 0,
    CanMerge        BIT NOT NULL DEFAULT 0,         -- may merge tickets in this group (SiteAdmin always can)
    AssignedAt      DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT UQ_TechGroupMember UNIQUE (UserId, GroupId)
);

-- 4) AD config keys (rows in your existing SiteConfiguration K/V table).
--    INSERT only if not present; admins customize values via the UI.
INSERT INTO SiteConfiguration (ConfigKey, ConfigValue) VALUES
    ('AdDomain',          ''),
    ('AdLdapPath',        ''),
    ('AdServiceUser',     ''),
    ('AdServicePassword', ''),
    ('AutoCreateAdUsers', 'false');

-- 5) First admin — NO seeded row.
--    There is deliberately no built-in 'admin' account and no well-known
--    placeholder hash (that pattern is a remote-takeover risk and was removed).
--    Create the first SiteAdmin in ONE of these ways:
--      (a) Hit /Account/Setup ONCE from the server box. It is loopback-gated
--          (refuses non-local requests) and refuses if any admin already
--          exists, then creates a SiteAdmin with a random temp password it
--          prints once. See AccountController.snippet.cs.
--      (b) Insert one row by hand, then sign in (AD validates the password):
--          INSERT INTO Users (EmployeeId, AdUsername, FullName, Email, Role, IsActive, PasswordHash)
--          VALUES ('E001', 'firstadmin', 'First Admin', 'admin@company.com', 'SiteAdmin', 1, '');
--      After the first admin exists, manage everyone else from /Admin/Users.
