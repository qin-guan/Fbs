using System.Collections.Concurrent;

namespace Fbs.WebApi.Endpoints.Auth;

/// <summary>
/// Limits how many times each login code can be tried, so a six digit code can't be guessed.
/// </summary>
/// <remarks>
/// Attempts are counted in memory, per phone number and code (identified by its stored hash, which
/// is different for every code that is sent). Asking for a new code starts a new count. The count is lost when the app restarts, which only gives a guesser a fresh few tries at
/// a code that also expires after <see cref="Lifetime"/>.
/// </remarks>
public sealed class OtpAttemptTracker
{
    /// <summary>How long a code can be used for after it was sent.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public const int MaxAttempts = 5;

    private readonly ConcurrentDictionary<string, (string CodeHash, int Attempts)> _attempts = new();

    /// <summary>
    /// Counts an attempt at the code with the stored hash <paramref name="codeHash"/> before it is
    /// checked, so parallel guesses can't all slip past the limit.
    /// </summary>
    /// <returns>False once the code has been tried <see cref="MaxAttempts"/> times.</returns>
    public bool TryStartAttempt(string phone, string codeHash)
    {
        var entry = _attempts.AddOrUpdate(
            phone,
            _ => (codeHash, 1),
            (_, current) => current.CodeHash == codeHash ? (codeHash, current.Attempts + 1) : (codeHash, 1)
        );

        return entry.Attempts <= MaxAttempts;
    }

    /// <summary>Forgets the attempts once a code has been used.</summary>
    public void Reset(string phone) => _attempts.TryRemove(phone, out _);
}
