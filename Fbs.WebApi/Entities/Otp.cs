using Fbs.WebApi.Repository;

namespace Fbs.WebApi.Entities;

public class Otp : Entity
{
    public string? Phone { get; set; }
    public string? Code { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    public override string GetId() => Phone;
}