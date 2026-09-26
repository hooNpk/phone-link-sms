namespace PhoneLinkSmsApp.Services;

/// <summary>
/// UI Automation·클립보드는 STA에서만 안정적이다. Task.Run은 MTA 스레드풀이라 쓰면 안 된다.
/// </summary>
public static class StaThread
{
    public static Task<T> Run<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        { IsBackground = true, Name = "PhoneLinkAutomation" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
