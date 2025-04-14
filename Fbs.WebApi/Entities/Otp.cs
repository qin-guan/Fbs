namespace Fbs.WebApi.Entities;

public class Otp : SheetEntity
{
    public required string Phone { get; set; }
    public required string Code { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
}