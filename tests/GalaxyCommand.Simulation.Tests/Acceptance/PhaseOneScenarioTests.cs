using GalaxyCommand.Simulation;
using GalaxyCommand.Simulation.Acceptance;

namespace GalaxyCommand.Simulation.Tests;

public sealed class PhaseOneScenarioTests
{
    [Fact]
    public void AcceptanceFacadeDoesNotExposeMutableWorld()
    {
        Assert.Null(typeof(PhaseOneScenario).GetProperty("World"));
    }

    [Fact]
    public void ApprovedPocChainConstructsAPersistentShip()
    {
        var scenario = new PhaseOneScenario();

        PhaseOneReport report = scenario.RunUntilFirstShip(new SimulationTime(1_000_000));

        Assert.NotNull(report.ConstructedShipId);
        Assert.Equal(2, report.StartingShipCount);
        Assert.Equal(3, report.EndingShipCount);
        Assert.Equal<ulong>(675_400, report.EndTime.Milliseconds);
        Assert.Equal<ulong>(45, report.EventsProcessed);
        Assert.Equal<ulong>(0x175eac5bd99a0695, report.EventLogDigest);
        Assert.Equal<ulong>(0x424bec2061b0e8f9, report.FinalStateDigest);
        Assert.True(report.Metrics.TransportJobsCreated >= report.Metrics.TransportJobsCompleted);
        Assert.True(report.Metrics.TransportJobsCompleted > 0);
        Assert.Equal<ulong>(0, report.Metrics.TransportJobsFailed);
        Assert.Equal(4, report.Metrics.FacilityTime.Count);
        Assert.All(report.Metrics.MaterialProduced.Values, quantity =>
            Assert.True(quantity > Quantity.Zero));
        Assert.NotEmpty(scenario.EventRecords);
        Assert.All(scenario.EventRecords, record =>
            Assert.Equal(ScheduledEventDisposition.Applied, record.Disposition));
        Assert.Contains(scenario.EventRecords, record =>
            record.Kind is ScenarioEventKind.ConstructionComplete);
        Assert.Contains(scenario.DecisionRecords, record =>
            record.Reason == DecisionReason.HighestRankedReachableTransport);
    }

    [Fact]
    public void IdenticalRunsProduceIdenticalEventAndStateDigests()
    {
        PhaseOneReport Run()
        {
            var scenario = new PhaseOneScenario();
            return scenario.RunUntilFirstShip(new SimulationTime(1_000_000));
        }

        PhaseOneReport first = Run();
        PhaseOneReport second = Run();

        Assert.Equal(first.EventLogDigest, second.EventLogDigest);
        Assert.Equal(first.FinalStateDigest, second.FinalStateDigest);
        Assert.Equal(first.Metrics.TransportJobsCreated, second.Metrics.TransportJobsCreated);
        Assert.Equal(first.Metrics.MaterialProduced, second.Metrics.MaterialProduced);
    }

    [Fact]
    public void ShipDesignCapacityContributesToFinalStateDigest()
    {
        var baseline = new PhaseOneScenario(
            new PhaseOneConfig { FreighterCargoCapacity = new Quantity(10) });
        var changed = new PhaseOneScenario(
            new PhaseOneConfig { FreighterCargoCapacity = new Quantity(11) });

        PhaseOneReport baselineReport =
            baseline.RunUntilFirstShip(new SimulationTime(1_000_000));
        PhaseOneReport changedReport =
            changed.RunUntilFirstShip(new SimulationTime(1_000_000));

        Assert.NotEqual(baselineReport.FinalStateDigest, changedReport.FinalStateDigest);
    }
}
