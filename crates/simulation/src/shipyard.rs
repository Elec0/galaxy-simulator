use crate::{
    FacilityId, IdAllocationError, IdSequence, Inventory, InventoryError, InventoryId,
    InventoryRegistry, LocationId, MaterialId, OrganizationId, ProductionError, Quantity,
    QuantityError, ReservationId, ReservationOwner, Ship, ShipBlueprint, ShipConstructionOrderId,
    ShipError, ShipId, ShipRegistry, SimulationTime, SimulationTimeError, Throughput, Work,
};
use std::collections::{BTreeMap, VecDeque};
use std::error::Error;
use std::fmt::{self, Display, Formatter};

/// Lifecycle of a finite ship construction order.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum ShipConstructionOrderState {
    /// The order is incrementally reserving materials.
    WaitingForInputs,
    /// Inputs were consumed and construction is underway.
    Running {
        /// Scheduled construction completion.
        completes_at: SimulationTime,
    },
    /// A persistent ship was created.
    Completed {
        /// Constructed ship.
        ship_id: ShipId,
    },
}

/// One explicit finite ship construction request.
#[derive(Clone, Debug, Eq, PartialEq)]
pub struct ShipConstructionOrder {
    id: ShipConstructionOrderId,
    blueprint: ShipBlueprint,
    inputs: BTreeMap<MaterialId, Quantity>,
    required_work: Work,
    state: ShipConstructionOrderState,
    reservation_ids: BTreeMap<MaterialId, Vec<ReservationId>>,
}

impl ShipConstructionOrder {
    /// Returns the stable construction order ID.
    #[must_use]
    pub const fn id(&self) -> ShipConstructionOrderId {
        self.id
    }

    /// Returns the ship blueprint.
    #[must_use]
    pub const fn blueprint(&self) -> ShipBlueprint {
        self.blueprint
    }

    /// Returns required construction materials.
    #[must_use]
    pub const fn inputs(&self) -> &BTreeMap<MaterialId, Quantity> {
        &self.inputs
    }

    /// Returns required construction work.
    #[must_use]
    pub const fn required_work(&self) -> Work {
        self.required_work
    }

    /// Returns the order lifecycle state.
    #[must_use]
    pub const fn state(&self) -> ShipConstructionOrderState {
        self.state
    }

    fn reserved_input(&self, inventory: &Inventory, material: MaterialId) -> Quantity {
        let units = self
            .reservation_ids
            .get(&material)
            .into_iter()
            .flatten()
            .filter_map(|id| inventory.reservation(*id))
            .fold(0_u64, |total, reservation| {
                total.saturating_add(reservation.quantity().as_units())
            });
        Quantity::from_units(units)
    }
}

/// Deterministic IDs owned specifically by shipyard queues.
#[derive(Clone, Debug, Default)]
pub struct ShipyardIdSequences {
    orders: IdSequence<ShipConstructionOrderId>,
}

impl ShipyardIdSequences {
    /// Creates construction order IDs beginning at one.
    #[must_use]
    pub const fn new() -> Self {
        Self {
            orders: IdSequence::new(),
        }
    }
}

/// Finite FIFO construction capability that creates persistent freighters.
#[derive(Clone, Debug)]
pub struct Shipyard {
    facility_id: FacilityId,
    organization_id: OrganizationId,
    location_id: LocationId,
    inventory_id: InventoryId,
    throughput: Throughput,
    active: Option<ShipConstructionOrder>,
    queued: VecDeque<ShipConstructionOrder>,
    completed: BTreeMap<ShipConstructionOrderId, ShipConstructionOrder>,
}

impl Shipyard {
    /// Creates an idle shipyard.
    #[must_use]
    pub const fn new(
        facility_id: FacilityId,
        organization_id: OrganizationId,
        location_id: LocationId,
        inventory_id: InventoryId,
        throughput: Throughput,
    ) -> Self {
        Self {
            facility_id,
            organization_id,
            location_id,
            inventory_id,
            throughput,
            active: None,
            queued: VecDeque::new(),
            completed: BTreeMap::new(),
        }
    }

    /// Returns the owning facility.
    #[must_use]
    pub const fn facility_id(&self) -> FacilityId {
        self.facility_id
    }

    /// Returns the owning organization.
    #[must_use]
    pub const fn organization_id(&self) -> OrganizationId {
        self.organization_id
    }

    /// Returns the shipyard location.
    #[must_use]
    pub const fn location_id(&self) -> LocationId {
        self.location_id
    }

    /// Returns the world-owned construction inventory.
    #[must_use]
    pub const fn inventory_id(&self) -> InventoryId {
        self.inventory_id
    }

    /// Returns the active order, including one waiting for inputs.
    #[must_use]
    pub const fn active_order(&self) -> Option<&ShipConstructionOrder> {
        self.active.as_ref()
    }

    /// Returns a completed construction order.
    #[must_use]
    pub fn completed_order(
        &self,
        order_id: ShipConstructionOrderId,
    ) -> Option<&ShipConstructionOrder> {
        self.completed.get(&order_id)
    }

    /// Returns active-order input quantities not yet reserved.
    #[must_use]
    pub fn unmet_inputs(&self, inventory: &Inventory) -> BTreeMap<MaterialId, Quantity> {
        let Some(order) = self.active.as_ref() else {
            return BTreeMap::new();
        };
        if order.state != ShipConstructionOrderState::WaitingForInputs {
            return BTreeMap::new();
        }
        order
            .inputs
            .iter()
            .filter_map(|(material, required)| {
                let reserved = order.reserved_input(inventory, *material);
                let missing = required.checked_sub(reserved).ok()?;
                (missing > Quantity::ZERO).then_some((*material, missing))
            })
            .collect()
    }

    /// Adds a finite order to the FIFO queue.
    ///
    /// # Errors
    ///
    /// Returns [`ShipyardError::IdAllocation`] if no order ID remains.
    pub fn enqueue(
        &mut self,
        ids: &mut ShipyardIdSequences,
        blueprint: ShipBlueprint,
        inputs: BTreeMap<MaterialId, Quantity>,
        required_work: Work,
    ) -> Result<ShipConstructionOrderId, ShipyardError> {
        let id = ids.orders.allocate()?;
        let order = ShipConstructionOrder {
            id,
            blueprint,
            inputs,
            required_work,
            state: ShipConstructionOrderState::WaitingForInputs,
            reservation_ids: BTreeMap::new(),
        };
        if self.active.is_none() {
            self.active = Some(order);
        } else {
            self.queued.push_back(order);
        }
        Ok(id)
    }

    /// Reserves available inputs and starts construction when all are present.
    ///
    /// Returns the scheduled completion timestamp when this call starts work.
    ///
    /// # Errors
    ///
    /// Returns an error without violating material conservation when inventory,
    /// reservation, ID, or timeline operations fail.
    pub fn prepare_active(
        &mut self,
        reservation_ids: &mut IdSequence<ReservationId>,
        inventory: &mut Inventory,
        now: SimulationTime,
    ) -> Result<Option<SimulationTime>, ShipyardError> {
        self.require_inventory(inventory)?;
        let Some(order) = self.active.as_mut() else {
            return Ok(None);
        };
        if order.state != ShipConstructionOrderState::WaitingForInputs {
            return Ok(None);
        }

        for (material, required) in &order.inputs {
            let reserved = order.reserved_input(inventory, *material);
            let missing = required.checked_sub(reserved)?;
            let to_reserve = missing.min(inventory.available(*material));
            if to_reserve == Quantity::ZERO {
                continue;
            }
            let reservation_id = reservation_ids.allocate()?;
            inventory.reserve(
                reservation_id,
                *material,
                to_reserve,
                ReservationOwner::ShipConstructionOrder(order.id),
            )?;
            order
                .reservation_ids
                .entry(*material)
                .or_default()
                .push(reservation_id);
        }

        let all_reserved = order
            .inputs
            .iter()
            .all(|(material, required)| order.reserved_input(inventory, *material) == *required);
        if !all_reserved {
            return Ok(None);
        }

        let owner = ReservationOwner::ShipConstructionOrder(order.id);
        let reservations: Vec<_> = order.reservation_ids.values().flatten().copied().collect();
        inventory.consume_reservations(&reservations, owner)?;
        order.reservation_ids.clear();
        let duration = self.throughput.duration_for(order.required_work)?;
        let completes_at = now.checked_add(duration)?;
        order.state = ShipConstructionOrderState::Running { completes_at };
        Ok(Some(completes_at))
    }

    /// Completes due construction and creates a persistent idle freighter.
    ///
    /// # Errors
    ///
    /// Returns an error when IDs, inventory registration, ship registration,
    /// or supplied construction inventory are invalid.
    #[allow(clippy::too_many_arguments)]
    pub fn complete_active(
        &mut self,
        ship_ids: &mut IdSequence<ShipId>,
        inventory_ids: &mut IdSequence<InventoryId>,
        inventories: &mut InventoryRegistry,
        ships: &mut ShipRegistry,
        now: SimulationTime,
    ) -> Result<Option<ShipId>, ShipyardError> {
        let Some(order) = self.active.as_ref() else {
            return Ok(None);
        };
        if !matches!(
            order.state,
            ShipConstructionOrderState::Running { completes_at } if now >= completes_at
        ) {
            return Ok(None);
        }

        let ship_id = ship_ids.allocate()?;
        let cargo_inventory_id = inventory_ids.allocate()?;
        inventories.insert(Inventory::new(
            cargo_inventory_id,
            order.blueprint.cargo_capacity(),
        ))?;
        ships.insert_freighter(Ship::new(
            ship_id,
            self.organization_id,
            order.blueprint.id(),
            self.location_id,
            cargo_inventory_id,
        ))?;

        let Some(mut completed) = self.active.take() else {
            return Ok(None);
        };
        completed.state = ShipConstructionOrderState::Completed { ship_id };
        self.completed.insert(completed.id, completed);
        self.active = self.queued.pop_front();
        Ok(Some(ship_id))
    }

    fn require_inventory(&self, inventory: &Inventory) -> Result<(), ShipyardError> {
        if inventory.id() == self.inventory_id {
            Ok(())
        } else {
            Err(ShipyardError::WrongInventory {
                expected: self.inventory_id,
                actual: inventory.id(),
            })
        }
    }
}

/// Errors produced by shipyard queue and construction operations.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum ShipyardError {
    /// Checked quantity arithmetic failed.
    Quantity(QuantityError),
    /// Inventory or reservation behavior failed.
    Inventory(InventoryError),
    /// Production timing calculation failed.
    Production(ProductionError),
    /// Checked simulation-time arithmetic failed.
    Time(SimulationTimeError),
    /// Deterministic identifier allocation failed.
    IdAllocation(IdAllocationError),
    /// Persistent ship registration failed.
    Ship(ShipError),
    /// A caller supplied the wrong construction inventory.
    WrongInventory {
        /// Configured inventory.
        expected: InventoryId,
        /// Supplied inventory.
        actual: InventoryId,
    },
}

impl From<QuantityError> for ShipyardError {
    fn from(error: QuantityError) -> Self {
        Self::Quantity(error)
    }
}

impl From<InventoryError> for ShipyardError {
    fn from(error: InventoryError) -> Self {
        Self::Inventory(error)
    }
}

impl From<ProductionError> for ShipyardError {
    fn from(error: ProductionError) -> Self {
        Self::Production(error)
    }
}

impl From<SimulationTimeError> for ShipyardError {
    fn from(error: SimulationTimeError) -> Self {
        Self::Time(error)
    }
}

impl From<IdAllocationError> for ShipyardError {
    fn from(error: IdAllocationError) -> Self {
        Self::IdAllocation(error)
    }
}

impl From<ShipError> for ShipyardError {
    fn from(error: ShipError) -> Self {
        Self::Ship(error)
    }
}

impl Display for ShipyardError {
    fn fmt(&self, formatter: &mut Formatter<'_>) -> fmt::Result {
        match self {
            Self::Quantity(error) => Display::fmt(error, formatter),
            Self::Inventory(error) => Display::fmt(error, formatter),
            Self::Production(error) => Display::fmt(error, formatter),
            Self::Time(error) => Display::fmt(error, formatter),
            Self::IdAllocation(error) => Display::fmt(error, formatter),
            Self::Ship(error) => Display::fmt(error, formatter),
            Self::WrongInventory { expected, actual } => write!(
                formatter,
                "shipyard expected inventory {expected}, but received {actual}"
            ),
        }
    }
}

impl Error for ShipyardError {}

#[cfg(test)]
mod tests {
    use super::{ShipConstructionOrderState, Shipyard, ShipyardIdSequences};
    use crate::{
        FacilityId, IdSequence, Inventory, InventoryId, InventoryRegistry, LocationId, MaterialId,
        OrganizationId, Quantity, ReservationId, ShipBlueprint, ShipBlueprintId, ShipId,
        ShipRegistry, SimulationTime, Throughput, Work,
    };
    use std::collections::BTreeMap;
    use std::num::NonZeroU64;

    #[test]
    fn completed_order_creates_persistent_idle_freighter() {
        let mut facility_ids = IdSequence::<FacilityId>::new();
        let mut organization_ids = IdSequence::<OrganizationId>::new();
        let mut location_ids = IdSequence::<LocationId>::new();
        let mut inventory_ids = IdSequence::<InventoryId>::new();
        let mut material_ids = IdSequence::<MaterialId>::new();
        let mut blueprint_ids = IdSequence::<ShipBlueprintId>::new();
        let mut ship_ids = IdSequence::<ShipId>::new();
        let mut reservation_ids = IdSequence::<ReservationId>::new();
        let inventory_id = inventory_ids.allocate().expect("shipyard inventory ID");
        let material_id = material_ids.allocate().expect("component material ID");
        let organization_id = organization_ids.allocate().expect("organization ID");
        let location_id = location_ids.allocate().expect("location ID");
        let blueprint = ShipBlueprint::new(
            blueprint_ids.allocate().expect("blueprint ID"),
            Quantity::from_units(7),
        );
        let mut inventories = InventoryRegistry::new();
        let mut construction_inventory = Inventory::new(inventory_id, Quantity::from_units(10));
        construction_inventory
            .add(material_id, Quantity::from_units(4))
            .expect("components should fit");
        inventories
            .insert(construction_inventory)
            .expect("shipyard inventory should be unique");
        let mut shipyard = Shipyard::new(
            facility_ids.allocate().expect("facility ID"),
            organization_id,
            location_id,
            inventory_id,
            Throughput::new(NonZeroU64::new(2).expect("non-zero throughput")),
        );
        let mut shipyard_ids = ShipyardIdSequences::new();
        let order_id = shipyard
            .enqueue(
                &mut shipyard_ids,
                blueprint,
                BTreeMap::from([(material_id, Quantity::from_units(4))]),
                Work::from_units(4),
            )
            .expect("construction order should enqueue");

        let completes_at = shipyard
            .prepare_active(
                &mut reservation_ids,
                inventories
                    .get_mut(inventory_id)
                    .expect("shipyard inventory"),
                SimulationTime::ZERO,
            )
            .expect("construction should prepare")
            .expect("construction should start");
        let mut ships = ShipRegistry::new();
        let ship_id = shipyard
            .complete_active(
                &mut ship_ids,
                &mut inventory_ids,
                &mut inventories,
                &mut ships,
                completes_at,
            )
            .expect("construction should complete")
            .expect("ship should be created");

        let ship = ships.ship(ship_id).expect("persistent ship");
        assert_eq!(ship.organization_id(), organization_id);
        assert_eq!(ship.blueprint_id(), blueprint.id());
        assert_eq!(ship.location_id(), location_id);
        assert_eq!(
            ships.freighter(ship_id).expect("freighter").active_job_id(),
            None
        );
        assert_eq!(
            inventories
                .get(ship.cargo_inventory_id())
                .expect("cargo inventory")
                .capacity(),
            Quantity::from_units(7)
        );
        assert_eq!(
            shipyard
                .completed_order(order_id)
                .expect("completed order")
                .state(),
            ShipConstructionOrderState::Completed { ship_id }
        );
    }
}
