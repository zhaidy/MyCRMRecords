using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace MyCRMRecords;

/// <summary>JSON settings shared by the data file and the API: camelCase names, line ids kept as they are.</summary>
public static class Json
{
    public static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false },
        },
        DateParseHandling = DateParseHandling.DateTimeOffset,
        // A list or object read from JSON replaces its default (e.g. the built-in MyAllocation forward) instead of being added to it.
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Include,
    };

    public static string Serialize(object? value, bool indented = false) =>
        JsonConvert.SerializeObject(value, indented ? Formatting.Indented : Formatting.None, Settings);

    public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings)!;

    public static T Clone<T>(T value) => Deserialize<T>(Serialize(value));
}
