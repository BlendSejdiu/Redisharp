using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RedSharp
{
    public class MiniRedis
    {
        private ConcurrentDictionary<string, CacheItem> store = new();

        #region String Operations
        public void Set(string key, string value, int? ttlSeconds = null)
        {
            DateTime? expiry = ttlSeconds.HasValue ? DateTime.UtcNow.AddSeconds(ttlSeconds.Value) : null;
            store[key] = new CacheItem(value, expiry);
        }
        public string? Get(string key)
        {
            if (store.TryGetValue(key, out var item))
            {
                if (item.IsExpired())
                {
                    store.TryRemove(key, out _);
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

            return keys.Count(key => store.TryRemove(key, out _));
        }

        public bool Exists(string key)
        {
            if (store.TryGetValue(key, out var item))
            {
                if (item.IsExpired())
                {
                    store.TryRemove(key, out _);
                    return false;
                }
                return true;
            }
            return false;
        }

        public long Ttl(string key)
        {
            if (!store.TryGetValue(key, out var item))
                return -2;

            if (item.IsExpired())
            {
                store.TryRemove(key, out _);
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
            if (store.TryGetValue(key, out var item))
                return item.GetList();

            var newList = new List<string>();
            store[key] = new CacheItem(newList, DataType.List);
            return newList;       
        }

        private bool TryGetValidList(string key, out List<string> list)
        {
            list = null;
            if (!store.TryGetValue(key, out var item))
                return false;

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
            if (store.TryGetValue(key, out var item))
                return item.GetHash();

            var newHash = new Dictionary<string, string>();
            store[key] = new CacheItem(newHash, DataType.Hash);
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
            if (!store.TryGetValue(key, out var item))
                return null;

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
            if (!store.TryGetValue(key, out var item))
                return null;

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
            if (!store.TryGetValue(key, out var item))
                return 0;

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
            if (!store.TryGetValue(key, out var item))
                return false;

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
            if (!store.TryGetValue(key, out var item))
                return 0;

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
            if (store.TryGetValue(key, out var item))
                return item.GetSet();

            var newHash = new HashSet<string>();
            store[key] = new CacheItem(newHash, DataType.Set);
            return newHash;
        }

        private bool TryGetValidSet(string key, out HashSet<string> set)
        {
            set = null;
            if (!store.TryGetValue(key, out var item))
                return false;

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
            if (store.TryGetValue(key, out var item))
                return item.GetSortedSet();

            var newSet = new SortedDictionary<double, HashSet<string>>();
            store[key] = new CacheItem(newSet, DataType.SortedSet);
            return newSet;
        }

        public long ZAdd(string key, Dictionary<string, double> members)
        {
            var zset = GetOrCreateSortedSet(key);
            long added = 0;

            foreach (var member in members)
            {
                foreach (var kvp in zset.ToList())
                    if (kvp.Value.Contains(member.Key))
                    {
                        kvp.Value.Remove(member.Key);
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
            if (!store.TryGetValue(key, out var item)) 
                return new List<string>();

            var result = new List<string>();
            var zset = item.GetSortedSet();
            var range = zset.Where(e => e.Key >= start && e.Key <= stop);

            if (reverse) range = range.Reverse();

            foreach (var entry in range)
            {
                var members = reverse ? entry.Value.Reverse() : entry.Value;
                foreach (var member in members)
                    result.Add(member);
                    if (withScores) result.Add(entry.Key.ToString());
            }

            return result;
        }

        public double? ZScore(string key, string member)
        {
            if (!store.TryGetValue(key, out var item)) 
                return null;

            var zset = item.GetSortedSet();
            foreach (var entry in zset)
                if (entry.Value.Contains(member))
                    return entry.Key;

            return null;
        }

        public long ZCount(string key, double min, double max)
        {
            if (!store.TryGetValue(key, out var item)) 
                return 0;

            return item.GetSortedSet()
                      .Where(e => e.Key >= min && e.Key <= max)
                      .Sum(e => e.Value.Count);
        }

        public long ZRem(string key, params string[] members)
        {
            if (!store.TryGetValue(key, out var item)) 
                return 0;

            var zset = item.GetSortedSet();
            long removed = 0;

            foreach (var member in members)
            {
                foreach (var entry in zset.ToList())
                {
                    if (entry.Value.Remove(member))
                    {
                        removed++;
                        if (entry.Value.Count == 0)
                        {
                            zset.Remove(entry.Key);
                        }
                        break;
                    }
                }
            }

            return removed;
        }

        #endregion
    }
}
