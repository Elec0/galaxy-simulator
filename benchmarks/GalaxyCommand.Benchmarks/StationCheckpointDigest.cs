using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Benchmarks;

/// <summary>Benchmark-only canonical hashing of typed checkpoint data; this is not a save encoder or restore boundary.</summary>
internal static class StationCheckpointDigest
{
    private static readonly ConcurrentDictionary<Type, MemberInfo[]> DataMembers = new();
    internal static string Compute(GameSessionCheckpoint checkpoint) => ComputeNode(checkpoint);

    /// <summary>Hashes semantic data recursively with explicit scalars, type tags and canonical map keys, never reference identities.</summary>
    internal static string ComputeNode(object? value) => ComputeNode(value, false);

    /// <summary>Compares gameplay authority across wake-envelope formats; the full digest remains the exact execution proof.</summary>
    internal static string ComputeGameplay(GameSessionCheckpoint checkpoint) => ComputeNode(checkpoint, true);

    private static string ComputeNode(object? value, bool gameplay)
    {
        var digest = new DeterministicDigest();
        Visit(value, token => { digest.Add((ulong)token.Length); digest.Add(token); }, new(ReferenceEqualityComparer.Instance), gameplay);
        return digest.ToString();
    }

    /// <summary>Rejects cycles, delegates and live owners rather than omitting data or traversing executable authority.</summary>
    private static void Visit(object? value, Action<string> write, HashSet<object> path, bool gameplay)
    {
        if (value is null) { write("null"); return; }
        Type type = value.GetType();
        if (Scalar(value, type, write)) return;
        if (value is Delegate or Type or MemberInfo || type.Name.EndsWith("Owner", StringComparison.Ordinal)
            || value is GameSession)
            throw new InvalidOperationException($"Checkpoint digest cannot visit executable authority {type.Name}.");
        bool reference = !type.IsValueType;
        if (reference && !path.Add(value)) throw new InvalidOperationException("Checkpoint data contains a reference cycle.");
        try
        {
            if (Map(value, type, write, path, gameplay)) return;
            if (value is IEnumerable sequence)
            {
                write("sequence");
                foreach (object? item in sequence) { write("item"); Visit(item, write, path, gameplay); }
                write("end-sequence");
                return;
            }
            if (type.Assembly != typeof(GameSessionCheckpoint).Assembly && type.Namespace != "GalaxyCommand.Content"
                && type.Namespace != "System") throw new InvalidOperationException($"Unexpected checkpoint data type {type.FullName}.");
            write(type.FullName ?? type.Name);
            MemberInfo[] members = DataMembers.GetOrAdd(type, static candidate => candidate
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(property => property.Name != "EqualityContract" && property.GetIndexParameters().Length == 0
                    && property.GetMethod is { } getter && (getter.IsPublic || getter.IsAssembly)).Cast<MemberInfo>()
                .Concat(candidate.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(field => (field.IsPublic || field.IsAssembly) && !field.IsDefined(typeof(CompilerGeneratedAttribute), false)))
                .OrderBy(member => member.Name, StringComparer.Ordinal).ToArray());
            if (members.Length == 0) throw new InvalidOperationException($"Checkpoint data has no inspectable members: {type.Name}.");
            foreach (MemberInfo member in members)
            {
                if (value is InteractionCandidateKey { StationarySource: not null } && member.Name == "Pair") continue;
                object? data = member is PropertyInfo property ? property.GetValue(value) : ((FieldInfo)member).GetValue(value);
                // Scheduling identities differ when equivalent work uses fewer wakes. Keep
                // progress, revisions, claim ordering and causal timestamp/phase authority.
                if (gameplay && ((value is GameSessionCheckpoint && member.Name == "Engine")
                    || (value is StationProjectCheckpoint && member.Name is "Pending" or "Generation")
                    || (value is EventKey && member.Name == "CreationSequence")
                    || data is null || data is ICollection { Count: 0 })) continue;
                write(member.Name);
                try { Visit(data, write, path, gameplay); }
                catch (InvalidOperationException error) { throw new InvalidOperationException($"{type.Name}.{member.Name}: {error.Message}", error); }
            }
            if (value is InteractionPhaseCurve curve)
            {
                write("exact-phase-pieces");
                Visit(curve.Pieces(curve.StartsAt), write, path, gameplay);
            }
            if (value is ExecutableBoundedTerminalManeuverPlan plan)
            {
                // The checkpoint's private pure evaluator is an execution adapter. Hash its
                // immutable exact geometry and the kinematic projection at all phase boundaries.
                write("kinematic-projection");
                foreach (ulong timestamp in plan.Phases.SelectMany(phase => new[] { phase.StartsAt.Milliseconds,
                    phase.StartsAt.Milliseconds + (phase.EndsAt.Milliseconds - phase.StartsAt.Milliseconds) / 2,
                    phase.EndsAt.Milliseconds }).Distinct().Order())
                {
                    write(timestamp.ToString(CultureInfo.InvariantCulture));
                    Visit(plan.StateAt(new(timestamp)), write, path, gameplay);
                }
            }
            write("end-object");
        }
        finally { if (reference) path.Remove(value); }
    }

    /// <summary>Formats scalar authority without culture, floating-point shortening or framework object identity.</summary>
    private static bool Scalar(object value, Type type, Action<string> write)
    {
        string? text = value switch
        {
            string item => item,
            bool item => item ? "true" : "false",
            char item => ((ushort)item).ToString(CultureInfo.InvariantCulture),
            byte or sbyte or short or ushort or int or uint or long or ulong => ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture),
            BigInteger item => item.ToString(CultureInfo.InvariantCulture),
            decimal item => item.ToString("G29", CultureInfo.InvariantCulture),
            double item when double.IsFinite(item) => item.ToString("R", CultureInfo.InvariantCulture),
            float item when float.IsFinite(item) => item.ToString("R", CultureInfo.InvariantCulture),
            Enum item => item.ToString("D"),
            _ => null,
        };
        if (text is null) return false;
        write(type.FullName ?? type.Name); write(text);
        return true;
    }

    /// <summary>Orders dictionaries by exact canonical key text, avoiding insertion order and hash-collision ordering.</summary>
    private static bool Map(object value, Type type, Action<string> write, HashSet<object> path, bool gameplay)
    {
        bool dictionary = type.GetInterfaces().Any(candidate => candidate.IsGenericType
            && (candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>) || candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));
        if (!dictionary && value is not IDictionary) return false;
        var entries = new List<(string KeyText, object? Key, object? Value)>();
        foreach (object item in (IEnumerable)value)
        {
            object? key = item.GetType().GetProperty("Key")!.GetValue(item);
            object? data = item.GetType().GetProperty("Value")!.GetValue(item);
            var text = new StringBuilder();
            Visit(key, token => text.Append(token.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(token), new(ReferenceEqualityComparer.Instance), gameplay);
            entries.Add((text.ToString(), key, data));
        }
        if (entries.Select(entry => entry.KeyText).Distinct(StringComparer.Ordinal).Count() != entries.Count)
            throw new InvalidOperationException("Checkpoint map keys do not have unique canonical values.");
        write("map");
        foreach (var entry in entries.OrderBy(entry => entry.KeyText, StringComparer.Ordinal))
        {
            Visit(entry.Key, write, path, gameplay); Visit(entry.Value, write, path, gameplay);
        }
        write("end-map");
        return true;
    }
}
