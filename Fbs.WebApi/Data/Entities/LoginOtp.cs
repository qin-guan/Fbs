using SqlSugar;

namespace Fbs.WebApi.Data.Entities;

/// <summary>
/// The code last sent to sign in with a phone number. Goes away with signing in by phone number.
/// </summary>
[SugarIndex("UX_LoginOtp_Phone", nameof(Phone), OrderByType.Asc, IsUnique = true)]
public class LoginOtp
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid Id { get; set; }

    /// <summary>In E.164 form.</summary>
    [SugarColumn(Length = 32)]
    public string Phone { get; set; } = null!;

    /// <summary>A hash of the code, so the code itself is never kept.</summary>
    [SugarColumn(Length = 256)]
    public string CodeHash { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
