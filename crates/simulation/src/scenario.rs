use crate::{
    CapacityReservationId, DemandPriority, EventAgenda, EventGeneration, EventPhase, FacilityId,
    IdAllocationError, IdSequence, Inventory, InventoryError, InventoryId, InventoryRegistry,
    LocationId, MaterialId, OrganizationId, ProductionError, ProductionIdSequences, ProductionJob,
    ProductionJobState, ProductionLine, Quantity, QuantityError, Recipe, ReservationId, RouteGraph,
    RouteId, ScheduleError, Ship, ShipBlueprint, ShipBlueprintId, ShipConstructionOrder,
    ShipConstructionOrderState, ShipError, ShipId, ShipRegistry, Shipyard, ShipyardError,
    ShipyardIdSequences, SimulationDuration, SimulationTime, SimulationTimeError, Throughput,
    TransferRate, TransportBoard, TransportError, TransportEvent, TransportIdSequences,
    TransportJobId, TransportJobState, TransportTiming, Work,
};
use std::collections::{BTreeMap, BTreeSet};
use std::error::Error;
use std::fmt::{self, Display, Formatter};
use std::num::NonZeroU64;

/// Tunable values for the first integrated headless scenario.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct PhaseOneConfig {
    /// Explicit seed reserved for deterministic systems that require randomness.
    pub random_seed: u64,
    /// Duration of each directed route leg.
    pub route_duration: SimulationDuration,
    /// Fixed loading and unloading overhead.
    pub docking_overhead: SimulationDuration,
    /// Material units loaded or unloaded per simulated second.
    pub transfer_rate: TransferRate,
    /// Shared capacity of each facility inventory.
    pub facility_storage_capacity: Quantity,
    /// Cargo capacity of starting and constructed freighters.
    pub freighter_cargo_capacity: Quantity,
    /// Mine output per batch.
    pub ore_batch: Quantity,
    /// Mine work per batch at one work unit per second.
    pub mine_work: Work,
    /// Ore consumed by one refinery batch.
    pub refinery_ore_input: Quantity,
    /// Alloy produced by one refinery batch.
    pub refinery_alloy_output: Quantity,
    /// Refinery work per batch.
    pub refinery_work: Work,
    /// Alloy consumed by one component batch.
    pub component_alloy_input: Quantity,
    /// Components produced by one component batch.
    pub component_output: Quantity,
    /// Component-factory work per batch.
    pub component_work: Work,
    /// Components consumed by the first ship order.
    pub shipyard_component_input: Quantity,
    /// Shipyard work for the first ship order.
    pub shipyard_work: Work,
}

impl Default for PhaseOneConfig {
    fn default() -> Self {
        Self {
            random_seed: 0,
            route_duration: SimulationDuration::from_millis(60_000),
            docking_overhead: SimulationDuration::from_millis(5_000),
            transfer_rate: TransferRate::new(
                NonZeroU64::new(10).expect("PoC transfer rate is non-zero"),
            ),
            facility_storage_capacity: Quantity::from_units(100),
            freighter_cargo_capacity: Quantity::from_units(10),
            ore_batch: Quantity::from_units(10),
            mine_work: Work::from_units(30),
            refinery_ore_input: Quantity::from_units(10),
            refinery_alloy_output: Quantity::from_units(5),
            refinery_work: Work::from_units(60),
            component_alloy_input: Quantity::from_units(5),
            component_output: Quantity::from_units(2),
            component_work: Work::from_units(60),
            shipyard_component_input: Quantity::from_units(4),
            shipyard_work: Work::from_units(120),
        }
    }
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum PhaseOneEvent {
    Transport(TransportEvent),
    ProductionComplete(FacilityId),
    ShipyardComplete,
    RouteEnabled { route_id: RouteId, enabled: bool },
}

impl From<TransportEvent> for PhaseOneEvent {
    fn from(event: TransportEvent) -> Self {
        Self::Transport(event)
    }
}

/// Canonical kind of one processed scenario event.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum ScenarioEventKind {
    /// One transport subsystem event was processed.
    Transport(TransportEvent),
    /// One production facility reached its scheduled completion.
    ProductionComplete(FacilityId),
    /// The shipyard reached its scheduled completion.
    ShipyardComplete,
    /// A directed route changed availability.
    RouteEnabled {
        /// Route whose availability changed.
        route_id: RouteId,
        /// New availability value.
        enabled: bool,
    },
}

/// Canonical record of a processed event and its complete ordering key.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct ScenarioEventRecord {
    /// Event timestamp.
    pub timestamp: SimulationTime,
    /// Deterministic phase within the timestamp.
    pub phase: EventPhase,
    /// Final deterministic ordering value.
    pub creation_sequence: u64,
    /// Domain payload processed at this position.
    pub kind: ScenarioEventKind,
}

/// Explainable reason for an autonomous scenario decision.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum DecisionReason {
    /// The central board selected its highest-ranked reachable match.
    HighestRankedReachableTransport,
    /// No enabled path currently reaches the reserved source.
    NoEnabledRouteToSource,
    /// No enabled path currently reaches the committed destination.
    NoEnabledRouteToDestination,
    /// Destination storage cannot yet accept the committed cargo.
    DestinationCapacityUnavailable,
}

/// Structured record explaining an autonomous transport decision.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct DecisionRecord {
    /// Time at which the decision was made.
    pub timestamp: SimulationTime,
    /// Ship affected by the decision.
    pub ship_id: ShipId,
    /// Concrete job selected or blocked.
    pub job_id: TransportJobId,
    /// Stable reason suitable for UI explanation and tests.
    pub reason: DecisionReason,
}

/// Time spent by a facility in each observable operating state.
#[derive(Clone, Copy, Debug, Default, Eq, PartialEq)]
pub struct FacilityTimeMetrics {
    /// Milliseconds actively producing or constructing.
    pub active_ms: u64,
    /// Milliseconds waiting for inputs or work.
    pub waiting_ms: u64,
    /// Milliseconds with completed output blocked by storage.
    pub output_blocked_ms: u64,
}

/// Aggregated measurements from one scenario run.
#[derive(Clone, Debug, Default, Eq, PartialEq)]
pub struct PhaseOneMetrics {
    /// Material output by `(facility, material)`.
    pub material_produced: BTreeMap<(FacilityId, MaterialId), Quantity>,
    /// Recipe inputs consumed by `(facility, material)`.
    pub material_consumed: BTreeMap<(FacilityId, MaterialId), Quantity>,
    /// Cargo delivered by `(destination inventory, material)`.
    pub cargo_delivered: BTreeMap<(InventoryId, MaterialId), Quantity>,
    /// Operating time by facility.
    pub facility_time: BTreeMap<FacilityId, FacilityTimeMetrics>,
    /// Concrete transport jobs created.
    pub transport_jobs_created: u64,
    /// Transport jobs that delivered all committed cargo.
    pub transport_jobs_completed: u64,
    /// Transport jobs that delivered less than their commitment.
    pub transport_jobs_partially_fulfilled: u64,
    /// Transport jobs cancelled before completion.
    pub transport_jobs_cancelled: u64,
    /// Transport jobs that failed before loading.
    pub transport_jobs_failed: u64,
}

/// Immediate reason an input requirement is currently unsatisfied.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum ShortageCause {
    /// A concrete transport job has committed some of the missing material.
    CommittedDeliveryPending,
    /// No concrete delivery currently covers the missing material.
    AwaitingSupplyOrReachableRoute,
}

/// Current unmet input at one destination inventory.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct ShortageRecord {
    /// Inventory that requires the material.
    pub inventory_id: InventoryId,
    /// Location containing the destination inventory.
    pub location_id: LocationId,
    /// Material required by the active recipe or construction order.
    pub material_id: MaterialId,
    /// Quantity not yet reserved locally.
    pub missing: Quantity,
    /// Immediate logistics status associated with the shortage.
    pub cause: ShortageCause,
}

/// Result of running the first integrated scenario.
#[derive(Clone, Debug, Eq, PartialEq)]
pub struct PhaseOneReport {
    /// Simulation time when the run began.
    pub start_time: SimulationTime,
    /// Simulation time when the run stopped.
    pub end_time: SimulationTime,
    /// Scheduled events processed.
    pub events_processed: u64,
    /// Ships present before running.
    pub starting_ship_count: usize,
    /// Ships present after running.
    pub ending_ship_count: usize,
    /// First ship constructed by the scenario.
    pub constructed_ship_id: Option<ShipId>,
    /// Aggregated material, logistics, and facility measurements.
    pub metrics: PhaseOneMetrics,
    /// FNV-1a fingerprint of canonical event and decision records.
    pub event_log_digest: u64,
    /// FNV-1a fingerprint of selected authoritative final state.
    pub final_state_digest: u64,
    /// Input shortages still present when the run stopped.
    pub current_shortages: Vec<ShortageRecord>,
}

/// Integrated three-location Phase 1 proof-of-concept simulation.
pub struct PhaseOneScenario {
    config: PhaseOneConfig,
    agenda: EventAgenda<PhaseOneEvent>,
    navigation: RouteGraph,
    inventories: InventoryRegistry,
    production_lines: BTreeMap<FacilityId, ProductionLine>,
    production_outputs: BTreeMap<FacilityId, MaterialId>,
    facility_locations: BTreeMap<FacilityId, LocationId>,
    shipyard: Shipyard,
    transport_board: TransportBoard,
    ships: ShipRegistry,
    production_ids: ProductionIdSequences,
    transport_ids: TransportIdSequences,
    reservation_ids: IdSequence<ReservationId>,
    capacity_reservation_ids: IdSequence<CapacityReservationId>,
    ship_ids: IdSequence<ShipId>,
    inventory_ids: IdSequence<InventoryId>,
    constructed_ship_id: Option<ShipId>,
    mine_to_refinery_route: RouteId,
    known_materials: [MaterialId; 3],
    event_records: Vec<ScenarioEventRecord>,
    decision_records: Vec<DecisionRecord>,
    metrics: PhaseOneMetrics,
    metrics_time: SimulationTime,
}

impl PhaseOneScenario {
    /// Builds the approved three-location proof-of-concept fixture.
    ///
    /// # Errors
    ///
    /// Returns a subsystem error when fixture IDs, routes, inventories, queues,
    /// or initial ships cannot be created.
    #[allow(clippy::too_many_lines)]
    pub fn new(config: PhaseOneConfig) -> Result<Self, ScenarioError> {
        let mut location_ids = IdSequence::<LocationId>::new();
        let mine_location = location_ids.allocate()?;
        let refinery_location = location_ids.allocate()?;
        let shipyard_location = location_ids.allocate()?;
        let mut navigation = RouteGraph::new();
        for location in [mine_location, refinery_location, shipyard_location] {
            navigation.add_location(location);
        }
        let (mine_to_refinery_route, _) = navigation.add_bidirectional_routes(
            mine_location,
            refinery_location,
            config.route_duration,
        )?;
        navigation.add_bidirectional_routes(
            refinery_location,
            shipyard_location,
            config.route_duration,
        )?;

        let mut material_ids = IdSequence::<MaterialId>::new();
        let ore = material_ids.allocate()?;
        let alloy = material_ids.allocate()?;
        let components = material_ids.allocate()?;
        let mut organization_ids = IdSequence::<OrganizationId>::new();
        let organization = organization_ids.allocate()?;
        let mut facility_ids = IdSequence::<FacilityId>::new();
        let mine = facility_ids.allocate()?;
        let refinery = facility_ids.allocate()?;
        let component_factory = facility_ids.allocate()?;
        let shipyard_facility = facility_ids.allocate()?;
        let mut inventory_ids = IdSequence::<InventoryId>::new();
        let mine_inventory = inventory_ids.allocate()?;
        let refinery_inventory = inventory_ids.allocate()?;
        let component_inventory = inventory_ids.allocate()?;
        let shipyard_inventory = inventory_ids.allocate()?;

        let mut inventories = InventoryRegistry::new();
        for inventory_id in [
            mine_inventory,
            refinery_inventory,
            component_inventory,
            shipyard_inventory,
        ] {
            inventories.insert(Inventory::new(
                inventory_id,
                config.facility_storage_capacity,
            ))?;
        }

        let throughput = Throughput::new(NonZeroU64::MIN);
        let mut production_ids = ProductionIdSequences::new();
        let mut production_lines = BTreeMap::new();
        let mut mine_line = ProductionLine::new(mine, mine_inventory, throughput);
        mine_line.enqueue(
            &mut production_ids,
            Recipe::new(BTreeMap::new(), ore, config.ore_batch, config.mine_work),
            true,
        )?;
        production_lines.insert(mine, mine_line);
        let mut refinery_line = ProductionLine::new(refinery, refinery_inventory, throughput);
        refinery_line.enqueue(
            &mut production_ids,
            Recipe::new(
                BTreeMap::from([(ore, config.refinery_ore_input)]),
                alloy,
                config.refinery_alloy_output,
                config.refinery_work,
            ),
            true,
        )?;
        production_lines.insert(refinery, refinery_line);
        let mut component_line =
            ProductionLine::new(component_factory, component_inventory, throughput);
        component_line.enqueue(
            &mut production_ids,
            Recipe::new(
                BTreeMap::from([(alloy, config.component_alloy_input)]),
                components,
                config.component_output,
                config.component_work,
            ),
            true,
        )?;
        production_lines.insert(component_factory, component_line);

        let mut blueprint_ids = IdSequence::<ShipBlueprintId>::new();
        let blueprint =
            ShipBlueprint::new(blueprint_ids.allocate()?, config.freighter_cargo_capacity);
        let mut shipyard = Shipyard::new(
            shipyard_facility,
            organization,
            shipyard_location,
            shipyard_inventory,
            throughput,
        );
        let mut shipyard_ids = ShipyardIdSequences::new();
        shipyard.enqueue(
            &mut shipyard_ids,
            blueprint,
            BTreeMap::from([(components, config.shipyard_component_input)]),
            config.shipyard_work,
        )?;

        let mut ships = ShipRegistry::new();
        let mut ship_ids = IdSequence::<ShipId>::new();
        for location in [mine_location, refinery_location] {
            let ship_id = ship_ids.allocate()?;
            let cargo_inventory_id = inventory_ids.allocate()?;
            inventories.insert(Inventory::new(
                cargo_inventory_id,
                config.freighter_cargo_capacity,
            ))?;
            ships.insert_freighter(Ship::new(
                ship_id,
                organization,
                blueprint.id(),
                location,
                cargo_inventory_id,
            ))?;
        }

        Ok(Self {
            config,
            agenda: EventAgenda::new(),
            navigation,
            inventories,
            production_lines,
            production_outputs: BTreeMap::from([
                (mine, ore),
                (refinery, alloy),
                (component_factory, components),
            ]),
            facility_locations: BTreeMap::from([
                (mine, mine_location),
                (refinery, refinery_location),
                (component_factory, shipyard_location),
            ]),
            shipyard,
            transport_board: TransportBoard::new(),
            ships,
            production_ids,
            transport_ids: TransportIdSequences::new(),
            reservation_ids: IdSequence::new(),
            capacity_reservation_ids: IdSequence::new(),
            ship_ids,
            inventory_ids,
            constructed_ship_id: None,
            mine_to_refinery_route,
            known_materials: [ore, alloy, components],
            event_records: Vec::new(),
            decision_records: Vec::new(),
            metrics: PhaseOneMetrics::default(),
            metrics_time: SimulationTime::ZERO,
        })
    }

    /// Schedules the approved proof-of-concept route outage.
    ///
    /// Mine-to-Refinery travel is disabled at 50 seconds and restored at 250
    /// seconds. Ships already traversing that route still complete their leg.
    ///
    /// # Errors
    ///
    /// Returns a scheduling error if the scenario has already advanced beyond
    /// either transition.
    pub fn schedule_approved_route_disruption(&mut self) -> Result<(), ScenarioError> {
        for (timestamp, enabled) in [(50_000, false), (250_000, true)] {
            self.agenda.schedule(
                SimulationTime::from_millis(timestamp),
                EventPhase::StateUpdate,
                EventGeneration::new(0),
                PhaseOneEvent::RouteEnabled {
                    route_id: self.mine_to_refinery_route,
                    enabled,
                },
            )?;
        }
        Ok(())
    }

    /// Returns canonical processed-event records accumulated by this scenario.
    #[must_use]
    pub fn event_records(&self) -> &[ScenarioEventRecord] {
        &self.event_records
    }

    /// Returns explainable autonomous-decision records accumulated by this scenario.
    #[must_use]
    pub fn decision_records(&self) -> &[DecisionRecord] {
        &self.decision_records
    }

    /// Runs until a ship is constructed or `target` is reached.
    ///
    /// # Errors
    ///
    /// Returns a subsystem error when any scheduled state transition or
    /// reconciliation operation fails.
    pub fn run_until_first_ship(
        &mut self,
        target: SimulationTime,
    ) -> Result<PhaseOneReport, ScenarioError> {
        let start_time = self.agenda.current_time();
        let starting_ship_count = self.ships.len();
        self.reconcile(start_time)?;
        let mut events_processed = 0_u64;

        while let Some(scheduled) = self.agenda.pop_next_through(target)? {
            let key = scheduled.key();
            let now = key.timestamp();
            let event = *scheduled.payload();
            self.accrue_facility_time(now)?;
            self.handle_event(event, now)?;
            self.event_records.push(ScenarioEventRecord {
                timestamp: now,
                phase: key.phase(),
                creation_sequence: key.creation_sequence(),
                kind: scenario_event_kind(event),
            });
            events_processed = events_processed
                .checked_add(1)
                .ok_or(ScenarioError::EventCountOverflow)?;
            if self.constructed_ship_id.is_some() {
                break;
            }
            self.reconcile(now)?;
        }

        self.accrue_facility_time(self.agenda.current_time())?;
        let event_log_digest = self.event_log_digest();
        let final_state_digest = self.final_state_digest();
        let current_shortages = self.current_shortages()?;

        Ok(PhaseOneReport {
            start_time,
            end_time: self.agenda.current_time(),
            events_processed,
            starting_ship_count,
            ending_ship_count: self.ships.len(),
            constructed_ship_id: self.constructed_ship_id,
            metrics: self.metrics.clone(),
            event_log_digest,
            final_state_digest,
            current_shortages,
        })
    }

    fn handle_event(
        &mut self,
        event: PhaseOneEvent,
        now: SimulationTime,
    ) -> Result<(), ScenarioError> {
        match event {
            PhaseOneEvent::Transport(event) => {
                let timing = self.transport_timing();
                let job_id = match event {
                    TransportEvent::Arrive { job_id, .. }
                    | TransportEvent::FinishLoading { job_id, .. }
                    | TransportEvent::FinishUnloading { job_id, .. } => job_id,
                };
                let ship_id = self
                    .transport_board
                    .job(job_id)
                    .ok_or(ScenarioError::MissingTransportJob)?
                    .ship_id();
                let freighter = self
                    .ships
                    .freighter_mut(ship_id)
                    .ok_or(ScenarioError::MissingFreighter(ship_id))?;
                let before = self
                    .transport_board
                    .job(job_id)
                    .ok_or(ScenarioError::MissingTransportJob)?;
                self.transport_board.handle_event(
                    event,
                    freighter,
                    &mut self.inventories,
                    &mut self.capacity_reservation_ids,
                    &self.navigation,
                    &mut self.agenda,
                    timing,
                    now,
                )?;
                let after = self
                    .transport_board
                    .job(job_id)
                    .ok_or(ScenarioError::MissingTransportJob)?;
                if before.state() != TransportJobState::Completed
                    && after.state() == TransportJobState::Completed
                {
                    increment_quantity(
                        &mut self.metrics.cargo_delivered,
                        (after.destination_inventory_id(), after.material_id()),
                        after.quantity(),
                    )?;
                    self.metrics.transport_jobs_completed = self
                        .metrics
                        .transport_jobs_completed
                        .checked_add(1)
                        .ok_or(ScenarioError::MetricOverflow)?;
                }
                if before.state() != TransportJobState::FailedBeforeLoading
                    && after.state() == TransportJobState::FailedBeforeLoading
                {
                    self.metrics.transport_jobs_failed = self
                        .metrics
                        .transport_jobs_failed
                        .checked_add(1)
                        .ok_or(ScenarioError::MetricOverflow)?;
                }
            }
            PhaseOneEvent::ProductionComplete(facility_id) => {
                let line = self
                    .production_lines
                    .get_mut(&facility_id)
                    .ok_or(ScenarioError::MissingProductionLine(facility_id))?;
                let inventory = self
                    .inventories
                    .get_mut(line.inventory_id())
                    .ok_or(ScenarioError::MissingInventory(line.inventory_id()))?;
                let output = line.active_job().map(|job| {
                    (
                        job.recipe().output_material(),
                        job.recipe().output_quantity(),
                    )
                });
                if line.complete_active(&mut self.production_ids, inventory, now)?
                    && let Some((material, quantity)) = output
                {
                    increment_quantity(
                        &mut self.metrics.material_produced,
                        (facility_id, material),
                        quantity,
                    )?;
                }
            }
            PhaseOneEvent::ShipyardComplete => {
                self.constructed_ship_id = self.shipyard.complete_active(
                    &mut self.ship_ids,
                    &mut self.inventory_ids,
                    &mut self.inventories,
                    &mut self.ships,
                    now,
                )?;
            }
            PhaseOneEvent::RouteEnabled { route_id, enabled } => {
                self.navigation.set_route_enabled(route_id, enabled)?;
            }
        }
        Ok(())
    }

    fn reconcile(&mut self, now: SimulationTime) -> Result<(), ScenarioError> {
        self.prepare_production(now)?;
        self.prepare_shipyard(now)?;
        self.publish_demands(now)?;
        self.publish_supplies()?;
        self.assign_and_retry_freighters(now)?;
        Ok(())
    }

    fn prepare_production(&mut self, now: SimulationTime) -> Result<(), ScenarioError> {
        let facility_ids: Vec<_> = self.production_lines.keys().copied().collect();
        for facility_id in facility_ids {
            let line = self
                .production_lines
                .get_mut(&facility_id)
                .ok_or(ScenarioError::MissingProductionLine(facility_id))?;
            let inventory_id = line.inventory_id();
            let inventory = self
                .inventories
                .get_mut(inventory_id)
                .ok_or(ScenarioError::MissingInventory(inventory_id))?;
            let inputs = line
                .active_job()
                .map(|job| job.recipe().inputs().clone())
                .unwrap_or_default();
            if let Some(completes_at) =
                line.prepare_active(&mut self.reservation_ids, inventory, now)?
            {
                for (material, quantity) in inputs {
                    increment_quantity(
                        &mut self.metrics.material_consumed,
                        (facility_id, material),
                        quantity,
                    )?;
                }
                self.agenda.schedule(
                    completes_at,
                    EventPhase::PhysicalCompletion,
                    EventGeneration::new(0),
                    PhaseOneEvent::ProductionComplete(facility_id),
                )?;
            }
        }
        Ok(())
    }

    fn prepare_shipyard(&mut self, now: SimulationTime) -> Result<(), ScenarioError> {
        let inventory_id = self.shipyard.inventory_id();
        let inventory = self
            .inventories
            .get_mut(inventory_id)
            .ok_or(ScenarioError::MissingInventory(inventory_id))?;
        let facility_id = self.shipyard.facility_id();
        let inputs = self
            .shipyard
            .active_order()
            .map(|order| order.inputs().clone())
            .unwrap_or_default();
        if let Some(completes_at) =
            self.shipyard
                .prepare_active(&mut self.reservation_ids, inventory, now)?
        {
            for (material, quantity) in inputs {
                increment_quantity(
                    &mut self.metrics.material_consumed,
                    (facility_id, material),
                    quantity,
                )?;
            }
            self.agenda.schedule(
                completes_at,
                EventPhase::PhysicalCompletion,
                EventGeneration::new(0),
                PhaseOneEvent::ShipyardComplete,
            )?;
        }
        Ok(())
    }

    fn publish_demands(&mut self, now: SimulationTime) -> Result<(), ScenarioError> {
        let mut requirements = Vec::new();
        for (facility_id, line) in &self.production_lines {
            let inventory = self
                .inventories
                .get(line.inventory_id())
                .ok_or(ScenarioError::MissingInventory(line.inventory_id()))?;
            let location = *self
                .facility_locations
                .get(facility_id)
                .ok_or(ScenarioError::MissingFacilityLocation(*facility_id))?;
            for (material, quantity) in line.unmet_inputs(inventory) {
                requirements.push((line.inventory_id(), location, material, quantity));
            }
        }
        let shipyard_inventory_id = self.shipyard.inventory_id();
        let shipyard_inventory = self
            .inventories
            .get(shipyard_inventory_id)
            .ok_or(ScenarioError::MissingInventory(shipyard_inventory_id))?;
        for (material, quantity) in self.shipyard.unmet_inputs(shipyard_inventory) {
            requirements.push((
                shipyard_inventory_id,
                self.shipyard.location_id(),
                material,
                quantity,
            ));
        }

        for (inventory_id, location, material, required) in requirements {
            let pending = self
                .transport_board
                .pending_delivery_quantity(inventory_id, material);
            if required > pending {
                self.transport_board.publish_demand(
                    &mut self.transport_ids,
                    inventory_id,
                    location,
                    material,
                    required.checked_sub(pending)?,
                    DemandPriority::new(1),
                    now,
                )?;
            }
        }
        Ok(())
    }

    fn publish_supplies(&mut self) -> Result<(), ScenarioError> {
        for (facility_id, material) in &self.production_outputs {
            let line = self
                .production_lines
                .get(facility_id)
                .ok_or(ScenarioError::MissingProductionLine(*facility_id))?;
            let inventory = self
                .inventories
                .get(line.inventory_id())
                .ok_or(ScenarioError::MissingInventory(line.inventory_id()))?;
            let available = inventory.available(*material);
            let offered = self
                .transport_board
                .offered_quantity(line.inventory_id(), *material);
            if available > offered {
                self.transport_board.publish_supply(
                    &mut self.transport_ids,
                    line.inventory_id(),
                    *self
                        .facility_locations
                        .get(facility_id)
                        .ok_or(ScenarioError::MissingFacilityLocation(*facility_id))?,
                    *material,
                    available.checked_sub(offered)?,
                )?;
            }
        }
        Ok(())
    }

    fn assign_and_retry_freighters(&mut self, now: SimulationTime) -> Result<(), ScenarioError> {
        let timing = self.transport_timing();
        let ship_ids: Vec<_> = self.ships.freighter_ids().collect();
        for ship_id in ship_ids {
            let freighter = self
                .ships
                .freighter_mut(ship_id)
                .ok_or(ScenarioError::MissingFreighter(ship_id))?;
            let existing_job_id = freighter.active_job_id();
            let job_id = if let Some(job_id) = existing_job_id {
                Some(job_id)
            } else {
                self.transport_board.assign_best(
                    &mut self.transport_ids,
                    &mut self.reservation_ids,
                    freighter,
                    &mut self.inventories,
                    &self.navigation,
                    now,
                )?
            };
            if let Some(job_id) = job_id {
                if existing_job_id.is_none() {
                    self.metrics.transport_jobs_created = self
                        .metrics
                        .transport_jobs_created
                        .checked_add(1)
                        .ok_or(ScenarioError::MetricOverflow)?;
                    self.decision_records.push(DecisionRecord {
                        timestamp: now,
                        ship_id,
                        job_id,
                        reason: DecisionReason::HighestRankedReachableTransport,
                    });
                }
                let before = self
                    .transport_board
                    .job(job_id)
                    .ok_or(ScenarioError::MissingTransportJob)?
                    .state();
                self.transport_board.start_or_retry(
                    job_id,
                    freighter,
                    &mut self.inventories,
                    &mut self.capacity_reservation_ids,
                    &self.navigation,
                    &mut self.agenda,
                    timing,
                    now,
                )?;
                let after = self
                    .transport_board
                    .job(job_id)
                    .ok_or(ScenarioError::MissingTransportJob)?
                    .state();
                if before != after
                    && let Some(reason) = waiting_reason(after)
                {
                    self.decision_records.push(DecisionRecord {
                        timestamp: now,
                        ship_id,
                        job_id,
                        reason,
                    });
                }
            }
        }
        Ok(())
    }

    fn transport_timing(&self) -> TransportTiming {
        TransportTiming::new(
            self.config.docking_overhead,
            self.config.transfer_rate,
            self.config.transfer_rate,
        )
    }

    fn accrue_facility_time(&mut self, now: SimulationTime) -> Result<(), ScenarioError> {
        let elapsed = now
            .as_millis()
            .checked_sub(self.metrics_time.as_millis())
            .ok_or(ScenarioError::MetricOverflow)?;
        if elapsed == 0 {
            return Ok(());
        }

        for (facility_id, line) in &self.production_lines {
            let state = line.active_job().map(ProductionJob::state);
            let metrics = self.metrics.facility_time.entry(*facility_id).or_default();
            match state {
                Some(ProductionJobState::Running { .. }) => {
                    metrics.active_ms = checked_metric_add(metrics.active_ms, elapsed)?;
                }
                Some(ProductionJobState::CompletedAwaitingStorage) => {
                    metrics.output_blocked_ms =
                        checked_metric_add(metrics.output_blocked_ms, elapsed)?;
                }
                Some(ProductionJobState::WaitingForInputs | ProductionJobState::Completed)
                | None => {
                    metrics.waiting_ms = checked_metric_add(metrics.waiting_ms, elapsed)?;
                }
            }
        }

        let shipyard_metrics = self
            .metrics
            .facility_time
            .entry(self.shipyard.facility_id())
            .or_default();
        match self
            .shipyard
            .active_order()
            .map(ShipConstructionOrder::state)
        {
            Some(ShipConstructionOrderState::Running { .. }) => {
                shipyard_metrics.active_ms =
                    checked_metric_add(shipyard_metrics.active_ms, elapsed)?;
            }
            Some(ShipConstructionOrderState::WaitingForInputs) | None => {
                shipyard_metrics.waiting_ms =
                    checked_metric_add(shipyard_metrics.waiting_ms, elapsed)?;
            }
            Some(ShipConstructionOrderState::Completed { .. }) => {}
        }
        self.metrics_time = now;
        Ok(())
    }

    fn event_log_digest(&self) -> u64 {
        let mut hash = Fnv1a64::new();
        hash.write_u64(u64::try_from(self.event_records.len()).unwrap_or(u64::MAX));
        for record in &self.event_records {
            hash_event_record(&mut hash, *record);
        }
        hash.write_u64(u64::try_from(self.decision_records.len()).unwrap_or(u64::MAX));
        for record in &self.decision_records {
            hash.write_u64(record.timestamp.as_millis());
            hash.write_u64(record.ship_id.get());
            hash.write_u64(record.job_id.get());
            hash.write_u8(decision_reason_tag(record.reason));
        }
        hash.finish()
    }

    fn final_state_digest(&self) -> u64 {
        let mut hash = Fnv1a64::new();
        hash.write_u64(self.agenda.current_time().as_millis());
        hash.write_u64(self.config.random_seed);
        hash.write_u64(self.constructed_ship_id.map_or(0, ShipId::get));
        let route = self
            .navigation
            .route(self.mine_to_refinery_route)
            .expect("approved fixture route must remain registered");
        hash.write_u64(route.id().get());
        hash.write_bool(route.is_enabled());

        let mut inventory_ids = BTreeSet::new();
        inventory_ids.extend(
            self.production_lines
                .values()
                .map(ProductionLine::inventory_id),
        );
        inventory_ids.insert(self.shipyard.inventory_id());
        for ship_id in self.ships.freighter_ids() {
            let freighter = self
                .ships
                .freighter(ship_id)
                .expect("freighter ID must resolve");
            inventory_ids.insert(freighter.cargo_inventory_id());
            hash.write_u64(ship_id.get());
            hash.write_u64(freighter.location_id().get());
            hash.write_u64(freighter.active_job_id().map_or(0, TransportJobId::get));
        }
        for inventory_id in inventory_ids {
            let inventory = self
                .inventories
                .get(inventory_id)
                .expect("registered scenario inventory must resolve");
            hash.write_u64(inventory_id.get());
            hash.write_u64(inventory.total_stored().as_units());
            hash.write_u64(inventory.reserved_capacity().as_units());
            for material in self.known_materials {
                hash.write_u64(material.get());
                hash.write_u64(inventory.stored(material).as_units());
                hash.write_u64(inventory.reserved(material).as_units());
            }
        }
        for job in self.transport_board.jobs() {
            hash.write_u64(job.id().get());
            hash.write_u64(job.ship_id().get());
            hash.write_u64(job.material_id().get());
            hash.write_u64(job.quantity().as_units());
            hash_transport_state(&mut hash, job.state());
        }
        hash.finish()
    }

    fn current_shortages(&self) -> Result<Vec<ShortageRecord>, ScenarioError> {
        let mut shortages = Vec::new();
        for (facility_id, line) in &self.production_lines {
            let inventory = self
                .inventories
                .get(line.inventory_id())
                .ok_or(ScenarioError::MissingInventory(line.inventory_id()))?;
            let location_id = *self
                .facility_locations
                .get(facility_id)
                .ok_or(ScenarioError::MissingFacilityLocation(*facility_id))?;
            for (material_id, missing) in line.unmet_inputs(inventory) {
                shortages.push(self.shortage_record(
                    line.inventory_id(),
                    location_id,
                    material_id,
                    missing,
                ));
            }
        }

        let inventory_id = self.shipyard.inventory_id();
        let inventory = self
            .inventories
            .get(inventory_id)
            .ok_or(ScenarioError::MissingInventory(inventory_id))?;
        for (material_id, missing) in self.shipyard.unmet_inputs(inventory) {
            shortages.push(self.shortage_record(
                inventory_id,
                self.shipyard.location_id(),
                material_id,
                missing,
            ));
        }
        Ok(shortages)
    }

    fn shortage_record(
        &self,
        inventory_id: InventoryId,
        location_id: LocationId,
        material_id: MaterialId,
        missing: Quantity,
    ) -> ShortageRecord {
        let committed = self
            .transport_board
            .committed_delivery_quantity(inventory_id, material_id);
        ShortageRecord {
            inventory_id,
            location_id,
            material_id,
            missing,
            cause: if committed > Quantity::ZERO {
                ShortageCause::CommittedDeliveryPending
            } else {
                ShortageCause::AwaitingSupplyOrReachableRoute
            },
        }
    }
}

fn scenario_event_kind(event: PhaseOneEvent) -> ScenarioEventKind {
    match event {
        PhaseOneEvent::Transport(event) => ScenarioEventKind::Transport(event),
        PhaseOneEvent::ProductionComplete(facility_id) => {
            ScenarioEventKind::ProductionComplete(facility_id)
        }
        PhaseOneEvent::ShipyardComplete => ScenarioEventKind::ShipyardComplete,
        PhaseOneEvent::RouteEnabled { route_id, enabled } => {
            ScenarioEventKind::RouteEnabled { route_id, enabled }
        }
    }
}

fn waiting_reason(state: TransportJobState) -> Option<DecisionReason> {
    match state {
        TransportJobState::WaitingForRouteToSource => Some(DecisionReason::NoEnabledRouteToSource),
        TransportJobState::WaitingForRouteToDestination => {
            Some(DecisionReason::NoEnabledRouteToDestination)
        }
        TransportJobState::WaitingForDestinationCapacity => {
            Some(DecisionReason::DestinationCapacityUnavailable)
        }
        _ => None,
    }
}

fn checked_metric_add(current: u64, added: u64) -> Result<u64, ScenarioError> {
    current
        .checked_add(added)
        .ok_or(ScenarioError::MetricOverflow)
}

fn increment_quantity<K: Ord + Copy>(
    values: &mut BTreeMap<K, Quantity>,
    key: K,
    added: Quantity,
) -> Result<(), ScenarioError> {
    let current = values.get(&key).copied().unwrap_or(Quantity::ZERO);
    values.insert(key, current.checked_add(added)?);
    Ok(())
}

const FNV_OFFSET_BASIS_64: u64 = 0xcbf2_9ce4_8422_2325;
const FNV_PRIME_64: u64 = 0x0000_0100_0000_01b3;

struct Fnv1a64(u64);

impl Fnv1a64 {
    const fn new() -> Self {
        Self(FNV_OFFSET_BASIS_64)
    }

    fn write_u8(&mut self, value: u8) {
        self.0 ^= u64::from(value);
        self.0 = self.0.wrapping_mul(FNV_PRIME_64);
    }

    fn write_bool(&mut self, value: bool) {
        self.write_u8(u8::from(value));
    }

    fn write_u64(&mut self, value: u64) {
        for byte in value.to_le_bytes() {
            self.write_u8(byte);
        }
    }

    const fn finish(self) -> u64 {
        self.0
    }
}

fn hash_event_record(hash: &mut Fnv1a64, record: ScenarioEventRecord) {
    hash.write_u64(record.timestamp.as_millis());
    hash.write_u8(match record.phase {
        EventPhase::PhysicalCompletion => 0,
        EventPhase::StateUpdate => 1,
        EventPhase::Decision => 2,
    });
    hash.write_u64(record.creation_sequence);
    match record.kind {
        ScenarioEventKind::Transport(event) => {
            hash.write_u8(0);
            hash_transport_event(hash, event);
        }
        ScenarioEventKind::ProductionComplete(facility_id) => {
            hash.write_u8(1);
            hash.write_u64(facility_id.get());
        }
        ScenarioEventKind::ShipyardComplete => hash.write_u8(2),
        ScenarioEventKind::RouteEnabled { route_id, enabled } => {
            hash.write_u8(3);
            hash.write_u64(route_id.get());
            hash.write_bool(enabled);
        }
    }
}

fn hash_transport_event(hash: &mut Fnv1a64, event: TransportEvent) {
    match event {
        TransportEvent::Arrive {
            job_id,
            generation,
            route_id,
            target,
        } => {
            hash.write_u8(0);
            hash.write_u64(job_id.get());
            hash.write_u64(generation.get());
            hash.write_u64(route_id.get());
            hash.write_u8(match target {
                crate::TravelTarget::Source => 0,
                crate::TravelTarget::Destination => 1,
            });
        }
        TransportEvent::FinishLoading { job_id, generation } => {
            hash.write_u8(1);
            hash.write_u64(job_id.get());
            hash.write_u64(generation.get());
        }
        TransportEvent::FinishUnloading { job_id, generation } => {
            hash.write_u8(2);
            hash.write_u64(job_id.get());
            hash.write_u64(generation.get());
        }
    }
}

fn hash_transport_state(hash: &mut Fnv1a64, state: TransportJobState) {
    match state {
        TransportJobState::Assigned => hash.write_u8(0),
        TransportJobState::WaitingForRouteToSource => hash.write_u8(1),
        TransportJobState::TravelingToSource {
            route_id,
            arrives_at,
        } => {
            hash.write_u8(2);
            hash.write_u64(route_id.get());
            hash.write_u64(arrives_at.as_millis());
        }
        TransportJobState::Loading { completes_at } => {
            hash.write_u8(3);
            hash.write_u64(completes_at.as_millis());
        }
        TransportJobState::WaitingForRouteToDestination => hash.write_u8(4),
        TransportJobState::TravelingToDestination {
            route_id,
            arrives_at,
        } => {
            hash.write_u8(5);
            hash.write_u64(route_id.get());
            hash.write_u64(arrives_at.as_millis());
        }
        TransportJobState::WaitingForDestinationCapacity => hash.write_u8(6),
        TransportJobState::Unloading { completes_at } => {
            hash.write_u8(7);
            hash.write_u64(completes_at.as_millis());
        }
        TransportJobState::Completed => hash.write_u8(8),
        TransportJobState::FailedBeforeLoading => hash.write_u8(9),
    }
}

const fn decision_reason_tag(reason: DecisionReason) -> u8 {
    match reason {
        DecisionReason::HighestRankedReachableTransport => 0,
        DecisionReason::NoEnabledRouteToSource => 1,
        DecisionReason::NoEnabledRouteToDestination => 2,
        DecisionReason::DestinationCapacityUnavailable => 3,
    }
}

/// Errors produced by the integrated Phase 1 scenario.
#[derive(Debug)]
pub enum ScenarioError {
    /// Typed identifier allocation failed.
    IdAllocation(IdAllocationError),
    /// Navigation setup or lookup failed.
    Navigation(crate::NavigationError),
    /// Inventory behavior failed.
    Inventory(InventoryError),
    /// Material production failed.
    Production(ProductionError),
    /// Shipyard behavior failed.
    Shipyard(ShipyardError),
    /// Ship registration failed.
    Ship(ShipError),
    /// Transport behavior failed.
    Transport(TransportError),
    /// Event scheduling failed.
    Schedule(ScheduleError),
    /// Time arithmetic failed.
    Time(SimulationTimeError),
    /// Quantity arithmetic failed.
    Quantity(QuantityError),
    /// A configured inventory is missing.
    MissingInventory(InventoryId),
    /// A configured production line is missing.
    MissingProductionLine(FacilityId),
    /// A facility has no configured location.
    MissingFacilityLocation(FacilityId),
    /// A freighter record is missing.
    MissingFreighter(ShipId),
    /// A scheduled transport job is missing.
    MissingTransportJob,
    /// Processed event count exceeded its integer range.
    EventCountOverflow,
    /// A metrics accumulator exceeded its integer range.
    MetricOverflow,
}

macro_rules! scenario_error_from {
    ($source:ty, $variant:ident) => {
        impl From<$source> for ScenarioError {
            fn from(error: $source) -> Self {
                Self::$variant(error)
            }
        }
    };
}

scenario_error_from!(IdAllocationError, IdAllocation);
scenario_error_from!(crate::NavigationError, Navigation);
scenario_error_from!(InventoryError, Inventory);
scenario_error_from!(ProductionError, Production);
scenario_error_from!(ShipyardError, Shipyard);
scenario_error_from!(ShipError, Ship);
scenario_error_from!(TransportError, Transport);
scenario_error_from!(ScheduleError, Schedule);
scenario_error_from!(SimulationTimeError, Time);
scenario_error_from!(QuantityError, Quantity);

impl Display for ScenarioError {
    fn fmt(&self, formatter: &mut Formatter<'_>) -> fmt::Result {
        match self {
            Self::IdAllocation(error) => Display::fmt(error, formatter),
            Self::Navigation(error) => Display::fmt(error, formatter),
            Self::Inventory(error) => Display::fmt(error, formatter),
            Self::Production(error) => Display::fmt(error, formatter),
            Self::Shipyard(error) => Display::fmt(error, formatter),
            Self::Ship(error) => Display::fmt(error, formatter),
            Self::Transport(error) => Display::fmt(error, formatter),
            Self::Schedule(error) => Display::fmt(error, formatter),
            Self::Time(error) => Display::fmt(error, formatter),
            Self::Quantity(error) => Display::fmt(error, formatter),
            Self::MissingInventory(id) => write!(formatter, "missing inventory {id}"),
            Self::MissingProductionLine(id) => write!(formatter, "missing production line {id}"),
            Self::MissingFacilityLocation(id) => {
                write!(formatter, "missing location for facility {id}")
            }
            Self::MissingFreighter(id) => write!(formatter, "missing freighter {id}"),
            Self::MissingTransportJob => formatter.write_str("missing scheduled transport job"),
            Self::EventCountOverflow => formatter.write_str("processed event count overflow"),
            Self::MetricOverflow => formatter.write_str("scenario metrics overflow"),
        }
    }
}

impl Error for ScenarioError {}

#[cfg(test)]
mod tests {
    use super::{DecisionReason, PhaseOneConfig, PhaseOneScenario, ScenarioEventKind};
    use crate::SimulationTime;

    #[test]
    fn approved_poc_chain_constructs_a_persistent_ship() {
        let mut scenario = PhaseOneScenario::new(PhaseOneConfig::default())
            .expect("approved fixture should build");

        let report = scenario
            .run_until_first_ship(SimulationTime::from_millis(1_000_000))
            .expect("approved fixture should run");

        assert!(report.constructed_ship_id.is_some());
        assert_eq!(report.starting_ship_count, 2);
        assert_eq!(report.ending_ship_count, 3);
        assert!(report.events_processed > 0);
        assert!(report.metrics.transport_jobs_created > 0);
        assert!(report.metrics.transport_jobs_created >= report.metrics.transport_jobs_completed);
        assert!(report.metrics.transport_jobs_completed > 0);
        assert_eq!(report.metrics.transport_jobs_failed, 0);
        assert_eq!(report.metrics.facility_time.len(), 4);
        assert!(
            report
                .metrics
                .material_produced
                .values()
                .all(|quantity| *quantity > crate::Quantity::ZERO)
        );
        assert!(!scenario.event_records().is_empty());
        assert!(
            scenario
                .decision_records()
                .iter()
                .any(|record| { record.reason == DecisionReason::HighestRankedReachableTransport })
        );
    }

    #[test]
    fn approved_disruption_delays_then_recovers_ship_construction() {
        let mut baseline = PhaseOneScenario::new(PhaseOneConfig::default())
            .expect("baseline fixture should build");
        let baseline_report = baseline
            .run_until_first_ship(SimulationTime::from_millis(1_000_000))
            .expect("baseline fixture should run");

        let mut disrupted = PhaseOneScenario::new(PhaseOneConfig::default())
            .expect("disruption fixture should build");
        disrupted
            .schedule_approved_route_disruption()
            .expect("route transitions should schedule");
        let shortage_report = disrupted
            .run_until_first_ship(SimulationTime::from_millis(200_000))
            .expect("disrupted fixture should reach its shortage window");
        assert!(shortage_report.constructed_ship_id.is_none());
        assert!(!shortage_report.current_shortages.is_empty());
        let disrupted_report = disrupted
            .run_until_first_ship(SimulationTime::from_millis(1_000_000))
            .expect("disrupted fixture should recover");

        assert!(disrupted_report.constructed_ship_id.is_some());
        assert!(disrupted_report.end_time > baseline_report.end_time);
        assert_ne!(
            disrupted_report.event_log_digest,
            baseline_report.event_log_digest
        );
        let route_changes: Vec<_> = disrupted
            .event_records()
            .iter()
            .filter_map(|record| match record.kind {
                ScenarioEventKind::RouteEnabled { enabled, .. } => {
                    Some((record.timestamp.as_millis(), enabled))
                }
                _ => None,
            })
            .collect();
        assert_eq!(route_changes, [(50_000, false), (250_000, true)]);
    }

    #[test]
    fn identical_runs_produce_identical_event_and_state_digests() {
        let run = || {
            let mut scenario = PhaseOneScenario::new(PhaseOneConfig::default())
                .expect("determinism fixture should build");
            scenario
                .run_until_first_ship(SimulationTime::from_millis(1_000_000))
                .expect("determinism fixture should run")
        };

        let first = run();
        let second = run();

        assert_eq!(first.event_log_digest, second.event_log_digest);
        assert_eq!(first.final_state_digest, second.final_state_digest);
        assert_eq!(first.metrics, second.metrics);
    }
}
