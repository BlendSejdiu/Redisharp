using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RedSharp
{
    public class AofManager : IDisposable
    {
        private readonly string _aofFilePath;
        private readonly ConcurrentDictionary<string, CacheItem> _store;
        private readonly ReaderWriterLockSlim _lock = new();
        private FileStream _stream;
        private StreamWriter _writer;
        private bool _isReplaying = false;
        private readonly Timer _flushTimer;
        private bool _needsFlush = false;
        private AofSyncMode _syncMode = AofSyncMode.EverySecond;

        public enum AofSyncMode
        {
            EverySecond, 
            Always,
            Never
        }

        public AofManager(ConcurrentDictionary<string, CacheItem> store, string aofFilePath)
        {
            _store = store;
            _aofFilePath = aofFilePath;

            Directory.CreateDirectory(Path.GetDirectoryName(_aofFilePath));

            OpenFileForWriting();

            if (File.Exists(_aofFilePath) && new FileInfo(_aofFilePath).Length > 0)
                ReplayAOF();

            _flushTimer = new Timer(FlushCallback, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        private void OpenFileForWriting()
        {
            try
            {
                _stream = new FileStream(
                    _aofFilePath,
                    FileMode.Append,  
                    FileAccess.Write,
                    FileShare.Read);
                _writer = new StreamWriter(_stream) { AutoFlush = true };
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Error opening AOF file: {ex.Message}");
                throw;
            }
        }

        public void SetSyncMode(AofSyncMode mode)
        {
            _syncMode = mode;
        }

        public void LogCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return;

            try
            {
                _lock.EnterWriteLock();

                if (_isReplaying)
                    return;

                _writer.WriteLine(command);

                switch (_syncMode)
                {
                    case AofSyncMode.Always:
                        _writer.Flush();
                        _stream.Flush(true);
                        break;
                    case AofSyncMode.EverySecond:
                        _needsFlush = true;
                        break;
                    case AofSyncMode.Never:
                        break;
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
        private void FlushCallback(object state)
        {
            if (_needsFlush)
            {
                _lock.EnterWriteLock();
                try
                {
                    _writer.Flush();
                    _needsFlush = false;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }

        public void RewriteAOF()
        {
            _lock.EnterWriteLock();
            try
            {
                _isReplaying = true;

                string tempPath = Path.GetTempFileName();

                using (var tempStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                using (var tempWriter = new StreamWriter(tempStream))
                {
                    foreach (var kvp in _store)
                    {
                        var command = SerializeToCommand(kvp.Key, kvp.Value);
                        tempWriter.WriteLine(command);
                    }
                }

                File.Replace(tempPath, _aofFilePath, null);

                _writer?.Dispose();
                _stream?.Dispose();
                _stream = new FileStream(_aofFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                _writer = new StreamWriter(_stream);
            }
            finally
            {
                _isReplaying = false;
                _lock.ExitWriteLock();
            }
        }

        private string? SerializeToCommand(string key, CacheItem item)
        {
            switch (item.Type)
            {
                case DataType.String:
                    return item.ExpiryTime.HasValue ? $"SET {key} {item.Value} EX {(int)(item.ExpiryTime.Value - DateTime.UtcNow).TotalSeconds}"
                        : $"SET {key} {item.Value}";

                case DataType.List:
                    var list = item.GetList();
                    return list.Count > 0 ? $"RPUSH {key} {string.Join(" ", list)}"
                        : null;

                case DataType.Hash:
                    var hash = item.GetHash();
                    return hash.Count > 0 ? $"HMSET {key} {string.Join(" ", hash.SelectMany(kvp => new[] { kvp.Key, kvp.Value }))}" : null;

                case DataType.Set:
                    var set = item.GetSet();
                    return set.Count > 0 ? $"SADD {key} {string.Join(" ", set)}" : null;

                case DataType.SortedSet:
                    var sortedSet = item.GetSortedSet();
                    if (sortedSet.Count == 0) return null;

                    var members = new List<string>();
                    foreach (var kvp in sortedSet)
                        foreach (var member in kvp.Value)
                            members.Add($"{kvp.Key} {member}");

                    return $"ZADD {key} {string.Join(" ", members)}";

                default:
                    return null;
            }
        }

        public void RotateFile()
        {
            _lock.EnterWriteLock();
            try
            {
                _writer.Flush();
                _writer.Dispose();
                _stream.Dispose();

                string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                string newPath = $"{_aofFilePath}.{timestamp}";
                File.Move(_aofFilePath, newPath);

                _stream = new FileStream(_aofFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                _writer = new StreamWriter(_stream);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Flush()
        {
            try
            {
                _lock.EnterWriteLock();
                _writer.Flush();
                _stream.Flush(true);
                _needsFlush = false;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Dispose()
        {
            _lock.EnterWriteLock();
            try
            {
                _writer?.Flush();
                _writer?.Dispose();
                _stream?.Dispose();
                _flushTimer?.Dispose();
            }
            finally
            {
                _lock.ExitWriteLock();
                _lock.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _lock.EnterWriteLock();
            try
            {
                await _writer.FlushAsync();
                await _writer.DisposeAsync();
                await _stream.DisposeAsync();
                _flushTimer?.Dispose();
            }
            finally
            {
                _lock.ExitWriteLock();
                _lock.Dispose();
            }
        }

        public void ReplayAOF()
        {
            if (!File.Exists(_aofFilePath))
                return;

            bool lockTaken = false;
            try
            {
                _lock.EnterWriteLock();
                lockTaken = true;
                _isReplaying = true;

                if (_writer != null)
                {
                    _writer.Flush();
                    _writer.Dispose();
                    _writer = null;
                }
                if (_stream != null)
                {
                    _stream.Dispose();
                    _stream = null;
                }


                using var fileStream = new FileStream(_aofFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fileStream);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    // Skip empty lines and comments
                    line = line.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                        continue;

                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0)
                        continue;

                    var command = parts[0].ToUpperInvariant();
                    
                    // Validate key format - keys should not contain null bytes or control characters
                    bool IsValidKey(string key) => !string.IsNullOrEmpty(key) && 
                                                   key.Length <= 512 && 
                                                   key.IndexOfAny(new[] { '\0', '\n', '\r' }) < 0;

                    try
                    {
                        switch (command)
                        {
                            case "SET":
                                if (parts.Length >= 3 && IsValidKey(parts[1]))
                                {
                                    var key = parts[1];
                                    var value = parts[2];
                                    DateTime? expiry = null;

                                    for (int i = 3; i < parts.Length; i++)
                                    {
                                        if (parts[i].ToUpperInvariant() == "EX" && i + 1 < parts.Length)
                                        {
                                            if (int.TryParse(parts[i + 1], out var ex) && ex > 0)
                                                expiry = DateTime.UtcNow.AddSeconds(ex);
                                            break;
                                        }
                                    }

                                    _store[key] = new CacheItem(value, expiry);
                                }
                                break;

                            case "DEL":
                                if (parts.Length >= 2)
                                    foreach (var key in parts.Skip(1))
                                        if (IsValidKey(key))
                                            _store.TryRemove(key, out _);
                                break;

                            case "EXPIRE":
                                if (parts.Length >= 3 && IsValidKey(parts[1]))
                                {
                                    var key = parts[1];
                                    if (int.TryParse(parts[2], out var seconds) && seconds > 0 && _store.TryGetValue(key, out var item))
                                        item.ExpiryTime = DateTime.UtcNow.AddSeconds(seconds);
                                }
                                break;

                            case "PEXPIRE":
                                if (parts.Length >= 3 && IsValidKey(parts[1]))
                                {
                                    var key = parts[1];
                                    if (int.TryParse(parts[2], out var milliseconds) && milliseconds > 0 && _store.TryGetValue(key, out var item))
                                        item.ExpiryTime = DateTime.UtcNow.AddMilliseconds(milliseconds);
                                }
                                break;

                            case "PERSIST":
                                if (parts.Length >= 2 && IsValidKey(parts[1]))
                                {
                                    var key = parts[1];
                                    if (_store.TryGetValue(key, out var item))
                                        item.ExpiryTime = null;
                                }
                                break;

                            case "LPUSH":
                                if (parts.Length >= 3 && IsValidKey(parts[1]))
                                {
                                    var key = parts[1];
                                    var values = parts.Skip(2).ToArray();

                                    if (_store.TryGetValue(key, out var existingItem) && existingItem.Type == DataType.List)
                                    {
                                        var list = existingItem.GetList();
                                        list.InsertRange(0, values);
                                    }
                                    else
                                    {
                                        var newList = new List<string>(values);
                                        _store[key] = new CacheItem(newList, DataType.List, null);
                                    }
                                }
                                break;

                            case "RPUSH":
                                if (parts.Length >= 3 && IsValidKey(parts[1]))
                                {
                                    var key = parts[1];
                                    var values = parts.Skip(2).ToArray();

                                    if (_store.TryGetValue(key, out var existingItem) && existingItem.Type == DataType.List)
                                    {
                                        var list = existingItem.GetList();
                                        list.AddRange(values);
                                    }
                                    else
                                    {
                                        var newList = new List<string>(values);
                                        _store[key] = new CacheItem(newList, DataType.List, null);
                                    }
                                }
                                break;

                            case "LPOP":
                                if (parts.Length >= 2 && IsValidKey(parts[1]))
                                {
                                    var key = parts[1];
                                    if (_store.TryGetValue(key, out var item) && item.Type == DataType.List)
                                    {
                                        var list = item.GetList();
                                        if (list.Count > 0)
                                        {
                                            list.RemoveAt(0);
                                            if (list.Count == 0)
                                                _store.TryRemove(key, out _);
                                        }
                                    }
                                }
                                break;

                            case "RPOP":
                                if (parts.Length >= 2 && IsValidKey(parts[1]))
                                {
                                    var key = parts[1];
                                    if (_store.TryGetValue(key, out var item) && item.Type == DataType.List)
                                    {
                                        var list = item.GetList();
                                        if (list.Count > 0)
                                        {
                                            list.RemoveAt(list.Count - 1);
                                            if (list.Count == 0)
                                                _store.TryRemove(key, out _);
                                        }
                                    }
                                }
                                break;

                            case "HSET":
                                if (parts.Length >= 4 && IsValidKey(parts[1]))
                                {
                                    var key = parts[1];
                                    var field = parts[2];
                                    var value = parts[3];

                                    if (_store.TryGetValue(key, out var existingItem) && existingItem.Type == DataType.Hash)
                                    {
                                        var hash = existingItem.GetHash();
                                        hash[field] = value;
                                    }
                                    else
                                    {
                                        var hash = new Dictionary<string, string> { [field] = value };
                                        _store[key] = new CacheItem(hash, DataType.Hash, null);
                                    }
                                }
                                break;

                            case "HMSET":
                                if (parts.Length >= 3 && parts.Length % 2 == 1 && IsValidKey(parts[1]))
                                {
                                    var key = parts[1];
                                    var hash = new Dictionary<string, string>();
                                    for (int i = 2; i < parts.Length; i += 2)
                                        if (i + 1 < parts.Length)
                                            hash[parts[i]] = parts[i + 1];

                                    if (_store.TryGetValue(key, out var existingItem) && existingItem.Type == DataType.Hash)
                                    {
                                        var existingHash = existingItem.GetHash();
                                        foreach (var kvp in hash)
                                            existingHash[kvp.Key] = kvp.Value;
                                    }
                                    else
                                        _store[key] = new CacheItem(hash, DataType.Hash, null);
                                }
                                break;

                            case "HDEL":
                                if (parts.Length >= 3 && IsValidKey(parts[1]))
                                {
                                    var key = parts[1];
                                    var fields = parts.Skip(2).ToArray();

                                    if (_store.TryGetValue(key, out var item) && item.Type == DataType.Hash)
                                    {
                                        var hash = item.GetHash();
                                        foreach (var field in fields)
                                            hash.Remove(field);

                                        if (hash.Count == 0)
                                            _store.TryRemove(key, out _);
                                    }
                                }
                                break;

                            case "SADD":
                                if (parts.Length >= 3 && IsValidKey(parts[1]))
                                {
                                    var key = parts[1];
                                    var members = parts.Skip(2).ToArray();

                                    if (_store.TryGetValue(key, out var existingItem) && existingItem.Type == DataType.Set)
                                    {
                                        var set = existingItem.GetSet();
                                        foreach (var member in members)
                                            set.Add(member);
                                    }
                                    else
                                    {
                                        var set = new HashSet<string>(members);
                                        _store[key] = new CacheItem(set, DataType.Set, null);
                                    }
                                }
                                break;

                            case "SREM":
                                if (parts.Length >= 3 && IsValidKey(parts[1]))
                                {
                                    var key = parts[1];
                                    var members = parts.Skip(2).ToArray();

                                    if (_store.TryGetValue(key, out var item) && item.Type == DataType.Set)
                                    {
                                        var set = item.GetSet();
                                        foreach (var member in members)
                                            set.Remove(member);

                                        if (set.Count == 0)
                                            _store.TryRemove(key, out _);
                                    }
                                }
                                break;

                            case "ZADD":
                                if (parts.Length >= 4 && IsValidKey(parts[1]))
                                {
                                    var key = parts[1];
                                    var dict = new SortedDictionary<double, HashSet<string>>();

                                    for (int i = 2; i < parts.Length; i += 2)
                                    {
                                        if (i + 1 < parts.Length && double.TryParse(parts[i], out double score) && !double.IsNaN(score) && !double.IsInfinity(score))
                                        {
                                            if (!dict.TryGetValue(score, out var members))
                                            {
                                                members = new HashSet<string>();
                                                dict[score] = members;
                                            }
                                            members.Add(parts[i + 1]);
                                        }
                                    }

                                    if (_store.TryGetValue(key, out var existingItem) && existingItem.Type == DataType.SortedSet)
                                    {
                                        var existingSortedSet = existingItem.GetSortedSet();
                                        foreach (var kvp in dict)
                                        {
                                            if (!existingSortedSet.TryGetValue(kvp.Key, out var existingMembers))
                                                existingSortedSet[kvp.Key] = new HashSet<string>(kvp.Value);
                                            else
                                                foreach (var member in kvp.Value)
                                                    existingMembers.Add(member);
                                        }
                                    }
                                    else
                                    {
                                        _store[key] = new CacheItem(dict, DataType.SortedSet, null);
                                    }
                                }
                                break;

                            case "ZREM":
                                if (parts.Length >= 3 && IsValidKey(parts[1]))
                                {
                                    var key = parts[1];
                                    var members = parts.Skip(2).ToArray();

                                    if (_store.TryGetValue(key, out var item) && item.Type == DataType.SortedSet)
                                    {
                                        var sortedSet = item.GetSortedSet();
                                        bool modified = false;
                                        var scoresToRemove = new List<double>();

                                        foreach (var kvp in sortedSet)
                                        {
                                            foreach (var member in members)
                                                if (kvp.Value.Contains(member))
                                                {
                                                    kvp.Value.Remove(member);
                                                    modified = true;
                                                }

                                            if (kvp.Value.Count == 0)
                                                scoresToRemove.Add(kvp.Key);
                                        }

                                        foreach (var score in scoresToRemove)
                                            sortedSet.Remove(score);

                                        if (sortedSet.Count == 0)
                                            _store.TryRemove(key, out _);
                                    }
                                }
                                break;

                            case "ZINCRBY":
                                if (parts.Length >= 4 && IsValidKey(parts[1]))
                                {
                                    var key = parts[1];
                                    if (double.TryParse(parts[2], out double increment) && !double.IsNaN(increment) && !double.IsInfinity(increment) && !string.IsNullOrEmpty(parts[3]))
                                    {
                                        var member = parts[3];

                                        if (_store.TryGetValue(key, out var item) &&
                                            item.Type == DataType.SortedSet)
                                        {
                                            var sortedSet = item.GetSortedSet();
                                            bool found = false;

                                            foreach (var kvp in sortedSet)
                                                if (kvp.Value.Contains(member))
                                                {
                                                    kvp.Value.Remove(member);
                                                    var newScore = kvp.Key + increment;
                                                    
                                                    if (!double.IsNaN(newScore) && !double.IsInfinity(newScore))
                                                    {
                                                        if (!sortedSet.TryGetValue(newScore, out var newBucket))
                                                        {
                                                            newBucket = new HashSet<string>();
                                                            sortedSet[newScore] = newBucket;
                                                        }
                                                        newBucket.Add(member);
                                                    }
                                                    found = true;
                                                    break;
                                                }

                                            if (!found)
                                            {
                                                if (!sortedSet.TryGetValue(increment, out var newBucket))
                                                {
                                                    newBucket = new HashSet<string>();
                                                    sortedSet[increment] = newBucket;
                                                }
                                                newBucket.Add(member);
                                            }
                                        }
                                        else
                                        {
                                            var sortedSet = new SortedDictionary<double, HashSet<string>>();
                                            var members = new HashSet<string> { member };
                                            sortedSet[increment] = members;
                                            _store[key] = new CacheItem(sortedSet, DataType.SortedSet, null);
                                        }
                                    }
                                }
                                break;

                            default:
                                // Silently ignore unknown commands during replay to prevent injection
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log error but continue processing remaining commands
                        Console.WriteLine($"Error replaying command '{command}': {ex.Message}");
                    }
                }
            }
            finally
            {
                if (lockTaken)
                {
                    _isReplaying = false;
                    OpenFileForWriting();
                    _lock.ExitWriteLock();
                }
            }
        }
    }
}