using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RedSharp
{
    public class PersistenceManager : IAsyncDisposable
    {
        private readonly ConcurrentDictionary<string, CacheItem> _store;
        private readonly AofManager _aofManager;
        private readonly RdbManager _rdbManager;
        private readonly TimeSpan _rdbInterval;
        private readonly string _rdbPath;
        private readonly string _aofPath;
        private readonly CancellationTokenSource _cts = new();
        private bool _disposed = false;
        private Task _backgroundSaveTask;

        public PersistenceManager(
            ConcurrentDictionary<string, CacheItem> store,
            string workingDirectory = "data",
            TimeSpan? rdbInterval = null,
            string rdbFileName = "dump.rdb",
            string aofFileName = "appendonly.aof",
            AofManager.AofSyncMode aofSyncMode = AofManager.AofSyncMode.EverySecond)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            Directory.CreateDirectory(workingDirectory);

            _rdbPath = Path.Combine(workingDirectory, rdbFileName);
            _aofPath = Path.Combine(workingDirectory, aofFileName);
            _rdbInterval = rdbInterval ?? TimeSpan.FromMinutes(5);

            _rdbManager = new RdbManager(_store, _rdbPath, _rdbInterval);
            _aofManager = new AofManager(_store, _aofPath);
            _aofManager.SetSyncMode(aofSyncMode);

            _backgroundSaveTask = Task.Run(BackgroundSaveLoop);
        }

        public async Task InitializeAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PersistenceManager));

            try
            {
                await _rdbManager.LoadSnapshotAsync().ConfigureAwait(false);

                if (File.Exists(_aofPath))
                    await Task.Run(() => _aofManager.ReplayAOF()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during persistence initialization: {ex.Message}");
                throw;
            }
        }

        public void LogCommand(string command)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PersistenceManager));

            if (string.IsNullOrWhiteSpace(command))
            {
                Console.WriteLine("Warning: Attempted to log empty command to AOF");
                return;
            }

            try
            {
                _aofManager.LogCommand(command);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error logging command to AOF: {ex.Message}");
                throw;
            }
        }

        public async Task SaveSnapshotAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PersistenceManager));

            try
            {
                await _rdbManager.SaveSnapshotAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving snapshot: {ex.Message}");
                throw;
            }
        }

        public async Task ForcePersistAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PersistenceManager));

            try
            {
                await Task.WhenAll(
                    _rdbManager.SaveSnapshotAsync(),
                    Task.Run(() => _aofManager.Flush())
                ).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during forced persistence: {ex.Message}");
                throw;
            }
        }

        private async Task BackgroundSaveLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_rdbInterval, _cts.Token).ConfigureAwait(false);
                    await SaveSnapshotAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in background save: {ex.Message}");
                }
            }
        }

        private async Task ShutdownAsync()
        {
            try
            {
                _cts.Cancel();

                await _backgroundSaveTask.ConfigureAwait(false);

                await ForcePersistAsync().ConfigureAwait(false);

                await Task.WhenAll(
                    _aofManager.DisposeAsync().AsTask(),
                    _rdbManager.DisposeAsync().AsTask()
                ).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during shutdown: {ex.Message}");
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            try
            {
                await ShutdownAsync().ConfigureAwait(false);
            }
            finally
            {
                _disposed = true;
                _cts.Dispose();
                GC.SuppressFinalize(this);
            }
        }

        ~PersistenceManager()
        {
            if (!_disposed)
            {
                DisposeAsync().AsTask().Wait();
            }
        }
    }
}