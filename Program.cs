
using RedSharp;

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
                    "HELP" => "Available commands: SET, GET, DEL, EXISTS, TTL, INCR, HELP, EXIT", 
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
}