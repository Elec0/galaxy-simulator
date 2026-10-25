using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ShipSensorCoverageTests
{
    [Fact]
    public void CoverageUsesOnlyOwnedSourcesAndAlwaysIncludesOwnedAssets()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(5));
        movement.Add(new ShipId(3), Position(50));
        movement.Add(new ShipId(4), Position(90));
        ShipSensorCoverageInput[] inputs = [Input(4, 1, 0), Input(3, 2, 100), Input(2, 2, 100), Input(1, 1, 5)];

        ShipSensorCoverageSnapshot result = Evaluate(movement, inputs);

        Assert.Equal<ShipId>([new(1), new(4)], result.OwnedShipIds);
        Assert.Equal<ShipId>([new(1), new(2), new(4)], result.CoveredShipIds);
        Assert.Equal<ShipId>([new(1), new(2), new(4)], result.VisibleShipIds);
        Assert.Equal(new PrincipalId(1), result.ObserverPrincipalId);
        Assert.Equal(SimulationTime.Zero, result.Timestamp);
    }

    [Theory]
    [InlineData(3, 4, 5, true)]
    [InlineData(3, 4, 4, false)]
    [InlineData(0, 0, 0, true)]
    [InlineData(1, 0, 0, false)]
    public void EuclideanBoundaryIsInclusive(long x, long y, ulong radius, bool covered)
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(x, y));
        ShipSensorCoverageSnapshot result = Evaluate(movement, [Input(1, 1, radius), Input(2, 2, 0)]);
        Assert.Equal(covered, result.VisibleShipIds.Contains(new ShipId(2)));
    }

    [Fact]
    public void MaximumRadiusDoesNotOverflowAcrossCoordinateExtremes()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(long.MinValue, long.MinValue));
        movement.Add(new ShipId(2), Position(long.MaxValue, long.MaxValue));
        movement.Add(new ShipId(3), Position(long.MaxValue, long.MinValue));
        ShipSensorCoverageSnapshot result = Evaluate(movement,
            [Input(1, 1, ulong.MaxValue), Input(2, 2, 0), Input(3, 2, 0)]);
        Assert.Equal<ShipId>([new(1), new(3)], result.VisibleShipIds);
    }

    [Fact]
    public void OverlapReducesToOneContactAndLossRequiresLosingEverySource()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(8));
        movement.Add(new ShipId(3), Position(4));
        ShipSensorCoverageInput[] inputs = [Input(1, 1, 4), Input(2, 1, 4), Input(3, 2, 0)];
        ShipSensorCoverageSnapshot before = Evaluate(movement, inputs);
        movement.CommitRemove(new ShipId(1), SimulationTime.Zero);
        ShipSensorCoverageSnapshot oneSource = Evaluate(movement, inputs.Skip(1));
        movement.CommitRemove(new ShipId(2), SimulationTime.Zero);
        ShipSensorCoverageSnapshot noSources = Evaluate(movement, inputs.Skip(2));

        Assert.Equal<ShipId>([new(1), new(2), new(3)], before.VisibleShipIds);
        Assert.Equal<ShipId>([new(2), new(3)], oneSource.VisibleShipIds);
        Assert.Empty(noSources.VisibleShipIds);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void MovingAndStationaryCombinationsUseCurrentExactGeometry(bool movingSource, bool movingTarget)
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(10));
        if (movingSource) StartLinear(movement, 1, 0, 4, 1000);
        if (movingTarget) StartLinear(movement, 2, 10, 6, 1000);
        ShipSensorCoverageInput[] inputs = [Input(1, 1, 8), Input(2, 2, 0)];

        Assert.DoesNotContain(new ShipId(2), Evaluate(movement, inputs).VisibleShipIds);
        Assert.Contains(new ShipId(2), Evaluate(movement, inputs, new SimulationTime(500)).VisibleShipIds);
        Assert.DoesNotContain(new ShipId(2), Evaluate(movement, inputs, new SimulationTime(1001)).VisibleShipIds);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CrossingContactsDisappearImmediatelyAfterInclusiveExit(bool movingSource, bool movingTarget)
    {
        var movement = new SpatialMovement();
        long sourceStart = movingSource ? -10 : 0;
        long targetStart = movingTarget ? 10 : 0;
        movement.Add(new ShipId(1), Position(sourceStart));
        movement.Add(new ShipId(2), Position(targetStart));
        if (movingSource) StartLinear(movement, 1, sourceStart, 10, 1000);
        if (movingTarget) StartLinear(movement, 2, targetStart, -10, 1000);
        ulong radius = movingSource && movingTarget ? 4UL : 2UL;
        ShipSensorCoverageInput[] inputs = [Input(1, 1, radius), Input(2, 2, 0)];

        foreach (ulong time in new ulong[] { 400, 500, 600 })
            Assert.Contains(new ShipId(2), Evaluate(movement, inputs, new SimulationTime(time)).VisibleShipIds);
        foreach (ulong time in new ulong[] { 399, 601 })
            Assert.DoesNotContain(new ShipId(2), Evaluate(movement, inputs, new SimulationTime(time)).VisibleShipIds);
    }

    [Fact]
    public void RoundedPublishedPositionsDoNotCreateFalseZeroRadiusContacts()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(0));
        StartLinear(movement, 1, 0, 2, 2000);
        var now = new SimulationTime(500);
        InteractionSpatialReadView view = movement.CaptureInteractionReadView(now);
        Assert.Equal(Position(0), view.Get(new ShipId(1))!.Spatial.Position);
        ShipSensorCoverageSnapshot result = ShipSensorCoverage.Evaluate(view, new PrincipalId(1),
            [Input(1, 1, 0), Input(2, 2, 0)], InteractionPreparationStrategy.Scan, 1);
        Assert.Equal<ShipId>([new(1)], result.VisibleShipIds);
    }

    [Fact]
    public void TransitSourceRemainsOwnedButContributesNoCoverageUntilEmergence()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(0));
        movement.Add(new ShipId(3), Position(0, system: 2));
        var transit = movement.CommitStartConnector(new ShipId(1), new TravelLeg.Connector(new TransitConnectionId(1),
            Position(0), Position(0, system: 2), new SimulationDuration(10)), SimulationTime.Zero, static value => value);
        ShipSensorCoverageInput[] inputs = [Input(1, 1, 0), Input(2, 2, 0), Input(3, 2, 0)];
        ShipSensorCoverageSnapshot during = Evaluate(movement, inputs);
        Assert.Equal<ShipId>([new(1)], during.VisibleShipIds);
        Assert.Empty(during.CoveredShipIds);

        Assert.Equal(ScheduledEventDisposition.Applied, movement.HandleEvent(transit.EventProposal.Payload,
            transit.Transit.Generation, new SimulationTime(10)));
        ShipSensorCoverageSnapshot after = Evaluate(movement, inputs, new SimulationTime(10));
        Assert.Equal<ShipId>([new(1), new(3)], after.VisibleShipIds);
        Assert.Equal<ShipId>([new(1), new(3)], after.CoveredShipIds);
    }

    [Fact]
    public void NonOwnedTransitAndDifferentSystemShipsAreHidden()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(0));
        movement.Add(new ShipId(3), Position(0, system: 2));
        movement.CommitStartConnector(new ShipId(2), new TravelLeg.Connector(new TransitConnectionId(1),
            Position(0), Position(0, system: 2), new SimulationDuration(10)), SimulationTime.Zero, static value => value);
        Assert.Equal<ShipId>([new(1)], Evaluate(movement,
            [Input(1, 1, ulong.MaxValue), Input(2, 2, 0), Input(3, 2, 0)]).VisibleShipIds);
    }

    [Fact]
    public void CreationAndRemovalAffectOnlyFreshCaptures()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        var inputs = new List<ShipSensorCoverageInput> { Input(1, 1, 5) };
        ShipSensorCoverageSnapshot before = Evaluate(movement, inputs);
        movement.Add(new ShipId(2), Position(5));
        inputs.Add(Input(2, 2, 0));
        InteractionSpatialReadView captured = movement.CaptureInteractionReadView(SimulationTime.Zero);
        ShipSensorCoverageSnapshot created = Evaluate(movement, inputs);
        movement.CommitRemove(new ShipId(2), SimulationTime.Zero);
        ShipSensorCoverageSnapshot stable = ShipSensorCoverage.Evaluate(captured, new PrincipalId(1), inputs,
            InteractionPreparationStrategy.Scan, 1);
        inputs.RemoveAt(1);
        ShipSensorCoverageSnapshot removed = Evaluate(movement, inputs);

        Assert.Equal<ShipId>([new(1)], before.VisibleShipIds);
        Assert.Equal<ShipId>([new(1), new(2)], created.VisibleShipIds);
        Assert.Equal(created.VisibleShipIds, stable.VisibleShipIds);
        Assert.Equal(before.VisibleShipIds, removed.VisibleShipIds);
        Assert.Throws<NotSupportedException>(() => ((IList<ShipId>)created.VisibleShipIds).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ShipId>)created.CoveredShipIds).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ShipId>)created.OwnedShipIds).Clear());
    }

    [Fact]
    public void EmptyScopeAndObserverWithoutAssetsProduceNoContacts()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        Assert.Empty(Evaluate(movement, []).VisibleShipIds);
        Assert.Empty(Evaluate(movement, [Input(1, 2, ulong.MaxValue)]).VisibleShipIds);
    }

    [Fact]
    public void SourceOwnershipChangesUseFreshInputsWithoutRememberingHiddenShips()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(1));
        Assert.Contains(new ShipId(2), Evaluate(movement, [Input(1, 1, 1), Input(2, 2, 0)]).VisibleShipIds);
        Assert.Empty(Evaluate(movement, [Input(1, 2, 1), Input(2, 2, 0)]).VisibleShipIds);
    }

    [Fact]
    public void AnalyticManeuversRetainExactGeometryAcrossInsertedAndOriginalBoundaries()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(0));
        movement.Add(new ShipId(3), Position(10));
        ExecutableBoundedTerminalManeuverPlan plan = BoundedTerminalManeuverPlanner.Select(
            SimulationTime.Zero, new ShipKinematicState(Position(0), ShipVelocity.Zero, ShipHeading.Zero), Position(10), null,
            new ManeuverAcceleration(1000), new ManeuverAcceleration(1000), new ManeuverSpeed(30_000),
            new ManeuverTurnRate(45_000)).ExecutablePlan!.WithAdditionalBoundaries([new SimulationTime(500)]);
        var commit = movement.CommitStartTerminalManeuver(new ShipId(1), plan, ManeuverObjective.FastestArrival,
            SimulationTime.Zero, static value => value);
        ShipSensorCoverageInput[] inputs = [Input(1, 1, 0), Input(2, 2, 0), Input(3, 2, 0)];
        Assert.Equal(ScheduledEventDisposition.Applied, movement.HandleEvent(commit.EventProposals[0].Payload,
            commit.Maneuver.Generation, new SimulationTime(500)));
        Assert.Equal<ShipId>([new(1)], Evaluate(movement, inputs, new SimulationTime(500)).VisibleShipIds);
        foreach (var proposal in commit.EventProposals.Skip(1))
            movement.HandleEvent(proposal.Payload, commit.Maneuver.Generation, proposal.Timestamp);
        Assert.Equal<ShipId>([new(1), new(3)], Evaluate(movement, inputs, plan.EndsAt).VisibleShipIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ControlledIntervalsDoNotUseRoundedPositionsForSourceOrTarget(bool controlledSource)
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        movement.Add(new ShipId(2), Position(0));
        ShipId controlled = new(controlledSource ? 1UL : 2UL);
        var interval = new AnalyticManeuverSegment(SimulationTime.Zero, new SimulationTime(1000),
            new ShipKinematicState(Position(0), ShipVelocity.Zero, ShipHeading.Zero),
            new ShipAcceleration(2_000, 0), ShipAngularRate.Zero);
        Assert.True(movement.TryCommitInteractionMotion(controlled, new InteractionMotionOwner("coverage-test", 1),
            interval, movement.GetSpatialRevision(controlled)!.Value, SimulationTime.Zero));
        var now = new SimulationTime(500);
        Assert.Equal(Position(0), movement.CaptureInteractionReadView(now).Get(controlled)!.Spatial.Position);
        Assert.Equal<ShipId>([new(1)], Evaluate(movement,
            [Input(1, 1, 0), Input(2, 2, 0)], now).VisibleShipIds);
    }

    [Fact]
    public void ScopePermutationsBatchesAndIndexesMatchIndependentReferenceQueries()
    {
        var movement = new SpatialMovement();
        var inputs = new List<ShipSensorCoverageInput>();
        for (ulong id = 1; id <= 48; id++)
        {
            long x = (long)(id * 7 % 23) - 11;
            ulong system = id % 3 + 1;
            movement.Add(new ShipId(id), Position(x, (long)(id % 5) - 2, system));
            inputs.Add(new ShipSensorCoverageInput(new EntityId(1000 - id), new ShipId(id),
                new PrincipalId(id % 4 == 0 ? 1UL : 2UL), id % 9));
        }
        InteractionSpatialReadView view = movement.CaptureInteractionReadView(SimulationTime.Zero);
        ShipId[] owned = inputs.Where(input => input.PrincipalId == new PrincipalId(1))
            .Select(input => input.ShipId).OrderBy(id => id.Value).ToArray();
        ShipId[] expected = inputs.Where(input => input.PrincipalId == new PrincipalId(1))
            .SelectMany(input => InteractionProximityQuery.Find(view, input.ShipId,
                inputs.Select(target => target.ShipId), input.RadiusUnits))
            .Union(owned).OrderBy(id => id.Value).ToArray();
        InteractionPreparationStrategy[] strategies = [InteractionPreparationStrategy.Scan,
            InteractionPreparationStrategy.SparseGrid(1, 1), InteractionPreparationStrategy.SparseGrid(7, 100),
            InteractionPreparationStrategy.BoundingBoxHierarchy(1), InteractionPreparationStrategy.BoundingBoxHierarchy(8)];
        foreach (InteractionPreparationStrategy strategy in strategies)
        foreach (int batchSize in new[] { 1, 3, 8, 100 })
        foreach (IEnumerable<ShipSensorCoverageInput> order in new[] { inputs, inputs.AsEnumerable().Reverse() })
        {
            ShipSensorCoverageSnapshot result = ShipSensorCoverage.Evaluate(view, new PrincipalId(1), order, strategy, batchSize);
            Assert.Equal(expected, result.CoveredShipIds);
            Assert.Equal(expected, result.VisibleShipIds);
            Assert.Equal(owned, result.OwnedShipIds);
        }
    }

    [Fact]
    public void InvalidOrInconsistentScopeIsRejectedBeforeReturningEvidence()
    {
        var movement = new SpatialMovement();
        movement.Add(new ShipId(1), Position(0));
        InteractionSpatialReadView view = movement.CaptureInteractionReadView(SimulationTime.Zero);
        var principal = new PrincipalId(1);
        ShipSensorCoverageInput valid = Input(1, 1, 0);
        InteractionPreparationStrategy strategy = InteractionPreparationStrategy.Scan;
        Assert.Throws<ArgumentNullException>(() => ShipSensorCoverage.Evaluate(null!, principal, [valid], strategy, 1));
        Assert.Throws<ArgumentNullException>(() => ShipSensorCoverage.Evaluate(view, principal, null!, strategy, 1));
        Assert.Throws<ArgumentNullException>(() => ShipSensorCoverage.Evaluate(view, principal, [valid], null!, 1));
        Assert.Throws<ArgumentNullException>(() => ShipSensorCoverage.Evaluate(view, principal, [null!], strategy, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShipSensorCoverage.Evaluate(view, default, [valid], strategy, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShipSensorCoverage.Evaluate(view, principal, [valid], strategy, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShipSensorCoverage.Evaluate(view, principal, [valid], strategy, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShipSensorCoverage.Evaluate(view, principal,
            [valid with { EntityId = default }], strategy, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShipSensorCoverage.Evaluate(view, principal,
            [valid with { ShipId = default }], strategy, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShipSensorCoverage.Evaluate(view, principal,
            [valid with { PrincipalId = default }], strategy, 1));
        Assert.Throws<ArgumentException>(() => ShipSensorCoverage.Evaluate(view, principal, [valid, valid], strategy, 1));
        Assert.Throws<ArgumentException>(() => ShipSensorCoverage.Evaluate(view, principal,
            [valid, valid with { EntityId = new EntityId(200) }], strategy, 1));
        movement.Add(new ShipId(2), Position(0));
        view = movement.CaptureInteractionReadView(SimulationTime.Zero);
        Assert.Throws<ArgumentException>(() => ShipSensorCoverage.Evaluate(view, principal,
            [valid, valid with { ShipId = new ShipId(2) }], strategy, 1));
        Assert.Throws<ArgumentException>(() => ShipSensorCoverage.Evaluate(view, principal, [Input(99, 1, 0)], strategy, 1));
    }

    private static ShipSensorCoverageSnapshot Evaluate(SpatialMovement movement,
        IEnumerable<ShipSensorCoverageInput> inputs, SimulationTime? now = null) =>
        ShipSensorCoverage.Evaluate(movement.CaptureInteractionReadView(now ?? SimulationTime.Zero),
            new PrincipalId(1), inputs, InteractionPreparationStrategy.Scan, 1);

    private static ShipSensorCoverageInput Input(ulong ship, ulong principal, ulong radius) =>
        new(new EntityId(ship + 100), new ShipId(ship), new PrincipalId(principal), radius);

    private static SystemPosition Position(long x, long y = 0, ulong system = 1) =>
        new(new SystemId(system), new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(y)));

    private static void StartLinear(SpatialMovement movement, ulong ship, long from, long to, ulong duration) =>
        movement.CommitStartOrReplace(new ShipId(ship), new TravelLeg.Local(Position(from), Position(to),
            new SimulationDuration(duration)), SimulationTime.Zero, static value => value);
}
