using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class AnalyticSweptRangeQueryTests
{
    [Fact]
    public void AccelerationUsesTheCurveRatherThanAnEndpointChord()
    {
        AnalyticManeuverSegment motion = Segment(0, 0, 2_000, 0, 10_000);
        SweptRangeContact contact = Assert.IsType<SweptRangeContact>(AnalyticSweptRangeQuery.FindFirstContact(
            motion, Position(25), SimulationTime.Zero, new SimulationTime(10_000), 0));
        Assert.Equal(new SimulationTime(5_000), contact.Timestamp);
        Assert.True(contact.InRangeAtTimestamp);
        Assert.False(contact.AlreadyInRange);
    }

    [Fact]
    public void BriefAcceleratedCrossingRetainsEvidenceAfterExit()
    {
        AnalyticManeuverSegment motion = Segment(0, 0, 200_000_000_000, 0, 5);
        SweptRangeContact contact = Assert.IsType<SweptRangeContact>(AnalyticSweptRangeQuery.FindFirstContact(
            motion, Position(2), SimulationTime.Zero, new SimulationTime(5), 1));
        Assert.Equal(new SimulationTime(1), contact.Timestamp);
        Assert.False(contact.InRangeAtTimestamp);
    }

    [Fact]
    public void RelativeCurvesHandleDifferentPhaseStartsAndClipOverlap()
    {
        AnalyticManeuverSegment first = Segment(0, 1_000, 0, 0, 10_000);
        AnalyticManeuverSegment second = Segment(8, -1_000, 0, 2_000, 6_000);
        SweptRangeContact contact = Assert.IsType<SweptRangeContact>(AnalyticSweptRangeQuery.FindFirstContact(
            first, second, SimulationTime.Zero, new SimulationTime(10_000), 0));
        Assert.Equal(new SimulationTime(5_000), contact.Timestamp);
        Assert.True(contact.InRangeAtTimestamp);
        Assert.Null(AnalyticSweptRangeQuery.FindFirstContact(first, second,
            SimulationTime.Zero, new SimulationTime(1_000), 0));
    }

    [Fact]
    public void PublishedRoundingDoesNotCreateEarlyContact()
    {
        AnalyticManeuverSegment motion = Segment(0, 1_000, 0, 0, 2_000);
        Assert.Equal(Position(1), motion.StateAt(new SimulationTime(500)).Position);
        SweptRangeContact contact = Assert.IsType<SweptRangeContact>(AnalyticSweptRangeQuery.FindFirstContact(
            motion, Position(1), SimulationTime.Zero, new SimulationTime(2_000), 0));
        Assert.Equal(new SimulationTime(1_000), contact.Timestamp);
    }

    [Fact]
    public void TwoDimensionalTangencyIsInclusive()
    {
        var start = new ShipKinematicState(new SystemPosition(new SystemId(1),
            new SpatialPosition(new SpatialCoordinate(-2), new SpatialCoordinate(1))),
            new ShipVelocity(1_000, 0), ShipHeading.Zero);
        var motion = new AnalyticManeuverSegment(SimulationTime.Zero, new SimulationTime(4_000),
            start, ShipAcceleration.Zero, ShipAngularRate.Zero);
        SweptRangeContact contact = Assert.IsType<SweptRangeContact>(AnalyticSweptRangeQuery.FindFirstContact(
            motion, Position(0), SimulationTime.Zero, new SimulationTime(4_000), 1));
        Assert.Equal(new SimulationTime(2_000), contact.Timestamp);
    }

    [Fact]
    public void DifferentSystemsAndNonOverlappingPhasesHaveNoLocalContact()
    {
        AnalyticManeuverSegment first = Segment(0, 0, 0, 0, 10);
        AnalyticManeuverSegment second = Segment(0, 0, 0, 20, 30);
        Assert.Null(AnalyticSweptRangeQuery.FindFirstContact(first, second, SimulationTime.Zero, new SimulationTime(30), 1));
        Assert.Null(AnalyticSweptRangeQuery.FindFirstContact(first,
            new SystemPosition(new SystemId(2), Position(0).Position), SimulationTime.Zero, new SimulationTime(10), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AnalyticSweptRangeQuery.FindFirstContact(
            first, Position(0), new SimulationTime(10), SimulationTime.Zero, 0));
    }

    private static AnalyticManeuverSegment Segment(long x, long velocity, long acceleration, ulong start, ulong end) =>
        new(new SimulationTime(start), new SimulationTime(end),
            new ShipKinematicState(Position(x), new ShipVelocity(velocity, 0), ShipHeading.Zero),
            new ShipAcceleration(acceleration, 0), ShipAngularRate.Zero);

    private static SystemPosition Position(long x) => new(new SystemId(1),
        new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(0)));
}
