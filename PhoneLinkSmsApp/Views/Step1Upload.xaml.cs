using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using PhoneLinkSmsApp.Models;
using PhoneLinkSmsApp.Services;

namespace PhoneLinkSmsApp.Views;

public partial class Step1Upload : UserControl, IStepView
{
    sealed record ColumnChoice(int Index, string Label)
    {
        public static readonly ColumnChoice None = new(-1, "(없음)");
        public override string ToString() => Label;
    }

    public sealed record RecentFile(string Path)
    {
        public string Name => System.IO.Path.GetFileName(Path);
    }

    readonly MainWindow _main;
    readonly ICollectionView _view;
    List<SheetData> _sheets = [];
    SheetData? _sheet;
    bool _suppress;
    bool _loading;

    public Step1Upload(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        _view = CollectionViewSource.GetDefaultView(main.Session.Recipients);
        RecipientGrid.ItemsSource = _view;
        RefreshRecentFiles();
    }

    // ── 하단 바 ──
    public string NextLabel => "다음: 문구 작성  ▶";
    public bool CanGoNext => !_loading && _main.Session.Targets.Any();
    public bool CanGoBack => !_loading;
    public string Hint => _loading ? "엑셀 파일을 읽는 중입니다..."
        : _sheet == null ? "엑셀 파일을 먼저 불러오세요."
        : CanGoNext ? $"{_main.Session.Targets.Count()}명에게 보낼 준비가 됐습니다."
        : "보낼 사람이 없습니다. 전화번호 컬럼이나 체크 상태를 확인하세요.";

    public void OnEnter() => UpdateSummary();

    public void OnNext()
    {
        RecipientGrid.CommitEdit(DataGridEditingUnit.Row, true);
        _main.GoTo(MainWindow.StepCompose);
    }

    // ── 파일 불러오기 ──
    void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "당원 명단 엑셀 파일 선택",
            Filter = "엑셀 파일 (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|모든 파일 (*.*)|*.*",
        };
        if (dialog.ShowDialog() == true) LoadFile(dialog.FileName);
    }

    void OnDropZoneClick(object sender, MouseButtonEventArgs e)
    {
        // 안쪽 버튼·링크를 누른 경우는 각자 처리
        if (e.OriginalSource is DependencyObject d && FindParent<Button>(d) != null) return;
        OnBrowse(sender, e);
    }

    void OnRecentClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RecentFile file }) LoadFile(file.Path);
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) LoadFile(files[0]);
    }

    async void LoadFile(string path)
    {
        if (_loading) return;
        if (Path.GetExtension(path).Equals(".xls", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("옛날 엑셀 형식(.xls)은 읽을 수 없습니다.\n엑셀에서 '다른 이름으로 저장 → Excel 통합 문서(.xlsx)'로 저장한 뒤 불러와 주세요.",
                "파일 형식", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 큰 파일은 몇 초 걸리므로 백그라운드에서 읽고, 그동안 로딩 표시를 띄운다
        List<SheetData> sheets;
        SetLoading(true, path);
        try
        {
            sheets = await Task.Run(() => ExcelParser.LoadWorkbook(path));
        }
        catch (Exception ex)
        {
            SetLoading(false);
            MessageBox.Show($"엑셀 파일을 읽지 못했습니다.\n\n{ex.Message}", "파일 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        SetLoading(false);
        if (sheets.Count == 0)
        {
            MessageBox.Show("데이터가 있는 시트가 없습니다.", "파일 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _sheets = sheets;
        _main.Session.SourceFile = path;
        FileNameText.Text = Path.GetFileName(path);
        FilePathText.Text = Path.GetDirectoryName(path);
        _main.Settings.AddRecentFile(path);
        _main.Settings.Save();
        RefreshRecentFiles();

        DropZone.Visibility = Visibility.Collapsed;
        LoadedPanel.Visibility = Visibility.Visible;

        var best = _sheets.FirstOrDefault(s => ExcelParser.DetectColumns(s).PhoneColumn != null) ?? _sheets[0];
        _suppress = true;
        SheetCombo.ItemsSource = _sheets.Select(s => s.SheetName).ToList();
        SheetCombo.SelectedIndex = _sheets.IndexOf(best);
        _suppress = false;
        SelectSheet(best);
    }

    void SetLoading(bool loading, string? path = null)
    {
        _loading = loading;
        LoadingOverlay.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        LoadingFileText.Text = path == null ? "" : Path.GetFileName(path);
        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, loading
            ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever }
            : null);
        _main.RefreshNav();
    }

    // ── 컬럼 지정 ──
    void OnSheetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || SheetCombo.SelectedIndex < 0) return;
        SelectSheet(_sheets[SheetCombo.SelectedIndex]);
    }

    void SelectSheet(SheetData sheet)
    {
        _sheet = sheet;
        var detected = ExcelParser.DetectColumns(sheet);
        var columns = sheet.Headers
            .Select((h, i) => new ColumnChoice(i, $"{ExcelParser.ColumnLetter(i + 1)}열: {h}"))
            .ToList();
        List<ColumnChoice> withNone = [ColumnChoice.None, .. columns];

        _suppress = true;
        NameCombo.ItemsSource = withNone;
        PhoneCombo.ItemsSource = columns;
        SmsCombo.ItemsSource = withNone;
        NameCombo.SelectedItem = withNone.FirstOrDefault(c => c.Index == detected.NameColumn) ?? ColumnChoice.None;
        PhoneCombo.SelectedItem = columns.FirstOrDefault(c => c.Index == detected.PhoneColumn);
        SmsCombo.SelectedItem = withNone.FirstOrDefault(c => c.Index == detected.SmsColumn) ?? ColumnChoice.None;
        _suppress = false;

        // 전화번호 칸을 못 찾았으면 설정을 펼쳐서 바로 고치게 한다
        MappingExpander.IsExpanded = detected.PhoneColumn == null;
        Rebuild();
    }

    void OnMappingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppress) Rebuild();
    }

    /// <summary>현재 컬럼 지정으로 명단을 다시 만든다 (표에서 고친 내용은 초기화됨).</summary>
    void Rebuild()
    {
        var recipients = _main.Session.Recipients;
        recipients.Clear();
        if (_sheet != null)
        {
            var mapping = new ColumnMapping(Selected(NameCombo), Selected(PhoneCombo), Selected(SmsCombo));
            var list = ExcelParser.BuildRecipients(_sheet, mapping);
            RecipientValidator.Revalidate(list, IncludeRefusedCheck.IsChecked == true, resetInclude: true);
            foreach (var r in list) recipients.Add(r);
        }
        UpdateMappingSummary();
        UpdateSummary();
    }

    static int? Selected(ComboBox combo) => combo.SelectedItem is ColumnChoice { Index: >= 0 } c ? c.Index : null;

    void UpdateMappingSummary()
    {
        string Col(ComboBox combo) => Selected(combo) is int i ? $"{ExcelParser.ColumnLetter(i + 1)}열" : "없음";
        MappingSummary.Text = Selected(PhoneCombo) == null
            ? "⚠ 전화번호 칸을 찾지 못했습니다. 여기를 눌러 직접 지정하세요."
            : $"자동으로 찾은 칸: 이름 {Col(NameCombo)} · 전화번호 {Col(PhoneCombo)} · 수신여부 {Col(SmsCombo)}   (틀렸으면 눌러서 바꾸기)";
    }

    // ── 표 편집 ──
    void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        // 바인딩 값이 반영된 뒤에 다시 검사한다
        Dispatcher.BeginInvoke(() =>
        {
            var recipients = _main.Session.Recipients;
            RecipientValidator.Revalidate(recipients, IncludeRefusedCheck.IsChecked == true, resetInclude: false);
            foreach (var r in recipients)
                if (r.NormalizedPhone != null && r.Phone != r.NormalizedPhone) r.Phone = r.NormalizedPhone;
            UpdateSummary();
        }, DispatcherPriority.Background);
    }

    void OnIncludeClick(object sender, RoutedEventArgs e) => UpdateSummary();

    void OnIncludeRefusedClick(object sender, RoutedEventArgs e)
    {
        var include = IncludeRefusedCheck.IsChecked == true;
        foreach (var r in _main.Session.Recipients.Where(r => r.Issue == RecipientIssue.SmsRefused))
            r.Include = include;
        UpdateSummary();
    }

    // ── 필터 ──
    void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (_view == null) return;
        _view.Filter = FilterTargets.IsChecked == true ? o => o is Recipient { Include: true, CanSend: true }
            : FilterExcluded.IsChecked == true ? o => o is Recipient r && !(r.Include && r.CanSend)
            : null;
    }

    void UpdateSummary()
    {
        var all = _main.Session.Recipients;
        int targets = _main.Session.Targets.Count();
        TargetCount.Text = $"{targets}명";
        RefusedCount.Text = $"{all.Count(r => r.Issue == RecipientIssue.SmsRefused)}명";
        int invalid = all.Count(r => r.Issue == RecipientIssue.InvalidPhone);
        int duplicate = all.Count(r => r.Issue == RecipientIssue.Duplicate);
        InvalidCount.Text = $"{invalid}명";
        DuplicateCount.Text = $"{duplicate}명";
        InvalidCount.Foreground = (System.Windows.Media.Brush)FindResource(invalid > 0 ? "DangerBrush" : "MutedBrush");
        DuplicateCount.Foreground = (System.Windows.Media.Brush)FindResource(duplicate > 0 ? "DangerBrush" : "MutedBrush");

        FilterAll.Content = $"전체 {all.Count}";
        FilterTargets.Content = $"보낼 사람 {targets}";
        FilterExcluded.Content = $"빠진 사람 {all.Count - targets}";
        _main.RefreshNav();
    }

    void RefreshRecentFiles()
    {
        var recent = _main.Settings.RecentFiles.Where(File.Exists).Take(4).Select(p => new RecentFile(p)).ToList();
        RecentList.ItemsSource = recent;
        RecentPanel.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T)
            d = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }
}
