using System.Collections.Concurrent;

namespace FullscreenStart.Services;

/// <summary>A single background STA thread for shell COM calls (enumeration, icon extraction).</summary>
public sealed class StaWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public StaWorker(string name)
    {
        _thread = new Thread(Run) { IsBackground = true, Name = name, Priority = ThreadPriority.BelowNormal };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<T> Run<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    public Task Run(Action work) => Run(() => { work(); return true; });

    private void Run()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
            work();
    }

    public void Dispose() => _queue.CompleteAdding();
}
