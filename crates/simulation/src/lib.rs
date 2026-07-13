//! Headless simulation primitives for Galaxy Command.

mod event;
mod id;
mod inventory;
mod navigation;
mod production;
mod quantity;
mod scenario;
mod ship;
mod shipyard;
mod time;
mod transport;

pub use event::{
    EventAgenda, EventGeneration, EventKey, EventPhase, ScheduleError, ScheduledEvent,
};
pub use id::{
    CapacityReservationId, DemandRequestId, FacilityId, IdAllocationError, IdSequence, InventoryId,
    LocationId, MaterialId, OrganizationId, ProductionJobId, ReservationId, RouteId,
    ShipBlueprintId, ShipConstructionOrderId, ShipId, SupplyOfferId, TransportJobId,
};
pub use inventory::{
    CapacityReservation, Inventory, InventoryError, InventoryRegistry, Reservation,
    ReservationOwner,
};
pub use navigation::{DirectedRoute, Navigation, NavigationError, RouteGraph, RoutePlan};
pub use production::{
    ProductionError, ProductionIdSequences, ProductionJob, ProductionJobState, ProductionLine,
    Recipe, Throughput, Work,
};
pub use quantity::{Quantity, QuantityError};
pub use scenario::{
    DecisionReason, DecisionRecord, FacilityTimeMetrics, PhaseOneConfig, PhaseOneMetrics,
    PhaseOneReport, PhaseOneScenario, ScenarioError, ScenarioEventKind, ScenarioEventRecord,
    ShortageCause, ShortageRecord,
};
pub use ship::{Ship, ShipBlueprint, ShipError, ShipRegistry};
pub use shipyard::{
    ShipConstructionOrder, ShipConstructionOrderState, Shipyard, ShipyardError, ShipyardIdSequences,
};
pub use time::{SimulationDuration, SimulationTime, SimulationTimeError};
pub use transport::{
    DemandPriority, DemandRequest, Freighter, SupplyOffer, TransferRate, TransportBoard,
    TransportError, TransportEvent, TransportIdSequences, TransportJob, TransportJobState,
    TransportTiming, TravelTarget,
};

use std::error::Error;
use std::fmt::{self, Display, Formatter};

/// Result of advancing a simulation.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct RunReport {
    /// Simulation time before the run.
    pub start_time: SimulationTime,
    /// Simulation time after the run.
    pub end_time: SimulationTime,
    /// Number of scheduled events processed during the run.
    pub events_processed: u64,
}

/// Errors produced by simulation control operations.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum SimulationError {
    /// The requested target is earlier than the authoritative time.
    TargetPrecedesCurrentTime {
        /// Current authoritative time.
        current: SimulationTime,
        /// Requested target time.
        target: SimulationTime,
    },
}

impl Display for SimulationError {
    fn fmt(&self, formatter: &mut Formatter<'_>) -> fmt::Result {
        match self {
            Self::TargetPrecedesCurrentTime { current, target } => write!(
                formatter,
                "cannot run backward from {} ms to {} ms",
                current.as_millis(),
                target.as_millis()
            ),
        }
    }
}

impl Error for SimulationError {}

/// Authoritative state and control surface for the headless simulation.
#[derive(Debug, Default)]
pub struct Simulation {
    current_time: SimulationTime,
}

impl Simulation {
    /// Creates a simulation at time zero.
    #[must_use]
    pub const fn new() -> Self {
        Self {
            current_time: SimulationTime::ZERO,
        }
    }

    /// Returns the current authoritative simulation time.
    #[must_use]
    pub const fn current_time(&self) -> SimulationTime {
        self.current_time
    }

    /// Advances to an absolute simulation timestamp.
    ///
    /// Domain event processing will be added behind this boundary. The event
    /// agenda is tested independently until those event payloads are defined.
    ///
    /// # Errors
    ///
    /// Returns [`SimulationError::TargetPrecedesCurrentTime`] when `target` is
    /// earlier than the current authoritative simulation time.
    pub fn run_until(&mut self, target: SimulationTime) -> Result<RunReport, SimulationError> {
        let start_time = self.current_time;

        if target < start_time {
            return Err(SimulationError::TargetPrecedesCurrentTime {
                current: start_time,
                target,
            });
        }

        self.current_time = target;

        Ok(RunReport {
            start_time,
            end_time: target,
            events_processed: 0,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::{Simulation, SimulationError, SimulationTime};

    #[test]
    fn new_simulation_starts_at_zero() {
        let simulation = Simulation::new();

        assert_eq!(simulation.current_time(), SimulationTime::ZERO);
    }

    #[test]
    fn run_until_advances_authoritative_time() {
        let mut simulation = Simulation::new();

        let report = simulation
            .run_until(SimulationTime::from_millis(100))
            .expect("forward time advancement should succeed");

        assert_eq!(report.start_time, SimulationTime::ZERO);
        assert_eq!(report.end_time, SimulationTime::from_millis(100));
        assert_eq!(report.events_processed, 0);
        assert_eq!(simulation.current_time(), report.end_time);
    }

    #[test]
    fn run_until_rejects_backward_time_travel() {
        let mut simulation = Simulation::new();
        simulation
            .run_until(SimulationTime::from_millis(100))
            .expect("initial advancement should succeed");

        let error = simulation
            .run_until(SimulationTime::from_millis(99))
            .expect_err("backward time advancement should fail");

        assert_eq!(
            error,
            SimulationError::TargetPrecedesCurrentTime {
                current: SimulationTime::from_millis(100),
                target: SimulationTime::from_millis(99),
            }
        );
        assert_eq!(simulation.current_time(), SimulationTime::from_millis(100));
    }
}
