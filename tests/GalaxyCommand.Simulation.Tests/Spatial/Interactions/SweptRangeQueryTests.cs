using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class SweptRangeQueryTests
{
    [Theory]
    [InlineData(-10, 10, 20, 3, 7, true, false)]
    [InlineData(-10, 10, 3, 1, 2, false, false)]
    [InlineData(-1, 10, 10, 2, 0, true, true)]
    [InlineData(-10, -2, 10, 2, 10, true, false)]
    public void StationaryContactRoundsEntryUpAndRetainsBriefCrossings(
        long originX, long destinationX, ulong duration, ulong range,
        ulong expectedTime, bool inRange, bool alreadyInRange)
    {
        LocalMotionSegment motion = Motion(originX, 0, destinationX, 0, 0, duration);

        SweptRangeContact? contact = SweptRangeQuery.FindFirstContact(
            motion, Position(0, 0), new SimulationTime(0),
            new SimulationTime(duration), range);

        Assert.NotNull(contact);
        Assert.Equal(new SimulationTime(expectedTime), contact.Timestamp);
        Assert.Equal(inRange, contact.InRangeAtTimestamp);
        Assert.Equal(alreadyInRange, contact.AlreadyInRange);
    }

    [Fact]
    public void MovingParticipantsUseBothExactTrajectories()
    {
        LocalMotionSegment first = Motion(-10, 0, 10, 0, 0, 20);
        LocalMotionSegment second = Motion(10, 0, -10, 0, 0, 20);

        SweptRangeContact? contact = SweptRangeQuery.FindFirstContact(
            first, second, SimulationTime.Zero, new SimulationTime(20), 3);

        Assert.NotNull(contact);
        Assert.Equal(new SimulationTime(9), contact.Timestamp);
        Assert.True(contact.InRangeAtTimestamp);
        Assert.False(contact.AlreadyInRange);
    }

    [Fact]
    public void TangentBetweenMillisecondsIsRetained()
    {
        LocalMotionSegment motion = Motion(-5, 3, 5, 3, 0, 3);

        SweptRangeContact? contact = SweptRangeQuery.FindFirstContact(
            motion, Position(0, 0), SimulationTime.Zero, new SimulationTime(3), 3);

        Assert.NotNull(contact);
        Assert.Equal(new SimulationTime(2), contact.Timestamp);
        Assert.False(contact.InRangeAtTimestamp);
    }

    [Fact]
    public void DiagonalDistanceUsesExactIrrationalEntry()
    {
        SweptRangeContact? contact = SweptRangeQuery.FindFirstContact(
            Motion(-5, 2, 5, 2, 0, 10), Position(0, 0),
            SimulationTime.Zero, new SimulationTime(10), 3);

        Assert.NotNull(contact);
        Assert.Equal(new SimulationTime(3), contact.Timestamp);
        Assert.True(contact.InRangeAtTimestamp);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 6)]
    public void EntryArbitrarilyCloseToAnIntegerIsNeverReportedEarly(long y, ulong expectedTime)
    {
        const long radius = 4_000_000_000_000_000_000;
        SweptRangeContact? contact = SweptRangeQuery.FindFirstContact(
            Motion(-radius - 5, y, radius + 5, y, 0, 8_000_000_000_000_000_010),
            Position(0, 0), SimulationTime.Zero,
            new SimulationTime(8_000_000_000_000_000_010), (ulong)radius);

        Assert.NotNull(contact);
        Assert.Equal(new SimulationTime(expectedTime), contact.Timestamp);
        Assert.True(contact.InRangeAtTimestamp);
    }

    [Fact]
    public void SwappingParticipantsPreservesContactEvidence()
    {
        LocalMotionSegment first = Motion(-10, 2, 10, -2, 10, 30);
        LocalMotionSegment second = Motion(10, 0, -10, 0, 0, 40);

        SweptRangeContact? forward = SweptRangeQuery.FindFirstContact(
            first, second, SimulationTime.Zero, new SimulationTime(50), 3);
        SweptRangeContact? reverse = SweptRangeQuery.FindFirstContact(
            second, first, SimulationTime.Zero, new SimulationTime(50), 3);

        Assert.NotNull(forward);
        Assert.Equal(forward, reverse);
    }

    [Fact]
    public void SharedEndpointAndZeroLengthQueryHorizonAreInclusive()
    {
        SweptRangeContact? contact = SweptRangeQuery.FindFirstContact(
            Motion(-10, 0, 0, 0, 0, 10), Motion(0, 0, 10, 0, 10, 20),
            SimulationTime.Zero, new SimulationTime(20), 0);
        Assert.NotNull(contact);
        Assert.Equal(new SimulationTime(10), contact.Timestamp);
        Assert.True(contact.AlreadyInRange);
        Assert.Equal(contact, SweptRangeQuery.FindFirstContact(
            Motion(-10, 0, 0, 0, 0, 10), Position(0, 0),
            new SimulationTime(10), new SimulationTime(10), 0));
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void EqualVelocitiesRetainConstantSeparation(long separation, bool expectedContact)
    {
        SweptRangeContact? contact = SweptRangeQuery.FindFirstContact(
            Motion(0, 0, 10, 0, 0, 10), Motion(0, separation, 10, separation, 0, 10),
            SimulationTime.Zero, new SimulationTime(10), 3);

        Assert.Equal(expectedContact, contact is not null);
        if (contact is not null)
        {
            Assert.True(contact.AlreadyInRange);
            Assert.Equal(SimulationTime.Zero, contact.Timestamp);
        }
    }

    [Theory]
    [InlineData(-10, 4, 10, 4)]
    [InlineData(4, 0, 10, 0)]
    [InlineData(4, 0, 4, 0)]
    public void PathsOutsideRangeProduceNoContact(long x1, long y1, long x2, long y2)
    {
        Assert.Null(SweptRangeQuery.FindFirstContact(
            Motion(x1, y1, x2, y2, 0, 10), Position(0, 0),
            SimulationTime.Zero, new SimulationTime(10), 3));
    }

    [Fact]
    public void QueryClipsToKnownMotionAndCallerHorizon()
    {
        LocalMotionSegment motion = Motion(-10, 0, 10, 0, 10, 30);

        Assert.Null(SweptRangeQuery.FindFirstContact(
            motion, Position(0, 0), SimulationTime.Zero, new SimulationTime(18), 1));
        Assert.Null(SweptRangeQuery.FindFirstContact(
            motion, Position(0, 0), new SimulationTime(31), new SimulationTime(40), 100));
        SweptRangeContact? contact = SweptRangeQuery.FindFirstContact(
            motion, Position(0, 0), new SimulationTime(20), new SimulationTime(40), 1);
        Assert.NotNull(contact);
        Assert.Equal(new SimulationTime(20), contact.Timestamp);
        Assert.True(contact.AlreadyInRange);
    }

    [Fact]
    public void ZeroRangeFindsExactCoincidenceWithoutRoundingPositions()
    {
        Assert.Null(SweptRangeQuery.FindFirstContact(
            Motion(0, 0, 1, 0, 0, 10), Position(0, 0),
            new SimulationTime(1), new SimulationTime(9), 0));
        SweptRangeContact? contact = SweptRangeQuery.FindFirstContact(
            Motion(-1, 0, 1, 0, 0, 3), Position(0, 0),
            SimulationTime.Zero, new SimulationTime(3), 0);
        Assert.NotNull(contact);
        Assert.Equal(new SimulationTime(2), contact.Timestamp);
        Assert.False(contact.InRangeAtTimestamp);
    }

    [Fact]
    public void DifferentSystemsAndDisjointSegmentsCannotContact()
    {
        LocalMotionSegment motion = Motion(-1, 0, 1, 0, 0, 10);
        Assert.Null(SweptRangeQuery.FindFirstContact(
            motion, Position(0, 0, 2), SimulationTime.Zero, new SimulationTime(10), 100));
        Assert.Null(SweptRangeQuery.FindFirstContact(
            motion, Motion(-1, 0, 1, 0, 11, 20),
            SimulationTime.Zero, new SimulationTime(20), 100));
        Assert.Null(SweptRangeQuery.FindFirstContact(
            motion, Motion(-1, 0, 1, 0, 0, 10, 2),
            SimulationTime.Zero, new SimulationTime(10), 100));
    }

    [Fact]
    public void UnequalDurationsAndDepartureTimesRetainExactRelativeMotion()
    {
        SweptRangeContact? contact = SweptRangeQuery.FindFirstContact(
            Motion(-10, 0, 10, 0, 10, 30), Motion(10, 0, -10, 0, 0, 40),
            SimulationTime.Zero, new SimulationTime(50), 2);

        Assert.NotNull(contact);
        Assert.Equal(new SimulationTime(19), contact.Timestamp);
    }

    [Fact]
    public void FullCoordinateAndTimelineBoundsDoNotOverflow()
    {
        SweptRangeContact? contact = SweptRangeQuery.FindFirstContact(
            Motion(long.MinValue, 0, long.MaxValue, 0, 0, ulong.MaxValue),
            Position(0, 0), SimulationTime.Zero, new SimulationTime(ulong.MaxValue), 0);

        Assert.NotNull(contact);
        Assert.Equal(new SimulationTime(9_223_372_036_854_775_808), contact.Timestamp);
        Assert.True(contact.InRangeAtTimestamp);
        Assert.Null(SweptRangeQuery.FindFirstContact(
            Motion(long.MinValue, long.MinValue, long.MinValue, long.MinValue, 0, 1),
            Position(long.MaxValue, long.MaxValue), SimulationTime.Zero,
            new SimulationTime(1), ulong.MaxValue));
    }

    [Fact]
    public void ReversedHorizonIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SweptRangeQuery.FindFirstContact(
            Motion(-10, 0, 10, 0, 0, 20), Position(0, 0),
            new SimulationTime(2), new SimulationTime(1), 3));
    }

    private static LocalMotionSegment Motion(
        long x1, long y1, long x2, long y2, ulong departure, ulong arrival,
        ulong system = 1) => new(
            new MotionId(1), new EventGeneration(1),
            Position(x1, y1, system), Position(x2, y2, system),
            new SimulationTime(departure), new SimulationTime(arrival));

    private static SystemPosition Position(long x, long y, ulong system = 1) => new(
        new SystemId(system), new SpatialPosition(new SpatialCoordinate(x), new SpatialCoordinate(y)));
}
