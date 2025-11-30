using System;
using System.Windows.Input;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoE2Inspector.Services.Config;

public class ModifierKeysConverter : JsonConverter<ModifierKeys>
{
    public override ModifierKeys Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return Enum.TryParse<ModifierKeys>(value, out var result) ? result : ModifierKeys.None;
    }

    public override void Write(Utf8JsonWriter writer, ModifierKeys value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}

public class KeyConverter : JsonConverter<Key>
{
    public override Key Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return Enum.TryParse<Key>(value, out var result) ? result : Key.None;
    }

    public override void Write(Utf8JsonWriter writer, Key value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
