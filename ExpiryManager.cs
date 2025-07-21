using System.Collections.Concurrent;

namespace RedSharp
{
    public class ExpiryManager
    {
        private readonly ConcurrentDictionary<string, CacheItem> _store;
        private readonly TimeSpan _cleanupInterval;
        private readonly CancellationTokenSource _cts;

        public ExpiryManager(ConcurrentDictionary<string, CacheItem> store , TimeSpan cleanup)
        {
            _store = store;
            _cleanupInterval = cleanup;
            _cts = new CancellationTokenSource();
            Task.Run(() => CleanupLoop(_cts.Token));

        }

        private async Task CleanupLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    foreach (var key in _store.Keys)
                    {
                        if (_store.TryGetValue(key, out var item) && item.IsExpired())
                            _store.TryRemove(key, out _);
                    }

                    await Task.Delay(_cleanupInterval, token);
                }
                catch (TaskCanceledException)
                {
                    throw;
                }
            }
        }

        public async Task Dispose()
        {
            _cts.Cancel();
            try
            {
                await Task.Delay(_cleanupInterval);
            }
            catch (TaskCanceledException)
            {
                throw;
            }
            finally
            {
                _cts.Dispose();
            }
        }
    }
}
