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

    }
}
