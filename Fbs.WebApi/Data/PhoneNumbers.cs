namespace Fbs.WebApi.Data;

/// <summary>
/// Phone numbers are stored in E.164 form, such as +6591234567. The API still speaks the form the
/// spreadsheet used, without the plus, until it stops needing to.
/// </summary>
public static class PhoneNumbers
{
    /// <summary>From the form the API uses, 6591234567, to the form that is stored.</summary>
    public static string? ToStored(string? phone)
    {
        var digits = Digits(phone);
        return digits.Length == 0 ? null : "+" + digits;
    }

    /// <summary>From the form that is stored to the form the API uses.</summary>
    public static string? ToApi(string? stored)
    {
        var digits = Digits(stored);
        return digits.Length == 0 ? null : digits;
    }

    private static string Digits(string? phone) =>
        new((phone ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
}
