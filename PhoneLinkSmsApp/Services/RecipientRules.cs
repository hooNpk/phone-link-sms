using PhoneLinkSmsApp.Models;

namespace PhoneLinkSmsApp.Services;

public static class SmsOptOut
{
    static readonly string[] RefusedWords = ["거부", "미동의", "비동의", "미수신", "불가", "반대"];
    static readonly string[] RefusedExact = ["n", "no", "x", "false", "0", "아니오", "아니요"];

    /// <summary>SMS 수신여부 칸 값이 수신거부를 뜻하는지. 빈 칸은 수신으로 본다.</summary>
    public static bool IsRefused(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value.Trim().ToLowerInvariant();
        return RefusedWords.Any(v.Contains) || RefusedExact.Contains(v);
    }
}

public static class RecipientValidator
{
    /// <summary>
    /// 번호 형식·중복·수신거부를 다시 판정한다.
    /// resetInclude가 false면 판정 결과가 바뀐 행만 포함 여부를 기본값으로 돌린다 (사용자가 직접 끈 체크는 유지).
    /// </summary>
    public static void Revalidate(IEnumerable<Recipient> recipients, bool includeRefused, bool resetInclude)
    {
        var firstRowByPhone = new Dictionary<string, int>();
        foreach (var r in recipients)
        {
            var normalized = PhoneNumberNormalizer.Normalize(r.Phone);
            r.NormalizedPhone = normalized;

            RecipientIssue issue;
            string reason;
            if (normalized == null)
            {
                issue = RecipientIssue.InvalidPhone;
                reason = string.IsNullOrWhiteSpace(r.Phone) ? "전화번호 없음" : "번호 형식 오류";
            }
            else if (firstRowByPhone.TryGetValue(normalized, out var firstRow))
            {
                issue = RecipientIssue.Duplicate;
                reason = $"중복 ({firstRow}행과 같은 번호)";
            }
            else
            {
                firstRowByPhone[normalized] = r.RowNumber;
                var refused = SmsOptOut.IsRefused(r.SmsValue);
                issue = refused ? RecipientIssue.SmsRefused : RecipientIssue.None;
                reason = refused ? $"SMS 수신거부 ({r.SmsValue})" : "";
            }

            var changed = r.Issue != issue;
            r.Issue = issue;
            r.Reason = reason;
            if (resetInclude || changed)
                r.Include = issue == RecipientIssue.None || (issue == RecipientIssue.SmsRefused && includeRefused);
            if (!r.CanSend) r.Include = false;
        }
    }
}
