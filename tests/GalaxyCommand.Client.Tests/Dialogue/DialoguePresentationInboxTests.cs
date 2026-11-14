using GalaxyCommand.Client;
using GalaxyCommand.Content;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Client.Tests;

public sealed class DialoguePresentationInboxTests
{
    private static readonly QualifiedContentKey Key = QualifiedContentKey.Parse("test/dialogue/notice");
    private static readonly DialogueParticipantBindings Bindings = new([], []);

    [Fact]
    public void CompletedInformationalPathSurvivesLiveStateRemovalWithoutDuplicatingStartAndEnd()
    {
        var inbox = new DialoguePresentationInbox(2);
        var started = new DialogueStartedFact(new(1), Key, Bindings, ["info"]);
        var ended = new DialogueEndedFact(new(1), Key, Bindings, DialogueEndReason.Completed, "done", ["info"]);

        inbox.Update([], Facts(new(new(1), default, started), new(new(2), default, ended)), Catalog());

        DialogueDisplayConversation entry = Assert.Single(inbox.Entries);
        Assert.True(entry.IsCompleted);
        Assert.False(entry.ResponseRequired);
        Assert.Empty(entry.Choices);
        Assert.Equal("Message", Assert.Single(entry.Segments).Text.InvariantFallback);
        inbox.Update([], Facts(new(new(1), default, started), new(new(2), default, ended)), Catalog());
        Assert.Single(inbox.Entries);
    }

    [Fact]
    public void DismissedNoticeDoesNotReappearFromAlreadyConsumedFacts()
    {
        var inbox = new DialoguePresentationInbox(2);
        GamePresentationFactReadResult facts = Facts(Ended(1, 1));
        inbox.Update([], facts, Catalog());

        Assert.True(inbox.Dismiss(new(1)));
        Assert.False(inbox.Dismiss(new(1)));
        inbox.Update([], facts, Catalog());

        Assert.Empty(inbox.Entries);
    }

    [Fact]
    public void BoundedCompletedHistoryEvictsOldestTerminalNoticeAndCursorGapClearsIt()
    {
        var inbox = new DialoguePresentationInbox(2);
        inbox.Update([], Facts(Ended(1, 1), Ended(2, 2), Ended(3, 3)), Catalog());

        Assert.Equal(new ulong[] { 2, 3 }, inbox.Entries.Select(entry => entry.Id.Value));
        inbox.Update([], new([], null, null, true), Catalog());
        Assert.Empty(inbox.Entries);
    }

    [Fact]
    public void ResponseCompletionWithoutInformationalEnteredNodesDoesNotCreateNotice()
    {
        var inbox = new DialoguePresentationInbox(2);
        var ended = new DialogueEndedFact(new(1), Key, Bindings, DialogueEndReason.Completed, "done", []);

        inbox.Update([], Facts(new GamePresentationFactEnvelope(new(1), default, ended)), Catalog());

        Assert.Empty(inbox.Entries);
    }

    [Fact]
    public void LiveEntriesPreserveSessionClassificationAndCannotBeDismissedLocally()
    {
        var inbox = new DialoguePresentationInbox(2);
        DialogueSegment[] segments = [new("line", 0, Text())];
        DialogueChoiceSnapshot[] choices = [new("respond", 0, Text(), false)];
        var live = new DialogueConversationSnapshot(new(1), Key, Bindings, "response", DialogueConversationStatus.Suspended,
            true, segments, choices);

        inbox.Update([live], Facts(), Catalog());

        DialogueDisplayConversation entry = Assert.Single(inbox.Entries);
        Assert.False(entry.IsCompleted);
        Assert.True(entry.IsSuspended);
        Assert.False(inbox.Dismiss(new(1)));
        Assert.False(Assert.Single(entry.Choices).IsAvailable);
        segments[0] = new("changed", 0, Text());
        choices[0] = new("changed", 0, Text(), true);
        Assert.Equal("line", Assert.Single(entry.Segments).Id);
        Assert.Equal("respond", Assert.Single(entry.Choices).Id);
    }

    private static GamePresentationFactEnvelope Ended(ulong sequence, ulong id) => new(new(sequence), default,
        new DialogueEndedFact(new(id), Key, Bindings, DialogueEndReason.Completed, "done", ["info"]));
    private static GamePresentationFactReadResult Facts(params GamePresentationFactEnvelope[] values) => new(values, null, null, false);
    private static DialogueText Text() => new("dialogue.message", "Message", DialogueMarkup.PlainText);
    private static DialogueDefinitionCatalog Catalog() => new([new DialogueDefinition(Key, [],
        [new DialogueNode("info", [new("line", 0, Text())], [], new DialogueTransition.Complete("done"))],
        "info", DialogueRepeatability.Repeatable)]);
}
