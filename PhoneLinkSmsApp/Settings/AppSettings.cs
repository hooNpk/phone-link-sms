using System.Text.Json;

namespace PhoneLinkSmsApp.Settings;

/// <summary>%AppData%\PhoneLinkSms\settings.json</summary>
public sealed class AppSettings
{
    const int MaxRecent = 10;

    public static string Dir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhoneLinkSms");
    public static string LogsDir => Path.Combine(Dir, "logs");
    static string FilePath => Path.Combine(Dir, "settings.json");

    public List<string> RecentFiles { get; set; } = [];
    public List<string> RecentMessages { get; set; } = [];
    public int IntervalSeconds { get; set; }
    public string TestPhone { get; set; } = "";

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* 설정 파일이 깨졌으면 기본값으로 시작 */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 설정 저장 실패는 발송에 영향 없음 */ }
    }

    public void AddRecentFile(string path) => AddRecent(RecentFiles, path);
    public void AddRecentMessage(string message) => AddRecent(RecentMessages, message);

    static void AddRecent(List<string> list, string value)
    {
        list.Remove(value);
        list.Insert(0, value);
        if (list.Count > MaxRecent) list.RemoveRange(MaxRecent, list.Count - MaxRecent);
    }
}
