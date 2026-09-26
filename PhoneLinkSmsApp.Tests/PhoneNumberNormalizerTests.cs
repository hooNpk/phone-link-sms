using PhoneLinkSmsApp.Services;

namespace PhoneLinkSmsApp.Tests;

public class PhoneNumberNormalizerTests
{
    [Theory]
    [InlineData("010-1234-5678", "010-1234-5678")]
    [InlineData("01012345678", "010-1234-5678")]
    [InlineData("010 1234 5678", "010-1234-5678")]
    [InlineData("010.1234.5678", "010-1234-5678")]
    [InlineData("1012345678", "010-1234-5678")]          // 엑셀 숫자 셀에서 앞 0이 빠진 경우
    [InlineData("+82 10-1234-5678", "010-1234-5678")]
    [InlineData("+82 010-1234-5678", "010-1234-5678")]
    [InlineData("011-123-4567", "011-123-4567")]
    [InlineData("016-1234-5678", "016-1234-5678")]
    public void Normalizes_mobile_numbers(string raw, string expected) =>
        Assert.Equal(expected, PhoneNumberNormalizer.Normalize(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("02-123-4567")]      // 유선
    [InlineData("031-234-5678")]
    [InlineData("010-123-4567")]     // 010은 11자리여야 함
    [InlineData("010-1234-56789")]
    [InlineData("없음")]
    public void Rejects_non_mobile_numbers(string? raw) =>
        Assert.Null(PhoneNumberNormalizer.Normalize(raw));
}
