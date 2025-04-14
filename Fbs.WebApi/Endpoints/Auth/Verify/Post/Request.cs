namespace Fbs.WebApi.Endpoints.Auth.Verify.Post;

public class Request
{
    public string Phone { get; set; }
    public string Code { get; set; }
}