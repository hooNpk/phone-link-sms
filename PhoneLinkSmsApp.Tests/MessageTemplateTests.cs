using PhoneLinkSmsApp.Services;

namespace PhoneLinkSmsApp.Tests;

public class MessageTemplateTests
{
    [Theory]
    [InlineData("{이름}님 안녕하세요", "홍길동님 안녕하세요")]
    [InlineData("{{이름}}님 안녕하세요", "홍길동님 안녕하세요")]
    [InlineData("{ 이름 }님", "홍길동님")]
    [InlineData("{이름}님, {이름}님의 참석을 기다립니다", "홍길동님, 홍길동님의 참석을 기다립니다")]
    [InlineData("이름 없는 문구", "이름 없는 문구")]
    public void Replaces_name_token(string template, string expected) =>
        Assert.Equal(expected, MessageTemplate.Render(template, " 홍길동 "));

    [Fact]
    public void Empty_name_leaves_blank() =>
        Assert.Equal("님 안녕하세요", MessageTemplate.Render("{이름}님 안녕하세요", null));

    [Theory]
    [InlineData("{이름}님", true)]
    [InlineData("{{이름}}", true)]
    [InlineData("이름을 적어 주세요", false)]
    [InlineData("{성명}", false)]
    public void Detects_name_token(string template, bool uses) =>
        Assert.Equal(uses, MessageTemplate.UsesName(template));
}
