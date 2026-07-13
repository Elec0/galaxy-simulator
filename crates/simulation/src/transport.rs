use crate::{
    CapacityReservationId, DemandRequestId, EventAgenda, EventGeneration, EventPhase,
    IdAllocationError, IdSequence, InventoryError, InventoryId, InventoryRegistry, LocationId,
    MaterialId, Navigation, NavigationError, Quantity, QuantityError, ReservationId,
    ReservationOwner, RouteId, ScheduleError, ShipId, SimulationDuration, SimulationTime,
    SimulationTimeError, SupplyOfferId, TransportJobId,
};
use std::cmp::Reverse;
use std::collections::BTreeMap;
use std::error::Error;
use std::fmt::{self, Display, Formatter};
use std::num::NonZeroU64;

/// Non-zero material units transferred per simulated second.
#[derive(Clone, Copy, Debug, Eq, Ord, PartialEq, PartialOrd)]
pub struct TransferRate(NonZeroU64);

impl TransferRate {
    /// Creates a non-zero material transfer rate.
    #[must_use]
    pub const fn new(units_per_second: NonZeroU64) -> Self {
        Self(units_per_second)
    }

    /// Returns material units transferred per simulated second.
    #[must_use]
    pub const fn units_per_second(self) -> u64 {
        self.0.get()
    }

    fn duration_for(self, quantity: Quantity) -> Result<SimulationDuration, TransportError> {
        let numerator = u128::from(quantity.as_units()) * 1_000;
        let milliseconds = numerator.div_ceil(u128::from(self.units_per_second()));
        let milliseconds =
            u64::try_from(milliseconds).map_err(|_| TransportError::TransferDurationOverflow)?;
        Ok(SimulationDuration::from_millis(milliseconds))
    }
}

/// Configurable loading and unloading timing controls.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct TransportTiming {
    docking_overhead: SimulationDuration,
    loading_rate: TransferRate,
    unloading_rate: TransferRate,
}

impl TransportTiming {
    /// Creates transfer timing controls.
    #[must_use]
    pub const fn new(
        docking_overhead: SimulationDuration,
        loading_rate: TransferRate,
        unloading_rate: TransferRate,
    ) -> Self {
        Self {
            docking_overhead,
            loading_rate,
            unloading_rate,
        }
    }

    fn loading_duration(self, quantity: Quantity) -> Result<SimulationDuration, TransportError> {
        Ok(self
            .docking_overhead
            .checked_add(self.loading_rate.duration_for(quantity)?)?)
    }

    fn unloading_duration(self, quantity: Quantity) -> Result<SimulationDuration, TransportError> {
        Ok(self
            .docking_overhead
            .checked_add(self.unloading_rate.duration_for(quantity)?)?)
    }
}

/// Integer urgency assigned to a material demand.
#[derive(Clone, Copy, Debug, Default, Eq, Ord, PartialEq, PartialOrd)]
pub struct DemandPriority(u32);

impl DemandPriority {
    /// Creates a demand priority. Larger values are more urgent.
    #[must_use]
    pub const fn new(value: u32) -> Self {
        Self(value)
    }

    /// Returns the integer priority value.
    #[must_use]
    pub const fn get(self) -> u32 {
        self.0
    }
}

/// Finite quantity of material offered from one inventory.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct SupplyOffer {
    id: SupplyOfferId,
    inventory_id: InventoryId,
    location_id: LocationId,
    material_id: MaterialId,
    remaining: Quantity,
}

impl SupplyOffer {
    /// Returns the stable offer ID.
    #[must_use]
    pub const fn id(self) -> SupplyOfferId {
        self.id
    }

    /// Returns the source inventory.
    #[must_use]
    pub const fn inventory_id(self) -> InventoryId {
        self.inventory_id
    }

    /// Returns the source location.
    #[must_use]
    pub const fn location_id(self) -> LocationId {
        self.location_id
    }

    /// Returns the offered material.
    #[must_use]
    pub const fn material_id(self) -> MaterialId {
        self.material_id
    }

    /// Returns the uncommitted quantity.
    #[must_use]
    pub const fn remaining(self) -> Quantity {
        self.remaining
    }
}

/// Finite unmet material demand at one inventory.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct DemandRequest {
    id: DemandRequestId,
    inventory_id: InventoryId,
    location_id: LocationId,
    material_id: MaterialId,
    remaining: Quantity,
    priority: DemandPriority,
    created_at: SimulationTime,
}

impl DemandRequest {
    /// Returns the stable demand ID.
    #[must_use]
    pub const fn id(self) -> DemandRequestId {
        self.id
    }

    /// Returns the destination inventory.
    #[must_use]
    pub const fn inventory_id(self) -> InventoryId {
        self.inventory_id
    }

    /// Returns the destination location.
    #[must_use]
    pub const fn location_id(self) -> LocationId {
        self.location_id
    }

    /// Returns the requested material.
    #[must_use]
    pub const fn material_id(self) -> MaterialId {
        self.material_id
    }

    /// Returns the unmet quantity.
    #[must_use]
    pub const fn remaining(self) -> Quantity {
        self.remaining
    }

    /// Returns demand urgency.
    #[must_use]
    pub const fn priority(self) -> DemandPriority {
        self.priority
    }

    /// Returns when the demand entered the board.
    #[must_use]
    pub const fn created_at(self) -> SimulationTime {
        self.created_at
    }
}

/// Phase 1 logistics state for one cargo ship.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct Freighter {
    ship: ShipId,
    location: LocationId,
    cargo_inventory: InventoryId,
    active_job: Option<TransportJobId>,
}

impl Freighter {
    /// Creates an idle freighter at a location.
    #[must_use]
    pub const fn new(
        ship_id: ShipId,
        location_id: LocationId,
        cargo_inventory_id: InventoryId,
    ) -> Self {
        Self {
            ship: ship_id,
            location: location_id,
            cargo_inventory: cargo_inventory_id,
            active_job: None,
        }
    }

    /// Returns the ship ID.
    #[must_use]
    pub const fn ship_id(self) -> ShipId {
        self.ship
    }

    /// Returns the current location.
    #[must_use]
    pub const fn location_id(self) -> LocationId {
        self.location
    }

    /// Returns the cargo inventory ID.
    #[must_use]
    pub const fn cargo_inventory_id(self) -> InventoryId {
        self.cargo_inventory
    }

    /// Returns the currently committed job.
    #[must_use]
    pub const fn active_job_id(self) -> Option<TransportJobId> {
        self.active_job
    }
}

/// Lifecycle of a concrete assigned transport job.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum TransportJobState {
    /// Source material and ship capacity are committed.
    Assigned,
    /// No enabled path currently reaches the source.
    WaitingForRouteToSource,
    /// The ship is traversing one route toward the source.
    TravelingToSource {
        /// Current directed route.
        route_id: RouteId,
        /// Scheduled route-leg arrival.
        arrives_at: SimulationTime,
    },
    /// Docking and material loading are underway.
    Loading {
        /// Scheduled loading completion.
        completes_at: SimulationTime,
    },
    /// No enabled path currently reaches the destination.
    WaitingForRouteToDestination,
    /// The ship is traversing one route toward the destination.
    TravelingToDestination {
        /// Current directed route.
        route_id: RouteId,
        /// Scheduled route-leg arrival.
        arrives_at: SimulationTime,
    },
    /// The ship has arrived, but destination storage lacks capacity.
    WaitingForDestinationCapacity,
    /// Destination capacity is reserved and unloading is underway.
    Unloading {
        /// Scheduled unloading completion.
        completes_at: SimulationTime,
    },
    /// Cargo reached destination storage.
    Completed,
    /// The job failed before source material became ship cargo.
    FailedBeforeLoading,
}

/// Which endpoint a route-leg arrival is approaching.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum TravelTarget {
    /// Travel toward the source inventory.
    Source,
    /// Travel toward the destination inventory.
    Destination,
}

/// Domain event payloads for freighter execution.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum TransportEvent {
    /// One directed route leg has completed.
    Arrive {
        /// Transport job being executed.
        job_id: TransportJobId,
        /// Validity generation captured when scheduled.
        generation: EventGeneration,
        /// Route traversed by the ship.
        route_id: RouteId,
        /// Endpoint the ship is approaching.
        target: TravelTarget,
    },
    /// Reserved source material should become ship cargo.
    FinishLoading {
        /// Transport job being executed.
        job_id: TransportJobId,
        /// Validity generation captured when scheduled.
        generation: EventGeneration,
    },
    /// Ship cargo should enter reserved destination capacity.
    FinishUnloading {
        /// Transport job being executed.
        job_id: TransportJobId,
        /// Validity generation captured when scheduled.
        generation: EventGeneration,
    },
}

/// Concrete material movement assigned to one freighter.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct TransportJob {
    id: TransportJobId,
    ship_id: ShipId,
    supply_offer_id: SupplyOfferId,
    demand_request_id: DemandRequestId,
    source_inventory_id: InventoryId,
    source_location_id: LocationId,
    destination_inventory_id: InventoryId,
    destination_location_id: LocationId,
    material_id: MaterialId,
    quantity: Quantity,
    source_reservation_id: ReservationId,
    destination_capacity_reservation_id: Option<CapacityReservationId>,
    assigned_at: SimulationTime,
    generation: EventGeneration,
    state: TransportJobState,
}

impl TransportJob {
    /// Returns the stable job ID.
    #[must_use]
    pub const fn id(self) -> TransportJobId {
        self.id
    }

    /// Returns the assigned ship.
    #[must_use]
    pub const fn ship_id(self) -> ShipId {
        self.ship_id
    }

    /// Returns the destination demand request.
    #[must_use]
    pub const fn demand_request_id(self) -> DemandRequestId {
        self.demand_request_id
    }

    /// Returns the source supply offer.
    #[must_use]
    pub const fn supply_offer_id(self) -> SupplyOfferId {
        self.supply_offer_id
    }

    /// Returns the source inventory.
    #[must_use]
    pub const fn source_inventory_id(self) -> InventoryId {
        self.source_inventory_id
    }

    /// Returns the source location.
    #[must_use]
    pub const fn source_location_id(self) -> LocationId {
        self.source_location_id
    }

    /// Returns the destination inventory.
    #[must_use]
    pub const fn destination_inventory_id(self) -> InventoryId {
        self.destination_inventory_id
    }

    /// Returns the destination location.
    #[must_use]
    pub const fn destination_location_id(self) -> LocationId {
        self.destination_location_id
    }

    /// Returns the material to move.
    #[must_use]
    pub const fn material_id(self) -> MaterialId {
        self.material_id
    }

    /// Returns the committed amount.
    #[must_use]
    pub const fn quantity(self) -> Quantity {
        self.quantity
    }

    /// Returns the reservation holding source material.
    #[must_use]
    pub const fn source_reservation_id(self) -> ReservationId {
        self.source_reservation_id
    }

    /// Returns destination capacity reserved for active unloading.
    #[must_use]
    pub const fn destination_capacity_reservation_id(self) -> Option<CapacityReservationId> {
        self.destination_capacity_reservation_id
    }

    /// Returns when the job was assigned.
    #[must_use]
    pub const fn assigned_at(self) -> SimulationTime {
        self.assigned_at
    }

    /// Returns the validity generation attached to job events.
    #[must_use]
    pub const fn generation(self) -> EventGeneration {
        self.generation
    }

    /// Returns the current lifecycle state.
    #[must_use]
    pub const fn state(self) -> TransportJobState {
        self.state
    }
}

/// Deterministic ID allocation owned by the transport board.
#[derive(Clone, Debug, Default)]
pub struct TransportIdSequences {
    offers: IdSequence<SupplyOfferId>,
    demands: IdSequence<DemandRequestId>,
    jobs: IdSequence<TransportJobId>,
}

impl TransportIdSequences {
    /// Creates transport ID sequences beginning at one.
    #[must_use]
    pub const fn new() -> Self {
        Self {
            offers: IdSequence::new(),
            demands: IdSequence::new(),
            jobs: IdSequence::new(),
        }
    }
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
struct Candidate {
    offer_id: SupplyOfferId,
    demand_id: DemandRequestId,
    priority: DemandPriority,
    demand_created_at: SimulationTime,
    journey_duration: SimulationDuration,
    quantity: Quantity,
}

impl Candidate {
    fn rank(
        self,
    ) -> (
        Reverse<DemandPriority>,
        SimulationTime,
        SimulationDuration,
        Reverse<Quantity>,
        DemandRequestId,
        SupplyOfferId,
    ) {
        (
            Reverse(self.priority),
            self.demand_created_at,
            self.journey_duration,
            Reverse(self.quantity),
            self.demand_id,
            self.offer_id,
        )
    }
}

/// Central Phase 1 exchange for supply, demand, and assigned jobs.
#[derive(Clone, Debug, Default)]
pub struct TransportBoard {
    supplies: BTreeMap<SupplyOfferId, SupplyOffer>,
    demands: BTreeMap<DemandRequestId, DemandRequest>,
    jobs: BTreeMap<TransportJobId, TransportJob>,
}

impl TransportBoard {
    /// Creates an empty transport board.
    #[must_use]
    pub const fn new() -> Self {
        Self {
            supplies: BTreeMap::new(),
            demands: BTreeMap::new(),
            jobs: BTreeMap::new(),
        }
    }

    /// Publishes a finite supply offer.
    ///
    /// # Errors
    ///
    /// Returns an ID allocation error or rejects a zero quantity.
    pub fn publish_supply(
        &mut self,
        ids: &mut TransportIdSequences,
        inventory_id: InventoryId,
        location_id: LocationId,
        material_id: MaterialId,
        quantity: Quantity,
    ) -> Result<SupplyOfferId, TransportError> {
        if quantity == Quantity::ZERO {
            return Err(TransportError::EmptyQuantity);
        }
        let id = ids.offers.allocate()?;
        self.supplies.insert(
            id,
            SupplyOffer {
                id,
                inventory_id,
                location_id,
                material_id,
                remaining: quantity,
            },
        );
        Ok(id)
    }

    /// Publishes a finite material demand.
    ///
    /// # Errors
    ///
    /// Returns an ID allocation error or rejects a zero quantity.
    #[allow(clippy::too_many_arguments)]
    pub fn publish_demand(
        &mut self,
        ids: &mut TransportIdSequences,
        inventory_id: InventoryId,
        location_id: LocationId,
        material_id: MaterialId,
        quantity: Quantity,
        priority: DemandPriority,
        created_at: SimulationTime,
    ) -> Result<DemandRequestId, TransportError> {
        if quantity == Quantity::ZERO {
            return Err(TransportError::EmptyQuantity);
        }
        let id = ids.demands.allocate()?;
        self.demands.insert(
            id,
            DemandRequest {
                id,
                inventory_id,
                location_id,
                material_id,
                remaining: quantity,
                priority,
                created_at,
            },
        );
        Ok(id)
    }

    /// Returns a supply offer.
    #[must_use]
    pub fn supply(&self, offer_id: SupplyOfferId) -> Option<SupplyOffer> {
        self.supplies.get(&offer_id).copied()
    }

    /// Returns a demand request.
    #[must_use]
    pub fn demand(&self, demand_id: DemandRequestId) -> Option<DemandRequest> {
        self.demands.get(&demand_id).copied()
    }

    /// Returns an assigned transport job.
    #[must_use]
    pub fn job(&self, job_id: TransportJobId) -> Option<TransportJob> {
        self.jobs.get(&job_id).copied()
    }

    /// Returns all concrete jobs in stable identifier order.
    pub fn jobs(&self) -> impl Iterator<Item = TransportJob> + '_ {
        self.jobs.values().copied()
    }

    /// Returns uncommitted offers for one inventory and material.
    #[must_use]
    pub fn offered_quantity(&self, inventory_id: InventoryId, material_id: MaterialId) -> Quantity {
        Quantity::from_units(
            self.supplies
                .values()
                .filter(|offer| {
                    offer.inventory_id == inventory_id && offer.material_id == material_id
                })
                .fold(0_u64, |total, offer| {
                    total.saturating_add(offer.remaining.as_units())
                }),
        )
    }

    /// Returns uncommitted demand plus material currently assigned in transit.
    #[must_use]
    pub fn pending_delivery_quantity(
        &self,
        inventory_id: InventoryId,
        material_id: MaterialId,
    ) -> Quantity {
        let requested = self
            .demands
            .values()
            .filter(|demand| {
                demand.inventory_id == inventory_id && demand.material_id == material_id
            })
            .fold(0_u64, |total, demand| {
                total.saturating_add(demand.remaining.as_units())
            });
        let in_transit = self
            .jobs
            .values()
            .filter(|job| {
                job.destination_inventory_id == inventory_id
                    && job.material_id == material_id
                    && !matches!(
                        job.state,
                        TransportJobState::Completed | TransportJobState::FailedBeforeLoading
                    )
            })
            .fold(0_u64, |total, job| {
                total.saturating_add(job.quantity.as_units())
            });
        Quantity::from_units(requested.saturating_add(in_transit))
    }

    /// Returns cargo committed to non-terminal jobs for one destination.
    #[must_use]
    pub fn committed_delivery_quantity(
        &self,
        inventory_id: InventoryId,
        material_id: MaterialId,
    ) -> Quantity {
        let units = self
            .jobs
            .values()
            .filter(|job| {
                job.destination_inventory_id == inventory_id
                    && job.material_id == material_id
                    && !matches!(
                        job.state,
                        TransportJobState::Completed | TransportJobState::FailedBeforeLoading
                    )
            })
            .fold(0_u64, |total, job| {
                total.saturating_add(job.quantity.as_units())
            });
        Quantity::from_units(units)
    }

    /// Selects and atomically assigns the best reachable supply-demand match.
    ///
    /// # Errors
    ///
    /// Returns an error for a busy freighter, unknown inventory, invalid route
    /// data, exhausted IDs, or failed source reservation.
    #[allow(clippy::too_many_arguments)]
    pub fn assign_best<N: Navigation>(
        &mut self,
        ids: &mut TransportIdSequences,
        reservation_ids: &mut IdSequence<ReservationId>,
        freighter: &mut Freighter,
        inventories: &mut InventoryRegistry,
        navigation: &N,
        now: SimulationTime,
    ) -> Result<Option<TransportJobId>, TransportError> {
        if let Some(job_id) = freighter.active_job {
            return Err(TransportError::FreighterBusy {
                ship_id: freighter.ship,
                job_id,
            });
        }

        let cargo_capacity = inventories
            .get(freighter.cargo_inventory)
            .ok_or(TransportError::UnknownInventory(freighter.cargo_inventory))?
            .remaining_capacity();
        if cargo_capacity == Quantity::ZERO {
            return Ok(None);
        }

        let Some(candidate) =
            self.best_candidate(freighter, inventories, navigation, cargo_capacity)?
        else {
            return Ok(None);
        };
        let job_id =
            self.commit_assignment(candidate, ids, reservation_ids, freighter, inventories, now)?;
        Ok(Some(job_id))
    }

    fn best_candidate<N: Navigation>(
        &self,
        freighter: &Freighter,
        inventories: &InventoryRegistry,
        navigation: &N,
        cargo_capacity: Quantity,
    ) -> Result<Option<Candidate>, TransportError> {
        let mut best: Option<Candidate> = None;
        for demand in self
            .demands
            .values()
            .filter(|demand| demand.remaining > Quantity::ZERO)
        {
            for supply in self.supplies.values().filter(|supply| {
                supply.remaining > Quantity::ZERO && supply.material_id == demand.material_id
            }) {
                let source_available = inventories
                    .get(supply.inventory_id)
                    .ok_or(TransportError::UnknownInventory(supply.inventory_id))?
                    .available(supply.material_id);
                let quantity = demand
                    .remaining
                    .min(supply.remaining)
                    .min(source_available)
                    .min(cargo_capacity);
                if quantity == Quantity::ZERO {
                    continue;
                }

                let Some(to_source) =
                    navigation.find_route(freighter.location, supply.location_id)?
                else {
                    continue;
                };
                let Some(to_destination) =
                    navigation.find_route(supply.location_id, demand.location_id)?
                else {
                    continue;
                };
                let candidate = Candidate {
                    offer_id: supply.id,
                    demand_id: demand.id,
                    priority: demand.priority,
                    demand_created_at: demand.created_at,
                    journey_duration: to_source
                        .total_duration()
                        .checked_add(to_destination.total_duration())?,
                    quantity,
                };
                if best.is_none_or(|current| candidate.rank() < current.rank()) {
                    best = Some(candidate);
                }
            }
        }
        Ok(best)
    }

    #[allow(clippy::too_many_arguments)]
    fn commit_assignment(
        &mut self,
        candidate: Candidate,
        ids: &mut TransportIdSequences,
        reservation_ids: &mut IdSequence<ReservationId>,
        freighter: &mut Freighter,
        inventories: &mut InventoryRegistry,
        now: SimulationTime,
    ) -> Result<TransportJobId, TransportError> {
        let job_id = ids.jobs.allocate()?;
        let reservation_id = reservation_ids.allocate()?;
        let supply = self
            .supplies
            .get(&candidate.offer_id)
            .copied()
            .ok_or(TransportError::UnknownSupply(candidate.offer_id))?;
        let demand = self
            .demands
            .get(&candidate.demand_id)
            .copied()
            .ok_or(TransportError::UnknownDemand(candidate.demand_id))?;

        inventories
            .get_mut(supply.inventory_id)
            .ok_or(TransportError::UnknownInventory(supply.inventory_id))?
            .reserve(
                reservation_id,
                supply.material_id,
                candidate.quantity,
                ReservationOwner::TransportJob(job_id),
            )?;

        self.supplies
            .get_mut(&supply.id)
            .ok_or(TransportError::UnknownSupply(supply.id))?
            .remaining = supply.remaining.checked_sub(candidate.quantity)?;
        self.demands
            .get_mut(&demand.id)
            .ok_or(TransportError::UnknownDemand(demand.id))?
            .remaining = demand.remaining.checked_sub(candidate.quantity)?;
        self.jobs.insert(
            job_id,
            TransportJob {
                id: job_id,
                ship_id: freighter.ship,
                supply_offer_id: supply.id,
                demand_request_id: demand.id,
                source_inventory_id: supply.inventory_id,
                source_location_id: supply.location_id,
                destination_inventory_id: demand.inventory_id,
                destination_location_id: demand.location_id,
                material_id: supply.material_id,
                quantity: candidate.quantity,
                source_reservation_id: reservation_id,
                destination_capacity_reservation_id: None,
                assigned_at: now,
                generation: EventGeneration::new(0),
                state: TransportJobState::Assigned,
            },
        );
        freighter.active_job = Some(job_id);
        Ok(job_id)
    }
}

impl TransportBoard {
    /// Starts an assigned job or retries a job waiting for a route or capacity.
    ///
    /// # Errors
    ///
    /// Returns an error when the job, ship, inventory, navigation data, timing,
    /// or event schedule is invalid.
    #[allow(clippy::too_many_arguments)]
    pub fn start_or_retry<N: Navigation, E: From<TransportEvent>>(
        &mut self,
        job_id: TransportJobId,
        freighter: &mut Freighter,
        inventories: &mut InventoryRegistry,
        capacity_reservation_ids: &mut IdSequence<CapacityReservationId>,
        navigation: &N,
        agenda: &mut EventAgenda<E>,
        timing: TransportTiming,
        now: SimulationTime,
    ) -> Result<bool, TransportError> {
        let job = self.require_job_for_freighter(job_id, freighter)?;
        match job.state {
            TransportJobState::Assigned | TransportJobState::WaitingForRouteToSource => self
                .advance_toward(
                    job_id,
                    TravelTarget::Source,
                    freighter,
                    inventories,
                    capacity_reservation_ids,
                    navigation,
                    agenda,
                    timing,
                    now,
                ),
            TransportJobState::WaitingForRouteToDestination => self.advance_toward(
                job_id,
                TravelTarget::Destination,
                freighter,
                inventories,
                capacity_reservation_ids,
                navigation,
                agenda,
                timing,
                now,
            ),
            TransportJobState::WaitingForDestinationCapacity => self.begin_unloading(
                job_id,
                inventories,
                capacity_reservation_ids,
                agenda,
                timing,
                now,
            ),
            _ => Ok(false),
        }
    }

    /// Applies one scheduled transport event.
    ///
    /// Returns `false` for an event made stale by generation or state changes.
    ///
    /// # Errors
    ///
    /// Returns an error when current world state violates the event's required
    /// inventory, route, timing, or ship relationships.
    #[allow(clippy::too_many_arguments)]
    pub fn handle_event<N: Navigation, E: From<TransportEvent>>(
        &mut self,
        event: TransportEvent,
        freighter: &mut Freighter,
        inventories: &mut InventoryRegistry,
        capacity_reservation_ids: &mut IdSequence<CapacityReservationId>,
        navigation: &N,
        agenda: &mut EventAgenda<E>,
        timing: TransportTiming,
        now: SimulationTime,
    ) -> Result<bool, TransportError> {
        match event {
            TransportEvent::Arrive {
                job_id,
                generation,
                route_id,
                target,
            } => {
                let job = self.require_job_for_freighter(job_id, freighter)?;
                if job.generation != generation
                    || !travel_state_matches(job.state, route_id, target, now)
                {
                    return Ok(false);
                }
                let route = navigation
                    .route(route_id)
                    .ok_or(TransportError::UnknownRoute(route_id))?;
                freighter.location = route.destination();
                self.advance_toward(
                    job_id,
                    target,
                    freighter,
                    inventories,
                    capacity_reservation_ids,
                    navigation,
                    agenda,
                    timing,
                    now,
                )
            }
            TransportEvent::FinishLoading { job_id, generation } => {
                let job = self.require_job_for_freighter(job_id, freighter)?;
                if job.generation != generation
                    || !matches!(
                        job.state,
                        TransportJobState::Loading { completes_at } if completes_at == now
                    )
                {
                    return Ok(false);
                }
                let owner = ReservationOwner::TransportJob(job_id);
                if inventories
                    .transfer_reserved(
                        job.source_inventory_id,
                        freighter.cargo_inventory,
                        job.source_reservation_id,
                        owner,
                    )
                    .is_err()
                {
                    self.fail_before_loading(job_id, freighter, inventories)?;
                    return Ok(true);
                }
                self.advance_toward(
                    job_id,
                    TravelTarget::Destination,
                    freighter,
                    inventories,
                    capacity_reservation_ids,
                    navigation,
                    agenda,
                    timing,
                    now,
                )
            }
            TransportEvent::FinishUnloading { job_id, generation } => {
                let job = self.require_job_for_freighter(job_id, freighter)?;
                if job.generation != generation
                    || !matches!(
                        job.state,
                        TransportJobState::Unloading { completes_at } if completes_at == now
                    )
                {
                    return Ok(false);
                }
                let capacity_reservation_id = job.destination_capacity_reservation_id.ok_or(
                    TransportError::MissingDestinationCapacityReservation(job_id),
                )?;
                inventories.transfer_into_reserved_capacity(
                    freighter.cargo_inventory,
                    job.destination_inventory_id,
                    job.material_id,
                    job.quantity,
                    capacity_reservation_id,
                    ReservationOwner::TransportJob(job_id),
                )?;
                self.set_job_state(job_id, TransportJobState::Completed)?;
                freighter.active_job = None;
                Ok(true)
            }
        }
    }

    #[allow(clippy::too_many_arguments)]
    fn advance_toward<N: Navigation, E: From<TransportEvent>>(
        &mut self,
        job_id: TransportJobId,
        target: TravelTarget,
        freighter: &mut Freighter,
        inventories: &mut InventoryRegistry,
        capacity_reservation_ids: &mut IdSequence<CapacityReservationId>,
        navigation: &N,
        agenda: &mut EventAgenda<E>,
        timing: TransportTiming,
        now: SimulationTime,
    ) -> Result<bool, TransportError> {
        let job = self
            .jobs
            .get(&job_id)
            .copied()
            .ok_or(TransportError::UnknownJob(job_id))?;
        let destination = match target {
            TravelTarget::Source => job.source_location_id,
            TravelTarget::Destination => job.destination_location_id,
        };
        let Some(plan) = navigation.find_route(freighter.location, destination)? else {
            self.set_job_state(job_id, waiting_route_state(target))?;
            return Ok(false);
        };
        let Some(route_id) = plan.route_ids().first().copied() else {
            return match target {
                TravelTarget::Source => self.begin_loading(job_id, agenda, timing, now),
                TravelTarget::Destination => self.begin_unloading(
                    job_id,
                    inventories,
                    capacity_reservation_ids,
                    agenda,
                    timing,
                    now,
                ),
            };
        };
        let route = navigation
            .route(route_id)
            .ok_or(TransportError::UnknownRoute(route_id))?;
        let arrives_at = now.checked_add(route.base_duration())?;
        agenda.schedule(
            arrives_at,
            EventPhase::PhysicalCompletion,
            job.generation,
            TransportEvent::Arrive {
                job_id,
                generation: job.generation,
                route_id,
                target,
            }
            .into(),
        )?;
        self.set_job_state(job_id, traveling_state(target, route_id, arrives_at))?;
        Ok(true)
    }

    fn begin_loading<E: From<TransportEvent>>(
        &mut self,
        job_id: TransportJobId,
        agenda: &mut EventAgenda<E>,
        timing: TransportTiming,
        now: SimulationTime,
    ) -> Result<bool, TransportError> {
        let job = self
            .jobs
            .get(&job_id)
            .copied()
            .ok_or(TransportError::UnknownJob(job_id))?;
        let completes_at = now.checked_add(timing.loading_duration(job.quantity)?)?;
        agenda.schedule(
            completes_at,
            EventPhase::PhysicalCompletion,
            job.generation,
            TransportEvent::FinishLoading {
                job_id,
                generation: job.generation,
            }
            .into(),
        )?;
        self.set_job_state(job_id, TransportJobState::Loading { completes_at })?;
        Ok(true)
    }

    fn begin_unloading<E: From<TransportEvent>>(
        &mut self,
        job_id: TransportJobId,
        inventories: &mut InventoryRegistry,
        capacity_reservation_ids: &mut IdSequence<CapacityReservationId>,
        agenda: &mut EventAgenda<E>,
        timing: TransportTiming,
        now: SimulationTime,
    ) -> Result<bool, TransportError> {
        let job = self
            .jobs
            .get(&job_id)
            .copied()
            .ok_or(TransportError::UnknownJob(job_id))?;
        let destination = inventories.get(job.destination_inventory_id).ok_or(
            TransportError::UnknownInventory(job.destination_inventory_id),
        )?;
        if job.quantity > destination.remaining_capacity() {
            self.set_job_state(job_id, TransportJobState::WaitingForDestinationCapacity)?;
            return Ok(false);
        }

        let reservation_id = capacity_reservation_ids.allocate()?;
        inventories
            .get_mut(job.destination_inventory_id)
            .ok_or(TransportError::UnknownInventory(
                job.destination_inventory_id,
            ))?
            .reserve_capacity(
                reservation_id,
                job.quantity,
                ReservationOwner::TransportJob(job_id),
            )?;
        let completes_at = now.checked_add(timing.unloading_duration(job.quantity)?)?;
        if let Err(error) = agenda.schedule(
            completes_at,
            EventPhase::PhysicalCompletion,
            job.generation,
            TransportEvent::FinishUnloading {
                job_id,
                generation: job.generation,
            }
            .into(),
        ) {
            inventories
                .get_mut(job.destination_inventory_id)
                .ok_or(TransportError::UnknownInventory(
                    job.destination_inventory_id,
                ))?
                .release_capacity(reservation_id)?;
            return Err(error.into());
        }
        let mutable_job = self
            .jobs
            .get_mut(&job_id)
            .ok_or(TransportError::UnknownJob(job_id))?;
        mutable_job.destination_capacity_reservation_id = Some(reservation_id);
        mutable_job.state = TransportJobState::Unloading { completes_at };
        Ok(true)
    }

    fn fail_before_loading(
        &mut self,
        job_id: TransportJobId,
        freighter: &mut Freighter,
        inventories: &mut InventoryRegistry,
    ) -> Result<(), TransportError> {
        let job = self
            .jobs
            .get(&job_id)
            .copied()
            .ok_or(TransportError::UnknownJob(job_id))?;
        let source = inventories
            .get_mut(job.source_inventory_id)
            .ok_or(TransportError::UnknownInventory(job.source_inventory_id))?;
        if source.reservation(job.source_reservation_id).is_some() {
            source.release(job.source_reservation_id)?;
        }
        let supply = self
            .supplies
            .get_mut(&job.supply_offer_id)
            .ok_or(TransportError::UnknownSupply(job.supply_offer_id))?;
        supply.remaining = supply.remaining.checked_add(job.quantity)?;
        let demand = self
            .demands
            .get_mut(&job.demand_request_id)
            .ok_or(TransportError::UnknownDemand(job.demand_request_id))?;
        demand.remaining = demand.remaining.checked_add(job.quantity)?;
        self.set_job_state(job_id, TransportJobState::FailedBeforeLoading)?;
        freighter.active_job = None;
        Ok(())
    }

    fn require_job_for_freighter(
        &self,
        job_id: TransportJobId,
        freighter: &Freighter,
    ) -> Result<TransportJob, TransportError> {
        let job = self
            .jobs
            .get(&job_id)
            .copied()
            .ok_or(TransportError::UnknownJob(job_id))?;
        if job.ship_id != freighter.ship || freighter.active_job != Some(job_id) {
            return Err(TransportError::JobShipMismatch {
                job_id,
                expected: job.ship_id,
                actual: freighter.ship,
            });
        }
        Ok(job)
    }

    fn set_job_state(
        &mut self,
        job_id: TransportJobId,
        state: TransportJobState,
    ) -> Result<(), TransportError> {
        self.jobs
            .get_mut(&job_id)
            .ok_or(TransportError::UnknownJob(job_id))?
            .state = state;
        Ok(())
    }
}

fn waiting_route_state(target: TravelTarget) -> TransportJobState {
    match target {
        TravelTarget::Source => TransportJobState::WaitingForRouteToSource,
        TravelTarget::Destination => TransportJobState::WaitingForRouteToDestination,
    }
}

fn traveling_state(
    target: TravelTarget,
    route_id: RouteId,
    arrives_at: SimulationTime,
) -> TransportJobState {
    match target {
        TravelTarget::Source => TransportJobState::TravelingToSource {
            route_id,
            arrives_at,
        },
        TravelTarget::Destination => TransportJobState::TravelingToDestination {
            route_id,
            arrives_at,
        },
    }
}

fn travel_state_matches(
    state: TransportJobState,
    route_id: RouteId,
    target: TravelTarget,
    now: SimulationTime,
) -> bool {
    match (state, target) {
        (
            TransportJobState::TravelingToSource {
                route_id: expected,
                arrives_at,
            },
            TravelTarget::Source,
        )
        | (
            TransportJobState::TravelingToDestination {
                route_id: expected,
                arrives_at,
            },
            TravelTarget::Destination,
        ) => expected == route_id && arrives_at == now,
        _ => false,
    }
}

/// Errors produced by transport publication and assignment.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum TransportError {
    /// A supply or demand publication had no quantity.
    EmptyQuantity,
    /// A freighter already has a committed job.
    FreighterBusy {
        /// Busy ship.
        ship_id: ShipId,
        /// Existing job.
        job_id: TransportJobId,
    },
    /// A referenced world inventory is absent.
    UnknownInventory(InventoryId),
    /// A referenced supply offer is absent.
    UnknownSupply(SupplyOfferId),
    /// A referenced demand request is absent.
    UnknownDemand(DemandRequestId),
    /// A referenced assigned job is absent.
    UnknownJob(TransportJobId),
    /// A referenced route is absent.
    UnknownRoute(RouteId),
    /// The supplied freighter does not own the job.
    JobShipMismatch {
        /// Job being operated.
        job_id: TransportJobId,
        /// Ship assigned to the job.
        expected: ShipId,
        /// Ship supplied by the caller.
        actual: ShipId,
    },
    /// An unloading job lacks its required destination capacity reservation.
    MissingDestinationCapacityReservation(TransportJobId),
    /// Transfer duration does not fit the simulation timeline.
    TransferDurationOverflow,
    /// Navigation failed while evaluating a match.
    Navigation(NavigationError),
    /// Source inventory reservation failed.
    Inventory(InventoryError),
    /// Checked quantity arithmetic failed.
    Quantity(QuantityError),
    /// Checked travel-duration arithmetic failed.
    Time(SimulationTimeError),
    /// Scheduling a transport event failed.
    Schedule(ScheduleError),
    /// A deterministic ID sequence was exhausted.
    IdAllocation(IdAllocationError),
}

impl From<NavigationError> for TransportError {
    fn from(error: NavigationError) -> Self {
        Self::Navigation(error)
    }
}

impl From<InventoryError> for TransportError {
    fn from(error: InventoryError) -> Self {
        Self::Inventory(error)
    }
}

impl From<QuantityError> for TransportError {
    fn from(error: QuantityError) -> Self {
        Self::Quantity(error)
    }
}

impl From<SimulationTimeError> for TransportError {
    fn from(error: SimulationTimeError) -> Self {
        Self::Time(error)
    }
}

impl From<ScheduleError> for TransportError {
    fn from(error: ScheduleError) -> Self {
        Self::Schedule(error)
    }
}

impl From<IdAllocationError> for TransportError {
    fn from(error: IdAllocationError) -> Self {
        Self::IdAllocation(error)
    }
}

impl Display for TransportError {
    fn fmt(&self, formatter: &mut Formatter<'_>) -> fmt::Result {
        match self {
            Self::EmptyQuantity => formatter.write_str("transport quantity must be positive"),
            Self::FreighterBusy { ship_id, job_id } => {
                write!(
                    formatter,
                    "ship {ship_id} is already assigned to job {job_id}"
                )
            }
            Self::UnknownInventory(id) => write!(formatter, "unknown inventory {id}"),
            Self::UnknownSupply(id) => write!(formatter, "unknown supply offer {id}"),
            Self::UnknownDemand(id) => write!(formatter, "unknown demand request {id}"),
            Self::UnknownJob(id) => write!(formatter, "unknown transport job {id}"),
            Self::UnknownRoute(id) => write!(formatter, "unknown route {id}"),
            Self::JobShipMismatch {
                job_id,
                expected,
                actual,
            } => write!(
                formatter,
                "transport job {job_id} belongs to ship {expected}, not {actual}"
            ),
            Self::MissingDestinationCapacityReservation(id) => write!(
                formatter,
                "transport job {id} has no destination capacity reservation"
            ),
            Self::TransferDurationOverflow => {
                formatter.write_str("material transfer duration overflow")
            }
            Self::Navigation(error) => Display::fmt(error, formatter),
            Self::Inventory(error) => Display::fmt(error, formatter),
            Self::Quantity(error) => Display::fmt(error, formatter),
            Self::Time(error) => Display::fmt(error, formatter),
            Self::Schedule(error) => Display::fmt(error, formatter),
            Self::IdAllocation(error) => Display::fmt(error, formatter),
        }
    }
}

impl Error for TransportError {}

#[cfg(test)]
mod tests {
    use super::{
        DemandPriority, Freighter, TransferRate, TransportBoard, TransportIdSequences,
        TransportJobState, TransportTiming,
    };
    use crate::{
        CapacityReservationId, EventAgenda, IdSequence, Inventory, InventoryId, InventoryRegistry,
        LocationId, MaterialId, Quantity, ReservationId, RouteGraph, ShipId, SimulationDuration,
        SimulationTime,
    };
    use std::num::NonZeroU64;

    struct Fixture {
        board: TransportBoard,
        ids: TransportIdSequences,
        reservations: IdSequence<ReservationId>,
        inventories: InventoryRegistry,
        graph: RouteGraph,
        freighter: Freighter,
        source_inventory: InventoryId,
        destination_inventory: InventoryId,
        source: LocationId,
        destination: LocationId,
        material: MaterialId,
    }

    fn fixture() -> Fixture {
        let mut location_ids = IdSequence::<LocationId>::new();
        let ship_location = location_ids.allocate().expect("ship location");
        let source = location_ids.allocate().expect("source location");
        let destination = location_ids.allocate().expect("destination location");
        let mut graph = RouteGraph::new();
        for location in [ship_location, source, destination] {
            graph.add_location(location);
        }
        graph
            .add_route(ship_location, source, SimulationDuration::from_millis(10))
            .expect("route to source");
        graph
            .add_route(source, destination, SimulationDuration::from_millis(20))
            .expect("route to destination");

        let mut inventory_ids = IdSequence::<InventoryId>::new();
        let source_inventory = inventory_ids.allocate().expect("source inventory");
        let destination_inventory = inventory_ids.allocate().expect("destination inventory");
        let cargo_inventory = inventory_ids.allocate().expect("cargo inventory");
        let mut material_ids = IdSequence::<MaterialId>::new();
        let material = material_ids.allocate().expect("material");
        let mut inventories = InventoryRegistry::new();
        let mut source_storage = Inventory::new(source_inventory, Quantity::from_units(20));
        source_storage
            .add(material, Quantity::from_units(10))
            .expect("source material should fit");
        for inventory in [
            source_storage,
            Inventory::new(destination_inventory, Quantity::from_units(20)),
            Inventory::new(cargo_inventory, Quantity::from_units(4)),
        ] {
            inventories.insert(inventory).expect("unique inventory");
        }
        let mut ship_ids = IdSequence::<ShipId>::new();

        Fixture {
            board: TransportBoard::new(),
            ids: TransportIdSequences::new(),
            reservations: IdSequence::new(),
            inventories,
            graph,
            freighter: Freighter::new(
                ship_ids.allocate().expect("ship"),
                ship_location,
                cargo_inventory,
            ),
            source_inventory,
            destination_inventory,
            source,
            destination,
            material,
        }
    }

    #[test]
    fn assignment_commits_smallest_available_quantity_atomically() {
        let mut fixture = fixture();
        let offer = fixture
            .board
            .publish_supply(
                &mut fixture.ids,
                fixture.source_inventory,
                fixture.source,
                fixture.material,
                Quantity::from_units(10),
            )
            .expect("supply should publish");
        let demand = fixture
            .board
            .publish_demand(
                &mut fixture.ids,
                fixture.destination_inventory,
                fixture.destination,
                fixture.material,
                Quantity::from_units(8),
                DemandPriority::new(1),
                SimulationTime::ZERO,
            )
            .expect("demand should publish");

        let job_id = fixture
            .board
            .assign_best(
                &mut fixture.ids,
                &mut fixture.reservations,
                &mut fixture.freighter,
                &mut fixture.inventories,
                &fixture.graph,
                SimulationTime::ZERO,
            )
            .expect("assignment should succeed")
            .expect("a match should exist");

        assert_eq!(
            fixture.board.job(job_id).expect("job").quantity(),
            Quantity::from_units(4)
        );
        assert_eq!(fixture.freighter.active_job_id(), Some(job_id));
        assert_eq!(
            fixture
                .board
                .supply(offer)
                .expect("offer")
                .remaining()
                .as_units(),
            6
        );
        assert_eq!(
            fixture
                .board
                .demand(demand)
                .expect("demand")
                .remaining()
                .as_units(),
            4
        );
        assert_eq!(
            fixture
                .inventories
                .get(fixture.source_inventory)
                .expect("source inventory")
                .reserved(fixture.material),
            Quantity::from_units(4)
        );
    }

    #[test]
    fn assigned_job_moves_material_through_scheduled_events() {
        let mut fixture = fixture();
        fixture
            .board
            .publish_supply(
                &mut fixture.ids,
                fixture.source_inventory,
                fixture.source,
                fixture.material,
                Quantity::from_units(10),
            )
            .expect("supply should publish");
        fixture
            .board
            .publish_demand(
                &mut fixture.ids,
                fixture.destination_inventory,
                fixture.destination,
                fixture.material,
                Quantity::from_units(4),
                DemandPriority::new(1),
                SimulationTime::ZERO,
            )
            .expect("demand should publish");
        let job_id = fixture
            .board
            .assign_best(
                &mut fixture.ids,
                &mut fixture.reservations,
                &mut fixture.freighter,
                &mut fixture.inventories,
                &fixture.graph,
                SimulationTime::ZERO,
            )
            .expect("assignment should succeed")
            .expect("job should match");
        let timing = TransportTiming::new(
            SimulationDuration::from_millis(100),
            TransferRate::new(NonZeroU64::new(2).expect("non-zero loading rate")),
            TransferRate::new(NonZeroU64::new(2).expect("non-zero unloading rate")),
        );
        let mut capacity_reservations = IdSequence::<CapacityReservationId>::new();
        let mut agenda = EventAgenda::new();
        fixture
            .board
            .start_or_retry(
                job_id,
                &mut fixture.freighter,
                &mut fixture.inventories,
                &mut capacity_reservations,
                &fixture.graph,
                &mut agenda,
                timing,
                SimulationTime::ZERO,
            )
            .expect("job should start");

        let mut processed = 0;
        while let Some(scheduled) = agenda
            .pop_next_through(SimulationTime::from_millis(10_000))
            .expect("agenda should advance")
        {
            let now = scheduled.key().timestamp();
            fixture
                .board
                .handle_event(
                    scheduled.into_payload(),
                    &mut fixture.freighter,
                    &mut fixture.inventories,
                    &mut capacity_reservations,
                    &fixture.graph,
                    &mut agenda,
                    timing,
                    now,
                )
                .expect("scheduled transport event should apply");
            processed += 1;
        }

        assert_eq!(processed, 4);
        assert_eq!(fixture.freighter.active_job_id(), None);
        assert_eq!(
            fixture.board.job(job_id).expect("job").state(),
            TransportJobState::Completed
        );
        assert_eq!(
            fixture
                .inventories
                .get(fixture.destination_inventory)
                .expect("destination inventory")
                .stored(fixture.material),
            Quantity::from_units(4)
        );
        assert_eq!(
            fixture
                .inventories
                .get(fixture.freighter.cargo_inventory_id())
                .expect("cargo inventory")
                .stored(fixture.material),
            Quantity::ZERO
        );
    }

    #[test]
    fn unreachable_match_is_excluded() {
        let mut fixture = fixture();
        let mut disconnected_ids = IdSequence::<LocationId>::new();
        let _ = disconnected_ids.allocate().expect("first location ID");
        let _ = disconnected_ids.allocate().expect("second location ID");
        let _ = disconnected_ids.allocate().expect("third location ID");
        let disconnected = disconnected_ids.allocate().expect("disconnected location");
        fixture.graph.add_location(disconnected);
        fixture
            .board
            .publish_supply(
                &mut fixture.ids,
                fixture.source_inventory,
                disconnected,
                fixture.material,
                Quantity::from_units(5),
            )
            .expect("supply should publish");
        fixture
            .board
            .publish_demand(
                &mut fixture.ids,
                fixture.destination_inventory,
                fixture.destination,
                fixture.material,
                Quantity::from_units(5),
                DemandPriority::new(1),
                SimulationTime::ZERO,
            )
            .expect("demand should publish");

        let result = fixture
            .board
            .assign_best(
                &mut fixture.ids,
                &mut fixture.reservations,
                &mut fixture.freighter,
                &mut fixture.inventories,
                &fixture.graph,
                SimulationTime::ZERO,
            )
            .expect("assignment evaluation should succeed");

        assert_eq!(result, None);
    }

    #[test]
    fn higher_priority_demand_wins_before_distance() {
        let mut fixture = fixture();
        let mut inventory_ids = IdSequence::<InventoryId>::new();
        let _ = inventory_ids.allocate().expect("source inventory ID");
        let _ = inventory_ids.allocate().expect("destination inventory ID");
        let _ = inventory_ids.allocate().expect("cargo inventory ID");
        let second_destination = inventory_ids.allocate().expect("second destination ID");
        fixture
            .inventories
            .insert(Inventory::new(second_destination, Quantity::from_units(20)))
            .expect("second destination inventory should be unique");
        fixture
            .board
            .publish_supply(
                &mut fixture.ids,
                fixture.source_inventory,
                fixture.source,
                fixture.material,
                Quantity::from_units(10),
            )
            .expect("supply should publish");
        fixture
            .board
            .publish_demand(
                &mut fixture.ids,
                fixture.destination_inventory,
                fixture.destination,
                fixture.material,
                Quantity::from_units(4),
                DemandPriority::new(1),
                SimulationTime::ZERO,
            )
            .expect("low-priority demand should publish");
        let high_priority = fixture
            .board
            .publish_demand(
                &mut fixture.ids,
                second_destination,
                fixture.destination,
                fixture.material,
                Quantity::from_units(4),
                DemandPriority::new(2),
                SimulationTime::ZERO,
            )
            .expect("high-priority demand should publish");

        let job_id = fixture
            .board
            .assign_best(
                &mut fixture.ids,
                &mut fixture.reservations,
                &mut fixture.freighter,
                &mut fixture.inventories,
                &fixture.graph,
                SimulationTime::ZERO,
            )
            .expect("assignment should succeed")
            .expect("a match should exist");

        assert_eq!(
            fixture
                .board
                .job(job_id)
                .expect("job should exist")
                .demand_request_id(),
            high_priority
        );
    }
}
