using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PhoneLinkSmsApp.Models;
using PhoneLinkSmsApp.Services;
using PhoneLinkSmsApp.Settings;

namespace PhoneLinkSmsApp.Views;

public enum SendMode { All, ResumePending, RetryFailed }

public partial class Step4Send : UserControl, IStepView
{
    readonly MainWindow _main;
    readonly Stopwatch _clock = new();
    List<Recipient> _all = [];
    SendWorker? _worker;
    SendLogger? _logger;
    bool _paused;
    bool _donationDismissed;
    int _doneThisRun;

    public Step4Send(MainWindow main)
    {
        InitializeComponent();
        _main = main;
    }

    // ── 하단 바 ──
    public string NextLabel => _main.IsSending ? "" : "처음으로 (새 발송)";
    public bool CanGoNext => !_main.IsSending;
    public bool CanGoBack => false;
    public string Hint => _main.IsSending ? "발송 중에는 이 창을 닫지 마세요." : "";

    public void OnEnter() { }

    public void OnNext() => _main.GoTo(MainWindow.StepConnect);

    public void StopWorker() => _worker?.Stop();

    public async void Start(SendMode mode)
    {
        var session = _main.Session;
        if (mode == SendMode.All)
        {
            _all = session.Targets.ToList();
            for (int i = 0; i < _all.Count; i++)
            {
                _all[i].SendIndex = i + 1;
                _all[i].Status = SendStatus.Pending;
                _all[i].StatusMessage = "";
            }
            _logger = new SendLogger();
            _logger.SaveMessage(session.Message, session.ImagePath);
            ResultGrid.ItemsSource = _all;
            LogBox.Clear();
        }

        var targets = mode == SendMode.RetryFailed
            ? _all.Where(r => r.Status == SendStatus.Failed).ToList()
            : _all.Where(r => r.Status == SendStatus.Pending).ToList();
        if (targets.Count == 0 || _logger == null) return;
        foreach (var r in targets)
        {
            r.Status = SendStatus.Pending;
            r.StatusMessage = "";
        }

        _worker = new SendWorker();
        _paused = false;
        _doneThisRun = 0;
        _clock.Restart();
        SetRunning(true);
        UpdateProgress(targets.Count);
        AppendLog(mode switch
        {
            SendMode.All => $"발송 시작: {targets.Count}명",
            SendMode.ResumePending => $"{targets[0].SendIndex}번부터 재개: {targets.Count}명",
            _ => $"실패한 {targets.Count}명 다시 보내기",
        });

        SendResult result;
        try
        {
            result = await _worker.RunAsync(
                new SendJob(targets, session.Message, session.ImagePath, _main.Settings.IntervalSeconds, _logger),
                new Progress<SendProgress>(p => OnProgress(p, targets.Count)));
        }
        catch (Exception ex)
        {
            result = new SendResult(SendOutcome.Aborted, 0, 0, "예상치 못한 오류: " + ex.Message);
        }

        // 아직 처리되지 않은 Progress 콜백을 먼저 반영
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        _clock.Stop();
        SetRunning(false);
        Finish(result);
    }

    void OnProgress(SendProgress p, int runTotal)
    {
        var r = p.Recipient;
        r.Status = p.Status;
        r.StatusMessage = p.Detail;
        switch (p.Status)
        {
            case SendStatus.Sending:
                CurrentText.Text = $"지금: {r.SendIndex}번 {r.Name} ({r.NormalizedPhone})";
                ResultGrid.ScrollIntoView(r);
                break;
            case SendStatus.Sent:
                _doneThisRun++;
                AppendLog($"{r.SendIndex}번 {r.Name} {r.NormalizedPhone} 완료");
                break;
            case SendStatus.Failed:
                _doneThisRun++;
                AppendLog($"{r.SendIndex}번 {r.Name} {r.NormalizedPhone} 실패: {p.Detail}");
                break;
            default:
                AppendLog($"{r.SendIndex}번 {r.Name} {r.NormalizedPhone} {p.Detail}");
                break;
        }
        UpdateProgress(runTotal);
    }

    void UpdateProgress(int runTotal)
    {
        int sent = _all.Count(r => r.Status == SendStatus.Sent);
        int failed = _all.Count(r => r.Status == SendStatus.Failed);
        int pending = _all.Count - sent - failed;
        DoneCountText.Text = $"{sent + failed}";
        TotalCountText.Text = $"/ {_all.Count}명 처리";
        SendProgressBar.Maximum = Math.Max(1, _all.Count);
        SendProgressBar.Value = sent + failed;
        SentChip.Text = $"성공 {sent}";
        FailedChip.Text = $"실패 {failed}";
        PendingChip.Text = $"남음 {pending}";

        // 이번 실행에서 실제로 걸린 시간으로 남은 시간을 추정
        int remaining = runTotal - _doneThisRun;
        if (_main.IsSending && _doneThisRun > 0 && remaining > 0)
        {
            var perMessage = _clock.Elapsed / _doneThisRun;
            RemainingText.Text = $"남은 시간 약 {Step3Confirm.FormatDuration(perMessage * remaining)}";
        }
        else if (_main.IsSending && remaining > 0)
        {
            RemainingText.Text = $"남은 시간 약 {Step3Confirm.FormatDuration(Step3Confirm.Estimate(remaining, _main.Session.ImagePath != null, _main.Settings.IntervalSeconds))}";
        }
        else
        {
            RemainingText.Text = "";
        }
    }

    void Finish(SendResult result)
    {
        int sent = _all.Count(r => r.Status == SendStatus.Sent);
        int failed = _all.Count(r => r.Status == SendStatus.Failed);
        var pending = _all.Where(r => r.Status == SendStatus.Pending).ToList();

        var (icon, title, fg, bg) = result.Outcome switch
        {
            SendOutcome.Completed when failed == 0 => ("✓", "모두 보냈습니다", "SuccessBrush", "SuccessSoftBrush"),
            SendOutcome.Completed => ("!", "발송이 끝났지만 실패한 사람이 있습니다", "WarningBrush", "WarningSoftBrush"),
            SendOutcome.Cancelled => ("■", "중단했습니다", "WarningBrush", "WarningSoftBrush"),
            _ => ("✕", "문제가 생겨 자동으로 멈췄습니다", "DangerBrush", "DangerSoftBrush"),
        };
        TitleText.Text = "발송 결과";
        DescText.Text = $"성공 {sent}명 · 실패 {failed}명 · 아직 안 보냄 {pending.Count}명  (걸린 시간 {Step3Confirm.FormatDuration(_clock.Elapsed)})";

        ResultIcon.Text = icon;
        ResultIcon.Foreground = (Brush)FindResource(fg);
        ResultTitle.Text = title;
        ResultTitle.Foreground = (Brush)FindResource(fg);
        ResultBanner.Background = (Brush)FindResource(bg);
        ResultBanner.BorderBrush = (Brush)FindResource(fg);

        var details = new List<string>();
        if (result.AbortReason != null) details.Add("원인: " + result.AbortReason);
        if (pending.Count > 0) details.Add("원인을 해결한 뒤 [재개]를 누르면 남은 사람에게 이어서 보냅니다.");
        if (failed > 0) details.Add("실패한 사람은 표에서 빨간 줄로 표시됩니다.");
        if (details.Count == 0) details.Add("발송 기록은 아래 '상세 기록 보기'의 로그 폴더에 저장되어 있습니다.");
        ResultDetail.Text = string.Join("\n", details);
        ResultBanner.Visibility = Visibility.Visible;

        ResumeButton.Visibility = pending.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (pending.Count > 0) ResumeButton.Content = $"{pending[0].SendIndex}번부터 재개";
        RetryButton.Visibility = failed > 0 ? Visibility.Visible : Visibility.Collapsed;

        // 실제로 보낸 사람이 있을 때만, 이번 실행에서 '다음에 할게요'를 누르지 않았다면 후원을 안내한다
        bool askDonation = sent > 0 && AppLinks.HasDonation && !_donationDismissed;
        DonationCard.Visibility = askDonation ? Visibility.Visible : Visibility.Collapsed;
        if (askDonation) DonationTitle.Text = $"방금 {sent}명에게 문자를 보냈습니다. 단체문자발송이 도움이 되셨나요?";

        AppendLog($"{title} — {DescText.Text}");
        if (result.AbortReason != null) AppendLog("원인: " + result.AbortReason);
        UpdateProgress(0);
        _main.Activate();
    }

    void SetRunning(bool running)
    {
        _main.IsSending = running;
        WarningBanner.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        ControlPanel.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        CurrentRow.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        PauseButton.Content = "일시정지";
        StopButton.IsEnabled = true;
        if (running)
        {
            TitleText.Text = "문자를 보내는 중입니다";
            DescText.Text = "끝날 때까지 이 창을 닫지 마세요.";
            ResultBanner.Visibility = Visibility.Collapsed;
            CurrentText.Text = "준비 중...";
        }
        else
        {
            CurrentText.Text = "";
        }
    }

    void AppendLog(string line)
    {
        LogBox.AppendText($"{DateTime.Now:HH:mm:ss}  {line}\r\n");
        LogBox.ScrollToEnd();
    }

    void OnPause(object sender, RoutedEventArgs e)
    {
        if (_worker == null) return;
        _paused = !_paused;
        if (_paused)
        {
            _worker.Pause();
            _clock.Stop();
            PauseButton.Content = "계속 보내기";
            CurrentText.Text = "일시정지 — 지금 보내는 1건까지 끝나면 멈춥니다.";
            AppendLog("일시정지");
        }
        else
        {
            _worker.Resume();
            _clock.Start();
            PauseButton.Content = "일시정지";
            AppendLog("계속 보냄");
        }
    }

    void OnStop(object sender, RoutedEventArgs e)
    {
        _worker?.Stop();
        StopButton.IsEnabled = false;
        CurrentText.Text = "중단 요청 — 지금 보내는 1건까지 끝나면 멈춥니다.";
        AppendLog("중단 요청");
    }

    void OnResume(object sender, RoutedEventArgs e) => Start(SendMode.ResumePending);

    void OnRetryFailed(object sender, RoutedEventArgs e) => Start(SendMode.RetryFailed);

    void OnOpenLogFolder(object sender, RoutedEventArgs e) => MainWindow.OpenLogFolder(_logger?.Folder);

    void OnDonate(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppLinks.DonationUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"후원 페이지를 열지 못했습니다.\n{AppLinks.DonationUrl}\n\n{ex.Message}", "후원하기", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    void OnDismissDonation(object sender, RoutedEventArgs e)
    {
        _donationDismissed = true;
        DonationCard.Visibility = Visibility.Collapsed;
    }
}
