using System.Text.Json;
using System.Text.Json.Nodes;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class MovingCourseTurnSaveTests
{
    private static readonly SaveJsonLimits Limits = new(16_777_216, 128, 65_536, 100_000);
    private static readonly GameSessionSaveMetadata Metadata = new(
        Guid.Parse("7b6f7328-86d6-43b5-a77a-9459724ab41f"),
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), "moving-course-turn");

    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(2, 3, false)]
    [InlineData(4, 1, true)]
    public void EncodedMidTurnReplacementRetainsItsSelectedContinuation(int workers, int batchSize, bool fromCruise)
    {
        using GameSession original = MovingReplacement(fromCruise);
        Assert.NotEqual(ShipHeading.Zero, Ship(original).Heading);
        Assert.NotEqual(ShipVelocity.Zero, Ship(original).Velocity);
        using GameSession restored = RoundTrip(original, new(workers, batchSize));
        Assert.Equal(Encoded(original), Encoded(restored));

        ulong capturedAt = original.CurrentTime.Milliseconds;
        for (ulong time = capturedAt; time <= capturedAt + 8_900; time += 137)
        {
            original.AdvanceTo(new(time));
            restored.AdvanceTo(new(time));
            Assert.Equal(Ship(original).Position, Ship(restored).Position);
            Assert.Equal(Ship(original).Velocity, Ship(restored).Velocity);
            Assert.Equal(Ship(original).Heading, Ship(restored).Heading);
        }
        original.AdvanceTo(new(1_000_000));
        restored.AdvanceTo(new(1_000_000));
        Assert.Equal(ShipOrderStatus.Completed, Ship(restored).CurrentOrder!.Status);
        Assert.Equal(Encoded(original), Encoded(restored));
    }

    [Theory]
    [InlineData("missing-braking")]
    [InlineData("passive-braking")]
    [InlineData("braking-heading")]
    [InlineData("course")]
    [InlineData("child-time")]
    [InlineData("phase-kind")]
    public void EncodedMovingTurnRejectsMalformedPrefixAndFutureContinuation(string fault)
    {
        using GameSession session = MovingReplacement();
        GameSessionCheckpoint sessionState = session.CaptureCheckpoint().Value!;
        var active = Assert.IsType<ShipSpatialStateCheckpoint.AnalyticManeuver>(Assert.Single(sessionState.Movement.Actors).State);
        ExecutableManeuverCheckpoint selected = active.Plan.CaptureCheckpoint();
        var payload = Assert.IsType<ExecutableManeuverPayload.MovingCourseTurn>(selected.Payload);
        DecelerationManeuverSegment braking = payload.BrakingPhase;
        ExecutableManeuverPayload.MovingCourseTurn changed = fault switch
        {
            "passive-braking" => payload with { BrakingPhase = new(ManeuverDecelerationKind.PassiveDrag,
                braking.StartsAt, braking.Start, braking.Deceleration) },
            "braking-heading" => payload with { BrakingPhase = new(braking.Kind, braking.StartsAt,
                braking.Start with { Heading = new(123_000) }, braking.Deceleration) },
            "child-time" => payload with { Continuation = ExecutableBoundedTerminalManeuverPlan.TerminalSettle(
                new(payload.Continuation.Phases[0].StartsAt.Milliseconds + 1),
                new(payload.Destination, ShipVelocity.Zero, ShipHeading.Zero)).CaptureCheckpoint() },
            "course" => payload with { CourseTurn = OtherRotation(payload.CourseTurn) },
            _ => payload,
        };
        ManeuverScheduledPhase[] phases = selected.Phases.ToArray();
        if (fault == "phase-kind")
            phases[0] = new(ManeuverPhaseKind.CoastUnderPassiveDrag, phases[0].StartsAt, phases[0].EndsAt);
        var codec = new CheckpointValueCodec();
        CheckpointJsonContracts.Register(codec);
        JsonNode malformed = JsonNode.Parse(codec.Encode(selected with { Payload = changed, Phases = phases }).GetRawText())!;
        if (fault == "missing-braking") malformed["payload"]!["brakingPhase"] = null;

        Assert.Throws<CheckpointJsonException>(() => codec.Decode<ExecutableBoundedTerminalManeuverPlan>(JsonSerializer.SerializeToElement(malformed)));
        Assert.True(session.IsHealthy);
    }

    private static StationaryTurnManeuverPlan OtherRotation(StationaryTurnManeuverPlan original)
    {
        Assert.True(StationaryTurnManeuverPlan.TryCreate(original.StartsAt, original.TurnPhase.Start,
            original.Destination, new(180_000), new(45_000), out StationaryTurnManeuverPlan? changed));
        return changed!;
    }

    private static GameSession MovingReplacement(bool fromCruise = false)
    {
        GameSession session = GameSessionTestFixture.Create(navigation:
            new DirectLocalNavigationPlanner(new ChebyshevLocalTravelTimeEstimator(10)));
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(GameSessionTestFixture.Ship, GameSessionTestFixture.Destination(100_000, 0), OrderPlacement.ReplaceAll)).Result.Status);
        session.AdvanceTo(new(1_000));
        if (fromCruise)
        {
            for (int phase = 0; phase < 16 && Ship(session).Maneuver?.CurrentPhase?.Kind != ManeuverPhaseKind.CruiseTravel; phase++)
                session.AdvanceTo(Ship(session).Maneuver!.NextBoundary!.Timestamp);
            Assert.Equal(ManeuverPhaseKind.CruiseTravel, Ship(session).Maneuver!.CurrentPhase!.Value.Kind);
        }
        SimulationTime replacementAt = session.CurrentTime;
        Assert.Equal(CommandResultStatus.Accepted, session.SubmitCommand(GameSessionTestFixture.Player,
            new MoveShipCommand(GameSessionTestFixture.Ship, GameSessionTestFixture.Destination(0, 10_000), OrderPlacement.ReplaceAll)).Result.Status);
        session.AdvanceTo(new(replacementAt.Milliseconds + 100));
        return session;
    }

    private static GameShipSnapshot Ship(GameSession session) => Assert.Single(session.CaptureSnapshot().Ships);

    private static InertSaveDocument Encode(GameSession session)
    {
        CheckpointResult<GameSessionCheckpoint> checkpoint = session.CaptureCheckpoint();
        Assert.True(checkpoint.IsSuccess, checkpoint.Failure?.Message);
        CheckpointResult<InertSaveDocument> encoded = GameSessionSaveCodec.Encode(checkpoint.Value!, Metadata, Limits);
        Assert.True(encoded.IsSuccess, encoded.Failure?.Message);
        return encoded.Value!;
    }

    private static string Encoded(GameSession session) => Encode(session).Root.GetRawText();

    private static GameSession RoundTrip(GameSession session, InteractionBatchExecution execution)
    {
        using var bytes = new MemoryStream();
        SaveWriteResult written = SaveJsonCodec.Write(bytes, Encode(session), Limits);
        Assert.True(written.IsSuccess, written.Failure?.Message);
        bytes.Position = 0;
        SaveReadResult decoded = SaveJsonCodec.Read(bytes, Limits, new SaveMigrationRegistry(SaveFormat.CurrentSchemaVersion, []));
        Assert.True(decoded.IsSuccess, decoded.Failure?.Message);
        CheckpointResult<GameSession> restored = GameSessionSaveCodec.Restore(decoded.Document!, Limits, interactionExecution: execution);
        Assert.True(restored.IsSuccess, restored.Failure?.Path + ": " + restored.Failure?.Message);
        return restored.Value!;
    }
}
