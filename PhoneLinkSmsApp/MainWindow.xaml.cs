using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PhoneLinkSmsApp.Models;
using PhoneLinkSmsApp.Settings;
using PhoneLinkSmsApp.Views;

namespace PhoneLinkSmsApp;

/// <summary>각 단계 화면이 하단 이전/다음 바에 알려주는 정보</summary>
public interface IStepView
{
    void OnEnter();
    /// <summary>다음 버튼 글자. 빈 문자열이면 버튼을 숨긴다.</summary>
    string NextLabel { get; }
    bool CanGoNext { get; }
    /// <summary>하단 안내 문구 (다음으로 못 가는 이유 등)</summary>
    string Hint { get; }
    bool CanGoBack { get; }
    void OnNext();
}

public partial class MainWindow : Window
{
    public const int StepConnect = 0, StepUpload = 1, StepCompose = 2, StepConfirm = 3, StepSend = 4;
    static readonly string[] StepNames = ["휴대폰 연결", "명단 불러오기", "문구 작성", "발송 확인", "발송"];

    readonly UserControl[] _steps;
    int _current;

    public SendSession Session { get; } = new();
    public AppSettings Settings { get; } = AppSettings.Load();
    public Step4Send SendView => (Step4Send)_steps[StepSend];
    public UserControl CurrentStep => _steps[_current];

    bool _isSending;
    public bool IsSending
    {
        get => _isSending;
        set { _isSending = value; RefreshNav(); }
    }

    public MainWindow()
    {
        InitializeComponent();
        _steps = [new Step0Connect(this), new Step1Upload(this), new Step2Compose(this), new Step3Confirm(this), new Step4Send(this)];
        GoTo(StepConnect);
    }

    public void GoTo(int step)
    {
        _current = step;
        Host.Content = _steps[step];
        ((IStepView)_steps[step]).OnEnter();
        RefreshNav();
    }

    /// <summary>화면 상태가 바뀌면 호출: 단계 표시와 이전/다음 버튼을 갱신한다.</summary>
    public void RefreshNav()
    {
        if (_steps == null) return;
        var view = (IStepView)_steps[_current];
        BackButton.Visibility = view.CanGoBack && !IsSending ? Visibility.Visible : Visibility.Hidden;
        NextButton.Content = view.NextLabel;
        NextButton.Visibility = string.IsNullOrEmpty(view.NextLabel) ? Visibility.Collapsed : Visibility.Visible;
        NextButton.IsEnabled = view.CanGoNext;
        FooterHint.Text = view.Hint;
        FooterHint.Foreground = (Brush)FindResource(view.CanGoNext || IsSending ? "MutedBrush" : "WarningBrush");
        BuildStepper();
    }

    void BuildStepper()
    {
        Stepper.Children.Clear();
        for (int i = 0; i < StepNames.Length; i++)
        {
            if (i > 0)
            {
                Stepper.Children.Add(new Rectangle
                {
                    Width = 28, Height = 2, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
                    Fill = (Brush)FindResource(i <= _current ? "AccentBrush" : "LineBrush"),
                });
            }

            bool done = i < _current, current = i == _current;
            var circle = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(13),
                Background = (Brush)FindResource(done ? "SuccessBrush" : current ? "AccentBrush" : "LineBrush"),
                Child = new TextBlock
                {
                    Text = done ? "✓" : i.ToString(),   // 0단계부터 번호를 매긴다
                    Foreground = done || current ? Brushes.White : (Brush)FindResource("MutedBrush"),
                    FontWeight = FontWeights.Bold, FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var label = new TextBlock
            {
                Text = StepNames[i], Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                FontWeight = current ? FontWeights.Bold : FontWeights.Normal,
                Foreground = (Brush)FindResource(current ? "TextBrush" : "MutedBrush"),
            };
            var item = new StackPanel { Orientation = Orientation.Horizontal, Children = { circle, label } };

            // 이미 지나온 단계는 눌러서 돌아갈 수 있다 (발송 중·발송 화면 제외)
            int target = i;
            if (done && !IsSending && _current != StepSend)
            {
                item.Cursor = Cursors.Hand;
                item.ToolTip = $"'{StepNames[i]}' 단계로 돌아가기";
                item.MouseLeftButtonUp += (_, _) => GoTo(target);
            }
            Stepper.Children.Add(item);
        }
    }

    void OnOpenLogs(object sender, RoutedEventArgs e) => OpenLogFolder();

    /// <summary>
    /// 로그 폴더를 연다. folder를 주면 그 폴더를, 아니면 전체 기록 폴더를 가장 최근 기록이 선택된 상태로 연다.
    /// </summary>
    public static void OpenLogFolder(string? folder = null)
    {
        try
        {
            if (folder != null && Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
                return;
            }
            Directory.CreateDirectory(AppSettings.LogsDir);
            var latest = new DirectoryInfo(AppSettings.LogsDir).GetDirectories()
                .OrderByDescending(d => d.CreationTime).FirstOrDefault();
            var args = latest == null ? $"\"{AppSettings.LogsDir}\"" : $"/select,\"{latest.FullName}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"기록 폴더를 열지 못했습니다.\n{AppSettings.LogsDir}\n\n{ex.Message}", "발송 기록", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void OnBack(object sender, RoutedEventArgs e)
    {
        if (_current > 0) GoTo(_current - 1);
    }

    void OnNext(object sender, RoutedEventArgs e) => ((IStepView)_steps[_current]).OnNext();

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (IsSending)
        {
            var answer = MessageBox.Show("지금 문자를 보내는 중입니다.\n프로그램을 닫으면 발송이 멈춥니다. 닫을까요?",
                "발송 중", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            SendView.StopWorker();
        }
        Settings.Save();
    }
}
