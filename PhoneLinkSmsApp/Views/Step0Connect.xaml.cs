using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PhoneLinkSmsApp.Services;

namespace PhoneLinkSmsApp.Views;

/// <summary>0단계: "휴대폰과 연결" 앱이 켜져 있고 휴대폰이 연결됐는지 확인한다.</summary>
public partial class Step0Connect : UserControl, IStepView
{
    static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    readonly MainWindow _main;
    readonly DispatcherTimer _poll;
    ConnectionState? _state;   // null = 아직 확인 전
    bool _checking;

    public Step0Connect(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        _poll = new DispatcherTimer { Interval = PollInterval };
        _poll.Tick += async (_, _) =>
        {
            // 이 화면을 보고 있을 때만 자동 확인
            if (_main.CurrentStep == this) await CheckAsync(showChecking: false);
            else _poll.Stop();
        };
    }

    // ── 하단 바 ──
    public string NextLabel => "다음: 명단 불러오기  ▶";
    public bool CanGoBack => false;
    public bool CanGoNext => _state == ConnectionState.Connected
                             || (_state == ConnectionState.Unknown && ManualConfirmCheck.IsChecked == true);
    public string Hint => _state switch
    {
        null => "연결 상태를 확인하는 중입니다...",
        ConnectionState.Connected => "연결됐습니다. 다음 단계로 넘어가세요.",
        ConnectionState.Unknown when ManualConfirmCheck.IsChecked == true => "직접 확인하셨으니 넘어갈 수 있습니다.",
        ConnectionState.Unknown => "앱 화면에서 '연결됨'을 확인한 뒤 체크해 주세요.",
        ConnectionState.AppNotRunning => "'휴대폰과 연결' 앱을 먼저 열어 주세요.",
        _ => "휴대폰에서 'Windows와 연결'을 켜면 다음으로 넘어갈 수 있습니다.",
    };

    public void OnEnter()
    {
        _ = CheckAsync(showChecking: _state == null);
        _poll.Start();
    }

    public void OnNext()
    {
        _poll.Stop();
        _main.GoTo(MainWindow.StepUpload);
    }

    async Task CheckAsync(bool showChecking)
    {
        if (_checking) return;
        _checking = true;
        if (showChecking)
        {
            Show(null, "");
            RecheckButton.IsEnabled = false;
        }
        try
        {
            var (state, detail) = await StaThread.Run(() =>
            {
                var s = new PhoneLinkAutomation().CheckConnection(out var d);
                return (s, d);
            });
            Show(state, detail);
        }
        finally
        {
            _checking = false;
            RecheckButton.IsEnabled = true;
        }
    }

    void Show(ConnectionState? state, string detail)
    {
        if (state != ConnectionState.Unknown) ManualConfirmCheck.IsChecked = false;
        _state = state;

        var (icon, title, text, fg, bg) = state switch
        {
            null => ("…", "확인하는 중입니다", "'휴대폰과 연결' 앱의 상태를 살펴보고 있습니다.", "MutedBrush", "SurfaceBrush"),
            ConnectionState.Connected => ("✓", "휴대폰이 연결되어 있습니다",
                "준비가 끝났습니다. 아래 [다음] 버튼을 눌러 명단을 불러오세요.", "SuccessBrush", "SuccessSoftBrush"),
            ConnectionState.Unknown => ("?", "앱은 켜져 있지만 연결 상태를 읽지 못했습니다",
                "'휴대폰과 연결' 앱 화면에서 '연결됨'이 보이는지 직접 확인한 뒤 아래에 체크해 주세요.", "WarningBrush", "WarningSoftBrush"),
            ConnectionState.AppNotRunning => ("!", "'휴대폰과 연결' 앱이 꺼져 있습니다",
                "아래 1번의 [앱 열기] 버튼을 눌러 앱을 켜 주세요.", "DangerBrush", "DangerSoftBrush"),
            _ => ("!", "휴대폰이 연결되어 있지 않습니다",
                $"앱에 표시된 상태: {detail}\n휴대폰에서 'Windows와 연결'이 켜져 있는지, 휴대폰 화면이 켜져 있는지 확인해 주세요 (아래 2~3번).", "DangerBrush", "DangerSoftBrush"),
        };
        StatusIcon.Text = icon;
        StatusIconBox.Background = (Brush)FindResource(fg);
        StatusTitle.Text = title;
        StatusTitle.Foreground = (Brush)FindResource(state == null ? "TextBrush" : fg);
        StatusDetail.Text = text;
        StatusCard.Background = (Brush)FindResource(bg);
        StatusCard.BorderBrush = (Brush)FindResource(state == null ? "LineBrush" : fg);

        bool connected = state == ConnectionState.Connected;
        GuidePanel.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        AutoCheckText.Visibility = connected || state == null ? Visibility.Collapsed : Visibility.Visible;
        ManualConfirmBox.Visibility = state == ConnectionState.Unknown ? Visibility.Visible : Visibility.Collapsed;
        LaunchButton.Content = state == ConnectionState.AppNotRunning ? "'휴대폰과 연결' 앱 열기" : "앱 창 앞으로 가져오기";
        _main.RefreshNav();
    }

    void OnRecheck(object sender, RoutedEventArgs e) => _ = CheckAsync(showChecking: true);

    void OnManualConfirm(object sender, RoutedEventArgs e) => _main.RefreshNav();

    void OnLaunch(object sender, RoutedEventArgs e)
    {
        try
        {
            PhoneLinkAutomation.LaunchApp();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"'휴대폰과 연결' 앱을 열지 못했습니다. 시작 메뉴에서 직접 실행해 주세요.\n\n{ex.Message}",
                "앱 열기", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
