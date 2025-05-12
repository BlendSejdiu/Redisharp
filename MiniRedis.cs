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
    }
}
