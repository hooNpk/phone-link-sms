using System.Text.RegularExpressions;

namespace PhoneLinkSmsApp.Services;

/// <summary>
/// 문구 속 {이름} 자리를 받는 사람 이름으로 바꾼다. {{이름}}, { 이름 } 처럼 써도 된다.
/// </summary>
public static partial class MessageTemplate
{
    public const string NameToken = "{이름}";

    [GeneratedRegex(@"\{\{?\s*이름\s*\}?\}")]
    private static partial Regex NamePattern();

    public static bool UsesName(string template) => NamePattern().IsMatch(template);

    public static string Render(string template, string? name) =>
        NamePattern().Replace(template, (name ?? "").Trim());
}
