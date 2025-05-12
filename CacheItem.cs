using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RedSharp
{
    public enum DataType
    {
        String,
        List,
        Hash,
        Set
    }
    public class CacheItem
    {
        public object Value { get; private set; }  
        public DateTime? ExpiryTime { get; set; }
        public DataType Type { get; private set; }

        public CacheItem(string value, DateTime? expiry = null): this(value, DataType.String, expiry)
        {
        }

        public CacheItem(object value, DataType type, DateTime? expiry = null)
        {
            ValidateType(value, type);

            Value = value;
            Type = type;
            ExpiryTime = expiry;
        }

        private void ValidateType(object value, DataType type)
        {
            switch (type)
            {
                case DataType.String:
                    if (!(value is string))
                        throw new ArgumentException("String type requires string value");
                    break;
                case DataType.List:
                    if (!(value is List<string>))
                        throw new ArgumentException("List type requires List<string> value");
                    break;
                case DataType.Hash:
                    if (!(value is Dictionary<string, string>))
                        throw new ArgumentException("Hash type requires Dictionary<string, string> value");
                    break;
                case DataType.Set:
                    if (!(value is HashSet<string>))
                        throw new ArgumentException("Set type requires HashSet<string> value");
                    break;
                default:
                    throw new ArgumentException("Unknown data type");
            }
        }

        public bool IsExpired() => ExpiryTime.HasValue && DateTime.UtcNow > ExpiryTime.Value;

        public string GetString() => Type == DataType.String ? (string)Value : throw new InvalidOperationException("Not a string");
        public List<string> GetList() => Type == DataType.List ? (List<string>)Value : throw new InvalidOperationException("Not a list");
        public Dictionary<string, string> GetHash() => Type == DataType.Hash ? (Dictionary<string, string>)Value : throw new InvalidOperationException("Not a hash");
        public HashSet<string> GetSet() => Type == DataType.Set ? (HashSet<string>)Value : throw new InvalidOperationException("Not a set");
    }
}
