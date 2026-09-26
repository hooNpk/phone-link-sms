using System.Text.RegularExpressions;

namespace PhoneLinkSmsApp.Services;

public static partial class PhoneNumberNormalizer
{
    [GeneratedRegex(@"^(010\d{8}|01[16789]\d{7,8})$")]
    private static partial Regex MobileDigits();

    /// <summary>휴대폰 번호를 010-XXXX-XXXX 형식으로 바꾼다. 휴대폰 번호가 아니면 null.</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());

        // 국가번호 (+82 10-..., +82 010-...)
        if (digits.StartsWith("820")) digits = digits[2..];
        else if (digits.StartsWith("82") && digits.Length is 11 or 12) digits = "0" + digits[2..];

        // 엑셀이 숫자로 저장하면서 앞자리 0이 사라진 경우 (1012345678)
        if (digits.Length is 9 or 10 && digits[0] == '1') digits = "0" + digits;

        if (!MobileDigits().IsMatch(digits)) return null;

        return digits.Length == 11
            ? $"{digits[..3]}-{digits[3..7]}-{digits[7..]}"
            : $"{digits[..3]}-{digits[3..6]}-{digits[6..]}";
    }
}
