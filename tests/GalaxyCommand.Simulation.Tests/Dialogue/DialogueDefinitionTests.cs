using GalaxyCommand.Content;
using System.Globalization;

namespace GalaxyCommand.Simulation.Tests;

public sealed class DialogueDefinitionTests
{
    private static readonly string[] OrderedIds = ["first", "later"];
    private static readonly QualifiedContentKey Key = QualifiedContentKey.Create("test.dialogue", "dialogue", "greeting");

    [Fact]
    public void CanonicalizesExplicitOrderAndCopiesInputCollections()
    {
        var choices = new List<DialogueChoice> { Choice("later", 20), Choice("first", 10) };
        var nodes = new List<DialogueNode> { new("entry", [Segment("later", 20), Segment("first", 10)], choices) };
        var definition = new DialogueDefinition(Key, [], nodes, "entry", DialogueRepeatability.Repeatable);
        choices.Clear();
        nodes.Clear();
        DialogueNode node = Assert.Single(definition.Nodes);
        Assert.Equal(OrderedIds, node.Choices.Select(choice => choice.Id));
        Assert.Equal(OrderedIds, node.Segments.Select(segment => segment.Id));
        Assert.True(node.ResponseRequired);
    }

    [Theory]
    [InlineData("missing-entry")]
    [InlineData("missing-target")]
    [InlineData("duplicate-node")]
    [InlineData("duplicate-choice")]
    [InlineData("duplicate-order")]
    [InlineData("missing-memory")]
    [InlineData("missing-role")]
    [InlineData("invalid-move-actor")]
    public void RejectsUnresolvedOrAmbiguousGraphBeforePublication(string invalid)
    {
        DialogueParticipantRole[] roles = [new("principal", [DialogueParticipantKind.Principal])];
        DialogueChoice choice = invalid switch
        {
            "missing-target" => new("accept", 0, Text(), new DialogueTransition.Node("absent"), DialogueRepeatability.Repeatable),
            "missing-memory" => new("accept", 0, Text(), new DialogueTransition.Complete("done"), DialogueRepeatability.Repeatable,
                admissionConditions: [new DialogueCondition.Receipt("absent", DialogueMemoryScope.Session, true)]),
            "missing-role" => new("accept", 0, Text(), new DialogueTransition.Complete("done"), DialogueRepeatability.Repeatable,
                admissionConditions: [new DialogueCondition.Detected("absent")]),
            "invalid-move-actor" => new("accept", 0, Text(), new DialogueTransition.Complete("done"), DialogueRepeatability.Repeatable,
                move: new("principal", "principal", OrderPlacement.ReplaceAll)),
            _ => Choice("accept", 0),
        };
        DialogueChoice[] choices = invalid switch
        {
            "duplicate-choice" => [choice, Choice("accept", 1)],
            "duplicate-order" => [choice, Choice("other", 0)],
            _ => [choice],
        };
        DialogueNode node = new("entry", [Segment("line", 0)], [choice]);
        Assert.Throws<ArgumentException>(() =>
        {
            DialogueNode candidate = new("entry", [Segment("line", 0)], choices);
            _ = new DialogueDefinition(Key, roles, invalid == "duplicate-node" ? [node, candidate] : [candidate],
                invalid == "missing-entry" ? "absent" : "entry", DialogueRepeatability.Repeatable);
        });
    }

    [Fact]
    public void InformationalNodeCompletesWithoutInventingAPlayerResponse()
    {
        var node = new DialogueNode("entry", [Segment("notice", 0)], [], new DialogueTransition.Complete("informed"));
        Assert.False(node.ResponseRequired);
        Assert.IsType<DialogueTransition.Complete>(node.AutomaticTransition);
        Assert.Throws<ArgumentException>(() => new DialogueNode("broken", [], []));
        Assert.Throws<ArgumentException>(() => new DialogueNode("broken", [], [Choice("accept", 0)], new DialogueTransition.Complete("done")));
    }

    [Fact]
    public void ValidatesDeclaredMemoryAndTypedParticipantConditions()
    {
        var choice = new DialogueChoice("accept", 0, Text(), new DialogueTransition.Complete("done"), DialogueRepeatability.Repeatable,
            admissionConditions: [new DialogueCondition.Receipt("heard", DialogueMemoryScope.Session, false),
                new DialogueCondition.Standing("player", "respondent-principal", 5),
                new DialogueCondition.Detected("respondent"), new DialogueCondition.Discovered("respondent")],
            memoryIds: ["heard"], move: new("actor", "respondent", OrderPlacement.ReplaceAll));
        var definition = new DialogueDefinition(Key,
            [new("player", [DialogueParticipantKind.Principal]), new("respondent-principal", [DialogueParticipantKind.Principal]), new("respondent", [DialogueParticipantKind.Station]),
                new("actor", [DialogueParticipantKind.Ship])],
            [new("entry", [Segment("line", 0)], [choice])], "entry", DialogueRepeatability.Repeatable, memories: [new("heard", DialogueMemoryScope.Session)]);
        Assert.Single(definition.MemoryIds);
    }

    [Fact]
    public void TextCopiesTypedArgumentsAndRejectsInvalidResourceContracts()
    {
        var arguments = new List<DialogueTextArgument> { new DialogueTextArgument.Text("name", "Captain"), new DialogueTextArgument.Number("count", 2) };
        var text = new DialogueText("test.dialogue.greeting", "Hello {name}: {count}", DialogueMarkup.PlainText, arguments, 2);
        arguments.Clear();
        Assert.Equal(2, text.Arguments.Count);
        Assert.Throws<ArgumentException>(() => new DialogueText("", "Hello", DialogueMarkup.PlainText));
        Assert.Throws<ArgumentException>(() => new DialogueText("key", "", DialogueMarkup.PlainText));
        Assert.Throws<ArgumentException>(() => new DialogueText("key", "Hello {missing}", DialogueMarkup.PlainText));
        Assert.Throws<ArgumentException>(() => new DialogueText("key", "{name}", DialogueMarkup.PlainText, [new DialogueTextArgument.Text("name", "a"), new DialogueTextArgument.Text("name", "b")]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DialogueText("key", "Hello", DialogueMarkup.PlainText, pluralSelector: -1));
    }

    [Fact]
    public void RejectsFractionalPluralSelector()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DialogueText("key", "Hello", DialogueMarkup.PlainText, pluralSelector: 1.5m));
    }

    [Fact]
    public void ValidatesWholeAutomaticGraphBeforeTraversingIt()
    {
        DialogueNode[] nodes = [new("a", [], [], new DialogueTransition.Node("b")),
            new("b", [], [], new DialogueTransition.Node("missing"))];
        Assert.Throws<ArgumentException>(() => new DialogueDefinition(Key, [], nodes, "a", DialogueRepeatability.Repeatable));
    }

    [Fact]
    public void RejectsAutomaticCycles()
    {
        DialogueNode[] nodes = [new("a", [], [], new DialogueTransition.Node("b")),
            new("b", [], [], new DialogueTransition.Node("a"))];
        Assert.Throws<ArgumentException>(() => new DialogueDefinition(Key, [], nodes, "a", DialogueRepeatability.Repeatable));
    }

    [Fact]
    public void ResolvesLongAutomaticChainToResponseWithoutRecursion()
    {
        DialogueNode[] nodes = Enumerable.Range(0, 2048).Select(index => index == 2047
            ? new DialogueNode(index.ToString(CultureInfo.InvariantCulture), [], [Choice("accept", 0)])
            : new DialogueNode(index.ToString(CultureInfo.InvariantCulture), [], [],
                new DialogueTransition.Node((index + 1).ToString(CultureInfo.InvariantCulture)))).ToArray();
        var definition = new DialogueDefinition(Key, [], nodes, "0", DialogueRepeatability.Repeatable);
        Assert.Equal(2048, definition.Nodes.Count);
    }

    [Fact]
    public void RejectsOtherContentKinds()
    {
        Assert.Throws<ArgumentException>(() => new DialogueDefinition(QualifiedContentKey.Create("test.dialogue", "ship", "greeting"),
            [], [new("entry", [], [Choice("accept", 0)])], "entry", DialogueRepeatability.Repeatable));
    }

    [Fact]
    public void RejectsInvalidEnumsNullEntriesAndGlobalChoiceIdentityCollisions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DialogueText("key", "Hello", (DialogueMarkup)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DialogueChoice("accept", 0, Text(), new DialogueTransition.Complete("done"), (DialogueRepeatability)99));
        Assert.Throws<ArgumentException>(() => new DialogueNode("entry", [null!], [Choice("accept", 0)]));
        Assert.Throws<ArgumentException>(() => new DialogueDefinition(Key, [], [null!], "entry", DialogueRepeatability.Repeatable));
        Assert.Throws<ArgumentException>(() => new DialogueDefinition(Key, [],
            [new("a", [], [Choice("accept", 0)]), new("b", [], [Choice("accept", 0)])], "a", DialogueRepeatability.Repeatable));
    }

    [Fact]
    public void RejectsUnknownSemanticArgumentKinds()
    {
        Assert.Throws<ArgumentException>(() => new DialogueText("key", "{value}", DialogueMarkup.PlainText,
            [new UnsupportedArgument("value")]));
    }

    [Theory]
    [InlineData("[b]Open")]
    [InlineData("[b][i]Mismatch[/b][/i]")]
    [InlineData("[url]Unsupported[/url]")]
    [InlineData("[lb]")]
    [InlineData("[/u]")]
    public void RejectsUnsupportedOrMalformedRestrictedMarkup(string fallback)
    {
        Assert.Throws<ArgumentException>(() => new DialogueText("key", fallback, DialogueMarkup.RestrictedRichText));
    }

    [Fact]
    public void PermitsBalancedRestrictedMarkupAndLeavesPlainTextLiteral()
    {
        DialogueText.ValidateMarkup("[b]Bold [i]italic[/i][/b] [u]underline[/u]", DialogueMarkup.RestrictedRichText);
        _ = new DialogueText("key", "[url]literal[/url]", DialogueMarkup.PlainText);
    }

    [Fact]
    public void RejectsPersistentDiscoveryPredicateForShipRoles()
    {
        Assert.Throws<ArgumentException>(() => new DialogueDefinition(Key,
            [new("ship", [DialogueParticipantKind.Ship])], [new("entry", [], [Choice("accept", 0)])],
            "entry", DialogueRepeatability.Repeatable, [new DialogueCondition.Discovered("ship")]));
    }

    [Fact]
    public void RejectsStandingOnNonPrincipalRoles()
    {
        Assert.Throws<ArgumentException>(() => new DialogueDefinition(Key,
            [new("ship", [DialogueParticipantKind.Ship])], [new("entry", [], [Choice("accept", 0)])],
            "entry", DialogueRepeatability.Repeatable, [new DialogueCondition.Standing("ship", "ship", 1)]));
    }

    [Fact]
    public void MemoryDeclarationsRetainExplicitScopesAndCopyCallerCollection()
    {
        var memories = new List<DialogueMemoryDeclaration> { new("session", DialogueMemoryScope.Session),
            new("binding", DialogueMemoryScope.ParticipantBinding) };
        var definition = new DialogueDefinition(Key, [], [new("entry", [], [Choice("accept", 0)])],
            "entry", DialogueRepeatability.Repeatable, [new DialogueCondition.Receipt("binding", DialogueMemoryScope.ParticipantBinding, false)], memories);
        memories.Clear();
        Assert.Equal(2, definition.Memories.Count);
        Assert.Equal(DialogueMemoryScope.ParticipantBinding, definition.Memories[0].Scope);
        Assert.Equal(DialogueMemoryScope.Session, definition.Memories[1].Scope);
    }

    [Fact]
    public void RejectsReceiptScopeThatDiffersFromDeclaration()
    {
        Assert.Throws<ArgumentException>(() => new DialogueDefinition(Key, [], [new("entry", [], [Choice("accept", 0)])],
            "entry", DialogueRepeatability.Repeatable,
            [new DialogueCondition.Receipt("heard", DialogueMemoryScope.ParticipantBinding, false)],
            [new("heard", DialogueMemoryScope.Session)]));
    }

    private sealed record UnsupportedArgument(string Name) : DialogueTextArgument(Name);

    private static DialogueChoice Choice(string id, int order) => new(id, order, Text(), new DialogueTransition.Complete("done"), DialogueRepeatability.Repeatable);
    private static DialogueSegment Segment(string id, int order) => new(id, order, Text());
    private static DialogueText Text() => new("test.dialogue.line", "Hello", DialogueMarkup.PlainText);
}
