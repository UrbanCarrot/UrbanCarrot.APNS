using System;
using Newtonsoft.Json;

namespace APNS
{
    public class UnixTimestampMillisecondsJsonConverter: JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset ReadJson(JsonReader reader, Type objectType, DateTimeOffset existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.Value == null)
                throw new ArgumentNullException(nameof(reader.Value), "JsonReader's value can not be null.");
            
            return DateTimeOffset.FromUnixTimeMilliseconds((long) reader.Value);
        }

        public override void WriteJson(JsonWriter writer, DateTimeOffset value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override bool CanWrite { get; } = false;
    }
}