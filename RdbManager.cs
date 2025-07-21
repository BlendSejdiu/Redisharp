using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RedSharp
{
    public class RdbManager : IDisposable, IAsyncDisposable
    {
        private readonly string _rdbFilePath;
        private readonly ConcurrentDictionary<string, CacheItem> _store;
        private readonly TimeSpan _interval;
        private readonly CancellationTokenSource _cts = new();
        private readonly ReaderWriterLockSlim _lock = new();
        private Task _backgroundTask;
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = false, 
            PropertyNameCaseInsensitive = true
        };

        public RdbManager(ConcurrentDictionary<string, CacheItem> store, string rdbFilePath, TimeSpan interval)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _rdbFilePath = rdbFilePath ?? throw new ArgumentNullException(nameof(rdbFilePath));
            _interval = interval;

            _backgroundTask = Task.Run(() => SaveLoop(_cts.Token));
        }

        private async Task SaveLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await SaveSnapshotAsync(token);
                    await Task.Delay(_interval, token);
                }

                await SaveSnapshotAsync(token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RDB save loop: {ex.Message}");
            }
        }

        public async Task SaveSnapshotAsync(CancellationToken token = default)
        {
            bool lockTaken = false;
            try
            {
                lockTaken = _lock.TryEnterReadLock(TimeSpan.FromMilliseconds(100));
                if (!lockTaken)
                    throw new TimeoutException("Failed to acquire read lock for snapshot");
                else
                    _lock.EnterReadLock();

                token.ThrowIfCancellationRequested();

                var snapshot = new Dictionary<string, CacheItemEntry>();
                var utcNow = DateTime.UtcNow;

                foreach (var kvp in _store)
                {
                    if (kvp.Value.IsExpired())
                        continue;

                    snapshot[kvp.Key] = new CacheItemEntry
                    {
                        Type = kvp.Value.Type,
                        ExpiryTime = kvp.Value.ExpiryTime,
                        Value = kvp.Value.Value
                    };
                }

                await using var fileStream = new FileStream(
                    _rdbFilePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.Asynchronous);

                await JsonSerializer.SerializeAsync(fileStream, snapshot, _jsonOptions, token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"Error saving RDB snapshot: {ex.Message}");
                throw;
            }
            finally
            {
                if (lockTaken)
                    _lock.ExitReadLock();
            }
        }

        public async Task LoadSnapshotAsync(CancellationToken token = default)
        {
            try
            {
                _lock.EnterWriteLock();
                token.ThrowIfCancellationRequested();

                if (!File.Exists(_rdbFilePath))
                    return;

                await using var fileStream = new FileStream(
                    _rdbFilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    FileOptions.Asynchronous);

                var snapshot = await JsonSerializer.DeserializeAsync<Dictionary<string, CacheItemEntry>>(
                    fileStream, _jsonOptions, token);

                if (snapshot == null)
                    return;

                var utcNow = DateTime.UtcNow;

                foreach (var entry in snapshot)
                {
                    if (entry.Value.ExpiryTime.HasValue && entry.Value.ExpiryTime < utcNow)
                        continue;

                    try
                    {
                        _store[entry.Key] = new CacheItem(
                            entry.Value.Value,
                            entry.Value.Type,
                            entry.Value.ExpiryTime);
                    }
                    catch (ArgumentException ex)
                    {
                        Console.WriteLine($"Error loading key {entry.Key}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"Error loading RDB snapshot: {ex.Message}");
                throw;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _cts.Cancel();
                try
                {
                    if (_backgroundTask != null)
                        await _backgroundTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
            finally
            {
                _cts.Dispose();
                _lock.Dispose();
            }
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        private class CacheItemEntry
        {
            public DataType Type { get; set; }
            public DateTime? ExpiryTime { get; set; }
            public object Value { get; set; }
        }
    }
}