namespace Fbs.WebApi.Data;

public static class DbErrors
{
    /// <summary>Whether it is the database refusing a row because a unique index already has one like it.</summary>
    public static bool IsDuplicate(this Exception exception) => exception.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase);
}
