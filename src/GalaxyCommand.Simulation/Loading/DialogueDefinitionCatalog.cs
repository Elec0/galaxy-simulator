using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>Completely validated dialogue definitions in canonical qualified-key order.</summary>
public sealed class DialogueDefinitionCatalog
{
    private readonly Dictionary<QualifiedContentKey, DialogueDefinition> _definitions;

    /// <summary>Copies immutable definitions and rejects null entries or duplicate identities before publication.</summary>
    public DialogueDefinitionCatalog(IEnumerable<DialogueDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _definitions = [];
        foreach (DialogueDefinition definition in definitions)
        {
            if (definition is null || !_definitions.TryAdd(definition.Key, definition))
                throw new ArgumentException("Dialogue definitions must be nonnull and have distinct identities.", nameof(definitions));
        }
        Definitions = Array.AsReadOnly(_definitions.Values.OrderBy(definition => definition.Key.ToString(), StringComparer.Ordinal).ToArray());
    }

    public ReadOnlyCollection<DialogueDefinition> Definitions { get; }

    /// <summary>Returns a resolved definition or throws when its stable identity is absent.</summary>
    public DialogueDefinition Get(QualifiedContentKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _definitions[key];
    }

    /// <summary>Looks up a resolved definition without altering catalog or session state.</summary>
    public bool TryGet(QualifiedContentKey key, [NotNullWhen(true)] out DialogueDefinition? definition)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _definitions.TryGetValue(key, out definition);
    }
}
