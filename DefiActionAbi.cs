using System.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nethereum.Web3;
using Nethereum.RPC.Eth.DTOs;

namespace z3nSafe;

// Rabby str_params contains strings, booleans, arrays and nested tuples; never use its numeric params.
public sealed class RabbyParametersConverter : JsonConverter<string[]>
{
    public override string[]? ReadJson(JsonReader reader, Type type, string[]? existing, bool hasExisting, JsonSerializer serializer)
        => reader.TokenType == JsonToken.Null ? null : JArray.Load(reader).Select(v => v is JContainer ? v.ToString(Formatting.None) : v.ToString()).ToArray();
    public override void WriteJson(JsonWriter writer, string[]? value, JsonSerializer serializer) => serializer.Serialize(writer, value);
}
public static class DefiActionAbi
{
    public static string[] Split(string value)
    {
        if (value.Length == 0) return [];
        var parts = new List<string>(); int start = 0, depth = 0;
        for (int i = 0; i < value.Length; i++) {
            if (value[i] is '(' or '[') depth++;
            if (value[i] is ')' or ']') depth--;
            if (depth < 0) throw new InvalidDataException("Unbalanced action ABI");
            if (value[i] == ',' && depth == 0) { parts.Add(value[start..i].Trim()); start = i + 1; }
        }
        if (depth != 0) throw new InvalidDataException("Unbalanced action ABI");
        parts.Add(value[start..].Trim()); return parts.ToArray();
    }
    public static (string Name, string[] Types) Signature(string value)
    {
        value = value.Trim(); if (value.StartsWith("function ")) value = value[9..];
        int first = value.IndexOf('('), end = first, depth = 0;
        if (first < 1) throw new InvalidDataException("Missing action function signature");
        for (; end < value.Length; end++) { if (value[end] == '(') depth++; if (value[end] == ')' && --depth == 0) break; }
        if (end == value.Length) throw new InvalidDataException("Unbalanced action function signature");
        var name = value[..first];
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z_]\w*$")) throw new InvalidDataException("Invalid action function name");
        return (name, Split(value[(first + 1)..end]));
    }
    public static JObject Input(string type)
    {
        if (!type.StartsWith('(')) return new JObject { ["type"] = type };
        int depth = 0, end = 0;
        for (; end < type.Length; end++) { if (type[end] == '(') depth++; if (type[end] == ')' && --depth == 0) break; }
        if (end == type.Length) throw new InvalidDataException("Invalid action tuple type");
        return new JObject { ["type"] = "tuple" + type[(end + 1)..], ["components"] = new JArray(Split(type[1..end]).Select(Input)) };
    }
    public static string Abi(string name, string[] types) => new JArray(new JObject {
        ["type"] = "function", ["name"] = name, ["stateMutability"] = "nonpayable",
        ["inputs"] = new JArray(types.Select(Input)), ["outputs"] = new JArray()
    }).ToString(Formatting.None);
    public static object Value(string type, string value)
    {
        if (type.EndsWith(']')) {
            int bracket = type.LastIndexOf('['); var width = type[(bracket + 1)..^1];
            var items = JArray.Parse(value);
            if (width.Length > 0 && items.Count != int.Parse(width)) throw new InvalidDataException("Action array length mismatch");
            return items.Select(v => Value(type[..bracket], v is JContainer ? v.ToString(Formatting.None) : v.ToString())).ToArray();
        }
        if (type.StartsWith('(')) {
            var types = Split(type[1..^1]); var items = JArray.Parse(value);
            if (items.Count != types.Length) throw new InvalidDataException("Action tuple length mismatch");
            return types.Select((t, i) => Value(t, items[i] is JContainer ? items[i].ToString(Formatting.None) : items[i].ToString())).ToArray();
        }
        return DefiRabbyActions.Parameter(type, value);
    }
    public static IEnumerable<string> Addresses(string type, string value)
    {
        if (type == "address") { yield return value; yield break; }
        if (type.EndsWith(']')) {
            var element = type[..type.LastIndexOf('[')];
            foreach (var item in JArray.Parse(value))
                foreach (var address in Addresses(element, item is JContainer ? item.ToString(Formatting.None) : item.ToString())) yield return address;
        } else if (type.StartsWith('(')) {
            var types = Split(type[1..^1]); var items = JArray.Parse(value);
            for (int i = 0; i < types.Length; i++)
                foreach (var address in Addresses(types[i], items[i] is JContainer ? items[i].ToString(Formatting.None) : items[i].ToString())) yield return address;
        }
    }
    public static TransactionInput Build(string wallet, string destination, string signature, string[] parameters)
    {
        var (name, types) = Signature(signature);
        if (types.Length != parameters.Length) throw new InvalidDataException("Action parameter count mismatch");
        var values = types.Select((t, i) => Value(t, parameters[i])).ToArray();
        return new Web3().Eth.GetContract(Abi(name, types), destination).GetFunction(name).CreateTransactionInput(wallet, values);
    }
}
