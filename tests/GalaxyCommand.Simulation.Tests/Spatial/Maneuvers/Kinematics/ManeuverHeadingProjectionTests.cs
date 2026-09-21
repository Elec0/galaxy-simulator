using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ManeuverHeadingProjectionTests
{
    [Theory]
    [InlineData(0, 10_000, 0)]
    [InlineData(90_000, 0, -10_000)]
    [InlineData(180_000, -10_000, 0)]
    [InlineData(270_000, 0, 10_000)]
    public void CardinalHeadingsProjectExactForwardAcceleration(
        uint heading,
        long expectedX,
        long expectedY)
    {
        ShipAcceleration projected = ManeuverHeadingProjection.ProjectForward(
            new ShipHeading(heading),
            new ManeuverAcceleration(10_000));

        Assert.Equal(new ShipAcceleration(expectedX, expectedY), projected);
    }

    [Theory]
    [InlineData(45_000, 7_071, -7_071)]
    [InlineData(135_000, -7_071, -7_071)]
    [InlineData(225_000, -7_071, 7_071)]
    [InlineData(315_000, 7_071, 7_071)]
    public void DiagonalHeadingsUseClockwiseWorldOrientation(
        uint heading,
        long expectedX,
        long expectedY)
    {
        ShipAcceleration projected = ManeuverHeadingProjection.ProjectForward(
            new ShipHeading(heading),
            new ManeuverAcceleration(10_000));

        Assert.Equal(new ShipAcceleration(expectedX, expectedY), projected);
    }

    [Fact]
    public void ArbitraryMillidegreeHeadingRoundsPublishedComponentsNormally()
    {
        ShipAcceleration projected = ManeuverHeadingProjection.ProjectForward(
            new ShipHeading(30_000),
            new ManeuverAcceleration(10_000));

        Assert.Equal(new ShipAcceleration(8_660, -5_000), projected);
    }

    [Fact]
    public void ProjectionRejectsAComponentOutsideSignedAccelerationRange()
    {
        Assert.Throws<OverflowException>(() =>
            ManeuverHeadingProjection.ProjectForward(
                ShipHeading.Zero,
                new ManeuverAcceleration((ulong)long.MaxValue + 1)));
    }

    [Theory]
    [InlineData(0, 5_200, -800)]
    [InlineData(90_000, -800, -5_200)]
    public void CompleteAllocationProjectsPositiveLateralClockwise(
        uint heading,
        long expectedX,
        long expectedY)
    {
        bool projected = TryProject(
            new ShipHeading(heading),
            new ShipRelativeThrustAllocation(5_000, 200, 800),
            out ShipAcceleration acceleration);

        Assert.True(projected);
        Assert.Equal(new ShipAcceleration(expectedX, expectedY), acceleration);
    }

    [Fact]
    public void CombinedAllocationUsesCordicBasisAndNormalRounding()
    {
        bool projected = TryProject(
            new ShipHeading(45_000),
            new ShipRelativeThrustAllocation(6_000, 0, 800),
            out ShipAcceleration acceleration);

        Assert.True(projected);
        Assert.Equal(new ShipAcceleration(3_677, -4_808), acceleration);
    }

    [Fact]
    public void ReversePrecisionProjectsOppositeForwardHeading()
    {
        bool projected = TryProject(
            new ShipHeading(90_000),
            new ShipRelativeThrustAllocation(0, -1_000, 0),
            out ShipAcceleration acceleration);

        Assert.True(projected);
        Assert.Equal(new ShipAcceleration(0, 1_000), acceleration);
    }

    [Fact]
    public void AllocationOutsideEnvelopeIsNotProjected()
    {
        bool projected = TryProject(
            ShipHeading.Zero,
            new ShipRelativeThrustAllocation(0, 1_001, 0),
            out ShipAcceleration acceleration);

        Assert.False(projected);
        Assert.Equal(ShipAcceleration.Zero, acceleration);
    }

    [Fact]
    public void UnrepresentableWorldComponentIsNotProjected()
    {
        var limit = new ManeuverAcceleration(ulong.MaxValue);

        bool projected = ManeuverHeadingProjection.TryProject(
            ShipHeading.Zero,
            new ShipRelativeThrustAllocation(ulong.MaxValue, 0, 0),
            limit,
            limit,
            out ShipAcceleration acceleration);

        Assert.False(projected);
        Assert.Equal(ShipAcceleration.Zero, acceleration);
    }

    [Theory]
    [InlineData(10, 0, 0)]
    [InlineData(0, -10, 90_000)]
    [InlineData(-10, 0, 180_000)]
    [InlineData(0, 10, 270_000)]
    public void CardinalDisplacementsResolveExactCourseHeading(
        long displacementX,
        long displacementY,
        uint expectedHeading)
    {
        ShipHeading heading = ManeuverHeadingProjection.ResolveCourseHeading(
            displacementX,
            displacementY);

        Assert.Equal(new ShipHeading(expectedHeading), heading);
    }

    [Theory]
    [InlineData(10, -10, 45_000)]
    [InlineData(-10, -10, 135_000)]
    [InlineData(-10, 10, 225_000)]
    [InlineData(10, 10, 315_000)]
    public void DiagonalDisplacementsResolveClockwiseCourseHeading(
        long displacementX,
        long displacementY,
        uint expectedHeading)
    {
        ShipHeading heading = ManeuverHeadingProjection.ResolveCourseHeading(
            displacementX,
            displacementY);

        Assert.Equal(new ShipHeading(expectedHeading), heading);
    }

    [Theory]
    [InlineData(4, -3, 36_870)]
    [InlineData(-4, -3, 143_130)]
    [InlineData(-4, 3, 216_870)]
    [InlineData(4, 3, 323_130)]
    public void ArbitraryDisplacementRoundsCourseNormallyAcrossQuadrants(
        long displacementX,
        long displacementY,
        uint expectedHeading)
    {
        ShipHeading heading = ManeuverHeadingProjection.ResolveCourseHeading(
            displacementX,
            displacementY);

        Assert.Equal(new ShipHeading(expectedHeading), heading);
    }

    [Fact]
    public void CourseResolutionSupportsMinimumSignedDisplacement()
    {
        Assert.Equal(
            new ShipHeading(180_000),
            ManeuverHeadingProjection.ResolveCourseHeading(long.MinValue, 0));
        Assert.Equal(
            new ShipHeading(90_000),
            ManeuverHeadingProjection.ResolveCourseHeading(0, long.MinValue));
    }

    [Fact]
    public void ZeroDisplacementHasNoCourseHeading()
    {
        Assert.Throws<ArgumentException>(() =>
            ManeuverHeadingProjection.ResolveCourseHeading(0, 0));
    }

    private static bool TryProject(
        ShipHeading heading,
        ShipRelativeThrustAllocation allocation,
        out ShipAcceleration acceleration) =>
        ManeuverHeadingProjection.TryProject(
            heading,
            allocation,
            new ManeuverAcceleration(10_000),
            new ManeuverAcceleration(1_000),
            out acceleration);
}
