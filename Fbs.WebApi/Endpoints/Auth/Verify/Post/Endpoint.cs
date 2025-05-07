using System.Security.Cryptography;
using System.Text;
using FastEndpoints;
using FastEndpoints.Security;
using Fbs.WebApi.Repository;
using Org.BouncyCastle.Crypto.Generators;

namespace Fbs.WebApi.Endpoints.Auth.Verify.Post;

public class Endpoint(
    ILogger<Endpoint> logger,
    OtpRepository otpRepository,
    UserRepository userRepository
) : Endpoint<Request>
{
    public override void Configure()
    {
        Post("/Auth/Verify");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var otp = await otpRepository.FindAsync(o => o.Phone == req.Phone, ct);
        if (otp is null or { Code: null })
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var hash = SCryptGenerate(Encoding.Default.GetBytes(req.Code), Encoding.Default.GetBytes(req.Phone));

        var valid = CryptographicOperations.FixedTimeEquals(Convert.FromHexString(otp.Code), hash);
        if (!valid)
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var user = await userRepository.FindAsync(u => u.Phone == req.Phone);
        if (user is null or { Phone: null })
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        await otpRepository.DeleteAsync(o => o.Phone == req.Phone, ct);
        
        logger.LogInformation("User {Phone} verified OTP", user.Phone);

        await CookieAuth.SignInAsync(u => { u["Phone"] = user.Phone; });
    }

    private static byte[] SCryptGenerate(byte[] password, byte[] salt)
    {
        // Reference : https://nodejs.org/api/crypto.html#cryptoscryptsyncpassword-salt-keylen-options
        return SCrypt.Generate(password, salt, 16384, 8, 1, 64);
    }
}