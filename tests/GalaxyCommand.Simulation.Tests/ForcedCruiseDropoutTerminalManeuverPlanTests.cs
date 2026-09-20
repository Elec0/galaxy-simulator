using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class ForcedCruiseDropoutTerminalManeuverPlanTests
{
    [Fact]
    public void CruiseVelocityBrakesAtTwicePrimaryThenContinuesFromSubCruiseCap()
    {
        bool created = ForcedCruiseDropoutTerminalManeuverPlan.TryCreate(
            SimulationTime.Zero,
            new ShipKinematicState(
                Position(0),
                new ShipVelocity(1_000_000, 0),
                ShipHeading.Zero),
            Position(1_000_000),
            requestedHeading: null,
            Capability(),
            ManeuverObjective.FastestArrival,
            out ForcedCruiseDropoutTerminalManeuverPlan? candidate);

        Assert.True(created);
        ForcedCruiseDropoutTerminalManeuverPlan plan =
            Assert.IsType<ForcedCruiseDropoutTerminalManeuverPlan>(candidate);
        Assert.Equal(new ManeuverAcceleration(20_000), plan.DropoutPhase.Deceleration);
        Assert.Equal(new SimulationTime(35_000), plan.DropoutEndsAt);
        Assert.Equal(BoundedTerminalPlanKind.CruiseTerminal, plan.ContinuationPlan.Kind);
        ShipKinematicState atCap = plan.StateAt(plan.DropoutEndsAt);
        Assert.Equal(Position(22_750), atCap.Position);
        Assert.Equal(new ShipVelocity(300_000, 0), atCap.Velocity);
        Assert.Equal(atCap, plan.ContinuationPlan.StateAt(plan.DropoutEndsAt));
        Assert.True(ManeuverArrival.EvaluateTerminal(
            plan.StateAt(plan.EndsAt),
            Position(1_000_000),
            requestedHeading: null).IsSatisfied);
    }

    [Fact]
    public void EqualCruiseAndSubCruiseCapsHaveNoForcedDropoutPhase()
    {
        EffectiveShipManeuverCapability capability =
            new ShipManeuverCapability(
                baseMassKilograms: 10_000,
                new ManeuverAcceleration(10_000),
                customPassiveDeceleration: null,
                new ManeuverSpeed(300_000),
                new ManeuverSpeed(300_000),
                new ManeuverTurnRate(45_000),
                new SimulationDuration(10_000))
            .ResolveForMass(10_000);

        bool created = ForcedCruiseDropoutTerminalManeuverPlan.TryCreate(
            SimulationTime.Zero,
            new ShipKinematicState(
                Position(0),
                new ShipVelocity(300_000, 0),
                ShipHeading.Zero),
            Position(1_000_000),
            requestedHeading: null,
            capability,
            ManeuverObjective.FastestArrival,
            out ForcedCruiseDropoutTerminalManeuverPlan? plan);

        Assert.False(created);
        Assert.Null(plan);
    }

    private static EffectiveShipManeuverCapability Capability() =>
        GameSessionTestFixture.ManeuverCapability.ResolveForMass(10_000);

    private static SystemPosition Position(long x) =>
        GameSessionTestFixture.Position(x, 0);
}
