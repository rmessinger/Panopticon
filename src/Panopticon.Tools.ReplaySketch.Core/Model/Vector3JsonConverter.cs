using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>
/// Round-trips <see cref="Vector3"/> as <c>{"x","y","z"}</c>. The validation-ground layout is planar
/// (Z is always 0 and terrain-sampled at spawn), but <see cref="LayoutConfig.TargetClusterCenter"/>
/// is typed <see cref="Vector3"/> for builder math, so JSON needs this converter.
/// </summary>
public sealed class Vector3JsonConverter : JsonConverter<Vector3>
{
    public override Vector3 Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
    {
        float x = 0f, y = 0f, z = 0f;
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            return Vector3.Zero;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var name = reader.GetString();
            reader.Read();
            switch (name?.ToLowerInvariant())
            {
                case "x": x = reader.GetSingle(); break;
                case "y": y = reader.GetSingle(); break;
                case "z": z = reader.GetSingle(); break;
            }
        }

        return new Vector3(x, y, z);
    }

    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteNumber("z", value.Z);
        writer.WriteEndObject();
    }
}
