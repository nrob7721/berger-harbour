using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BergerHarbour.Infrastructure.Web;

/// <summary>Times of day in the API are "HH:mm" (seconds are accepted on input and ignored).</summary>
public sealed class HourMinuteTimeConverter : JsonConverter<TimeOnly>
{
    private static readonly string[] Formats = ["HH:mm", "HH:mm:ss", "H:mm"];

    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (TimeOnly.TryParseExact(text, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return new TimeOnly(time.Hour, time.Minute);
        }

        throw new JsonException($"'{text}' is not a time (HH:mm).");
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("HH:mm", CultureInfo.InvariantCulture));
}
