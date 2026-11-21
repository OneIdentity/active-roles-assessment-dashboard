using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ActiveRolesDashboard.Models.Definitions;

/// <summary>Serializer settings shared by every reader and writer of definition files.</summary>
public static class DefinitionJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        AllowOutOfOrderMetadataProperties = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { OmitDefaults } }
    };

    public static DefinitionSet Deserialize(string json) =>
        JsonSerializer.Deserialize<DefinitionSet>(json, Options)
        ?? throw new JsonException("Definition file is empty.");

    public static string Serialize(DefinitionSet set) => JsonSerializer.Serialize(set, Options);

    /// <summary>
    /// Keeps files small and readable: a property is written only when it differs from the
    /// value a new instance has (empty lists, null, <c>enabled: true</c>, <c>version: 1</c> are omitted).
    /// </summary>
    private static void OmitDefaults(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object
            || typeInfo.Type.IsAbstract
            || typeInfo.Type.GetConstructor(Type.EmptyTypes) is null
            || typeInfo.Type.Namespace != typeof(DefinitionSet).Namespace)
        {
            return;
        }

        var template = Activator.CreateInstance(typeInfo.Type)!;
        foreach (var property in typeInfo.Properties)
        {
            if (property.Get is not { } getter || property.IsRequired)
            {
                continue;
            }

            var defaultValue = getter(template);
            property.ShouldSerialize = (_, value) => value switch
            {
                null => false,
                System.Collections.ICollection { Count: 0 } => false,
                _ => !Equals(value, defaultValue)
            };
        }
    }
}
