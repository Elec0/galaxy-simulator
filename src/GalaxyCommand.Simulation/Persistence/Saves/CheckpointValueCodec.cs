using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>A bounded schema failure that never exposes untrusted document values.</summary>
internal sealed class CheckpointJsonException(string path, string message) : Exception(message)
{
    internal string Path { get; } = path.Length <= 1_024 ? path : path[..1_024];
}

/// <summary>
/// Encodes only explicitly registered immutable contracts. Reflection binds the
/// frozen member list; runtime type discovery never extends the wire schema.
/// </summary>
internal sealed class CheckpointValueCodec
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Dictionary<Type, ObjectContract> _objects = [];
    // Metadata is frozen during registration; concurrent reads never mutate reflection caches.
    private readonly NullabilityInfoContext _nullability = new();
    private readonly Dictionary<Type, PropertyInfo> _scalars = [];
    private readonly Dictionary<Type, (string Token, Type Type)[]> _unions = [];
    private readonly Dictionary<Type, (string Token, object Value)[]> _enums = [];

    private sealed record ObjectContract(PropertyInfo[] Properties, NullabilityInfo[] Nullability, string[] Names,
        Func<object?[], object> Create);

    /// <summary>Freezes one constructor and its additional authoritative init properties.</summary>
    internal void Object<T>(string[] constructorMemberNames, string[]? extraMemberNames = null,
        string[]? parameterNames = null)
    {
        string[] names = [.. constructorMemberNames, .. extraMemberNames ?? []];
        PropertyInfo[] properties = Properties(typeof(T), names);
        string[] parameters = parameterNames ?? constructorMemberNames.Select(name => name.Split(':')[0]).ToArray();
        ConstructorInfo constructor = typeof(T).GetConstructors(Members)
            .Single(candidate => candidate.GetParameters().Select(parameter => parameter.Name!)
                .SequenceEqual(parameters, StringComparer.OrdinalIgnoreCase));
        _objects.Add(typeof(T), new ObjectContract(properties, properties.Select(_nullability.Create).ToArray(), names.Select(WireName).ToArray(), values =>
        {
            object instance = constructor.Invoke(values.Take(constructorMemberNames.Length).ToArray());
            for (int index = constructorMemberNames.Length; index < values.Length; index++)
                properties[index].SetValue(instance, values[index]);
            return instance;
        }));
    }

    /// <summary>Registers a validated factory when the runtime constructor does not mirror stored fields.</summary>
    internal void Factory<T>(string[] memberNames, Func<object?[], T> create) where T : notnull
    {
        PropertyInfo[] properties = Properties(typeof(T), memberNames);
        _objects.Add(typeof(T), new ObjectContract(properties, properties.Select(_nullability.Create).ToArray(),
            memberNames.Select(WireName).ToArray(), values => create(values)));
    }

    /// <summary>Represents one domain scalar directly, without a CLR wrapper object.</summary>
    internal void Scalar<T>(string memberName) => _scalars.Add(typeof(T), Properties(typeof(T), [memberName])[0]);

    /// <summary>Registers stable discriminants explicitly rather than deriving them from CLR names.</summary>
    internal void Union<T>((string Token, Type Type)[] cases) => _unions.Add(typeof(T), cases);

    /// <summary>Registers the exact accepted enumeration vocabulary, excluding numeric alternatives.</summary>
    internal void Enum<T>((string Token, T Value)[] values) where T : struct, Enum =>
        _enums.Add(typeof(T), values.Select(value => (value.Token, (object)value.Value)).ToArray());

    /// <summary>Encodes an immutable value in fixed contract order.</summary>
    internal JsonElement Encode<T>(T value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { MaxDepth = 256 }))
            Write(writer, value, typeof(T), "$", 0);
        using JsonDocument document = JsonDocument.Parse(stream.ToArray(), new JsonDocumentOptions { MaxDepth = 256 });
        return document.RootElement.Clone();
    }

    /// <summary>Decodes inert values without constructing runtime owners or accepting omitted fields.</summary>
    internal T Decode<T>(JsonElement value) => (T)(Read(value, typeof(T), "$", 0)
        ?? throw Invalid("$", "A non-null checkpoint value is required."));

    /// <summary>Writes primitives, ordered collections, or an explicitly frozen object contract.</summary>
    private void Write(Utf8JsonWriter writer, object? value, Type type, string path, int depth, NullabilityInfo? nullability = null)
    {
        GuardDepth(depth, path);
        if (value is null)
        {
            if (nullability?.ReadState == NullabilityState.NotNull) throw Invalid(path, "A non-null value is required.");
            writer.WriteNullValue(); return;
        }
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string)) { writer.WriteStringValue((string)value); return; }
        if (type == typeof(bool)) { writer.WriteBooleanValue((bool)value); return; }
        if (type == typeof(ulong) || type == typeof(long) || type == typeof(BigInteger) || type == typeof(UInt128) || type == typeof(Int128))
        { writer.WriteStringValue(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture)); return; }
        if (type == typeof(int)) { writer.WriteNumberValue((int)value); return; }
        if (type == typeof(uint)) { writer.WriteNumberValue((uint)value); return; }
        if (type == typeof(ushort)) { writer.WriteNumberValue((ushort)value); return; }
        if (type == typeof(byte)) { writer.WriteNumberValue((byte)value); return; }
        if (type == typeof(short)) { writer.WriteNumberValue((short)value); return; }
        if (type == typeof(sbyte)) { writer.WriteNumberValue((sbyte)value); return; }
        if (type == typeof(ExecutableBoundedTerminalManeuverPlan))
        {
            Write(writer, ((ExecutableBoundedTerminalManeuverPlan)value).CaptureCheckpoint(), typeof(ExecutableManeuverCheckpoint), path, depth + 1);
            return;
        }
        if (ContentScalar(type)) { writer.WriteStringValue(value.ToString()); return; }
        if (_scalars.TryGetValue(type, out PropertyInfo? scalar))
        {
            object scalarValue = scalar.GetValue(value)!;
            writer.WriteStringValue(scalarValue is IFormattable number
                ? number.ToString(null, CultureInfo.InvariantCulture) : scalarValue.ToString());
            return;
        }
        if (_enums.TryGetValue(type, out var vocabulary))
        {
            var match = vocabulary.SingleOrDefault(entry => entry.Value.Equals(value));
            if (match.Token is null) throw Invalid(path, "Undefined enumeration value.");
            writer.WriteStringValue(match.Token); return;
        }
        if (DictionaryTypes(type) is { } dictionaryTypes)
        {
            var pairs = ((IEnumerable)value).Cast<object>().Select(pair =>
                (Key: pair.GetType().GetProperty("Key")!.GetValue(pair)!,
                 Value: pair.GetType().GetProperty("Value")!.GetValue(pair))).ToArray();
            Array.Sort(pairs, (first, second) => CompareKeys(first.Key, second.Key));
            writer.WriteStartArray();
            foreach (var pair in pairs)
            {
                writer.WriteStartObject(); writer.WritePropertyName("key");
                Write(writer, pair.Key, dictionaryTypes.Key, path, depth + 1, GenericArgument(nullability, 0));
                writer.WritePropertyName("value"); Write(writer, pair.Value, dictionaryTypes.Value, path, depth + 1, GenericArgument(nullability, 1));
                writer.WriteEndObject();
            }
            writer.WriteEndArray(); return;
        }
        if (ElementType(type) is { } element)
        {
            writer.WriteStartArray();
            int index = 0;
            foreach (object? item in (IEnumerable)value)
                Write(writer, item, element, $"{path}[{index++}]", depth + 1, ElementNullability(nullability));
            writer.WriteEndArray(); return;
        }
        string? token = null;
        if (_unions.TryGetValue(type, out var cases))
        {
            var match = cases.SingleOrDefault(entry => entry.Type == value.GetType());
            if (match.Token is null) throw Invalid(path, "Unsupported union variant.");
            token = match.Token; type = match.Type;
        }
        if (!_objects.TryGetValue(type, out ObjectContract? contract))
            throw Invalid(path, "Unregistered checkpoint value.");
        writer.WriteStartObject();
        if (token is not null) writer.WriteString("kind", token);
        for (int index = 0; index < contract.Properties.Length; index++)
        {
            writer.WritePropertyName(contract.Names[index]);
            PropertyInfo property = contract.Properties[index];
            Write(writer, property.GetValue(value), property.PropertyType,
                path + "." + contract.Names[index], depth + 1, contract.Nullability[index]);
        }
        writer.WriteEndObject();
    }

    /// <summary>Validates every member before invoking the registered immutable constructor.</summary>
    private object? Read(JsonElement value, Type type, string path, int depth, NullabilityInfo? nullability = null)
    {
        GuardDepth(depth, path);
        Type? nullable = Nullable.GetUnderlyingType(type);
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (nullability?.ReadState == NullabilityState.NotNull) throw Invalid(path, "A non-null value is required.");
            if (nullable is not null || !type.IsValueType) return null;
            throw Invalid(path, "A non-null value is required.");
        }
        type = nullable ?? type;
        try
        {
            if (type == typeof(string)) return value.GetString();
            if (type == typeof(bool)) return value.GetBoolean();
            if (type == typeof(int)) return value.GetInt32();
            if (type == typeof(uint)) return value.GetUInt32();
            if (type == typeof(ushort)) return value.GetUInt16();
            if (type == typeof(byte)) return value.GetByte();
            if (type == typeof(short)) return value.GetInt16();
            if (type == typeof(sbyte)) return value.GetSByte();
            if (type == typeof(ExecutableBoundedTerminalManeuverPlan))
                return ExecutableBoundedTerminalManeuverPlan.RestoreCheckpoint(
                    (ExecutableManeuverCheckpoint)Read(value, typeof(ExecutableManeuverCheckpoint), path, depth + 1)!);
            if (type == typeof(ulong)) return ulong.Parse(IntegerText(value, path), NumberStyles.None, CultureInfo.InvariantCulture);
            if (type == typeof(long)) return long.Parse(IntegerText(value, path), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            if (type == typeof(UInt128)) return UInt128.Parse(IntegerText(value, path), NumberStyles.None, CultureInfo.InvariantCulture);
            if (type == typeof(Int128)) return Int128.Parse(IntegerText(value, path), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            if (type == typeof(BigInteger)) return BigInteger.Parse(IntegerText(value, path), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            if (type == typeof(QualifiedContentKey)) return QualifiedContentKey.Parse(value.GetString()!);
            if (type == typeof(PackageId)) return PackageId.Create(value.GetString()!);
            if (type == typeof(ContentKind)) return ContentKind.Create(value.GetString()!);
            if (type == typeof(LocalContentId)) return LocalContentId.Create(value.GetString()!);
            if (_scalars.TryGetValue(type, out PropertyInfo? scalar))
            {
                object? scalarValue;
                if (scalar.PropertyType == typeof(string)) scalarValue = value.GetString();
                else
                {
                    string text = IntegerText(value, path);
                    scalarValue = scalar.PropertyType == typeof(int)
                        ? int.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
                        : scalar.PropertyType == typeof(uint)
                            ? uint.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture)
                            : Read(value, scalar.PropertyType, path, depth + 1);
                }
                return _objects.TryGetValue(type, out ObjectContract? scalarContract)
                    ? scalarContract.Create([scalarValue])
                    : Activator.CreateInstance(type, Members, null, [scalarValue], CultureInfo.InvariantCulture);
            }
            if (_enums.TryGetValue(type, out var vocabulary))
            {
                string? token = value.GetString();
                var match = vocabulary.SingleOrDefault(entry => entry.Token == token);
                return match.Token is null ? throw Invalid(path, "Unknown enumeration token.") : match.Value;
            }
            if (DictionaryTypes(type) is { } dictionaryTypes)
            {
                Type concrete = typeof(Dictionary<,>).MakeGenericType(dictionaryTypes.Key, dictionaryTypes.Value);
                var result = (IDictionary)Activator.CreateInstance(concrete)!;
                int index = 0;
                foreach (JsonElement item in value.EnumerateArray())
                {
                    string itemPath = $"{path}[{index++}]";
                    RequireMembers(item, ["key", "value"], itemPath);
                    object key = Read(item.GetProperty("key"), dictionaryTypes.Key, itemPath + ".key", depth + 1, GenericArgument(nullability, 0))!;
                    if (result.Contains(key)) throw Invalid(itemPath + ".key", "Duplicate dictionary key.");
                    result.Add(key, Read(item.GetProperty("value"), dictionaryTypes.Value, itemPath + ".value", depth + 1, GenericArgument(nullability, 1)));
                }
                return result;
            }
            if (ElementType(type) is { } element)
            {
                Array result = Array.CreateInstance(element, value.GetArrayLength());
                int index = 0;
                foreach (JsonElement item in value.EnumerateArray())
                {
                    result.SetValue(Read(item, element, $"{path}[{index}]", depth + 1, ElementNullability(nullability)), index);
                    index++;
                }
                if (type.IsArray || type.IsAssignableFrom(result.GetType())) return result;
                return Activator.CreateInstance(type, [result]);
            }
            bool union = _unions.TryGetValue(type, out var cases);
            if (union)
            {
                if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("kind", out JsonElement kind))
                    throw Invalid(path + ".kind", "A union discriminant is required.");
                string? token = kind.GetString();
                var match = cases!.SingleOrDefault(entry => entry.Token == token);
                if (match.Token is null) throw Invalid(path + ".kind", "Unknown union discriminant.");
                type = match.Type;
            }
            if (!_objects.TryGetValue(type, out ObjectContract? contract))
                throw Invalid(path, "Unregistered checkpoint value.");
            RequireMembers(value, union ? ["kind", .. contract.Names] : contract.Names, path);
            var values = new object?[contract.Properties.Length];
            for (int index = 0; index < values.Length; index++)
            {
                PropertyInfo property = contract.Properties[index];
                string memberPath = path + "." + contract.Names[index];
                JsonElement member = value.GetProperty(contract.Names[index]);
                if (member.ValueKind == JsonValueKind.Null && contract.Nullability[index].ReadState == NullabilityState.NotNull)
                    throw Invalid(memberPath, "A non-null member is required.");
                values[index] = Read(member, property.PropertyType, memberPath, depth + 1, contract.Nullability[index]);
            }
            return contract.Create(values);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException
            or OverflowException or TargetInvocationException or MissingMethodException)
        {
            throw Invalid(path, "The value violates its checkpoint schema or domain contract.");
        }
    }

    /// <summary>Rejects duplicate, missing, and unknown members before defaults can be applied.</summary>
    internal static void RequireMembers(JsonElement value, string[] names, string path)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid(path, "An object is required.");
        var remaining = names.ToHashSet(StringComparer.Ordinal);
        foreach (JsonProperty member in value.EnumerateObject())
            if (!remaining.Remove(member.Name)) throw Invalid(member.Name.Length <= 64 && member.Name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')
                    ? path + "." + member.Name : path, "Unknown or duplicate member.");
        foreach (string missing in names.Where(remaining.Contains))
            throw Invalid(path + "." + missing, "A required member is missing.");
    }

    /// <summary>Uses a canonical decimal grammar without whitespace, exponent, or leading zeros.</summary>
    private static string IntegerText(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.String) throw Invalid(path, "An exact integer string is required.");
        string text = value.GetString()!;
        ReadOnlySpan<char> digits = text.AsSpan();
        if (digits.Length > 0 && digits[0] == '-') digits = digits[1..];
        if (digits.Length == 0 || digits.Length > 1 && digits[0] == '0'
            || digits.ContainsAnyExceptInRange('0', '9') || text == "-0")
            throw Invalid(path, "A canonical decimal integer is required.");
        return text;
    }

    /// <summary>Sorts unordered maps by exact scalar domain value, never dictionary insertion order.</summary>
    private int CompareKeys(object first, object second)
    {
        Type type = first.GetType();
        if (_scalars.TryGetValue(type, out PropertyInfo? scalar))
        {
            object a = scalar.GetValue(first)!;
            object b = scalar.GetValue(second)!;
            return a is string text ? StringComparer.Ordinal.Compare(text, (string)b) : ((IComparable)a).CompareTo(b);
        }
        if (ContentScalar(type) || type == typeof(string)) return StringComparer.Ordinal.Compare(first.ToString(), second.ToString());
        if (first is IComparable comparable) return comparable.CompareTo(second);
        throw Invalid("$", "Dictionary keys require a stable domain ordering.");
    }

    private static NullabilityInfo? GenericArgument(NullabilityInfo? info, int index) =>
        info is not null && index < info.GenericTypeArguments.Length ? info.GenericTypeArguments[index] : null;

    private static NullabilityInfo? ElementNullability(NullabilityInfo? info) =>
        info?.ElementType ?? GenericArgument(info, 0);

    private static bool ContentScalar(Type type) => type == typeof(QualifiedContentKey)
        || type == typeof(PackageId) || type == typeof(ContentKind) || type == typeof(LocalContentId);

    /// <summary>Recognizes only generic dictionary shapes with explicit typed keys and values.</summary>
    private static (Type Key, Type Value)? DictionaryTypes(Type type)
    {
        Type? dictionary = type.GetInterfaces().Append(type).FirstOrDefault(candidate => candidate.IsGenericType
            && (candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
                || candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>)));
        return dictionary is null ? null : (dictionary.GetGenericArguments()[0], dictionary.GetGenericArguments()[1]);
    }

    /// <summary>Preserves sequence order and accepts only typed enumerable collections.</summary>
    private static Type? ElementType(Type type) => type.IsArray ? type.GetElementType()
        : type.GetInterfaces().Append(type).FirstOrDefault(candidate => candidate.IsGenericType
            && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];

    private static PropertyInfo[] Properties(Type type, string[] names) =>
        names.Select(name => type.GetProperty(name.Split(':')[0], Members) ?? throw new InvalidOperationException($"Missing registered member {type.Name}.{name}.")).ToArray();

    private static string WireName(string name) => name.Contains(':', StringComparison.Ordinal)
        ? name.Split(':')[1] : char.ToLowerInvariant(name[0]) + name[1..];

    /// <summary>Bounds recursive work independently of serializer implementation defaults.</summary>
    private static void GuardDepth(int depth, string path)
    {
        if (depth > 128) throw Invalid(path, "Checkpoint nesting exceeds the codec limit.");
    }

    private static CheckpointJsonException Invalid(string path, string message) => new(path, message);
}
