namespace Fbs.WebApi.Tenancy;

public static class MemberPhones
{
    /// <summary>
    /// A phone number as it is stored (E.164, such as +6591234567), from what somebody typed, or null if it isn't
    /// a phone number. With a plus, or 00, it is taken as it is; without, it is in the organisation's country.
    /// </summary>
    public static string? Normalize(string? input, string defaultCountryCode)
    {
        var typed = input?.Trim();
        if (string.IsNullOrEmpty(typed) || !typed.All(c => char.IsAsciiDigit(c) || c is ' ' or '-' or '(' or ')' or '+'))
        {
            return null;
        }

        if (typed.Count(c => c == '+') > 1 || (typed.Contains('+') && typed[0] != '+'))
        {
            return null;
        }

        var digits = new string(typed.Where(char.IsAsciiDigit).ToArray());
        var international = typed[0] == '+' ? digits : typed.StartsWith("00", StringComparison.Ordinal) ? digits[2..] : defaultCountryCode + digits.TrimStart('0');
        return international.Length is >= 7 and <= 15 ? "+" + international : null;
    }
}
