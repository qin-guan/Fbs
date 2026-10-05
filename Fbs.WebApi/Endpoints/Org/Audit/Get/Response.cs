namespace Fbs.WebApi.Endpoints.Org.Audit.Get;

public class Person
{
    public Guid MemberId { get; init; }

    public required string DisplayName { get; init; }
}

public class Response
{
    public Guid Id { get; init; }

    public DateTimeOffset At { get; init; }

    /// <summary>Who did it. Nobody, if it was done by whoever runs the system.</summary>
    public Person? Actor { get; init; }

    public required string Action { get; init; }

    public required string Summary { get; init; }

    public string? TargetType { get; init; }

    public Guid? TargetId { get; init; }

    /// <summary>Who it was done to, if it was done to a person.</summary>
    public Person? Target { get; init; }
}
