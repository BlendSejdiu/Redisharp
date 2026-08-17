# RedSharp - Mini Redis-like Cache Database in C#

A lightweight, in-memory key-value store inspired by Redis, built with C# and .NET 8.0. RedSharp provides Redis-compatible commands with persistence support (RDB snapshots and AOF logging), TTL expiration, and multiple data types.

## Features

- **Multiple Data Types**: String, List, Hash, Set, SortedSet
- **Persistence**: 
  - RDB (Redis Database) - Periodic snapshots
  - AOF (Append Only File) - Command logging with configurable sync modes
- **TTL Support**: Automatic key expiration with background cleanup
- **Thread-Safe**: Uses `ConcurrentDictionary` and proper locking mechanisms
- **Async/Await**: Full async support for I/O operations
- **Redis-Compatible Commands**: Familiar command syntax for Redis users

## Requirements

- .NET 8.0 SDK or later

## Project Structure

```
RedSharp/
├── Program.cs           # CLI entry point and command handlers
├── MiniRedis.cs         # Main cache engine with all operations
├── CacheItem.cs         # Data structure for stored items
├── ExpiryManager.cs     # Handles TTL and automatic expiration
├── PersistenceManager.cs# Coordinates RDB and AOF persistence
├── RdbManager.cs        # RDB snapshot management
├── AofManager.cs        # AOF logging and replay
└── RedSharp.csproj      # Project configuration
```

## Installation

1. Clone the repository
2. Navigate to the project directory
3. Build the project:

```bash
dotnet build
```

## Usage

### Running the Application

```bash
dotnet run
```

Once started, you'll see an interactive CLI prompt where you can enter Redis-like commands.

### Supported Commands

#### String Operations
- `SET key value [EX seconds]` - Set a string value with optional TTL
- `GET key` - Get a string value
- `DEL key [key ...]` - Delete one or more keys
- `EXISTS key` - Check if a key exists
- `TTL key` - Get remaining TTL in seconds
- `INCR key` - Increment integer value
- `EXPIRE key seconds` - Set expiration time
- `PEXPIRE key milliseconds` - Set expiration time in milliseconds
- `PERSIST key` - Remove expiration from key

#### List Operations
- `LPUSH key value [value ...]` - Prepend values to list
- `RPUSH key value [value ...]` - Append values to list
- `LPOP key` - Remove and return first element
- `RPOP key` - Remove and return last element
- `LLEN key` - Get list length
- `LRANGE key start stop` - Get range of elements

#### Hash Operations
- `HSET key field value [field value ...]` - Set hash field(s)
- `HGET key field` - Get hash field value
- `HGETALL key` - Get all hash fields and values
- `HDEL key field [field ...]` - Delete hash field(s)
- `HEXISTS key field` - Check if hash field exists
- `HLEN key` - Get number of fields in hash

#### Set Operations
- `SADD key member [member ...]` - Add members to set
- `SISMEMBER key member` - Check if member exists in set
- `SMEMBERS key` - Get all set members
- `SCARD key` - Get set cardinality
- `SREM key member [member ...]` - Remove members from set

#### Sorted Set Operations
- `ZADD key score member [score member ...]` - Add members with scores
- `ZRANGE key start stop` - Get range by index (ascending)
- `ZREVRANGE key start stop` - Get range by index (descending)
- `ZSCORE key member` - Get member's score
- `ZCOUNT key min max` - Count members within score range
- `ZREM key member [member ...]` - Remove members
- `ZRANGEBYSCORE key min max` - Get range by score
- `ZINCRBY key increment member` - Increment member's score
- `ZUNIONSTORE dest key [key ...]` - Union sorted sets
- `ZINTERSTORE dest key [key ...]` - Intersect sorted sets

#### Persistence Commands
- `SAVE` - Create RDB snapshot synchronously
- `BGSAVE` - Create RDB snapshot in background

#### Other
- `HELP` - Show available commands
- `EXIT` - Exit the application

### Example Session

```
Mini Redis-like Cache DB using C# (type 'exit' to quit or 'help' to see commands)
> SET name John
OK
> GET name
John
> SET counter 0 EX 60
OK
> INCR counter
(integer) 1
> LPUSH mylist item1 item2 item3
(integer) 3
> LRANGE mylist 0 -1
1) item3
2) item2
3) item1
> HSET user name Alice age 30
(integer) 2
> HGETALL user
name: Alice
age: 30
> SADD tags redis csharp dotnet
(integer) 3
> ZADD leaderboard 100 player1 200 player2
(integer) 2
> exit
```

## Configuration

### Persistence Settings

The `MiniRedis` class accepts several configuration parameters:

```csharp
var redis = new MiniRedis(
    workingDirectory: "data",           // Directory for persistence files
    rdbInterval: TimeSpan.FromMinutes(5), // RDB snapshot interval
    aofSyncMode: AofManager.AofSyncMode.EverySecond, // AOF sync mode
    expiryCheckInterval: TimeSpan.FromSeconds(60)     // TTL cleanup interval
);
```

### AOF Sync Modes

- **EverySecond**: Flush AOF buffer every second (default, good balance)
- **Always**: Flush after every write (safest, slower)
- **Never**: Let OS decide when to flush (fastest, risk of data loss)

## Architecture

### Core Components

1. **MiniRedis**: Main cache engine providing all Redis-like operations
2. **CacheItem**: Wrapper for stored values with type information and TTL
3. **ExpiryManager**: Background task for cleaning up expired keys
4. **PersistenceManager**: Coordinates RDB and AOF persistence
5. **RdbManager**: Handles periodic snapshots in JSON format
6. **AofManager**: Logs commands for durability and recovery

### Data Types

| Type | Internal Storage | Description |
|------|------------------|-------------|
| String | `string` | Simple text values |
| List | `List<string>` | Ordered collection of strings |
| Hash | `Dictionary<string, string>` | Field-value pairs |
| Set | `HashSet<string>` | Unordered unique collection |
| SortedSet | `SortedDictionary<double, HashSet<string>>` | Scored, ordered collection |

### Thread Safety

- All operations use `ConcurrentDictionary` for the main store
- Read-write locks (`ReaderWriterLockSlim`) protect persistence operations
- Proper disposal patterns for async resources

## Persistence Files

By default, data is stored in the `data/` directory:

- `dump.rdb` - RDB snapshot file (JSON format)
- `appendonly.aof` - AOF command log

On startup, RedSharp loads data from both files (RDB first, then replays AOF).

## Error Handling

- Returns Redis-style error messages prefixed with `ERR`
- Type mismatches return `WRONGTYPE` errors
- Invalid syntax returns appropriate error messages
- Expired keys are automatically cleaned up

## Limitations

This is a simplified Redis implementation with some limitations:

- Single-threaded command processing
- No network server (CLI only)
- Limited command set compared to full Redis
- No replication or clustering
- Simplified RDB format (JSON instead of binary)

## Development

### Build

```bash
dotnet build
```

### Run Tests

(Add your test project here)

### Code Style

The project uses:
- Nullable reference types enabled
- Implicit usings enabled
- .NET 8.0 target framework

## License

This project is open source. Feel free to use and modify as needed.

## Contributing

Contributions are welcome! Please feel free to submit issues and pull requests.

## Acknowledgments

- Inspired by [Redis](https://redis.io/)
- Built with .NET 8.0
