using System.Collections.ObjectModel;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>Either a fully admitted dialogue catalog or deterministic domain diagnostics.</summary>
public sealed class DialogueContentLoadResult
{
    internal DialogueContentLoadResult(DialogueDefinitionCatalog? catalog, IEnumerable<ContentDiagnostic> diagnostics)
    {
        Catalog = catalog;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }
    public bool IsSuccess => Catalog is not null;
    public DialogueDefinitionCatalog? Catalog { get; }
    public ReadOnlyCollection<ContentDiagnostic> Diagnostics { get; }
}

/// <summary>Interprets format-neutral authored dialogue through the same admission path for CLI and new games.</summary>
public static class DialogueContentLoader
{
    /// <summary>Validates every selected dialogue definition, including unreferenced definitions, before publishing any catalog.</summary>
    public static DialogueContentLoadResult Load(ResolvedContentCatalog content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var definitions = new List<DialogueDefinition>();
        var diagnostics = new List<ContentDiagnostic>();
        foreach (ContentDefinitionSource definition in content.Definitions.Values.Where(value => value.Key.ContentKind.Value == "dialogue"))
        {
            try { definitions.Add(DialogueContentReader.Read(definition)); }
            catch (DialogueContentReadException failure)
            {
                diagnostics.Add(new(failure.Kind, definition.Key.ToString(), failure.Path, failure.Message));
            }
            catch (Exception failure) when (failure is ArgumentException or OverflowException)
            {
                // Graph validators reject before publication. Exception wording and runtime type names are not authored diagnostics.
                diagnostics.Add(new(ContentDiagnosticKind.InvalidValue, definition.Key.ToString(), "$definition.values",
                    "The dialogue graph, participant references, memory, or presentation contract is invalid."));
            }
        }
        return diagnostics.Count == 0 ? new(new DialogueDefinitionCatalog(definitions), []) : new(null, diagnostics);
    }
}

internal sealed class DialogueContentReadException(ContentDiagnosticKind kind, string path, string message) : Exception(message)
{
    internal ContentDiagnosticKind Kind { get; } = kind;
    internal string Path { get; } = path;
}

/// <summary>Strict adapter over neutral content values; no physical JSON nodes or callbacks enter runtime definitions.</summary>
internal static class DialogueContentReader
{
    /// <summary>Materializes one complete immutable graph from the explicitly versioned package document vocabulary.</summary>
    internal static DialogueDefinition Read(ContentDefinitionSource source)
    {
        var value = new Reader(source.Values, "$definition.values",
            ["roles", "nodes", "initialNodeId", "repeatability", "startConditions", "memories"]);
        DialogueParticipantRole[] roles = value.Array("roles", item =>
        {
            Reader role = item.Object(["id", "acceptedKinds"]);
            return new DialogueParticipantRole(role.Text("id"), role.Array("acceptedKinds", kind => kind.Token(
                ("ship", DialogueParticipantKind.Ship), ("station", DialogueParticipantKind.Station), ("principal", DialogueParticipantKind.Principal))));
        });
        DialogueMemoryDeclaration[] memories = value.Array("memories", item =>
        {
            Reader memory = item.Object(["id", "scope"]);
            return new DialogueMemoryDeclaration(memory.Text("id"), Scope(memory.Field("scope")));
        });
        return new DialogueDefinition(source.Key, roles, value.Array("nodes", ReadNode), value.Text("initialNodeId"),
            Repeatability(value.Field("repeatability")), value.Array("startConditions", ReadCondition), memories);
    }

    /// <summary>Admits explicitly ordered segments and responses with no inferred acknowledgement or timing behavior.</summary>
    private static DialogueNode ReadNode(Value item)
    {
        Reader node = item.Object(["id", "segments", "choices"], ["automaticTransition"]);
        DialogueSegment[] segments = node.Array("segments", line =>
        {
            Reader segment = line.Object(["id", "order", "text"], ["speaker"]);
            return new DialogueSegment(segment.Text("id"), segment.Field("order").Int32(), ReadText(segment.Field("text")),
                segment.Optional("speaker") is { } speaker ? ReadText(speaker) : null);
        });
        return new DialogueNode(node.Text("id"), segments, node.Array("choices", ReadChoice),
            node.Optional("automaticTransition") is { } automatic ? ReadTransition(automatic) : null);
    }

    /// <summary>Reads at most one approved ordinary command consequence and explicit memory references.</summary>
    private static DialogueChoice ReadChoice(Value item)
    {
        Reader choice = item.Object(["id", "order", "label", "transition", "repeatability", "offerConditions", "admissionConditions", "memoryIds"], ["move"]);
        DialogueMoveConsequence? move = null;
        if (choice.Optional("move") is { } authoredMove)
        {
            Reader command = authoredMove.Object(["kind", "actorRoleId", "destinationRoleId", "placement"]);
            _ = command.Field("kind").Token(("move-ship", true));
            move = new(command.Text("actorRoleId"), command.Text("destinationRoleId"), command.Field("placement").Token(
                ("replace-all", OrderPlacement.ReplaceAll), ("append", OrderPlacement.Append)));
        }
        return new DialogueChoice(choice.Text("id"), choice.Field("order").Int32(), ReadText(choice.Field("label")),
            ReadTransition(choice.Field("transition")), Repeatability(choice.Field("repeatability")),
            choice.Array("offerConditions", ReadCondition), choice.Array("admissionConditions", ReadCondition),
            choice.Array("memoryIds", memory => memory.Text()), move);
    }

    /// <summary>Allows only authored node edges or explicit terminal identities.</summary>
    private static DialogueTransition ReadTransition(Value value)
    {
        Reader transition = value.Object(["kind"], ["nodeId", "outcomeId"]);
        string kind = transition.Text("kind");
        if (kind == "node")
        {
            transition = value.Object(["kind", "nodeId"]);
            return new DialogueTransition.Node(transition.Text("nodeId"));
        }
        if (kind == "complete")
        {
            transition = value.Object(["kind", "outcomeId"]);
            return new DialogueTransition.Complete(transition.Text("outcomeId"));
        }
        throw value.Invalid("The dialogue transition kind is unsupported.");
    }

    /// <summary>Interprets the closed condition vocabulary without executable content or inferred knowledge policy.</summary>
    private static DialogueCondition ReadCondition(Value value)
    {
        Reader condition = value.Object(["kind"], ["memoryId", "scope", "exists", "assessingRoleId", "subjectRoleId", "minimumValue", "roleId"]);
        switch (condition.Text("kind"))
        {
            case "receipt":
                condition = value.Object(["kind", "memoryId", "scope", "exists"]);
                return new DialogueCondition.Receipt(condition.Text("memoryId"), Scope(condition.Field("scope")), condition.Field("exists").Boolean());
            case "standing":
                condition = value.Object(["kind", "assessingRoleId", "subjectRoleId", "minimumValue"]);
                return new DialogueCondition.Standing(condition.Text("assessingRoleId"), condition.Text("subjectRoleId"), condition.Field("minimumValue").Int64());
            case "detected":
                condition = value.Object(["kind", "roleId"]);
                return new DialogueCondition.Detected(condition.Text("roleId"));
            case "discovered":
                condition = value.Object(["kind", "roleId"]);
                return new DialogueCondition.Discovered(condition.Text("roleId"));
            default: throw value.Invalid("The dialogue condition kind is unsupported.");
        }
    }

    /// <summary>Preserves exact typed resource arguments and explicit markup while validating their fallback contract.</summary>
    private static DialogueText ReadText(Value value)
    {
        Reader text = value.Object(["resourceKey", "invariantFallback", "markup", "arguments"], ["pluralSelector"]);
        DialogueTextArgument[] arguments = text.Array("arguments", item =>
        {
            Reader argument = item.Object(["kind", "name", "value"]);
            string name = argument.Text("name");
            Value argumentValue = argument.Field("value");
            return argument.Text("kind") switch
            {
                "text" => (DialogueTextArgument)new DialogueTextArgument.Text(name, argumentValue.Text()),
                "number" => new DialogueTextArgument.Number(name, argumentValue.Number()),
                "count" => new DialogueTextArgument.Count(name, argumentValue.UInt64()),
                "duration" => new DialogueTextArgument.Duration(name, new SimulationDuration(argumentValue.UInt64())),
                _ => throw item.Invalid("The dialogue presentation argument kind is unsupported."),
            };
        });
        return new DialogueText(text.Text("resourceKey"), text.Text("invariantFallback"), text.Field("markup").Token(
            ("plain-text", DialogueMarkup.PlainText), ("restricted-rich-text", DialogueMarkup.RestrictedRichText)), arguments,
            text.Optional("pluralSelector")?.Number());
    }

    private static DialogueRepeatability Repeatability(Value value) => value.Token(
        ("repeatable", DialogueRepeatability.Repeatable), ("once-per-session", DialogueRepeatability.OncePerSession),
        ("once-per-participant-binding", DialogueRepeatability.OncePerParticipantBinding));

    private static DialogueMemoryScope Scope(Value value) => value.Token(
        ("session", DialogueMemoryScope.Session), ("participant-binding", DialogueMemoryScope.ParticipantBinding));

    /// <summary>Path-bearing neutral scalar access; narrowing never rounds, truncates, or parses numeric strings.</summary>
    private sealed record Value(ContentValue Content, string Path)
    {
        internal Reader Object(string[] required, string[]? optional = null) => new(Content, Path, required, optional);
        internal string Text() => Content is ContentStringValue text ? text.Value : throw Invalid("A string value is required.");
        internal decimal Number() => Content is ContentNumberValue number ? number.Value : throw Invalid("A numeric value is required.");
        internal bool Boolean() => Content is ContentBooleanValue boolean ? boolean.Value : throw Invalid("A boolean value is required.");

        /// <summary>Requires an exactly represented nonnegative unsigned integer.</summary>
        internal ulong UInt64()
        {
            decimal number = Number();
            return number >= 0 && number <= ulong.MaxValue && decimal.Truncate(number) == number
                ? (ulong)number : throw Invalid("An unsigned integral value within the supported bound is required.");
        }

        /// <summary>Requires exact signed integral thresholds.</summary>
        internal long Int64()
        {
            decimal number = Number();
            return number >= long.MinValue && number <= long.MaxValue && decimal.Truncate(number) == number
                ? (long)number : throw Invalid("A signed integral value within the supported bound is required.");
        }

        /// <summary>Rejects fractional or oversized authored display orders before narrowing.</summary>
        internal int Int32()
        {
            ulong number = UInt64();
            return number <= int.MaxValue ? (int)number : throw Invalid("Display order exceeds its supported bound.");
        }

        /// <summary>Resolves explicit ordinal tokens; numeric enum representations and aliases are rejected.</summary>
        internal T Token<T>(params (string Name, T Result)[] tokens)
        {
            string name = Text();
            foreach ((string token, T result) in tokens)
                if (name == token) return result;
            throw Invalid("The authored dialogue token is unsupported.");
        }
        internal DialogueContentReadException Invalid(string message) => new(ContentDiagnosticKind.InvalidValue, Path, message);
    }

    /// <summary>Validates an object's entire field set before reading any child values.</summary>
    private sealed class Reader
    {
        private readonly ContentObjectValue _content;
        private readonly string _path;

        /// <summary>Rejects unknown and missing properties with stable neutral-model paths.</summary>
        internal Reader(ContentValue content, string path, string[] required, string[]? optional = null)
        {
            if (content is not ContentObjectValue value) throw new DialogueContentReadException(ContentDiagnosticKind.InvalidValue, path, "An object value is required.");
            _content = value; _path = path;
            foreach (string name in value.Properties.Keys)
                if (!required.Contains(name, StringComparer.Ordinal) && !(optional ?? []).Contains(name, StringComparer.Ordinal))
                    throw new DialogueContentReadException(ContentDiagnosticKind.UnknownProperty, path + "." + name, "The dialogue property is unsupported.");
            foreach (string name in required)
                if (!value.Properties.ContainsKey(name))
                    throw new DialogueContentReadException(ContentDiagnosticKind.MissingProperty, path + "." + name, "A required dialogue property is missing.");
        }
        internal Value Field(string name) => new(_content.Properties[name], _path + "." + name);
        internal Value? Optional(string name) => _content.Properties.TryGetValue(name, out ContentValue? value) ? new(value, _path + "." + name) : null;
        internal string Text(string name) => Field(name).Text();

        /// <summary>Reads a bounded, already copied neutral array in authored order with indexed diagnostics.</summary>
        internal T[] Array<T>(string name, Func<Value, T> read)
        {
            Value field = Field(name);
            if (field.Content is not ContentArrayValue array) throw field.Invalid("An array value is required.");
            return array.Items.Select((item, index) => read(new(item, $"{field.Path}[{index}]"))).ToArray();
        }
    }
}
