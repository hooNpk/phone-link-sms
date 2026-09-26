using PhoneLinkSmsApp.Models;

namespace PhoneLinkSmsApp.Services;

public sealed record SendJob(
    IReadOnlyList<Recipient> Targets,
    string Message,
    string? ImagePath,
    int IntervalSeconds,
    SendLogger Logger,
    bool CaptureOnSuccess = false,
    string? NameOverride = null)   // 테스트 발송: 내 번호로 보내되 {이름}은 실제 받는 사람 이름으로 채운다
{
    public string MessageFor(Recipient r) => MessageTemplate.Render(Message, NameOverride ?? r.Name);
}

public sealed record SendProgress(Recipient Recipient, SendStatus Status, string Detail);

public enum SendOutcome { Completed, Cancelled, Aborted }

public sealed record SendResult(SendOutcome Outcome, int Sent, int Failed, string? AbortReason);

/// <summary>전용 STA 스레드에서 한 명씩 순서대로 발송한다. 중단·일시정지는 다음 건을 보내기 전에 적용된다.</summary>
public sealed class SendWorker
{
    const int MaxConsecutiveFailures = 3;

    readonly CancellationTokenSource _cts = new();
    readonly ManualResetEventSlim _running = new(true);

    public void Pause() => _running.Reset();
    public void Resume() => _running.Set();

    public void Stop()
    {
        _cts.Cancel();
        _running.Set();
    }

    /// <summary>progress는 UI 스레드에서 만든 Progress&lt;T&gt;를 넘기면 UI 스레드로 전달된다.</summary>
    public Task<SendResult> RunAsync(SendJob job, IProgress<SendProgress> progress) => StaThread.Run(() => Run(job, progress));

    SendResult Run(SendJob job, IProgress<SendProgress> progress)
    {
        var token = _cts.Token;
        var automation = new PhoneLinkAutomation();
        int sent = 0, failed = 0, consecutiveFailures = 0;
        using var awake = NativeMethods.KeepAwake();

        for (int i = 0; i < job.Targets.Count; i++)
        {
            _running.Wait();
            if (token.IsCancellationRequested) return new SendResult(SendOutcome.Cancelled, sent, failed, null);

            var r = job.Targets[i];
            progress.Report(new SendProgress(r, SendStatus.Sending, ""));
            try
            {
                automation.SendMessage(r.NormalizedPhone!, job.MessageFor(r), job.ImagePath);
                sent++;
                consecutiveFailures = 0;
                if (job.CaptureOnSuccess) automation.TryCapture(job.Logger.ScreenshotPath(r, "ok"));
                job.Logger.Log(r, "성공", "");
                progress.Report(new SendProgress(r, SendStatus.Sent, ""));
            }
            catch (PhoneLinkException ex) when (ex.IsFatal)
            {
                // 보내기 전 단계에서 난 오류라 이 수신자는 아직 안 보낸 것으로 두고 재개 대상에 남긴다
                automation.TryCapture(job.Logger.ScreenshotPath(r, "stop"));
                job.Logger.Log(r, "중단", ex.Message);
                progress.Report(new SendProgress(r, SendStatus.Pending, "중단됨: " + ex.Message));
                return new SendResult(SendOutcome.Aborted, sent, failed, ex.Message);
            }
            catch (Exception ex)
            {
                failed++;
                consecutiveFailures++;
                automation.TryCapture(job.Logger.ScreenshotPath(r, "fail"));
                job.Logger.Log(r, "실패", ex.Message);
                progress.Report(new SendProgress(r, SendStatus.Failed, ex.Message));
                if (consecutiveFailures >= MaxConsecutiveFailures)
                    return new SendResult(SendOutcome.Aborted, sent, failed,
                        $"연속 {MaxConsecutiveFailures}건 실패로 자동 중단했습니다. 마지막 오류: {ex.Message}");
            }

            if (job.IntervalSeconds > 0 && i < job.Targets.Count - 1)
                token.WaitHandle.WaitOne(TimeSpan.FromSeconds(job.IntervalSeconds));
        }
        return new SendResult(SendOutcome.Completed, sent, failed, null);
    }
}
