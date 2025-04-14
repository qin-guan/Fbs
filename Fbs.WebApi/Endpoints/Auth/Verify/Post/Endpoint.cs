using System.Security.Cryptography;
using System.Text;
using FastEndpoints;
using FastEndpoints.Security;
using Fbs.WebApi.Services.GoogleSheets;
using Org.BouncyCastle.Crypto.Generators;

namespace Fbs.WebApi.Endpoints.Auth.Verify.Post;

public class Endpoint(
    GoogleSheetsUsersService usersService,
    GoogleSheetsOtpService sheetsOtpService
) : Endpoint<Request>
{
    public override void Configure()
    {
        Post("/Auth/Verify");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var hash = SCryptGenerate(Encoding.Default.GetBytes(req.Code), Encoding.Default.GetBytes(req.Phone));

        var otp = await sheetsOtpService.GetOtpAsync(req.Phone);
        if (otp is null)
        {
            throw new ArgumentNullException();
        }

        var valid = CryptographicOperations.FixedTimeEquals(Convert.FromHexString(otp.Code), hash);
        if (!valid)
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var users = await usersService.GetAsync();
        var user = users.Single(u => u.Phone == req.Phone);

        await sheetsOtpService.RemoveOtpAsync(req.Phone);

        await CookieAuth.SignInAsync(u =>
        {
            u["Phone"] = req.Phone;
            u["Name"] = user.Name;
        });
    }

    private static byte[] SCryptGenerate(byte[] password, byte[] salt)
    {
        // Reference : https://nodejs.org/api/crypto.html#cryptoscryptsyncpassword-salt-keylen-options
        return SCrypt.Generate(password, salt, 16384, 8, 1, 64);
    }
}