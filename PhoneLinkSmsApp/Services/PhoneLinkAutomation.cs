using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using static PhoneLinkSmsApp.Services.NativeMethods;

namespace PhoneLinkSmsApp.Services;

public enum ConnectionState { Connected, Disconnected, Unknown, AppNotRunning }

/// <param name="isFatal">true면 전체 발송을 멈춰야 하는 오류(앱 없음, 연결 끊김 등), false면 해당 수신자만 실패 처리</param>
public sealed class PhoneLinkException(string message, bool isFatal) : Exception(message)
{
    public bool IsFatal { get; } = isFatal;
}

/// <summary>
/// Windows "휴대폰과 연결" 앱을 UI Automation으로 조작해 문자 1건을 보낸다.
/// 반드시 STA 스레드에서 호출해야 한다 (StaThread.Run 사용).
/// </summary>
public sealed class PhoneLinkAutomation
{
    // ── "휴대폰과 연결" 앱 UI 식별자. 앱 업데이트로 자동화가 깨지면 여기부터 확인한다. ──
    static readonly string[] WindowTitles = ["휴대폰과 연결", "Phone Link"];
    const string MessagesTabName = "메시지";
    const string NewMessageButtonId = "NewMessageButton";
    const string RecipientBoxName = "받는 사람";
    const string SuggestionsListId = "SuggestionsList";
    const string RemoveRecipientButtonId = "RemoveButton";
    const string RemoveImagePrefix = "이미지 제거";
    const string AttachmentsAreaId = "AttachmentsScrollViewer";
    const string InputTextBoxId = "InputTextBox";
    const string SendMessageButtonId = "SendMessageButton";
    const string ConnectivityStatusId = "ConnectivityStatusTextBlock";
    const string ConnectedText = "연결됨";

    const string AppUserModelId = "Microsoft.YourPhone_8wekyb3d8bbwe!App";

    const int RecipientRetries = 3;
    static readonly TimeSpan ElementTimeout = TimeSpan.FromSeconds(5);
    static readonly TimeSpan SendVerifyTimeout = TimeSpan.FromSeconds(10);

    IntPtr _hwnd;

    /// <summary>"휴대폰과 연결" 앱을 실행하거나, 이미 실행 중이면 앞으로 가져온다.</summary>
    public static void LaunchApp() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{AppUserModelId}"));

    public ConnectionState CheckConnection(out string detail)
    {
        var window = FindWindow();
        if (window == null)
        {
            detail = "'휴대폰과 연결' 앱이 실행되어 있지 않습니다.";
            return ConnectionState.AppNotRunning;
        }
        var status = Safe(() => window.FindFirst(TreeScope.Descendants, ById(ConnectivityStatusId)));
        var text = status == null ? null : Safe(() => status.Current.Name);
        if (string.IsNullOrEmpty(text))
        {
            detail = "연결 상태 표시를 찾지 못했습니다 (앱 창을 열어두면 확인됩니다).";
            return ConnectionState.Unknown;
        }
        detail = text;
        return text.Contains(ConnectedText) ? ConnectionState.Connected : ConnectionState.Disconnected;
    }

    /// <summary>진단용: 각 단계에서 무슨 일이 있었는지 받아 본다.</summary>
    public Action<string>? Trace { get; set; }

    /// <param name="dryRun">true면 보내기 버튼을 누르지 않고, 입력한 받는 사람·본문을 지운 뒤 끝난다 (점검용).</param>
    public void SendMessage(string phone, string message, string? imagePath, bool dryRun = false)
    {
        var window = FindWindow()
            ?? throw new PhoneLinkException("'휴대폰과 연결' 앱 창을 찾지 못했습니다. 앱을 실행하고 휴대폰을 연결해 주세요.", true);
        BringToForeground(window);

        if (CheckConnection(out var detail) == ConnectionState.Disconnected)
            throw new PhoneLinkException($"휴대폰 연결이 끊겼습니다 ({detail}).", true);

        var newButton = FindNewMessageButton(window)
            ?? throw new PhoneLinkException("'새 메시지' 버튼을 찾지 못했습니다. 앱의 메시지 탭을 열어 주세요.", true);
        Click(newButton);
        Trace?.Invoke("새 메시지 열기");

        var recipientBox = WaitFor(() => window.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                new PropertyCondition(AutomationElement.NameProperty, RecipientBoxName))), ElementTimeout)
            ?? throw new PhoneLinkException("새 메시지 창의 '받는 사람' 입력칸을 찾지 못했습니다.", true);
        ConfirmRecipient(window, recipientBox, phone);
        Trace?.Invoke($"받는 사람 확정: {string.Join(", ", FindRecipientChips(window).Select(c => Safe(() => c.Current.Name)))}");

        // 대화 기록이 있는 번호는 새 메시지 화면이 기존 대화 화면으로 바뀌면서 본문 칸·보내기 버튼이 새로 만들어진다.
        // 그래서 본문 칸은 매번 '지금 화면에 보이는 것'을 다시 찾아 쓴다.
        var input = WaitForStableInput(window)
            ?? throw new PhoneLinkException("본문 입력칸을 찾지 못했습니다.", false);
        RemoveAttachments(window);   // 기존 대화에 예전 첨부가 남아 있으면 두 장이 가므로 지운다
        Trace?.Invoke($"본문 칸: '{Safe(() => input.Current.Name)}'");

        if (imagePath != null)
        {
            AttachImage(window, input, imagePath);
            input = WaitForStableInput(window)
                ?? throw new PhoneLinkException("이미지를 붙인 뒤 본문 입력칸을 찾지 못했습니다.", false);
            Trace?.Invoke($"이미지 첨부 후 본문 칸: '{Safe(() => input.Current.Name)}'");
        }

        if (message.Length > 0) input = EnterMessage(window, message);
        Trace?.Invoke($"본문 입력 확인: {Normalize(GetValue(input)) == Normalize(message)}");

        if (dryRun)
        {
            var b = FindActive(window, SendMessageButtonId);
            Trace?.Invoke($"[점검 모드] 보내기 버튼 활성화: {Safe(() => b?.Current.IsEnabled)} — 누르지 않고 되돌립니다");
            Safe(() => SetValue(input, ""));
            RemoveAttachments(window);
            ClearRecipients(window);
            return;
        }

        var sendButton = WaitFor(() => FindActive(window, SendMessageButtonId, mustBeEnabled: true), ElementTimeout)
            ?? throw new PhoneLinkException("보내기 버튼이 활성화되지 않았습니다.", false);
        Click(sendButton);
        Trace?.Invoke("보내기 누름");

        // 보내면 본문 칸이 비고 첨부도 사라진다
        if (WaitFor(() => FindActive(window, InputTextBoxId) is { } i && IsEmpty(i) && CountAttachments(window) == 0 ? i : null, SendVerifyTimeout) == null)
            throw new PhoneLinkException("보내기 후 입력칸이 비워지지 않아 전송을 확인하지 못했습니다.", false);
    }

    /// <summary>본문을 넣고 실제로 들어갔는지 확인한다. 화면이 바뀌어 본문 칸이 새로 생기면 다시 넣는다.</summary>
    AutomationElement EnterMessage(AutomationElement window, string message)
    {
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            var input = WaitForStableInput(window)
                ?? throw new PhoneLinkException("본문 입력칸을 찾지 못했습니다.", false);
            SetValue(input, message);
            Thread.Sleep(400);
            var current = WaitForStableInput(window) ?? input;
            if (Normalize(GetValue(current)) == Normalize(message)) return current;
            Trace?.Invoke($"본문이 들어가지 않아 다시 입력 ({attempt}/3)");
        }
        throw new PhoneLinkException("본문 입력칸에 문구가 들어가지 않았습니다.", false);
    }

    static string Normalize(string? text) => (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>화면에 보이는 요소 (숨겨진 이전 화면의 같은 이름 요소는 제외)</summary>
    static AutomationElement? FindActive(AutomationElement window, string automationId, bool mustBeEnabled = false) =>
        Safe(() => window.FindAll(TreeScope.Descendants, ById(automationId))
            .Cast<AutomationElement>()
            .FirstOrDefault(e => Safe(() => !e.Current.IsOffscreen && (!mustBeEnabled || e.Current.IsEnabled))));

    /// <summary>화면 전환이 끝나 본문 칸이 더 이상 바뀌지 않을 때까지 기다린다.</summary>
    static AutomationElement? WaitForStableInput(AutomationElement window)
    {
        var deadline = DateTime.UtcNow + ElementTimeout;
        AutomationElement? last = null;
        string lastKey = "";
        int stableCount = 0;
        while (DateTime.UtcNow < deadline)
        {
            var input = FindActive(window, InputTextBoxId);
            var key = input == null ? "" : string.Join(".", Safe(input.GetRuntimeId) ?? []) + "|" + Safe(() => input.Current.Name);
            if (input != null && key == lastKey)
            {
                if (++stableCount >= 3) return input;   // 약 0.6초 동안 그대로면 전환이 끝난 것
            }
            else
            {
                stableCount = 0;
            }
            (last, lastKey) = (input, key);
            Thread.Sleep(200);
        }
        return last;
    }

    /// <summary>첨부 이미지의 제거 버튼: "이미지 제거 n/n" 또는 (앱 버전에 따라) 첨부 영역 안의 버튼</summary>
    static List<AutomationElement> FindAttachmentRemoveButtons(AutomationElement window)
    {
        var buttonType = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
        var byName = Safe(() => window.FindAll(TreeScope.Descendants, buttonType)
            .Cast<AutomationElement>()
            .Where(b => (Safe(() => b.Current.Name) ?? "").StartsWith(RemoveImagePrefix, StringComparison.Ordinal))
            .ToList()) ?? [];
        if (byName.Count > 0) return byName;

        var area = FindActive(window, AttachmentsAreaId);
        return area == null ? [] : Safe(() => area.FindAll(TreeScope.Descendants, buttonType).Cast<AutomationElement>().ToList()) ?? [];
    }

    /// <summary>첨부 개수. 제거 버튼이 안 보이는 앱 버전도 있어 본문 칸 이름("1개 첨부 파일 추가됨")도 함께 본다.</summary>
    static int CountAttachments(AutomationElement window)
    {
        int byButton = FindAttachmentRemoveButtons(window).Count;
        if (byButton > 0) return byButton;
        var name = Safe(() => FindActive(window, InputTextBoxId)?.Current.Name) ?? "";
        var m = System.Text.RegularExpressions.Regex.Match(name, @"(\d+)\s*개\s*첨부");
        if (m.Success) return int.Parse(m.Groups[1].Value);
        return name.Contains("attachment", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    static void RemoveAttachments(AutomationElement window)
    {
        for (int i = 0; i < 10; i++)
        {
            var remove = FindAttachmentRemoveButtons(window).FirstOrDefault();
            if (remove == null) return;
            Click(remove);
        }
    }

    /// <summary>현재 앱 창을 PNG로 저장한다. 실패해도 예외를 던지지 않는다.</summary>
    public void TryCapture(string path)
    {
        try
        {
            if (_hwnd == IntPtr.Zero) FindWindow();
            if (_hwnd != IntPtr.Zero) CaptureWindow(_hwnd, path);
        }
        catch { /* 스크린샷은 부가 기능 */ }
    }

    AutomationElement? FindWindow()
    {
        var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition);
        foreach (AutomationElement w in windows)
        {
            var name = Safe(() => w.Current.Name) ?? "";
            if (WindowTitles.Any(t => name.StartsWith(t, StringComparison.Ordinal)))
            {
                _hwnd = new IntPtr(Safe(() => w.Current.NativeWindowHandle));
                return w;
            }
        }
        _hwnd = IntPtr.Zero;
        return null;
    }

    void BringToForeground(AutomationElement window)
    {
        var hwnd = _hwnd;
        if (hwnd == IntPtr.Zero) return;
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);

        for (int attempt = 0; attempt < 3 && GetForegroundWindow() != hwnd; attempt++)
        {
            uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
            uint thisThread = GetCurrentThreadId();
            bool attached = fgThread != thisThread && AttachThreadInput(thisThread, fgThread, true);
            try
            {
                if (attempt > 0) PressKey(VK_MENU); // Alt 입력이 있으면 포그라운드 전환 제한이 풀린다
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
            }
            finally
            {
                if (attached) AttachThreadInput(thisThread, fgThread, false);
            }
            Thread.Sleep(300);
        }
    }

    AutomationElement? FindNewMessageButton(AutomationElement window)
    {
        var button = WaitFor(() => window.FindFirst(TreeScope.Descendants, ById(NewMessageButtonId)), TimeSpan.FromSeconds(2));
        if (button != null) return button;

        // 통화·사진 등 다른 탭이 열려 있으면 메시지 탭으로 이동
        var tab = Safe(() => window.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.NameProperty, MessagesTabName),
            new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))));
        if (tab == null) return null;
        Click(tab);
        return WaitFor(() => window.FindFirst(TreeScope.Descendants, ById(NewMessageButtonId)), ElementTimeout);
    }

    /// <summary>
    /// 받는 사람에 번호를 넣고 Enter로 확정한다.
    /// 추천 목록에는 연락처가 아닌 안내 문구("번호를 입력한 후 &lt;Enter&gt;를 누릅니다")도 ListItem으로 뜨므로,
    /// 목록 항목을 누르지 말고 항상 Enter를 먼저 쓴다. 저장된 연락처라 Enter가 안 먹힐 때만 번호가 일치하는 항목을 누른다.
    /// </summary>
    void ConfirmRecipient(AutomationElement window, AutomationElement recipientBox, string phone)
    {
        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());
        var notes = new List<string>();   // 실패하면 로그에 남길 관찰 기록
        for (int attempt = 1; attempt <= RecipientRetries; attempt++)
        {
            // 이전 시도가 남긴 받는 사람이 있으면 엉뚱한 사람에게 가므로 먼저 모두 지운다
            ClearRecipients(window);
            BringToForeground(window);
            Safe(recipientBox.SetFocus);
            Thread.Sleep(200);

            // 1·2번째는 값 넣기(빠름), 3번째는 키보드로 한 글자씩 입력 (값 넣기를 무시하는 앱 버전 대비)
            bool typed = attempt == RecipientRetries;
            if (typed)
            {
                Safe(() => SetValue(recipientBox, ""));
                TypeText(phone);
            }
            else
            {
                SetValue(recipientBox, phone);
            }
            Thread.Sleep(1000);

            // Enter가 본문 칸에서 눌리면 전송될 수 있으므로, 앱이 앞에 있고 초점이 본문 칸이 아닐 때만 누른다
            if (!CanPressEnterSafely(window))
            {
                BringToForeground(window);
                Safe(recipientBox.SetFocus);
                Thread.Sleep(300);
            }
            var focusInfo = DescribeFocus(window);
            bool pressed = CanPressEnterSafely(window);
            if (pressed)
            {
                PressKey(VK_RETURN);
                if (WaitFor(() => IsRecipientConfirmed(window, recipientBox) ? window : null, TimeSpan.FromSeconds(4)) != null) return;
            }

            var contact = FindMatchingSuggestion(window, digits);
            if (contact != null)
            {
                ClickPhysically(contact);
                if (WaitFor(() => IsRecipientConfirmed(window, recipientBox) ? window : null, TimeSpan.FromSeconds(3)) != null) return;
            }

            notes.Add($"{attempt}차({(typed ? "타이핑" : "값넣기")}): 초점={focusInfo}, Enter={(pressed ? "누름" : "안누름")}, " +
                      $"입력칸='{MaskDigits(GetValue(recipientBox))}', 칩={FindRecipientChips(window).Count}개, " +
                      $"추천항목={CountSuggestions(window)}개, 연락처일치={(contact != null ? "있음" : "없음")}");
            Safe(() => SetValue(recipientBox, ""));
        }
        ClearRecipients(window);
        throw new PhoneLinkException($"받는 사람({phone})을 확정하지 못했습니다. [{string.Join(" / ", notes)}]", false);
    }

    /// <summary>앱 창이 앞에 있고, 초점이 (글자가 든) 본문 칸이 아니면 Enter를 눌러도 안전하다.</summary>
    bool CanPressEnterSafely(AutomationElement window)
    {
        if (GetForegroundWindow() != _hwnd) return false;
        var focused = Safe(() => AutomationElement.FocusedElement);
        if (focused == null) return false;
        if (Safe(() => focused.Current.AutomationId) == InputTextBoxId) return IsEmpty(focused);
        return Safe(() => focused.Current.ControlType) == ControlType.Edit;
    }

    string DescribeFocus(AutomationElement window)
    {
        var focused = Safe(() => AutomationElement.FocusedElement);
        var fg = GetForegroundWindow() == _hwnd ? "앱" : "다른창";
        return focused == null ? $"{fg}/없음" : $"{fg}/{Safe(() => focused.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""))}:{MaskDigits(Safe(() => focused.Current.Name))}";
    }

    static int CountSuggestions(AutomationElement window) =>
        Safe(() => window.FindFirst(TreeScope.Descendants, ById(SuggestionsListId))?
            .FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)).Count) ?? 0;

    /// <summary>로그에 전화번호가 그대로 남지 않도록 숫자를 가린다 (끝 4자리만 남김)</summary>
    static string MaskDigits(string? s) =>
        System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\d(?=(?:[^\d]*\d){4})", "#");

    /// <summary>확정되면 입력칸이 비고 받는 사람 칩(삭제 버튼)이 생긴다. 앞에서 칩을 모두 지웠으므로 1개 이상이면 이번 번호다.</summary>
    static bool IsRecipientConfirmed(AutomationElement window, AutomationElement recipientBox) =>
        IsEmpty(recipientBox) && FindRecipientChips(window).Count >= 1;

    /// <summary>받는 사람 칩의 삭제 버튼. 앱 버전에 따라 이름이 다를 수 있어 id로 찾는다.</summary>
    static List<AutomationElement> FindRecipientChips(AutomationElement window) =>
        Safe(() => window.FindAll(TreeScope.Descendants, ById(RemoveRecipientButtonId))
            .Cast<AutomationElement>()
            .Where(b => !(Safe(() => b.Current.Name) ?? "").StartsWith(RemoveImagePrefix, StringComparison.Ordinal))
            .ToList()) ?? [];

    static void ClearRecipients(AutomationElement window)
    {
        for (int i = 0; i < 20; i++)
        {
            var chip = FindRecipientChips(window).FirstOrDefault();
            if (chip == null) return;
            Click(chip);
        }
    }

    /// <summary>추천 목록에서 전화번호 뒷자리가 일치하는 연락처 항목 (안내 문구 항목은 제외됨)</summary>
    static AutomationElement? FindMatchingSuggestion(AutomationElement window, string digits)
    {
        var list = Safe(() => window.FindFirst(TreeScope.Descendants, ById(SuggestionsListId)));
        if (list == null || digits.Length < 8) return null;
        var tail = digits[^8..];
        return Safe(() => list.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
            .Cast<AutomationElement>()
            .FirstOrDefault(item => new string((Safe(() => item.Current.Name) ?? "").Where(char.IsAsciiDigit).ToArray()).Contains(tail)));
    }

    /// <summary>
    /// 클립보드 붙여넣기(Ctrl+V)로 이미지를 첨부한다. 안 붙으면 한 번 더 시도하고,
    /// 그래도 안 되면 무엇을 봤는지 오류 메시지에 남긴다.
    /// </summary>
    void AttachImage(AutomationElement window, AutomationElement input, string imagePath)
    {
        if (!File.Exists(imagePath)) throw new PhoneLinkException($"이미지 파일이 없습니다: {imagePath}", true);
        var notes = new List<string>();

        // 안 붙으면 한 번 더 붙여넣는다
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            var target = FindActive(window, InputTextBoxId) ?? input;
            var clip = PasteImage(window, target, imagePath);
            if (WaitFor(() => CountAttachments(window) > 0 ? window : null, TimeSpan.FromSeconds(6)) != null)
            {
                Trace?.Invoke($"이미지 첨부: 붙여넣기 {attempt}차 성공");
                return;
            }
            notes.Add($"붙여넣기{attempt}차: 클립보드={clip}, 초점={DescribeFocus(window)}");
        }

        throw new PhoneLinkException($"이미지가 첨부되지 않았습니다. [{string.Join(" / ", notes)}]", false);
    }

    /// <summary>클립보드에 파일을 올려 본문 칸에 Ctrl+V. 클립보드 설정 결과를 돌려준다.</summary>
    string PasteImage(AutomationElement window, AutomationElement input, string imagePath)
    {
        string clip = "실패";
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Clipboard.SetFileDropList([imagePath]);
                clip = "성공";
                break;
            }
            catch (ExternalException)
            {
                Thread.Sleep(200); // 다른 프로그램이 클립보드를 잡고 있음
            }
        }

        BringToForeground(window);
        Safe(input.SetFocus);
        Thread.Sleep(300);
        PressCtrl(VK_V);
        Thread.Sleep(500);
        return clip;
    }

    static void Click(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
        {
            ((InvokePattern)invoke).Invoke();
        }
        else if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var select))
        {
            ((SelectionItemPattern)select).Select();
        }
        else
        {
            ClickPhysically(element);
        }
        Thread.Sleep(500);
    }

    static void ClickPhysically(AutomationElement element)
    {
        if (element.TryGetClickablePoint(out var point))
        {
            LeftClick((int)point.X, (int)point.Y);
        }
        else if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
        {
            ((InvokePattern)invoke).Invoke();
        }
        else if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var select))
        {
            ((SelectionItemPattern)select).Select();
        }
        Thread.Sleep(500);
    }

    static void SetValue(AutomationElement element, string value)
    {
        try
        {
            ((ValuePattern)element.GetCurrentPattern(ValuePattern.Pattern)).SetValue(value);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ElementNotAvailableException or ElementNotEnabledException)
        {
            throw new PhoneLinkException($"입력칸에 값을 넣지 못했습니다: {ex.Message}", false);
        }
    }

    static string? GetValue(AutomationElement element) =>
        Safe(() => ((ValuePattern)element.GetCurrentPattern(ValuePattern.Pattern)).Current.Value);

    /// <summary>입력칸이 비었는지. 요소가 사라졌으면 비워진 것으로 본다.</summary>
    static bool IsEmpty(AutomationElement element)
    {
        try
        {
            return string.IsNullOrWhiteSpace(((ValuePattern)element.GetCurrentPattern(ValuePattern.Pattern)).Current.Value);
        }
        catch (ElementNotAvailableException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    static PropertyCondition ById(string automationId) => new(AutomationElement.AutomationIdProperty, automationId);

    static T? WaitFor<T>(Func<T?> probe, TimeSpan timeout) where T : class
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var result = Safe(probe);
            if (result != null || DateTime.UtcNow >= deadline) return result;
            Thread.Sleep(200);
        }
    }

    /// <summary>UIA 요소는 언제든 사라질 수 있어서, 조회 실패는 '없음'으로 처리한다.</summary>
    static T? Safe<T>(Func<T?> func)
    {
        try { return func(); }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException) { return default; }
    }

    static void Safe(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException) { }
    }
}
