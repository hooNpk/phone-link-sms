namespace PhoneLinkSmsApp;

/// <summary>프로그램 밖으로 연결되는 주소 모음</summary>
public static class AppLinks
{
    /// <summary>
    /// 후원 페이지 주소 (예: 토스 송금 링크, 카카오페이 송금 링크).
    /// 비워 두면 발송 완료 화면에 후원 안내가 나오지 않는다.
    /// </summary>
    public static string DonationUrl { get; set; } = "";

    public static bool HasDonation => Uri.TryCreate(DonationUrl, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;
}
