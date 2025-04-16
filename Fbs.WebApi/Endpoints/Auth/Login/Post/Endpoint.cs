using System.Security.Cryptography;
using System.Text;
using FastEndpoints;
using Fbs.WebApi.Repository;
using Org.BouncyCastle.Crypto.Generators;
using Telegram.Bot;

namespace Fbs.WebApi.Endpoints.Auth.Login.Post;

public class Endpoint(
    OtpRepository otpRepository,
    UserRepository userRepository,
    TelegramBotClient client
) : Endpoint<Request>
{
    public override void Configure()
    {
        Post("/Auth/Login");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var user = await userRepository.FindAsync(u => u.Phone == req.Phone, ct);
        if (user is null or { TelegramChatId: null })
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var code = RandomNumberGenerator.GetString("1234567890", 6);
        var hash = Convert.ToHexString(
            SCryptGenerate(
                Encoding.Default.GetBytes(code),
                Encoding.Default.GetBytes(req.Phone)
            )
        );

        var existingOtp = await otpRepository.FindAsync(o => o.Phone == req.Phone, ct);
        if (existingOtp is not null)
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        await otpRepository.InsertAsync(new Entities.Otp
        {
            Phone = req.Phone,
            Code = hash,
            CreatedAt = DateTimeOffset.UtcNow
        }, ct);

        await client.SendMessage(
            user.TelegramChatId,
            $"Your login OTP is {code}",
            cancellationToken: ct
        );
    }

    private static byte[] SCryptGenerate(byte[] password, byte[] salt)
    {
        // Reference : https://nodejs.org/api/crypto.html#cryptoscryptsyncpassword-salt-keylen-options
        return SCrypt.Generate(password, salt, 16384, 8, 1, 64);
    }
}