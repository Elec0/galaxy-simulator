using System.Text.Json;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class NonOwnedManeuverObservationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AnalyticContactPublishesCapturedKinematicsWithoutItsFuturePlan(int completedPhases)
    {
        using GameSession session = GameSessionFogOfWarTests.Create(5000, radius: 200_000);
        var other = new CommandSource(CommandSourceKind.Autonomous, new CommandSourceId("other"));
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(other,
            new MoveShipCommand(new(2), GameSessionTestFixture.Destination(100_000, 0), OrderPlacement.ReplaceAll)).Result.Status);
        for (int phase = 0; phase < completedPhases; phase++)
        {
            TerminalManeuverSnapshot plan = Assert.IsType<TerminalManeuverSnapshot>(Authoritative(session).Maneuver);
            session.AdvanceTo(plan.NextBoundary!.Timestamp);
        }
        GameShipSnapshot before = Authoritative(session);
        TerminalManeuverSnapshot maneuver = Assert.IsType<TerminalManeuverSnapshot>(before.Maneuver);
        Assert.NotNull(maneuver.NextBoundary);
        Assert.NotNull(before.CurrentOrder);
        session.AdvanceTo(new(session.CurrentTime.Milliseconds + 1));
        GameShipSnapshot authoritative = Authoritative(session);
        GamePresentationSnapshot presentation = GameSessionFogOfWarTests.Capture(session);
        GameObservedShipSnapshot captured = Assert.Single(presentation.World.Ships, ship => ship.Id == new ShipId(2));

        Assert.Equal(authoritative.Position, captured.Position);
        Assert.Equal(authoritative.Velocity, captured.Velocity);
        Assert.Equal(authoritative.Heading, captured.Heading);
        Assert.Null(captured.OwnedDetail);
        Assert.Equal(captured, presentation.Selection.FocusedShip);
        Assert.All(presentation.SelectedShipFacts, fact => Assert.IsType<ObservedShipChangeFact>(fact.Fact));
        // This is a closed disclosure vocabulary: adding a future plan or private
        // capability to the contact must require an explicit observation decision.
        JsonElement exported = JsonSerializer.SerializeToElement(captured);
        Assert.Equal(["EntityId", "Id", "PrincipalId", "DesignId", "Position", "Velocity", "Heading", "OwnedDetail"],
            exported.EnumerateObject().Select(property => property.Name));
        Assert.Equal(JsonValueKind.Null, exported.GetProperty("OwnedDetail").ValueKind);

        SystemPosition? position = captured.Position;
        ShipVelocity velocity = captured.Velocity;
        ShipHeading heading = captured.Heading;
        session.AdvanceTo(new(session.CurrentTime.Milliseconds + 100));
        Assert.Equal(position, captured.Position);
        Assert.Equal(velocity, captured.Velocity);
        Assert.Equal(heading, captured.Heading);
        session.RemoveEntity(new(new(1), EntityRemovalReason.Despawned, EntityCargoDisposition.DiscardCargo));
        GamePresentationSnapshot hidden = GameSessionFogOfWarTests.Capture(session);
        Assert.DoesNotContain(hidden.World.Ships, ship => ship.Id == new ShipId(2));
        Assert.Null(hidden.Selection.FocusedShip);
        Assert.Contains(new ShipId(2), hidden.Selection.UnresolvedShipIds);
    }

    private static GameShipSnapshot Authoritative(GameSession session) =>
        Assert.Single(session.CaptureSnapshot().Ships, ship => ship.Id == new ShipId(2));
}
