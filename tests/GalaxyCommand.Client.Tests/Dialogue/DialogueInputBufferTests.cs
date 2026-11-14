using GalaxyCommand.Client;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Client.Tests;

public sealed class DialogueInputBufferTests
{
    [Fact]
    public void LocalDialogueSelectionSharesCaptureOrderWithGameplayAndPacing()
    {
        var buffer = new ApplicationInputBuffer();
        var source = new CommandSource(CommandSourceKind.Player, new("player"));
        buffer.EnqueuePacing(new ApplicationPacingAction.Pause());
        buffer.EnqueueDialoguePresentation(new DialoguePresentationAction.Open(new(7)));
        buffer.EnqueueGameplay(source, new SuspendDialogueCommand(new(7)));
        buffer.EnqueueDialoguePresentation(new DialoguePresentationAction.Close(new(7)));

        IReadOnlyList<BufferedApplicationInput> captured = buffer.Drain();

        Assert.IsType<BufferedApplicationInput.Pacing>(captured[0]);
        Assert.Equal(new DialogueConversationId(7), Assert.IsType<DialoguePresentationAction.Open>(
            Assert.IsType<BufferedApplicationInput.DialoguePresentation>(captured[1]).Action).ConversationId);
        Assert.IsType<SuspendDialogueCommand>(Assert.IsType<BufferedApplicationInput.Gameplay>(captured[2]).Command);
        Assert.IsType<DialoguePresentationAction.Close>(Assert.IsType<BufferedApplicationInput.DialoguePresentation>(captured[3]).Action);
        Assert.Empty(buffer.Drain());
    }
}
