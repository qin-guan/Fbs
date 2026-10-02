namespace Fbs.WebApi.Endpoints.Org.Units.ById.Put;

public class Request
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
