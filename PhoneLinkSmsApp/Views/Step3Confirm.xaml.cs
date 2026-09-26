using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PhoneLinkSmsApp.Models;
using PhoneLinkSmsApp.Services;

namespace PhoneLinkSmsApp.Views;

public partial class Step3Confirm : UserControl, IStepView
{
    /// <summary>1건당 대략 걸리는 시간 (받는 사람 확정·전송 확인 대기 포함)</summary>
    public const int SecondsPerMessage = 12;
    public const int ExtraSecondsForImage = 4;

    readonly MainWindow _main;
    ConnectionState? _connection;   // null = 확인 중
    bool _testRunning;
    bool _testFailed;
    string? _testLogFolder;

    public Step3Confirm(MainWindow main)
    {
        InitializeComponent();
        _main = main;
    }

    // ── 하단 바 ──
    public string NextLabel => "전체 발송 시작  ▶";
    public bool CanGoBack => !_testRunning;
    bool ConnectionBlocked => _connection is ConnectionState.Disconnected or ConnectionState.AppNotRunning;
    public bool CanGoNext => _main.Session.TestPassed && _main.Session.Targets.Any() && !ConnectionBlocked && !_testRunning;
    public string Hint =>
        _testRunning ? "테스트 발송 중입니다. 마우스와 키보드를 만지지 마세요."
        : ConnectionBlocked ? "① 휴대폰 연결을 먼저 확인하세요."
        : !_main.Session.TestPassed ? "② 나에게 먼저 보내 보기를 마쳐야 시작할 수 있습니다."
        : "준비가 끝났습니다. 휴대폰에서 테스트 문자를 확인했다면 시작하세요.";

    public void OnEnter()
    {
        var session = _main.Session;
        var targets = session.Targets.Count();
        var all = session.Recipients;
        TargetText.Text = $"{targets}명";
        int refused = all.Count(r => r.Issue == RecipientIssue.SmsRefused && !r.Include);
        int invalid = all.Count(r => r.Issue is RecipientIssue.InvalidPhone or RecipientIssue.Duplicate);
        int manual = all.Count(r => r.Issue == RecipientIssue.None && !r.Include);
        ExcludedText.Text = $"명단 {all.Count}명 중 빠진 사람 {all.Count - targets}명 — 수신거부 {refused} · 번호 오류/중복 {invalid} · 직접 뺀 사람 {manual}";

        // {이름}이 있으면 첫 번째 받는 사람 기준으로 보여 준다
        var first = session.Targets.FirstOrDefault();
        PreviewText.Text = MessageTemplate.Render(session.Message, first?.Name);
        PreviewText.Visibility = session.Message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowNameNote(PreviewNote, session);
        PreviewImage.Source = session.ImagePath == null ? null : Step2Compose.LoadThumbnail(session.ImagePath, 400);
        PreviewImage.Visibility = session.ImagePath == null ? Visibility.Collapsed : Visibility.Visible;

        IntervalBox.Text = _main.Settings.IntervalSeconds.ToString();
        if (string.IsNullOrEmpty(TestPhoneBox.Text)) TestPhoneBox.Text = _main.Settings.TestPhone;

        UpdateEstimate();
        UpdateTestState();
        _ = RefreshConnectionAsync();
    }

    /// <summary>{이름} 사용 시 안내: 누구 기준 미리보기인지, 이름이 빈 사람이 몇 명인지</summary>
    public static void ShowNameNote(TextBlock note, SendSession session)
    {
        if (!MessageTemplate.UsesName(session.Message))
        {
            note.Visibility = Visibility.Collapsed;
            return;
        }
        var targets = session.Targets.ToList();
        int noName = targets.Count(r => string.IsNullOrWhiteSpace(r.Name));
        var first = targets.FirstOrDefault();
        note.Text = (first == null ? "{이름}은 받는 사람마다 그 사람 이름으로 바뀝니다."
                        : $"{{이름}}은 받는 사람마다 바뀝니다. 아래는 첫 번째 사람({first.Name}) 기준입니다.")
                    + (noName > 0 ? $"\n⚠ 이름이 비어 있는 {noName}명은 {{이름}} 자리가 빈칸으로 나갑니다. 1단계 표에서 이름을 채울 수 있습니다." : "");
        note.Foreground = (Brush)note.FindResource(noName > 0 ? "WarningBrush" : "AccentBrush");
        note.Visibility = Visibility.Visible;
    }

    public void OnNext()
    {
        var count = _main.Session.Targets.Count();
        var answer = MessageBox.Show(
            $"{count}명에게 문자를 보냅니다.\n\n발송하는 동안에는 마우스와 키보드를 만지지 마세요.\n시작할까요?",
            "전체 발송", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        if (_main.Session.Message.Length > 0) _main.Settings.AddRecentMessage(_main.Session.Message);
        _main.Settings.Save();
        _main.GoTo(MainWindow.StepSend);
        _main.SendView.Start(SendMode.All);
    }

    public static TimeSpan Estimate(int count, bool hasImage, int intervalSeconds) =>
        TimeSpan.FromSeconds(count * (SecondsPerMessage + (hasImage ? ExtraSecondsForImage : 0) + intervalSeconds));

    public static string FormatDuration(TimeSpan t) =>
        t.TotalMinutes >= 60 ? $"{(int)t.TotalHours}시간 {t.Minutes}분"
        : t.TotalMinutes >= 1 ? $"{(int)Math.Ceiling(t.TotalMinutes)}분"
        : $"{Math.Max(1, (int)t.TotalSeconds)}초";

    void UpdateEstimate()
    {
        var interval = _main.Settings.IntervalSeconds;
        var total = Estimate(_main.Session.Targets.Count(), _main.Session.ImagePath != null, interval);
        EstimateText.Text = $"⏱  예상 소요 시간 약 {FormatDuration(total)}   (그동안 PC를 쓸 수 없습니다)";
        IntervalHeader.Text = $"발송 간격 설정 (선택) — 지금: {(interval == 0 ? "쉬지 않고 연속 발송" : $"{interval}초씩 쉬기")}";
    }

    void UpdateTestState()
    {
        var session = _main.Session;
        if (_testRunning) SetBadge(Badge2, Badge2Text, "…", "MutedBrush");
        else if (session.TestPassed) SetBadge(Badge2, Badge2Text, "✓", "SuccessBrush");
        else if (_testFailed) SetBadge(Badge2, Badge2Text, "!", "DangerBrush");
        else SetBadge(Badge2, Badge2Text, "2", "AccentBrush");

        if (!_testRunning && !session.TestPassed && session.TestPassedFor != null)
            ShowTestResult("문구나 사진이 바뀌었습니다. 테스트 발송을 다시 해 주세요.", "WarningBrush", "WarningSoftBrush");
        TestButton.IsEnabled = !_testRunning;
        _main.RefreshNav();
    }

    async Task RefreshConnectionAsync()
    {
        _connection = null;
        SetBadge(Badge1, Badge1Text, "…", "MutedBrush");
        StatusText.Text = "확인하는 중...";
        StatusHelp.Visibility = Visibility.Collapsed;
        _main.RefreshNav();

        var (state, detail) = await StaThread.Run(() =>
        {
            var s = new PhoneLinkAutomation().CheckConnection(out var d);
            return (s, d);
        });
        _connection = state;
        switch (state)
        {
            case ConnectionState.Connected:
                SetBadge(Badge1, Badge1Text, "✓", "SuccessBrush");
                StatusText.Text = "'휴대폰과 연결' 앱에 휴대폰이 연결되어 있습니다.";
                break;
            case ConnectionState.Unknown:
                SetBadge(Badge1, Badge1Text, "?", "WarningBrush");
                StatusText.Text = "앱은 열려 있지만 연결 상태를 읽지 못했습니다. 테스트 발송으로 확인하세요.";
                break;
            default:
                SetBadge(Badge1, Badge1Text, "!", "DangerBrush");
                StatusText.Text = state == ConnectionState.AppNotRunning ? "'휴대폰과 연결' 앱 창이 열려 있지 않습니다." : $"휴대폰 연결이 끊겨 있습니다 ({detail}).";
                StatusHelp.Visibility = Visibility.Visible;
                break;
        }
        _main.RefreshNav();
    }

    void OnRefreshConnection(object sender, RoutedEventArgs e) => _ = RefreshConnectionAsync();

    void OnTestPhoneChanged(object sender, TextChangedEventArgs e) =>
        TestPhonePlaceholder.Visibility = TestPhoneBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    void OnIntervalChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(IntervalBox.Text, out var seconds) && seconds is >= 0 and <= 600)
        {
            _main.Settings.IntervalSeconds = seconds;
            IntervalBox.ClearValue(Control.BorderBrushProperty);
        }
        else
        {
            IntervalBox.BorderBrush = (Brush)FindResource("DangerBrush");
        }
        UpdateEstimate();
    }

    async void OnTestSend(object sender, RoutedEventArgs e)
    {
        var phone = PhoneNumberNormalizer.Normalize(TestPhoneBox.Text);
        if (phone == null)
        {
            ShowTestResult("휴대폰 번호를 정확히 입력해 주세요. (예: 010-1234-5678)", "DangerBrush", "DangerSoftBrush");
            TestPhoneBox.Focus();
            return;
        }
        TestPhoneBox.Text = phone;
        _main.Settings.TestPhone = phone;
        _main.Settings.Save();

        var session = _main.Session;
        var testRecipient = new Recipient { Name = "(테스트)", Phone = phone, NormalizedPhone = phone };
        var signature = session.ContentSignature;
        var logger = new SendLogger("_테스트");
        logger.SaveMessage(session.Message, session.ImagePath);

        string lastError = "";
        var progress = new Progress<SendProgress>(p => { if (p.Detail.Length > 0) lastError = p.Detail; });

        _testRunning = true;
        _testFailed = false;
        ShowTestResult("보내는 중... 마우스와 키보드를 만지지 마세요.", "TextBrush", "AccentSoftBrush");
        UpdateTestState();
        _main.IsSending = true;
        SendResult result;
        try
        {
            result = await new SendWorker().RunAsync(
                new SendJob([testRecipient], session.Message, session.ImagePath, 0, logger, CaptureOnSuccess: true,
                    NameOverride: session.Targets.FirstOrDefault()?.Name), progress);
        }
        catch (Exception ex)
        {
            result = new SendResult(SendOutcome.Aborted, 0, 0, ex.Message);
        }
        finally
        {
            _testRunning = false;
            _main.IsSending = false;
            _main.Activate();
        }

        // Progress 콜백이 먼저 처리되도록 한 번 양보
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);

        if (result.Sent == 1)
        {
            session.TestPassedFor = signature;
            ShowTestResult($"✓ {phone}(으)로 보냈습니다.\n휴대폰에서 문자가 제대로 왔는지 확인하세요.", "SuccessBrush", "SuccessSoftBrush");
        }
        else
        {
            _testFailed = true;
            ShowTestResult($"보내지 못했습니다: {result.AbortReason ?? lastError}", "DangerBrush", "DangerSoftBrush");
            _testLogFolder = logger.Folder;
            TestLogLink.Visibility = Visibility.Visible;
        }
        UpdateTestState();
        _ = RefreshConnectionAsync();
    }

    void OnOpenTestLog(object sender, RoutedEventArgs e) => MainWindow.OpenLogFolder(_testLogFolder);

    void ShowTestResult(string text, string fg, string bg)
    {
        TestLogLink.Visibility = Visibility.Collapsed;   // 실패일 때만 호출한 쪽에서 다시 켠다
        TestStatus.Text = text;
        TestStatus.Foreground = (Brush)FindResource(fg);
        TestResultBox.Background = (Brush)FindResource(bg);
        TestResultBox.Visibility = Visibility.Visible;
    }

    void SetBadge(Border badge, TextBlock text, string symbol, string brushKey)
    {
        badge.Background = (Brush)FindResource(brushKey);
        text.Text = symbol;
    }
}
