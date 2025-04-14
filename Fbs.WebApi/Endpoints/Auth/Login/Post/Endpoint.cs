using System.Security.Cryptography;
using System.Text;
using FastEndpoints;
using Fbs.WebApi.Services.GoogleSheets;
using Org.BouncyCastle.Crypto.Generators;
using Telegram.Bot;

namespace Fbs.WebApi.Endpoints.Auth.Login.Post;

public class Endpoint(
    GoogleSheetsUsersService usersService,
    GoogleSheetsOtpService otpService,
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
        var users = await usersService.GetAsync();
        var user = users.SingleOrDefault(u => u.Phone == req.Phone);

        if (user is null or { TelegramChatId : { Length: 0 } })
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

        await otpService.SetOtpAsync(req.Phone, hash);

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