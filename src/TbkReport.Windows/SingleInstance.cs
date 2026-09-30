using System;
using System.Threading;

namespace TbkReport;

// A stable per-session identity shared by every build of this application.
internal sealed class SingleInstance : IDisposable
{
    private const string Identity = @"Local\TBK.Report.7D57D83A";
    private readonly Mutex mutex;
    private readonly EventWaitHandle activation;
    public bool IsPrimary { get; }

    public SingleInstance()
    {
        mutex = new Mutex(false, Identity);
        activation = new EventWaitHandle(false, EventResetMode.AutoReset, Identity + ".Activate");
        try { IsPrimary = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
        if (!IsPrimary) activation.Set();
    }

    public bool TakeActivationRequest() => activation.WaitOne(0);

    public void Dispose()
    {
        if (IsPrimary) mutex.ReleaseMutex();
        activation.Dispose();
        mutex.Dispose();
    }
}
