using System.Collections.ObjectModel;

namespace PhoneLinkSmsApp.Models;

/// <summary>마법사 4단계가 공유하는 발송 상태</summary>
public sealed class SendSession
{
    public string? SourceFile { get; set; }
    public ObservableCollection<Recipient> Recipients { get; } = [];
    public string Message { get; set; } = "";
    public string? ImagePath { get; set; }

    /// <summary>테스트 발송에 성공한 문구·이미지 조합. 내용이 바뀌면 테스트를 다시 해야 한다.</summary>
    public string? TestPassedFor { get; set; }
    public string ContentSignature => $"{Message}\u0001{ImagePath}";
    public bool TestPassed => TestPassedFor == ContentSignature;

    public IEnumerable<Recipient> Targets => Recipients.Where(r => r.Include && r.CanSend);
}
