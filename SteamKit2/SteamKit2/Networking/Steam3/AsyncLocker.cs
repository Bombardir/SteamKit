using System;
using System.Threading;
using System.Threading.Tasks;
using SPS.Core;

namespace SteamKit2.Networking.Steam3;

public class AsyncLocker : IDisposable
{
    private readonly SemaphoreSlim _semaphoreSlim;

    public AsyncLocker()
    {
        _semaphoreSlim = new SemaphoreSlim(1, 1);
    }

    public void Dispose()
    {
        _semaphoreSlim.Dispose();
    }

    public async ValueTask<IDisposable> LockAsync()
    {
        await _semaphoreSlim.WaitAsync();
        return new DisposableAction(() => _semaphoreSlim.Release());
    }

    public IDisposable Lock()
    {
        _semaphoreSlim.Wait();
        return new DisposableAction(() => _semaphoreSlim.Release());
    }
}
