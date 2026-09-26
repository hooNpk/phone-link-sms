using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PhoneLinkSmsApp.Models;

public enum RecipientIssue { None, InvalidPhone, Duplicate, SmsRefused }

public enum SendStatus { Pending, Sending, Sent, Failed }

public sealed class Recipient : INotifyPropertyChanged
{
    /// <summary>엑셀 원본 행 번호 (테스트 수신자는 0)</summary>
    public int RowNumber { get; init; }
    public string RawPhone { get; init; } = "";
    public string SmsValue { get; init; } = "";

    string _name = "";
    public string Name { get => _name; set => Set(ref _name, value); }

    /// <summary>표에서 사용자가 보고 고치는 번호</summary>
    string _phone = "";
    public string Phone { get => _phone; set => Set(ref _phone, value); }

    /// <summary>검증을 통과한 010-XXXX-XXXX 형식 번호. 형식 오류면 null</summary>
    string? _normalizedPhone;
    public string? NormalizedPhone { get => _normalizedPhone; set => Set(ref _normalizedPhone, value); }

    bool _include;
    public bool Include { get => _include; set => Set(ref _include, value); }

    RecipientIssue _issue;
    public RecipientIssue Issue
    {
        get => _issue;
        set { if (Set(ref _issue, value)) OnPropertyChanged(nameof(CanSend)); }
    }

    string _reason = "";
    public string Reason { get => _reason; set => Set(ref _reason, value); }

    /// <summary>번호 오류·중복이 아니면 발송 가능 (수신거부는 사용자가 포함시킬 수 있음)</summary>
    public bool CanSend => Issue is RecipientIssue.None or RecipientIssue.SmsRefused;

    int _sendIndex;
    public int SendIndex { get => _sendIndex; set => Set(ref _sendIndex, value); }

    SendStatus _status;
    public SendStatus Status
    {
        get => _status;
        set { if (Set(ref _status, value)) OnPropertyChanged(nameof(StatusText)); }
    }

    public string StatusText => Status switch
    {
        SendStatus.Sending => "보내는 중",
        SendStatus.Sent => "완료",
        SendStatus.Failed => "실패",
        _ => "대기",
    };

    string _statusMessage = "";
    public string StatusMessage { get => _statusMessage; set => Set(ref _statusMessage, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    bool Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
