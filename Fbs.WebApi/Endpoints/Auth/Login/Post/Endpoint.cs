using System.Security.Cryptography;
using System.Text;
using FastEndpoints;
using Fbs.WebApi.Repository;
using Org.BouncyCastle.Crypto.Generators;
using Telegram.Bot;

namespace Fbs.WebApi.Endpoints.Auth.Login.Post;

public class Endpoint(
    ILogger<Endpoint> logger,
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
        switch (user)
        {
            case null:
                AddError(r => r.Phone, "User is not allow-listed.", "EX01");
                await SendErrorsAsync(cancellation: ct);
                return;
            case { TelegramChatId: null or { Length: 0 } }:
                AddError(r => r.Phone, "User is not registered on Telegram.", "EX02");
                await SendErrorsAsync(cancellation: ct);
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
        if (existingOtp is not null && existingOtp.CreatedAt + TimeSpan.FromSeconds(60) > DateTimeOffset.UtcNow)
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        if (existingOtp is not null)
        {
            existingOtp.Code = hash;
            existingOtp.CreatedAt = DateTimeOffset.UtcNow;
            await otpRepository.UpdateAsync(existingOtp, ct);
        }
        else
        {
            await otpRepository.InsertAsync(new Entities.Otp
            {
                Phone = req.Phone,
                Code = hash,
                CreatedAt = DateTimeOffset.UtcNow
            }, ct);
        }
        
        logger.LogInformation("User {Phone} requested for OTP", user.Phone);
        
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