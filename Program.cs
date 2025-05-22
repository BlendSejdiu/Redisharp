
using RedSharp;
using System.Collections.Generic;

class Program
{
    static void Main()
    {
        var redis = new MiniRedis();

        Console.WriteLine("Mini Redis-like Cache DB using C# (type 'exit' to quit or 'help' to see commands)");

        while (true)
        {
            Console.Write("> ");
            var input = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(input))
                continue;
            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            try
            {
                var parts = input.Split(' ');
                var command = parts[0].ToUpper();

                var response = command switch
                {
                    "SET" => HandleSet(redis, parts),
                    "GET" => HandleGet(redis, parts),
                    "DEL" => HandleDel(redis, parts),
                    "EXISTS" => HandleExists(redis, parts),
                    "TTL" => HandleTtl(redis, parts),
                    "INCR" => HandleIncr(redis, parts),
                    "LPUSH" => HandleLPush(redis, parts),
                    "RPUSH" => HandleRPush(redis, parts),
                    "LPOP" => HandleLPop(redis, parts),
                    "RPOP" => HandleRPop(redis, parts),
                    "LLEN" => HandleLLen(redis, parts),
                    "LRANGE" => HandleLRange(redis, parts),
                    "HSET" => HandleHSet(redis, parts),
                    "HGET" => HandleHGet(redis, parts),
                    "HGETALL" => HandleHGetAll(redis, parts),
                    "HDEL" => HandleHDel(redis, parts),
                    "HEXISTS" => HandleHExists(redis, parts),
                    "HLEN" => HandleHLen(redis, parts),
                    "SADD" => HandleSAdd(redis, parts),
                    "SISMEMBER" => HandleSIsMember(redis, parts),
                    "SMEMBERS" => HandleSMembers(redis, parts),
                    "SCARD" => HandleSCard(redis, parts),
                    "SREM" => HandleSRem(redis, parts),
                    "ZADD" => HandleZAdd(redis, parts),
                    "ZRANGE" => HandleZRange(redis, parts),
                    "ZREVRANGE" => HandleZRange(redis, parts),
                    "ZSCORE" => HandleZScore(redis, parts),
                    "ZCOUNT" => HandleZCount(redis, parts),
                    "ZREM" => HandleZRem(redis, parts),
                    "HELP" => "Available commands: SET, GET, DEL, EXISTS, TTL, INCR, " +
                              "LPUSH, RPUSH, LPOP, RPOP, LLEN, LRANGE, " +
                              "HSET, HGET, HGETALL, HDEL, HEXISTS, HLEN, " +
                              "SADD, SREM, SMEMBERS, SISMEMBER, SCARD, " + "ZADD, ZRANGE, ZREVRANGE, ZSCORE, ZCOUNT, ZREM, " + "HELP, EXIT",
                    _ => $"Unknown command: {command}"
                };

                Console.WriteLine(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }

    #region String Handlers
    static string HandleSet(MiniRedis redis, string[] parts)
    {
        if (parts.Length < 3)
            return "Syntax: SET key value [EX seconds]";

        string key = parts[1];
        string value = parts[2];
        int? ttl = null;

        if (parts.Length > 4 && parts[3].Equals("EX", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(parts[4], out int seconds))
                ttl = seconds;
            else
                return "Invalid TTL value.";
        }

        redis.Set(key, value, ttl);
        return "OK";
    }

    static string HandleGet(MiniRedis redis, string[] parts)
    {
        if (parts.Length != 2)
            return "Syntax: GET key";

        return redis.Get(parts[1]) ?? "Not Found";
    }

    static string HandleDel(MiniRedis redis, string[] parts)
    {
        if (parts.Length < 2)
            return "Syntax: DEL key [key ...]";

        var keys = parts[1..];
        var deleted = redis.Delete(keys);
        return "Delete Success.";
    }

    static string HandleExists(MiniRedis redis, string[] parts)
    {
        if (parts.Length != 2)
            return "Syntax: EXISTS key";

        return redis.Exists(parts[1]) ? "True" : "False";
    }

    static string HandleTtl(MiniRedis redis, string[] parts)
    {
        if (parts.Length != 2)
            return "Syntax: TTL key";

        var ttl = redis.Ttl(parts[1]);
        return $"(integer) {ttl}";
    }

    static string HandleIncr(MiniRedis redis, string[] parts)
    {
        if (parts.Length != 2)
            return "Syntax: INCR key";

        var newValue = redis.Increment(parts[1]);
        return $"(integer) {newValue}";
    }
    #endregion

    #region List Handlers
    static string HandleLPush(MiniRedis redis, string[] parts)
    { 
        if(parts.Length < 3)
            return "ERR wrong number of arguments for 'LPUSH' command";

        try
        {
            var count = redis.LPush(parts[1], parts[2..]);
            return $"(integer) {count}";
        }
        catch (InvalidOperationException ex)
        {
            return $"ERR {ex.Message}";
        }
    }

    static string HandleRPush(MiniRedis redis, string[] parts)
    {
        if (parts.Length < 3)
            return "ERR wrong number of arguments for 'RPush' command";

        try
        {
            var count = redis.RPush(parts[1], parts[2..]);
            return $"(integer) {count}";
        }
        catch (InvalidOperationException ex)
        {
            return $"ERR {ex.Message}";
        }
    }

    static string HandleLPop(MiniRedis redis, string[] parts)
    {
        if(parts.Length != 2)
            return "ERR wrong number of arguments for 'LPOP' command";

        try
        {
            var value = redis.LPop(parts[1]);
            return value ?? "null";
        }
        catch (InvalidOperationException ex)
        {
            return $"ERR {ex.Message}";
        }
    }

    static string HandleRPop(MiniRedis redis, string[] parts)
    {
        if (parts.Length != 2)
            return "ERR wrong number of arguments for 'RPOP' command";

        try
        {
            var value = redis.RPop(parts[1]);
            return value ?? "null";
        }
        catch (InvalidOperationException ex)
        {
            return $"ERR {ex.Message}";
        }
    }

    static string HandleLLen(MiniRedis redis, string[] parts)
    {
        if (parts.Length != 2)
            return "ERR wrong number of arguments for 'LLEN' command";

        try
        {
            var length = redis.LLen(parts[1]);
            return $"(integer) {length}";
        }
        catch (InvalidOperationException ex)
        {
            return $"ERR {ex.Message}";
        }
    }

    static string HandleLRange(MiniRedis redis, string[] parts)
    {
        if (parts.Length != 4)
            return "ERR wrong number of arguments for 'LRANGE' command";

        if (!long.TryParse(parts[2], out long start) || !long.TryParse(parts[3], out long stop))
            return "ERR value is not an integer or out of range";

        try
        {
            var range = redis.LRange(parts[1], start, stop);

            if (range == null || range.Count == 0)
                return "(empty list or set)";

            return string.Join("\n", range.Select((item, index) => $"{index + 1}) {item}"));
        }
        catch (InvalidOperationException ex)
        {
            return $"ERR {ex.Message}";
        }
    }

    #endregion

    #region Hash Handlers

    static string HandleHSet(MiniRedis redis, string[] parts)
    {
        if (parts.Length < 3)
            return "ERR wrong number of arguments for 'HSET' command";

        if ((parts.Length - 2) % 2 != 0)
            return "ERR wrong number of arguments for 'HSET' command";

        try
        {
            var key = parts[1];

            if (parts.Length == 3)
            {
                var result = redis.HSet(key, parts[1], parts[2]);
                return $"(integer) {result}";
            }

            var fields = new Dictionary<string, string>();
            for (int i = 2; i < parts.Length; i += 2)
            {
                fields[parts[i]] = parts[i + 1];
            }
            var count = redis.HSet(key, fields);
            return $"(integer) {count}";
        }
        catch (InvalidOperationException ex)
        {
            return $"ERR {ex.Message}";
        }
    }
    static string HandleHGet(MiniRedis redis, string[] parts)
    {
        if (parts.Length != 3)
            return "ERR wrong number of arguments for 'HGET' command";

        try
        {
            var value = redis.HGet(parts[1], parts[2]);
            return value ?? "(nil)";
        }
        catch (InvalidOperationException ex)
        {
            return $"ERR {ex.Message}";
        }
    }

    static string HandleHGetAll(MiniRedis redis, string[] parts)
    {
        if (parts.Length != 2)
            return "ERR wrong number of arguments for 'HGETALL' command";

        try
        {
            var hash = redis.HGetAll(parts[1]);
            if (hash == null || hash.Count == 0)
                return "(empty hash)";

            return string.Join("\n", hash.Select(kvp => $"{kvp.Key}: {kvp.Value}"));
        }
        catch (InvalidOperationException ex)
        {
            return $"ERR {ex.Message}";
        }
    }

    static string HandleHDel(MiniRedis redis, string[] parts)
    {
        if (parts.Length < 3)
            return "ERR wrong number of arguments for 'HDEL' command";

        try
        {
            var deleted = redis.HDel(parts[1], parts[2..]);
            return $"(integer) {deleted}";
        }
        catch (InvalidOperationException ex)
        {
            return $"ERR {ex.Message}";
        }
    }

    static string HandleHExists(MiniRedis redis, string[] parts)
    {
        if (parts.Length != 3)
            return "ERR wrong number of arguments for 'HEXISTS' command";

        try
        {
            var exists = redis.HExists(parts[1], parts[2]);
            return $"(integer) {(exists ? 1 : 0)}";
        }
        catch (InvalidOperationException ex)
        {
            return $"ERR {ex.Message}";
        }
    }

    static string HandleHLen(MiniRedis redis, string[] parts)
    {
        if (parts.Length != 2)
            return "ERR wrong number of arguments for 'HLEN' command";

        try
        {
            var length = redis.HLen(parts[1]);
            return $"(integer) {length}";
        }
        catch (InvalidOperationException ex)
        {
            return $"ERR {ex.Message}";
        }
    }
    #endregion

    #region Set Handlers
    static string HandleSAdd(MiniRedis redis, string[] parts)
    {
        if (parts.Length < 3)
            return "ERR wrong number of arguments for 'SADD' command";

        try
        {
            var added = redis.SAdd(parts[1], parts[2..]);
            return $"(integer) {added}";
        }
        catch (InvalidOperationException ex)
        {
            return $"ERR {ex.Message}";
        }
    }

    static string HandleSIsMember(MiniRedis redis, string[] parts)
    {
        if (parts.Length != 3)
            return "ERR wrong number of arguments for 'SISMEMBER' command";

        try
        {
            var isMember = redis.SIsMember(parts[1], parts[2]);
            return $"(integer) {(isMember ? 1 : 0)}";
        }
        catch (InvalidOperationException ex)
        {
            return $"ERR {ex.Message}";
        }
    }

    static string HandleSMembers(MiniRedis redis, string[] parts)
    {
        if (parts.Length != 2)
            return "ERR wrong number of arguments for 'SMEMBERS' command";

        try
        {
            var members = redis.SMembers(parts[1]);
            if (members == null || members.Count == 0)
                return "(empty set or key does not exist)";

            return string.Join("\n", members.Select((m, i) => $"{i + 1}) {m}"));
        }
        catch (InvalidOperationException ex)
        {
            return $"ERR {ex.Message}";
        }
    }

    static string HandleSCard(MiniRedis redis, string[] parts)
    {
        if (parts.Length != 2)
            return "ERR wrong number of arguments for 'SCARD' command";

        try
        {
            var count = redis.SCard(parts[1]);
            return $"(integer) {count}";
        }
        catch (InvalidOperationException ex)
        {
            return $"ERR {ex.Message}";
        }
    }

    static string HandleSRem(MiniRedis redis, string[] parts)
    {
        if (parts.Length < 3)
            return "ERR wrong number of arguments for 'SREM' command";

        try
        {
            var removed = redis.SRem(parts[1], parts[2..]);
            return $"(integer) {removed}";
        }
        catch (InvalidOperationException ex)
        {
            return $"ERR {ex.Message}";
        }
    }
    #endregion

    #region Sorted Set Handlers

    static string HandleZAdd(MiniRedis redis, string[] parts)
    {
        try
        {
            if (parts.Length < 4 || (parts.Length - 2) % 2 != 0)
                return "ERR syntax: ZADD key score1 member1 [score2 member2 ...]";

            var members = new Dictionary<string, double>();
            for (int i = 2; i < parts.Length; i += 2)
            {
                if (!double.TryParse(parts[i], out var score))
                    return "ERR invalid score";
                members[parts[i + 1]] = score;
            }

            var added = redis.ZAdd(parts[1], members);
            return $"(integer) {added}";
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Not a sorted set"))
        {
            return "WRONGTYPE Operation against a key holding the wrong kind of value";
        }
        catch (Exception ex)
        {
            return $"ERR {ex.Message}";
        }
    }

    static string HandleZRange(MiniRedis redis, string[] parts)
    {
        try
        {
            bool reverse = parts[0].Equals("ZREVRANGE", StringComparison.OrdinalIgnoreCase);

            if (parts.Length < 4 || !double.TryParse(parts[2], out var start) ||
                                   !double.TryParse(parts[3], out var stop))
                return $"ERR syntax: {(reverse ? "ZREVRANGE" : "ZRANGE")} key start stop [WITHSCORES]";

            bool withScores = parts.Length > 4 && parts[4].Equals("WITHSCORES", StringComparison.OrdinalIgnoreCase);
            var range = redis.ZRange(parts[1], start, stop, withScores, reverse);

            if (range.Count == 0) return "(empty list or set)";
            return string.Join("\n", range.Select((x, i) => $"{i + 1}) {x}"));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Not a sorted set"))
        {
            return "WRONGTYPE Operation against a key holding the wrong kind of value";
        }
        catch (Exception ex)
        {
            return $"ERR {ex.Message}";
        }
    }

    static string HandleZScore(MiniRedis redis, string[] parts)
    {
        try
        {
            if (parts.Length != 3)
                return "ERR syntax: ZSCORE key member";

            var score = redis.ZScore(parts[1], parts[2]);
            return score.HasValue ? score.Value.ToString() : "(nil)";
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Not a sorted set"))
        {
            return "WRONGTYPE Operation against a key holding the wrong kind of value";
        }
        catch (Exception ex)
        {
            return $"ERR {ex.Message}";
        }
    }

    static string HandleZCount(MiniRedis redis, string[] parts)
    {
        try
        {
            if (parts.Length != 4 || !double.TryParse(parts[2], out var min) ||
                                    !double.TryParse(parts[3], out var max))
                return "ERR syntax: ZCOUNT key min max";

            var count = redis.ZCount(parts[1], min, max);
            return $"(integer) {count}";
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Not a sorted set"))
        {
            return "WRONGTYPE Operation against a key holding the wrong kind of value";
        }
        catch (Exception ex)
        {
            return $"ERR {ex.Message}";
        }
    }

    static string HandleZRem(MiniRedis redis, string[] parts)
    {
        try
        {
            if (parts.Length < 3)
                return "ERR syntax: ZREM key member [member ...]";

            var removed = redis.ZRem(parts[1], parts[2..]);
            return $"(integer) {removed}";
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Not a sorted set"))
        {
            return "WRONGTYPE Operation against a key holding the wrong kind of value";
        }
        catch (Exception ex)
        {
            return $"ERR {ex.Message}";
        }
    }

    #endregion
}