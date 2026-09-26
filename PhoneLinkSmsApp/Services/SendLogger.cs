using System.Text;
using PhoneLinkSmsApp.Models;
using PhoneLinkSmsApp.Settings;

namespace PhoneLinkSmsApp.Services;

/// <summary>발송 1회분 로그 폴더: 발송로그.csv + 문구.txt + 실패 스크린샷</summary>
public sealed class SendLogger
{
    static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    readonly object _lock = new();

    public string Folder { get; }
    public string CsvPath => Path.Combine(Folder, "발송로그.csv");

    public SendLogger(string suffix = "")
    {
        Folder = Path.Combine(AppSettings.LogsDir, DateTime.Now.ToString("yyyyMMdd_HHmmss") + suffix);
        Directory.CreateDirectory(Folder);
        // BOM이 있어야 엑셀에서 한글이 깨지지 않는다
        File.WriteAllText(CsvPath, "시각,순번,이름,전화번호,결과,내용\r\n", new UTF8Encoding(true));
    }

    public void SaveMessage(string message, string? imagePath)
    {
        var text = message + (imagePath == null ? "" : $"\r\n\r\n[이미지] {imagePath}");
        File.WriteAllText(Path.Combine(Folder, "문구.txt"), text, new UTF8Encoding(true));
    }

    public void Log(Recipient r, string result, string detail)
    {
        var line = string.Join(",",
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), r.SendIndex, Csv(r.Name), Csv(r.NormalizedPhone ?? r.Phone), Csv(result), Csv(detail)) + "\r\n";
        lock (_lock) File.AppendAllText(CsvPath, line, Utf8NoBom);
    }

    public string ScreenshotPath(Recipient r, string kind) =>
        Path.Combine(Folder, $"{r.SendIndex:000}_{new string((r.NormalizedPhone ?? "").Where(char.IsAsciiDigit).ToArray())}_{kind}.png");

    static string Csv(string s) => s.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
