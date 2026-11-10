using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class EntityRemovalCommitmentTests
{
    [Theory]
    [InlineData("material")]
    [InlineData("capacity")]
    [InlineData("physical")]
    public void EconomyPresenceCannotAuthorizeUnpreparedCargoCommitments(string commitment)
    {
        var (lifecycle, economy, cargo) = Create();
        var owner = new ReservationOwner.TransportJob(new(99));
        switch (commitment)
        {
            case "material":
                cargo.Add(new(1), new(2));
                cargo.Reserve(new(1), new(1), new(1), owner);
                break;
            case "capacity":
                cargo.ReserveCapacity(new(1), new(1), owner);
                break;
            case "physical":
                Assert.True(cargo.ReservePhysical(new(1),
                    new PhysicalReservationSubject.IncomingCapacity(new(1)), owner).IsAccepted);
                break;
        }
        InventoryCheckpoint before = cargo.CaptureCheckpoint();

        Assert.False(economy.TryPrepareEntityRemoval(
            GameSessionTestFixture.Ship, cargo.Id, out var prepared));

        Assert.Null(prepared);
        InventoryCheckpoint after = cargo.CaptureCheckpoint();
        Assert.Equal(before.Reservations, after.Reservations);
        Assert.Equal(before.CapacityReservations, after.CapacityReservations);
        Assert.Equal(before.PhysicalReservations, after.PhysicalReservations);
        Assert.Equal(GameSessionTestFixture.Ship, lifecycle.Entities.GetShipId(GameSessionTestFixture.Entity));
    }

    [Theory]
    [InlineData("material")]
    [InlineData("capacity")]
    [InlineData("physical")]
    public void ReleaseCoverageRejectsDuplicateAndReusedReservationClaims(string kind)
    {
        var cargo = new Inventory(new(1), new InventoryCustody.PrincipalControlled(
            new InventoryOwnerReference.SessionEntity(new(1)), new(1)), new(20));
        cargo.Add(new(1), new(5));
        var owner = new ReservationOwner.TransportJob(new(1));
        InventoryCommitmentRelease release = kind switch
        {
            "material" => new InventoryCommitmentRelease.Material(cargo, cargo.Reserve(new(1), new(1), new(1), owner)),
            "capacity" => new InventoryCommitmentRelease.Capacity(cargo, cargo.ReserveCapacity(new(1), new(1), owner)),
            _ => new InventoryCommitmentRelease.Physical(cargo,
                cargo.ReservePhysical(new(1), new PhysicalReservationSubject.IncomingCapacity(new(1)), owner).Reservation!),
        };
        Assert.True(cargo.HasExactReleaseCoverage([release]));
        Assert.False(cargo.HasExactReleaseCoverage([release, release]));
        switch (kind)
        {
            case "material":
                cargo.Release(new(1));
                cargo.Reserve(new(1), new(1), new(1), owner);
                break;
            case "capacity":
                cargo.ReleaseCapacity(new(1));
                cargo.ReserveCapacity(new(1), new(1), owner);
                break;
            default:
                cargo.ReleasePhysicalReservation(new(1), owner);
                cargo.ReservePhysical(new(1), new PhysicalReservationSubject.IncomingCapacity(new(1)), owner);
                break;
        }
        Assert.False(release.CanCommit());
        Assert.False(cargo.HasExactReleaseCoverage([release]));
    }

    [Fact]
    public void OnePreparedReleaseCannotCoverAnAdditionalWorkflowCommitment()
    {
        var cargo = new Inventory(new(1), new(20));
        cargo.Add(new(1), new(5));
        Reservation reservation = cargo.Reserve(new(1), new(1), new(1), new ReservationOwner.TransportJob(new(1)));
        var release = new InventoryCommitmentRelease.Material(cargo, reservation);
        cargo.ReserveCapacity(new(1), new(1), new ReservationOwner.ConstructionOrder(new(1)));
        Assert.True(release.CanCommit());
        Assert.False(cargo.HasExactReleaseCoverage([release]));
        Assert.Equal(new Quantity(1), cargo.ReservedCapacity);
        Assert.Same(reservation, cargo.GetReservation(reservation.Id));
    }

    [Fact]
    public void EconomyProofCannotClaimAReleaseOutsideItsPreparedCancellations()
    {
        var (_, economy, cargo) = Create();
        Assert.True(economy.TryPrepareEntityRemoval(GameSessionTestFixture.Ship, cargo.Id, out var prepared));
        CapacityReservation reservation = cargo.ReserveCapacity(new(1), new(1), new ReservationOwner.TransportJob(new(99)));
        PreparedEconomyEntityRemoval forged = prepared! with
        {
            Releases = new InventoryCommitmentRelease[] { new InventoryCommitmentRelease.Capacity(cargo, reservation) },
        };
        Assert.False(economy.CanCommitEntityRemoval(forged));
        Assert.Same(reservation, cargo.GetCapacityReservation(reservation.Id));
    }

    [Fact]
    public void LifecycleRequiresExactLiveProofBeforeRemovalAndStillRequiresActualReleaseAtApply()
    {
        var (lifecycle, _, cargo) = Create();
        var request = new EntityRemovalRequest(GameSessionTestFixture.Entity,
            EntityRemovalReason.Despawned, EntityCargoDisposition.DiscardCargo);
        cargo.Add(new(1), new(2));
        Reservation reservation = cargo.Reserve(new(1), new(1), new(1), new ReservationOwner.TransportJob(new(1)));
        var releases = new InventoryCommitmentRelease[] { new InventoryCommitmentRelease.Material(cargo, reservation) };
        EntityRemovalResult.Rejected rejected = Assert.IsType<EntityRemovalResult.Rejected>(
            Assert.IsType<EntityRemovalPreparation.Resolved>(lifecycle.PrepareRemoval(request)).Value);
        Assert.Equal(EntityRemovalRejectionReason.CargoHasCommitments, rejected.Reason);
        PreparedEntityRemoval removal = Assert.IsType<EntityRemovalPreparation.Prepared>(lifecycle.PrepareRemoval(request, releases)).Value;
        Assert.True(lifecycle.CanCommitRemoval(removal, releases));
        Assert.Throws<InvalidOperationException>(() => lifecycle.ApplyRemoval(removal, SimulationTime.Zero));
        Assert.Equal(GameSessionTestFixture.Ship, lifecycle.Entities.GetShipId(request.EntityId));
        cargo.Release(reservation.Id);
        Assert.False(lifecycle.CanCommitRemoval(removal, releases));
        Assert.IsType<EntityRemovalResult.Removed>(lifecycle.ApplyRemoval(removal, SimulationTime.Zero));
        Assert.Null(lifecycle.Entities.GetShipId(request.EntityId));
    }

    private static (EntityLifecycleOwner Lifecycle, SessionEconomyOwner Economy, Inventory Cargo) Create()
    {
        var lifecycle = new EntityLifecycleOwner(
            new SpatialMovement(), new ActorControlRegistry(), new ShipOrderCoordinator(), [],
            shipDefinitions: [GameSessionTestFixture.Design]);
        lifecycle.RegisterSetup([new InitialShipSetup(
            GameSessionTestFixture.Entity, GameSessionTestFixture.Ship,
            GameSessionTestFixture.CargoInventory, GameSessionTestFixture.Principal,
            GameSessionTestFixture.Design, GameSessionTestFixture.Position(0, 0),
            GameSessionTestFixture.PlayerController)]);
        var economy = new SessionEconomyOwner(new GameSessionEconomySetup(
            [], [], [], [], [], [], [], new UnreachableNavigation(),
            new TransportTiming(SimulationDuration.Zero, new TransferRate(1), new TransferRate(1))), lifecycle);
        Inventory cargo = lifecycle.GetRequiredCargo(GameSessionTestFixture.Ship);
        return (lifecycle, economy, cargo);
    }

    private sealed class UnreachableNavigation : ILogisticsNavigation
    {
        public LogisticsTravelEstimate? Estimate(ShipId actorId, LocationId origin,
            LocationId destination, SimulationTime plannedAt) => null;
    }
}
