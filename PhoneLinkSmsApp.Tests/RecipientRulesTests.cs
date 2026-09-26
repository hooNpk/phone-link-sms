using PhoneLinkSmsApp.Models;
using PhoneLinkSmsApp.Services;

namespace PhoneLinkSmsApp.Tests;

public class RecipientRulesTests
{
    [Theory]
    [InlineData("거부", true)]
    [InlineData("수신거부", true)]
    [InlineData("미동의", true)]
    [InlineData("N", true)]
    [InlineData("수신", false)]
    [InlineData("동의", false)]
    [InlineData("Y", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Detects_sms_opt_out(string? value, bool refused) =>
        Assert.Equal(refused, SmsOptOut.IsRefused(value));

    static Recipient R(int row, string phone, string sms = "수신") =>
        new() { RowNumber = row, Name = $"사람{row}", Phone = phone, SmsValue = sms };

    [Fact]
    public void Marks_invalid_duplicate_and_refused_rows()
    {
        var list = new List<Recipient>
        {
            R(2, "010-1111-2222"),
            R(3, "01011112222"),          // 2행과 같은 번호
            R(4, "02-123-4567"),
            R(5, "010-3333-4444", "거부"),
            R(6, ""),
        };

        RecipientValidator.Revalidate(list, includeRefused: false, resetInclude: true);

        Assert.Equal(RecipientIssue.None, list[0].Issue);
        Assert.True(list[0].Include);
        Assert.Equal(RecipientIssue.Duplicate, list[1].Issue);
        Assert.Contains("2행", list[1].Reason);
        Assert.False(list[1].Include);
        Assert.Equal(RecipientIssue.InvalidPhone, list[2].Issue);
        Assert.False(list[2].Include);
        Assert.Equal(RecipientIssue.SmsRefused, list[3].Issue);
        Assert.False(list[3].Include);
        Assert.Equal("전화번호 없음", list[4].Reason);
    }

    [Fact]
    public void Include_refused_option_includes_refused_rows()
    {
        var list = new List<Recipient> { R(2, "010-3333-4444", "거부") };
        RecipientValidator.Revalidate(list, includeRefused: true, resetInclude: true);
        Assert.True(list[0].Include);
    }

    [Fact]
    public void Revalidate_keeps_manual_exclusion_but_includes_fixed_number()
    {
        var list = new List<Recipient> { R(2, "010-1111-2222"), R(3, "010-12") };
        RecipientValidator.Revalidate(list, includeRefused: false, resetInclude: true);

        list[0].Include = false;      // 사용자가 직접 뺌
        list[1].Phone = "010-5555-6666"; // 오탈자 수정
        RecipientValidator.Revalidate(list, includeRefused: false, resetInclude: false);

        Assert.False(list[0].Include);
        Assert.Equal(RecipientIssue.None, list[1].Issue);
        Assert.True(list[1].Include);
    }
}
