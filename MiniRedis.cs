using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace RedSharp
{
    public class MiniRedis : IAsyncDisposable
    {
        private readonly ConcurrentDictionary<string, CacheItem> _store = new();
        private readonly ExpiryManager _expiryManager;
        private readonly PersistenceManager _persistenceManager;
        private bool _disposed = false;
        private readonly Task _initializationTask;

        public MiniRedis(
            string workingDirectory = "data",
            TimeSpan? rdbInterval = null,
            AofManager.AofSyncMode aofSyncMode = AofManager.AofSyncMode.EverySecond,
            TimeSpan? expiryCheckInterval = null)
        {
            _expiryManager = new ExpiryManager(_store, expiryCheckInterval ?? TimeSpan.FromSeconds(60));
            _persistenceManager = new PersistenceManager(
                _store,
                workingDirectory,
                rdbInterval,
                aofSyncMode: aofSyncMode);

            _initializationTask = InitializePersistenceAsync();
        }

        #region Persistence
        private async Task InitializePersistenceAsync()
        {
            try
            {
                await _persistenceManager.InitializeAsync();
                Console.WriteLine("Persistence initialized successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize persistence: {ex.Message}");
                throw;
            }
        }

        public void LogCommand(string command)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MiniRedis));

            if (string.IsNullOrWhiteSpace(command))
            {
                Console.WriteLine("Warning: Attempted to log empty command");
                return;
            }

            try
            {
                _persistenceManager.LogCommand(command);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error logging command: {ex.Message}");
                throw;
            }
        }

        public async Task SaveAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MiniRedis));

            try
            {
                await _persistenceManager.ForcePersistAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during save: {ex.Message}");
                throw;
            }
        }

        public async Task WaitForInitializationAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MiniRedis));

            await _initializationTask;
        }
        #endregion

        #region Cleanup
        public async Task ShutdownAsync()
        {
            if (_disposed) 
                return;

            try
            {
                await Task.WhenAll(
                    _persistenceManager.DisposeAsync().AsTask(),
                    _expiryManager.Dispose());
            }
            finally
            {
                _disposed = true;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await ShutdownAsync();
            GC.SuppressFinalize(this);
        }

        ~MiniRedis()
        {
            if (!_disposed)
                DisposeAsync().AsTask().Wait();
        }
        #endregion

        #region TTL
        public void Stop()
        {
            _ = _expiryManager.Dispose();
        }
        public bool Expired(string key, int seconds)
        {
            if (_store.TryGetValue(key, out var item))
            {
                if (item.IsExpired())
                {
                    _store.TryRemove(key, out _);
                    return false;
                }

                item.ExpiryTime = DateTime.UtcNow.AddSeconds(seconds);
                return true;
            }
            
            return false;
        }

        public bool PExpire(string key, int miliseconds)
        {
            if (_store.TryGetValue(key, out var item))
            {
                if (item.IsExpired())
                {
                    _store.TryRemove(key, out _);
                    return false;
                }

                item.ExpiryTime = DateTime.UtcNow.AddMilliseconds(miliseconds);
                return true;
            }

            return false;
        }

        public bool Persist(string key)
        {
            if (_store.TryGetValue(key, out var item))
            {
                if (item.IsExpired())
                {
                    _store.TryRemove(key, out _);
                    return false;
                }

                if (item.ExpiryTime.HasValue)
                {
                    item.ExpiryTime = null;
                    return true;
                }
                return false;
            }

            return false;
        }
        #endregion

        #region String Operations
        public void Set(string key, string value, int? ttlSeconds = null)
        {
            DateTime? expiry = ttlSeconds.HasValue ? DateTime.UtcNow.AddSeconds(ttlSeconds.Value) : null;
            _store[key] = new CacheItem(value, expiry);
        }
        public string? Get(string key)
        {
            if (_store.TryGetValue(key, out var item))
            {
                if (item.IsExpired())
                {
                    _store.TryRemove(key, out _);
                    return null;
                }
                return item.GetString();
            }
            return null;
        }

        public long Delete(params string[] keys)
        {
            if (keys == null || keys.Length == 0)
                return 0;

            return keys.Count(key => _store.TryRemove(key, out _));
        }

        public bool Exists(string key)
        {
            if (_store.TryGetValue(key, out var item))
            {
                if (item.IsExpired())
                {
                    _store.TryRemove(key, out _);
                    return false;
                }
                return true;
            }
            return false;
        }

        public long Ttl(string key)
        {
            if (!_store.TryGetValue(key, out var item))
                return -2;

            if (item.IsExpired())
            {
                _store.TryRemove(key, out _);
                return -2;
            }

            if (!item.ExpiryTime.HasValue)
                return -1;

            var ttl = (item.ExpiryTime.Value - DateTime.UtcNow).TotalSeconds;
            return ttl > 0 ? (long)ttl : -2;
        }

        public long Increment(string key)
        {
            var val = Get(key) ?? "0";
            if (!long.TryParse(val, out var number))
                throw new InvalidOperationException("Value is not a integer.");

            number++;
            Set(key, number.ToString());
            return number;
        }
        #endregion

        #region List Operations

        #region Helper Methods
        private List<string> GetOrCreateList(string key)
        {
            if (_store.TryGetValue(key, out var item))
            {
                if (item.IsExpired())
                    _store.TryRemove(key, out _);
                else
                    return item.GetList();
            }

            var newList = new List<string>();
            _store[key] = new CacheItem(newList, DataType.List);
            return newList;
        }

        private bool TryGetValidList(string key, out List<string> list)
        {
            list = null;
            if (!_store.TryGetValue(key, out var item))
                return false;

            if (item.IsExpired())
            {
                _store.TryRemove(key, out _);
                return false;
            }

            try
            {
                list = item.GetList();
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
        #endregion

        public long LPush(string key, params string[] values)
        { 
            var list = GetOrCreateList(key);

            foreach (var item in values.Reverse())
                list.Insert(0, item);

            return list.Count;
        }

        public long RPush(string key, params string[] values)
        { 
            var list = GetOrCreateList(key);
            list.AddRange(values);
            return list.Count;
        }

        public string? LPop(string key)
        {
            if (!TryGetValidList(key, out var list))
                return null;

            if (list.Count == 0)
                return null;
                
            var value = list[0];
            list.RemoveAt(0);
            return value;
        }

        public string? RPop(string key)
        {
            if (!TryGetValidList(key, out var list))
                return null;

            if (list.Count == 0)
                return null;

            var lastIndex = list.Count - 1;
            var value = list[lastIndex];
            list.RemoveAt(lastIndex);
            return value;
        }

        public long LLen(string key)
        {
            if (!TryGetValidList(key, out var list))
                return 0;

            return list.Count;
        }

        public List<string>? LRange(string key, long start, long stop)
        {
            if (!TryGetValidList(key, out var list))
                return null;

            start = start < 0 ? list.Count + start : start;
            stop = stop < 0 ? list.Count + stop : stop;

            start = Math.Max(0, start);
            stop = Math.Min(list.Count - 1, stop);

            if (start > stop || list.Count == 0)
                return new List<string>();

            return list.GetRange((int)start, (int)(stop - start + 1));
        }

        #endregion

        #region Hash Operations 

        #region Helper Methods
        private Dictionary<string, string> GetOrCreateHash(string key)
        {
            if (_store.TryGetValue(key, out var item))
            {
                if (item.IsExpired())
                    _store.TryRemove(key, out _);
                else
                    return item.GetHash();
            }

            var newHash = new Dictionary<string, string>();
            _store[key] = new CacheItem(newHash, DataType.Hash);
            return newHash;
        }
        #endregion

        public long HSet(string key, string field, string value)
        {
            var hash = GetOrCreateHash(key);
            var newField = !hash.ContainsKey(field);
            hash[field] = value;
            return newField ? 1 : 0;
        }

        public long HSet(string key, Dictionary<string, string> fields)
        {
            var hash = GetOrCreateHash(key);
            var count = 0L;
            foreach (var kvp in fields)
            {
                if (!hash.ContainsKey(key))
                    count++;
                hash[kvp.Key] = kvp.Value;
            }
            return count;
        }

        public string? HGet(string key, string field)
        {
            if (!_store.TryGetValue(key, out var item))
                return null;

            if (item.IsExpired())
            {
                _store.TryRemove(key, out _);
                return null;
            }

            try
            {
                var hash = item.GetHash();
                return hash.TryGetValue(field, out var value) ? value : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        public Dictionary<string, string>? HGetAll(string key)
        {
            if (!_store.TryGetValue(key, out var item))
                return null;

            if (item.IsExpired())
            {
                _store.TryRemove(key, out _);
                return null;
            }

            try
            {
                return new Dictionary<string, string>(item.GetHash());
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        public long HDel(string key, params string[] fields)
        {
            if (!_store.TryGetValue(key, out var item))
                return 0;

            if (item.IsExpired())
            {
                _store.TryRemove(key, out _);
                return 0;
            }

            try
            {
                var hash = item.GetHash();
                var deleted = 0L;

                foreach (var kvp in fields)
                    if (hash.Remove(kvp))
                        deleted++;

                return deleted;
            }
            catch (InvalidOperationException)
            {
                return 0;
            }
        }

        public bool HExists(string key, string field)
        {
            if (!_store.TryGetValue(key, out var item))
                return false;

            if (item.IsExpired())
            {
                _store.TryRemove(key, out _);
                return false;
            }

            try
            {
                return item.GetHash().ContainsKey(field);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public long HLen(string key)
        {
            if (!_store.TryGetValue(key, out var item))
                return 0;

            if (item.IsExpired())
            {
                _store.TryRemove(key, out _);
                return 0;
            }

            try
            {
                return item.GetHash().Count;
            }
            catch (InvalidOperationException)
            {
                return 0;
            }
        }
        #endregion

        #region Set Operations

        #region Helper Methods
        private HashSet<string> GetOrCreateSet(string key)
        {
            if (_store.TryGetValue(key, out var item))
            {
                if (item.IsExpired())
                    _store.TryRemove(key, out _);
                else
                    return item.GetSet();
            }

            var newSet = new HashSet<string>();
            _store[key] = new CacheItem(newSet, DataType.Set);
            return newSet;
        }

        private bool TryGetValidSet(string key, out HashSet<string> set)
        {
            set = null;
            if (!_store.TryGetValue(key, out var item))
                return false;

            if (item.IsExpired())
            {
                _store.TryRemove(key, out _);
                return false;
            }

            try
            {
                set = item.GetSet();
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
        #endregion

        public long SAdd(string key, params string[] members)
        { 
            var set = GetOrCreateSet(key);
            var added = 0L;
            foreach (var member in members)
                if(set.Add(member))
                    added++;
            return added;
        }

        public bool SIsMember(string key, string member)
        {
            if (!TryGetValidSet(key, out var set))
                return false;

            return set.Contains(member);
        }

        public HashSet<string>? SMembers(string key)
        {
            if (!TryGetValidSet(key, out var set))
                return null;

            return new HashSet<string>(set);
        }

        public long SCard(string key)
        {
            if (!TryGetValidSet(key, out var set))
                return 0;

            return set.Count;
        }

        public long SRem(string key, params string[] members)
        {
            if (!TryGetValidSet(key, out var set))
                return 0;

            var removed = 0;
            foreach (var member in members)
                if (set.Remove(member))
                    removed++;
               
            return removed;
        }

        public HashSet<string>? SDiff(params string[] keys)
        {
            if (keys.Length == 0)
                return null;

            HashSet<string>? result = null;
            foreach (var key in keys)
            {
                if (!TryGetValidSet(key, out var set))
                    continue;

                if (result == null)
                    result = new HashSet<string>(set);
                else
                    result.ExceptWith(set);
            }
            return result ?? new HashSet<string>();
        }
        #endregion

        #region Sorted Set Operations
        private SortedDictionary<double, HashSet<string>> GetOrCreateSortedSet(string key)
        {
            if (_store.TryGetValue(key, out var item))
            {
                if (item.IsExpired())
                    _store.TryRemove(key, out _);
                else
                    return item.GetSortedSet();
            }

            var newSet = new SortedDictionary<double, HashSet<string>>();
            _store[key] = new CacheItem(newSet, DataType.SortedSet);
            return newSet;
        }

        private bool TryGetValidSortedSet(string key, out SortedDictionary<double, HashSet<string>> zset)
        {
            zset = null;
            if (!_store.TryGetValue(key, out var item))
                return false;

            if (item.IsExpired())
            {
                _store.TryRemove(key, out _);
                return false;
            }

            try
            {
                zset = item.GetSortedSet();
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public long ZAdd(string key, Dictionary<string, double> members)
        {
            var zset = GetOrCreateSortedSet(key);
            long added = 0;

            foreach (var member in members)
            {
                foreach (var kvp in zset.ToList())
                    if (kvp.Value.Remove(member.Key))
                    {
                        if (kvp.Value.Count == 0)
                            zset.Remove(kvp.Key);
                        break;
                    }

                if (!zset.TryGetValue(member.Value, out var membersAtScore))
                {
                    membersAtScore = new HashSet<string>();
                    zset[member.Value] = membersAtScore;
                }

                if (membersAtScore.Add(member.Key))
                    added++;
            }

            return added;
        }

        public List<string> ZRange(string key, double start, double stop, bool withScores = false, bool reverse = false)
        {
            if (!TryGetValidSortedSet(key, out var zset))
                return new List<string>();

            var result = new List<string>();
            var range = zset.Where(e => e.Key >= start && e.Key <= stop);
            if (reverse) range = range.Reverse();

            foreach (var entry in range)
            {
                var members = reverse ? entry.Value.Reverse() : entry.Value;
                foreach (var member in members)
                {
                    result.Add(member);
                    if (withScores)
                        result.Add(entry.Key.ToString());
                }
            }

            return result;
        }

        public double? ZScore(string key, string member)
        {
            if (!TryGetValidSortedSet(key, out var zset))
                return null;

            foreach (var entry in zset)
                if (entry.Value.Contains(member))
                    return entry.Key;

            return null;
        }

        public long ZCount(string key, double min, double max)
        {
            if (!TryGetValidSortedSet(key, out var zset))
                return 0;

            return zset.Where(e => e.Key >= min && e.Key <= max).Sum(e => e.Value.Count);
        }
        public long ZRem(string key, params string[] members)
        {
            if (!TryGetValidSortedSet(key, out var zset))
                return 0;

            long removed = 0;
            foreach (var member in members)
            {
                foreach (var entry in zset.ToList())
                {
                    if (entry.Value.Remove(member))
                    {
                        removed++;
                        if (entry.Value.Count == 0)
                            zset.Remove(entry.Key);
                        break;
                    }
                }
            }

            return removed;
        }
        public List<string> ZRangeByScore(string key, double min, double max, bool withScores = false, int offset = 0, int count = int.MaxValue)
        {
            if (!TryGetValidSortedSet(key, out var zset))
                return new List<string>();

            var result = new List<string>();
            int itemsSkipped = 0, itemsAdded = 0;

            foreach (var entry in zset)
            {
                if (entry.Key < min) continue;
                if (entry.Key > max) break;

                foreach (var member in entry.Value)
                {
                    if (itemsSkipped < offset)
                    {
                        itemsSkipped++;
                        continue;
                    }

                    if (itemsAdded >= count)
                        return result;

                    result.Add(member);
                    if (withScores)
                        result.Add(entry.Key.ToString());

                    itemsAdded++;
                }
            }

            return result;
        }

        public double ZIncrBy(string key, string member, double increment)
        {
            var zset = GetOrCreateSortedSet(key);
            double currentScore = 0;
            bool memberExists = false;

            foreach (var entry in zset.ToList())
            {
                if (entry.Value.Contains(member))
                {
                    currentScore = entry.Key;
                    entry.Value.Remove(member);

                    if (entry.Value.Count == 0)
                        zset.Remove(entry.Key);

                    memberExists = true;
                    break;
                }
            }

            double newScore = currentScore + increment;

            if (!zset.TryGetValue(newScore, out var members))
            {
                members = new HashSet<string>();
                zset[newScore] = members;
            }
            members.Add(member);

            return newScore;
        }

        public long ZUnionStore(string destination, Dictionary<string, double> keysWithWeights, AggregateType aggregate = AggregateType.Sum)
        {
            var merged = new Dictionary<string, double>();

            foreach (var kvp in keysWithWeights)
            {
                if (!TryGetValidSortedSet(kvp.Key, out var zset)) continue;

                foreach (var entry in zset)
                {
                    foreach (var member in entry.Value)
                    {
                        var weightedScore = entry.Key * kvp.Value;

                        if (!merged.TryGetValue(member, out var currentScore))
                        {
                            merged[member] = weightedScore;
                        }
                        else
                        {
                            merged[member] = aggregate switch
                            {
                                AggregateType.Sum => currentScore + weightedScore,
                                AggregateType.Min => Math.Min(currentScore, weightedScore),
                                AggregateType.Max => Math.Max(currentScore, weightedScore),
                                _ => currentScore + weightedScore
                            };
                        }
                    }
                }
            }

            var result = new SortedDictionary<double, HashSet<string>>();
            foreach (var kvp in merged)
            {
                if (!result.TryGetValue(kvp.Value, out var members))
                {
                    members = new HashSet<string>();
                    result[kvp.Value] = members;
                }
                members.Add(kvp.Key);
            }

            _store[destination] = new CacheItem(result, DataType.SortedSet);
            return merged.Count;
        }

        public long ZInterStore(string destination, Dictionary<string, double> keysWithWeights, AggregateType aggregate = AggregateType.Sum)
        {
            if (keysWithWeights.Count == 0)
                return 0;

            var commonMembers = new HashSet<string>();
            bool firstSet = true;

            foreach (var kvp in keysWithWeights)
            {
                if (!TryGetValidSortedSet(kvp.Key, out var zset))
                    return 0;

                var currentMembers = new HashSet<string>();
                foreach (var entry in zset)
                    foreach (var member in entry.Value)
                        currentMembers.Add(member);

                if (firstSet)
                {
                    commonMembers.UnionWith(currentMembers);
                    firstSet = false;
                }
                else
                {
                    commonMembers.IntersectWith(currentMembers);
                    if (commonMembers.Count == 0) break;
                }
            }

            var result = new SortedDictionary<double, HashSet<string>>();
            foreach (var member in commonMembers)
            {
                double? aggregatedScore = null;

                foreach (var kvp in keysWithWeights)
                {
                    if (!TryGetValidSortedSet(kvp.Key, out var zset))
                        continue;

                    foreach (var entry in zset)
                    {
                        if (entry.Value.Contains(member))
                        {
                            var weightedScore = entry.Key * kvp.Value;
                            aggregatedScore = aggregatedScore.HasValue
                                ? aggregate switch
                                {
                                    AggregateType.Sum => aggregatedScore + weightedScore,
                                    AggregateType.Min => Math.Min(aggregatedScore.Value, weightedScore),
                                    AggregateType.Max => Math.Max(aggregatedScore.Value, weightedScore),
                                    _ => aggregatedScore + weightedScore
                                }
                                : weightedScore;
                            break;
                        }
                    }
                }

                if (aggregatedScore.HasValue)
                {
                    if (!result.TryGetValue(aggregatedScore.Value, out var members))
                    {
                        members = new HashSet<string>();
                        result[aggregatedScore.Value] = members;
                    }
                    members.Add(member);
                }
            }

            _store[destination] = new CacheItem(result, DataType.SortedSet);
            return commonMembers.Count;
        }

        public enum AggregateType { Sum, Min, Max }

        #endregion
    }
}