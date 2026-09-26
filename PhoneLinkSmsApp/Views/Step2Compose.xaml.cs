using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PhoneLinkSmsApp.Services;

namespace PhoneLinkSmsApp.Views;

public partial class Step2Compose : UserControl, IStepView
{
    /// <summary>한글 2바이트·영숫자 1바이트 기준으로 90바이트를 넘으면 장문(LMS)이 된다.</summary>
    const int SmsByteLimit = 90;
    const int RecentPreviewLength = 50;

    readonly MainWindow _main;

    public Step2Compose(MainWindow main)
    {
        InitializeComponent();
        _main = main;
    }

    // ── 하단 바 ──
    public string NextLabel => "다음: 발송 확인  ▶";
    public bool CanGoNext => !string.IsNullOrWhiteSpace(_main.Session.Message) || _main.Session.ImagePath != null;
    public bool CanGoBack => true;
    public string Hint => CanGoNext ? "" : "보낼 문구나 사진을 넣어 주세요.";

    public void OnEnter()
    {
        if (MessageText.Text != _main.Session.Message) MessageText.Text = _main.Session.Message;
        RecentButton.IsEnabled = _main.Settings.RecentMessages.Count > 0;
        ShowImage(_main.Session.ImagePath);
        UpdateAll();
        MessageText.Focus();
    }

    public void OnNext() => _main.GoTo(MainWindow.StepConfirm);

    void OnRecentClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = RecentButton, Placement = PlacementMode.Bottom };
        foreach (var message in _main.Settings.RecentMessages)
        {
            var oneLine = message.ReplaceLineEndings(" ");
            var item = new MenuItem
            {
                Header = oneLine.Length > RecentPreviewLength ? oneLine[..RecentPreviewLength] + "…" : oneLine,
                ToolTip = message,
            };
            item.Click += (_, _) => MessageText.Text = message;
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    void OnMessageChanged(object sender, TextChangedEventArgs e)
    {
        _main.Session.Message = MessageText.Text;
        UpdateAll();
    }

    void UpdateAll()
    {
        var text = MessageText.Text;
        Placeholder.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        // {이름}이 있으면 이름 길이에 따라 받는 사람마다 길이가 달라지므로 가장 긴 경우로 판단한다
        var targets = _main.Session.Targets.ToList();
        var names = targets.Count > 0 ? targets.Select(r => r.Name) : [""];
        var rendered = names.Select(n => MessageTemplate.Render(text, n)).ToList();
        int minBytes = rendered.Min(ByteCount), maxBytes = rendered.Max(ByteCount);
        var (kind, fg, bg) = _main.Session.ImagePath != null ? ("사진 문자 MMS", "AccentBrush", "AccentSoftBrush")
            : maxBytes > SmsByteLimit ? ("장문 LMS", "WarningBrush", "WarningSoftBrush")
            : ("단문 SMS", "SuccessBrush", "SuccessSoftBrush");
        KindText.Text = kind;
        KindText.Foreground = (Brush)FindResource(fg);
        KindBadge.Background = (Brush)FindResource(bg);
        CounterText.Text = minBytes == maxBytes
            ? $"{rendered[0].Length}자 · {maxBytes}바이트"
            : $"이름에 따라 {minBytes}~{maxBytes}바이트";
        Step3Confirm.ShowNameNote(NameNote, _main.Session);

        // 미리보기 (첫 번째 받는 사람 이름으로)
        bool hasContent = text.Length > 0 || _main.Session.ImagePath != null;
        PreviewEmpty.Visibility = hasContent ? Visibility.Collapsed : Visibility.Visible;
        PreviewBubble.Visibility = hasContent ? Visibility.Visible : Visibility.Collapsed;
        PreviewText.Text = MessageTemplate.Render(text, targets.FirstOrDefault()?.Name);
        PreviewText.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        _main.RefreshNav();
    }

    static int ByteCount(string s) => s.Sum(c => c < 128 ? 1 : 2);

    void OnInsertName(object sender, RoutedEventArgs e)
    {
        int caret = MessageText.CaretIndex;
        MessageText.Text = MessageText.Text.Insert(caret, MessageTemplate.NameToken);
        MessageText.CaretIndex = caret + MessageTemplate.NameToken.Length;
        MessageText.Focus();
    }

    void OnPickImage(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "첨부할 사진 선택",
            Filter = "이미지 (*.jpg;*.jpeg;*.png;*.gif;*.bmp)|*.jpg;*.jpeg;*.png;*.gif;*.bmp",
        };
        if (dialog.ShowDialog() != true) return;
        _main.Session.ImagePath = dialog.FileName;
        ShowImage(dialog.FileName);
        UpdateAll();
    }

    void OnRemoveImage(object sender, RoutedEventArgs e)
    {
        _main.Session.ImagePath = null;
        ShowImage(null);
        UpdateAll();
    }

    void ShowImage(string? path)
    {
        var bitmap = path is not null && File.Exists(path) ? LoadThumbnail(path, 500) : null;
        ThumbImage.Source = bitmap;
        PreviewImage.Source = bitmap;
        PreviewImage.Visibility = bitmap == null ? Visibility.Collapsed : Visibility.Visible;
        NoThumbText.Visibility = bitmap == null ? Visibility.Visible : Visibility.Collapsed;
        RemoveImageButton.Visibility = path != null ? Visibility.Visible : Visibility.Collapsed;
        PickImageButton.Content = path != null ? "다른 사진..." : "사진 선택...";
        ImageInfoText.Text = path == null
            ? "포스터·웹자보 등 이미지를 함께 보낼 수 있습니다."
            : $"{Path.GetFileName(path)}  ({new FileInfo(path).Length / 1024:N0}KB)";
    }

    /// <summary>파일을 잠그지 않도록 메모리로 읽어 들인다.</summary>
    public static BitmapImage? LoadThumbnail(string path, int decodeWidth)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = decodeWidth;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
