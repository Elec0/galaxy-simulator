use crate::{
    CapacityReservationId, IdAllocationError, InventoryId, MaterialId, ProductionJobId, Quantity,
    QuantityError, ReservationId, ShipConstructionOrderId, TransportJobId,
};
use std::collections::{BTreeMap, BTreeSet};
use std::error::Error;
use std::fmt::{self, Display, Formatter};

/// Domain activity that owns an inventory reservation.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum ReservationOwner {
    /// A transport job will load the reserved material.
    TransportJob(TransportJobId),
    /// A production job will consume the reserved material.
    ProductionJob(ProductionJobId),
    /// A ship construction order will consume the reserved material.
    ShipConstructionOrder(ShipConstructionOrderId),
}

/// Material held aside for one domain activity.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct Reservation {
    id: ReservationId,
    inventory_id: InventoryId,
    material_id: MaterialId,
    quantity: Quantity,
    owner: ReservationOwner,
}

impl Reservation {
    /// Returns the stable reservation identifier.
    #[must_use]
    pub const fn id(self) -> ReservationId {
        self.id
    }

    /// Returns the inventory containing the physical material.
    #[must_use]
    pub const fn inventory_id(self) -> InventoryId {
        self.inventory_id
    }

    /// Returns the reserved material.
    #[must_use]
    pub const fn material_id(self) -> MaterialId {
        self.material_id
    }

    /// Returns the reserved amount.
    #[must_use]
    pub const fn quantity(self) -> Quantity {
        self.quantity
    }

    /// Returns the activity that owns the reservation.
    #[must_use]
    pub const fn owner(self) -> ReservationOwner {
        self.owner
    }
}

/// Empty inventory capacity held for one future transfer.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct CapacityReservation {
    id: CapacityReservationId,
    inventory_id: InventoryId,
    quantity: Quantity,
    owner: ReservationOwner,
}

impl CapacityReservation {
    /// Returns the stable capacity reservation ID.
    #[must_use]
    pub const fn id(self) -> CapacityReservationId {
        self.id
    }

    /// Returns the destination inventory.
    #[must_use]
    pub const fn inventory_id(self) -> InventoryId {
        self.inventory_id
    }

    /// Returns the held empty capacity.
    #[must_use]
    pub const fn quantity(self) -> Quantity {
        self.quantity
    }

    /// Returns the activity that owns the reservation.
    #[must_use]
    pub const fn owner(self) -> ReservationOwner {
        self.owner
    }
}

/// Capacity-limited storage with explicit material reservations.
#[derive(Clone, Debug)]
pub struct Inventory {
    id: InventoryId,
    capacity: Quantity,
    total_stored: Quantity,
    reserved_capacity: Quantity,
    stored: BTreeMap<MaterialId, Quantity>,
    reserved_by_material: BTreeMap<MaterialId, Quantity>,
    reservations: BTreeMap<ReservationId, Reservation>,
    capacity_reservations: BTreeMap<CapacityReservationId, CapacityReservation>,
}

/// Deterministically ordered world ownership of all physical inventories.
#[derive(Clone, Debug, Default)]
pub struct InventoryRegistry {
    inventories: BTreeMap<InventoryId, Inventory>,
}

impl InventoryRegistry {
    /// Creates an empty registry.
    #[must_use]
    pub const fn new() -> Self {
        Self {
            inventories: BTreeMap::new(),
        }
    }

    /// Inserts an inventory.
    ///
    /// # Errors
    ///
    /// Returns [`InventoryError::DuplicateInventory`] when the ID already
    /// exists.
    pub fn insert(&mut self, inventory: Inventory) -> Result<(), InventoryError> {
        let id = inventory.id();
        if self.inventories.contains_key(&id) {
            return Err(InventoryError::DuplicateInventory(id));
        }
        self.inventories.insert(id, inventory);
        Ok(())
    }

    /// Returns an inventory by ID.
    #[must_use]
    pub fn get(&self, inventory_id: InventoryId) -> Option<&Inventory> {
        self.inventories.get(&inventory_id)
    }

    /// Returns a mutable inventory by ID.
    pub fn get_mut(&mut self, inventory_id: InventoryId) -> Option<&mut Inventory> {
        self.inventories.get_mut(&inventory_id)
    }

    /// Atomically transfers a reserved quantity between inventories.
    ///
    /// # Errors
    ///
    /// Returns an error without mutation when an inventory is missing, the
    /// reservation is invalid, or destination capacity is insufficient.
    pub fn transfer_reserved(
        &mut self,
        source_id: InventoryId,
        destination_id: InventoryId,
        reservation_id: ReservationId,
        owner: ReservationOwner,
    ) -> Result<Reservation, InventoryError> {
        if source_id == destination_id {
            return Err(InventoryError::SameInventoryTransfer(source_id));
        }
        let reservation = self
            .get(source_id)
            .ok_or(InventoryError::UnknownInventory(source_id))?
            .reservation(reservation_id)
            .ok_or(InventoryError::UnknownReservation(reservation_id))?;
        if reservation.owner() != owner {
            return Err(InventoryError::ReservationOwnerMismatch {
                reservation_id,
                expected: owner,
                actual: reservation.owner(),
            });
        }
        self.ensure_destination_capacity(destination_id, reservation.quantity())?;

        self.get_mut(source_id)
            .ok_or(InventoryError::UnknownInventory(source_id))?
            .consume_reservations(&[reservation_id], owner)?;
        self.get_mut(destination_id)
            .ok_or(InventoryError::UnknownInventory(destination_id))?
            .add(reservation.material_id(), reservation.quantity())?;
        Ok(reservation)
    }

    /// Atomically transfers unreserved material between inventories.
    ///
    /// # Errors
    ///
    /// Returns an error without mutation when an inventory is missing, source
    /// material is unavailable, or destination capacity is insufficient.
    pub fn transfer_available(
        &mut self,
        source_id: InventoryId,
        destination_id: InventoryId,
        material_id: MaterialId,
        quantity: Quantity,
    ) -> Result<(), InventoryError> {
        if source_id == destination_id {
            return Err(InventoryError::SameInventoryTransfer(source_id));
        }
        let available = self
            .get(source_id)
            .ok_or(InventoryError::UnknownInventory(source_id))?
            .available(material_id);
        if quantity > available {
            return Err(InventoryError::InsufficientAvailable {
                material_id,
                available,
                requested: quantity,
            });
        }
        self.ensure_destination_capacity(destination_id, quantity)?;

        self.get_mut(source_id)
            .ok_or(InventoryError::UnknownInventory(source_id))?
            .remove_available(material_id, quantity)?;
        self.get_mut(destination_id)
            .ok_or(InventoryError::UnknownInventory(destination_id))?
            .add(material_id, quantity)?;
        Ok(())
    }

    /// Atomically transfers material into previously reserved capacity.
    ///
    /// # Errors
    ///
    /// Returns an error without mutation when material is unavailable or the
    /// destination reservation does not match the owner and quantity.
    #[allow(clippy::too_many_arguments)]
    pub fn transfer_into_reserved_capacity(
        &mut self,
        source_id: InventoryId,
        destination_id: InventoryId,
        material_id: MaterialId,
        quantity: Quantity,
        reservation_id: CapacityReservationId,
        owner: ReservationOwner,
    ) -> Result<(), InventoryError> {
        if source_id == destination_id {
            return Err(InventoryError::SameInventoryTransfer(source_id));
        }
        let available = self
            .get(source_id)
            .ok_or(InventoryError::UnknownInventory(source_id))?
            .available(material_id);
        if quantity > available {
            return Err(InventoryError::InsufficientAvailable {
                material_id,
                available,
                requested: quantity,
            });
        }
        let reservation = self
            .get(destination_id)
            .ok_or(InventoryError::UnknownInventory(destination_id))?
            .capacity_reservation(reservation_id)
            .ok_or(InventoryError::UnknownCapacityReservation(reservation_id))?;
        if reservation.owner() != owner {
            return Err(InventoryError::CapacityReservationOwnerMismatch {
                reservation_id,
                expected: owner,
                actual: reservation.owner(),
            });
        }
        if reservation.quantity() != quantity {
            return Err(InventoryError::CapacityReservationQuantityMismatch {
                reservation_id,
                expected: reservation.quantity(),
                actual: quantity,
            });
        }

        self.get_mut(source_id)
            .ok_or(InventoryError::UnknownInventory(source_id))?
            .remove_available(material_id, quantity)?;
        let destination = self
            .get_mut(destination_id)
            .ok_or(InventoryError::UnknownInventory(destination_id))?;
        destination.release_capacity(reservation_id)?;
        destination.add(material_id, quantity)?;
        Ok(())
    }

    fn ensure_destination_capacity(
        &self,
        destination_id: InventoryId,
        incoming: Quantity,
    ) -> Result<(), InventoryError> {
        let destination = self
            .get(destination_id)
            .ok_or(InventoryError::UnknownInventory(destination_id))?;
        if incoming > destination.remaining_capacity() {
            return Err(InventoryError::CapacityExceeded {
                capacity: destination.capacity(),
                stored: destination.total_stored(),
                incoming,
            });
        }
        Ok(())
    }
}

impl Inventory {
    /// Creates an empty inventory with a shared capacity for all materials.
    #[must_use]
    pub const fn new(id: InventoryId, capacity: Quantity) -> Self {
        Self {
            id,
            capacity,
            total_stored: Quantity::ZERO,
            reserved_capacity: Quantity::ZERO,
            stored: BTreeMap::new(),
            reserved_by_material: BTreeMap::new(),
            reservations: BTreeMap::new(),
            capacity_reservations: BTreeMap::new(),
        }
    }

    /// Returns the stable inventory identifier.
    #[must_use]
    pub const fn id(&self) -> InventoryId {
        self.id
    }

    /// Returns the shared storage capacity.
    #[must_use]
    pub const fn capacity(&self) -> Quantity {
        self.capacity
    }

    /// Returns the total amount physically stored across all materials.
    #[must_use]
    pub const fn total_stored(&self) -> Quantity {
        self.total_stored
    }

    /// Returns empty capacity committed to future transfers.
    #[must_use]
    pub const fn reserved_capacity(&self) -> Quantity {
        self.reserved_capacity
    }

    /// Returns the unoccupied capacity.
    #[must_use]
    pub fn remaining_capacity(&self) -> Quantity {
        let occupied = self
            .total_stored
            .as_units()
            .saturating_add(self.reserved_capacity.as_units());
        Quantity::from_units(self.capacity.as_units().saturating_sub(occupied))
    }

    /// Returns the physical amount of one material, including reservations.
    #[must_use]
    pub fn stored(&self, material_id: MaterialId) -> Quantity {
        self.stored
            .get(&material_id)
            .copied()
            .unwrap_or(Quantity::ZERO)
    }

    /// Returns the reserved amount of one material.
    #[must_use]
    pub fn reserved(&self, material_id: MaterialId) -> Quantity {
        self.reserved_by_material
            .get(&material_id)
            .copied()
            .unwrap_or(Quantity::ZERO)
    }

    /// Returns the amount available to new reservations or removal.
    #[must_use]
    pub fn available(&self, material_id: MaterialId) -> Quantity {
        Quantity::from_units(
            self.stored(material_id)
                .as_units()
                .saturating_sub(self.reserved(material_id).as_units()),
        )
    }

    /// Returns a reservation by its stable ID.
    #[must_use]
    pub fn reservation(&self, reservation_id: ReservationId) -> Option<Reservation> {
        self.reservations.get(&reservation_id).copied()
    }

    /// Returns a capacity reservation by its stable ID.
    #[must_use]
    pub fn capacity_reservation(
        &self,
        reservation_id: CapacityReservationId,
    ) -> Option<CapacityReservation> {
        self.capacity_reservations.get(&reservation_id).copied()
    }

    /// Adds physical material to storage.
    ///
    /// # Errors
    ///
    /// Returns [`InventoryError::CapacityExceeded`] when shared storage is too
    /// small, or [`InventoryError::Quantity`] on arithmetic overflow.
    pub fn add(
        &mut self,
        material_id: MaterialId,
        quantity: Quantity,
    ) -> Result<(), InventoryError> {
        let new_total = self.total_stored.checked_add(quantity)?;
        if new_total.checked_add(self.reserved_capacity)? > self.capacity {
            return Err(InventoryError::CapacityExceeded {
                capacity: self.capacity,
                stored: self.total_stored,
                incoming: quantity,
            });
        }

        let new_material_total = self.stored(material_id).checked_add(quantity)?;
        self.stored.insert(material_id, new_material_total);
        self.total_stored = new_total;
        Ok(())
    }

    /// Reserves empty capacity for a future transfer.
    ///
    /// # Errors
    ///
    /// Returns an error for a duplicate ID, zero quantity, or insufficient
    /// uncommitted capacity.
    pub fn reserve_capacity(
        &mut self,
        reservation_id: CapacityReservationId,
        quantity: Quantity,
        owner: ReservationOwner,
    ) -> Result<CapacityReservation, InventoryError> {
        if self.capacity_reservations.contains_key(&reservation_id) {
            return Err(InventoryError::DuplicateCapacityReservation(reservation_id));
        }
        if quantity == Quantity::ZERO {
            return Err(InventoryError::EmptyReservation);
        }
        if quantity > self.remaining_capacity() {
            return Err(InventoryError::CapacityExceeded {
                capacity: self.capacity,
                stored: self.total_stored,
                incoming: quantity,
            });
        }

        let reservation = CapacityReservation {
            id: reservation_id,
            inventory_id: self.id,
            quantity,
            owner,
        };
        self.reserved_capacity = self.reserved_capacity.checked_add(quantity)?;
        self.capacity_reservations
            .insert(reservation_id, reservation);
        Ok(reservation)
    }

    /// Releases empty capacity without adding material.
    ///
    /// # Errors
    ///
    /// Returns [`InventoryError::UnknownCapacityReservation`] when absent.
    pub fn release_capacity(
        &mut self,
        reservation_id: CapacityReservationId,
    ) -> Result<CapacityReservation, InventoryError> {
        let reservation = self
            .capacity_reservations
            .remove(&reservation_id)
            .ok_or(InventoryError::UnknownCapacityReservation(reservation_id))?;
        self.reserved_capacity = self.reserved_capacity.checked_sub(reservation.quantity)?;
        Ok(reservation)
    }

    /// Removes unreserved physical material from storage.
    ///
    /// # Errors
    ///
    /// Returns [`InventoryError::InsufficientAvailable`] when reservations or
    /// physical quantity leave too little material available.
    pub fn remove_available(
        &mut self,
        material_id: MaterialId,
        quantity: Quantity,
    ) -> Result<(), InventoryError> {
        let available = self.available(material_id);
        if quantity > available {
            return Err(InventoryError::InsufficientAvailable {
                material_id,
                available,
                requested: quantity,
            });
        }
        let remaining = self.stored(material_id).checked_sub(quantity)?;
        self.set_material_quantity(material_id, remaining);
        self.total_stored = self.total_stored.checked_sub(quantity)?;
        Ok(())
    }

    /// Reserves physically stored material for one owner.
    ///
    /// # Errors
    ///
    /// Returns an error when the ID already exists, the quantity is zero, or
    /// insufficient unreserved material is available.
    pub fn reserve(
        &mut self,
        reservation_id: ReservationId,
        material_id: MaterialId,
        quantity: Quantity,
        owner: ReservationOwner,
    ) -> Result<Reservation, InventoryError> {
        if self.reservations.contains_key(&reservation_id) {
            return Err(InventoryError::DuplicateReservation(reservation_id));
        }
        if quantity == Quantity::ZERO {
            return Err(InventoryError::EmptyReservation);
        }

        let available = self.available(material_id);
        if quantity > available {
            return Err(InventoryError::InsufficientAvailable {
                material_id,
                available,
                requested: quantity,
            });
        }

        let reservation = Reservation {
            id: reservation_id,
            inventory_id: self.id,
            material_id,
            quantity,
            owner,
        };
        let new_reserved = self.reserved(material_id).checked_add(quantity)?;
        self.reservations.insert(reservation_id, reservation);
        self.reserved_by_material.insert(material_id, new_reserved);
        Ok(reservation)
    }

    /// Releases a reservation without moving physical material.
    ///
    /// # Errors
    ///
    /// Returns [`InventoryError::UnknownReservation`] when the ID is absent.
    pub fn release(
        &mut self,
        reservation_id: ReservationId,
    ) -> Result<Reservation, InventoryError> {
        let reservation = self
            .reservations
            .remove(&reservation_id)
            .ok_or(InventoryError::UnknownReservation(reservation_id))?;
        self.decrease_reserved(reservation)?;
        Ok(reservation)
    }

    /// Atomically consumes several reservations and their physical material.
    ///
    /// # Errors
    ///
    /// Returns an error without mutation if any reservation is missing,
    /// duplicated in the request, or owned by a different activity.
    pub fn consume_reservations(
        &mut self,
        reservation_ids: &[ReservationId],
        expected_owner: ReservationOwner,
    ) -> Result<Vec<Reservation>, InventoryError> {
        let mut selected = Vec::with_capacity(reservation_ids.len());
        let mut seen = BTreeSet::new();

        for reservation_id in reservation_ids {
            if !seen.insert(*reservation_id) {
                return Err(InventoryError::DuplicateConsumption(*reservation_id));
            }
            let reservation = self
                .reservation(*reservation_id)
                .ok_or(InventoryError::UnknownReservation(*reservation_id))?;
            if reservation.owner != expected_owner {
                return Err(InventoryError::ReservationOwnerMismatch {
                    reservation_id: *reservation_id,
                    expected: expected_owner,
                    actual: reservation.owner,
                });
            }
            selected.push(reservation);
        }

        let mut material_totals = BTreeMap::<MaterialId, Quantity>::new();
        for reservation in &selected {
            let total = material_totals
                .get(&reservation.material_id)
                .copied()
                .unwrap_or(Quantity::ZERO)
                .checked_add(reservation.quantity)?;
            material_totals.insert(reservation.material_id, total);
        }

        for (material_id, quantity) in &material_totals {
            self.stored(*material_id).checked_sub(*quantity)?;
            self.reserved(*material_id).checked_sub(*quantity)?;
        }

        for reservation in &selected {
            self.reservations.remove(&reservation.id);
        }
        for (material_id, quantity) in material_totals {
            let stored = self.stored(material_id).checked_sub(quantity)?;
            let reserved = self.reserved(material_id).checked_sub(quantity)?;
            self.set_material_quantity(material_id, stored);
            self.set_reserved_quantity(material_id, reserved);
            self.total_stored = self.total_stored.checked_sub(quantity)?;
        }

        Ok(selected)
    }

    fn decrease_reserved(&mut self, reservation: Reservation) -> Result<(), InventoryError> {
        let new_reserved = self
            .reserved(reservation.material_id)
            .checked_sub(reservation.quantity)?;
        self.set_reserved_quantity(reservation.material_id, new_reserved);
        Ok(())
    }

    fn set_material_quantity(&mut self, material_id: MaterialId, quantity: Quantity) {
        if quantity == Quantity::ZERO {
            self.stored.remove(&material_id);
        } else {
            self.stored.insert(material_id, quantity);
        }
    }

    fn set_reserved_quantity(&mut self, material_id: MaterialId, quantity: Quantity) {
        if quantity == Quantity::ZERO {
            self.reserved_by_material.remove(&material_id);
        } else {
            self.reserved_by_material.insert(material_id, quantity);
        }
    }
}

/// Errors produced by storage and reservation operations.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum InventoryError {
    /// The registry already contains this inventory ID.
    DuplicateInventory(InventoryId),
    /// A referenced inventory is not registered.
    UnknownInventory(InventoryId),
    /// Source and destination refer to the same inventory.
    SameInventoryTransfer(InventoryId),
    /// Incoming material would exceed shared capacity.
    CapacityExceeded {
        /// Maximum shared capacity.
        capacity: Quantity,
        /// Quantity stored before the operation.
        stored: Quantity,
        /// Incoming quantity.
        incoming: Quantity,
    },
    /// Unreserved storage does not contain the requested amount.
    InsufficientAvailable {
        /// Requested material.
        material_id: MaterialId,
        /// Unreserved amount available.
        available: Quantity,
        /// Amount requested.
        requested: Quantity,
    },
    /// The reservation identifier is already in use.
    DuplicateReservation(ReservationId),
    /// The capacity reservation identifier is already in use.
    DuplicateCapacityReservation(CapacityReservationId),
    /// The same reservation was included twice in one consumption request.
    DuplicateConsumption(ReservationId),
    /// The reservation identifier is not present.
    UnknownReservation(ReservationId),
    /// The capacity reservation identifier is not present.
    UnknownCapacityReservation(CapacityReservationId),
    /// A reservation was presented by an activity that does not own it.
    ReservationOwnerMismatch {
        /// Reservation with the unexpected owner.
        reservation_id: ReservationId,
        /// Owner attempting consumption.
        expected: ReservationOwner,
        /// Actual reservation owner.
        actual: ReservationOwner,
    },
    /// A capacity reservation was presented by an activity that does not own it.
    CapacityReservationOwnerMismatch {
        /// Reservation with the unexpected owner.
        reservation_id: CapacityReservationId,
        /// Owner attempting consumption.
        expected: ReservationOwner,
        /// Actual reservation owner.
        actual: ReservationOwner,
    },
    /// An unload did not match the quantity of its capacity reservation.
    CapacityReservationQuantityMismatch {
        /// Reservation with a different quantity.
        reservation_id: CapacityReservationId,
        /// Quantity held by the reservation.
        expected: Quantity,
        /// Quantity presented by the transfer.
        actual: Quantity,
    },
    /// A zero-sized reservation was requested.
    EmptyReservation,
    /// Checked quantity arithmetic failed.
    Quantity(QuantityError),
    /// An identifier allocator was exhausted.
    IdAllocation(IdAllocationError),
}

impl From<QuantityError> for InventoryError {
    fn from(error: QuantityError) -> Self {
        Self::Quantity(error)
    }
}

impl From<IdAllocationError> for InventoryError {
    fn from(error: IdAllocationError) -> Self {
        Self::IdAllocation(error)
    }
}

impl Display for InventoryError {
    fn fmt(&self, formatter: &mut Formatter<'_>) -> fmt::Result {
        match self {
            Self::DuplicateInventory(id) => write!(formatter, "duplicate inventory {id}"),
            Self::UnknownInventory(id) => write!(formatter, "unknown inventory {id}"),
            Self::SameInventoryTransfer(id) => {
                write!(formatter, "inventory {id} cannot transfer to itself")
            }
            Self::CapacityExceeded {
                capacity,
                stored,
                incoming,
            } => write!(
                formatter,
                "inventory capacity {} exceeded by adding {} to {} stored units",
                capacity.as_units(),
                incoming.as_units(),
                stored.as_units()
            ),
            Self::InsufficientAvailable {
                material_id,
                available,
                requested,
            } => write!(
                formatter,
                "material {material_id} has {} available units, but {} were requested",
                available.as_units(),
                requested.as_units()
            ),
            Self::DuplicateReservation(id) => write!(formatter, "duplicate reservation {id}"),
            Self::DuplicateCapacityReservation(id) => {
                write!(formatter, "duplicate capacity reservation {id}")
            }
            Self::DuplicateConsumption(id) => {
                write!(formatter, "reservation {id} was requested twice")
            }
            Self::UnknownReservation(id) => write!(formatter, "unknown reservation {id}"),
            Self::UnknownCapacityReservation(id) => {
                write!(formatter, "unknown capacity reservation {id}")
            }
            Self::ReservationOwnerMismatch {
                reservation_id,
                expected,
                actual,
            } => write!(
                formatter,
                "reservation {reservation_id} belongs to {actual:?}, not {expected:?}"
            ),
            Self::CapacityReservationOwnerMismatch {
                reservation_id,
                expected,
                actual,
            } => write!(
                formatter,
                "capacity reservation {reservation_id} belongs to {actual:?}, not {expected:?}"
            ),
            Self::CapacityReservationQuantityMismatch {
                reservation_id,
                expected,
                actual,
            } => write!(
                formatter,
                "capacity reservation {reservation_id} holds {}, not {} units",
                expected.as_units(),
                actual.as_units()
            ),
            Self::EmptyReservation => formatter.write_str("reservation quantity must be positive"),
            Self::Quantity(error) => Display::fmt(error, formatter),
            Self::IdAllocation(error) => Display::fmt(error, formatter),
        }
    }
}

impl Error for InventoryError {}

#[cfg(test)]
mod tests {
    use super::{Inventory, InventoryError, ReservationOwner};
    use crate::{IdSequence, InventoryId, MaterialId, ProductionJobId, Quantity, ReservationId};

    struct Fixture {
        inventory: Inventory,
        material: MaterialId,
        other_material: MaterialId,
        jobs: IdSequence<ProductionJobId>,
        reservations: IdSequence<ReservationId>,
    }

    fn fixture(capacity: u64) -> Fixture {
        let mut inventory_ids = IdSequence::<InventoryId>::new();
        let mut material_ids = IdSequence::<MaterialId>::new();
        Fixture {
            inventory: Inventory::new(
                inventory_ids.allocate().expect("inventory ID"),
                Quantity::from_units(capacity),
            ),
            material: material_ids.allocate().expect("material ID"),
            other_material: material_ids.allocate().expect("second material ID"),
            jobs: IdSequence::new(),
            reservations: IdSequence::new(),
        }
    }

    #[test]
    fn shared_capacity_applies_across_materials() {
        let mut fixture = fixture(10);
        fixture
            .inventory
            .add(fixture.material, Quantity::from_units(7))
            .expect("first material should fit");

        let result = fixture
            .inventory
            .add(fixture.other_material, Quantity::from_units(4));

        assert!(matches!(
            result,
            Err(InventoryError::CapacityExceeded { .. })
        ));
    }

    #[test]
    fn reservation_reduces_availability_without_removing_material() {
        let mut fixture = fixture(10);
        fixture
            .inventory
            .add(fixture.material, Quantity::from_units(8))
            .expect("material should fit");
        let job = fixture.jobs.allocate().expect("job ID");
        let reservation = fixture.reservations.allocate().expect("reservation ID");

        fixture
            .inventory
            .reserve(
                reservation,
                fixture.material,
                Quantity::from_units(3),
                ReservationOwner::ProductionJob(job),
            )
            .expect("reservation should succeed");

        assert_eq!(fixture.inventory.stored(fixture.material).as_units(), 8);
        assert_eq!(fixture.inventory.reserved(fixture.material).as_units(), 3);
        assert_eq!(fixture.inventory.available(fixture.material).as_units(), 5);
    }

    #[test]
    fn consuming_reservations_removes_material_atomically() {
        let mut fixture = fixture(10);
        fixture
            .inventory
            .add(fixture.material, Quantity::from_units(8))
            .expect("material should fit");
        let job = fixture.jobs.allocate().expect("job ID");
        let owner = ReservationOwner::ProductionJob(job);
        let first = fixture.reservations.allocate().expect("reservation ID");
        let second = fixture.reservations.allocate().expect("reservation ID");
        for (id, quantity) in [(first, 2), (second, 3)] {
            fixture
                .inventory
                .reserve(id, fixture.material, Quantity::from_units(quantity), owner)
                .expect("reservation should succeed");
        }

        fixture
            .inventory
            .consume_reservations(&[first, second], owner)
            .expect("owned reservations should consume");

        assert_eq!(fixture.inventory.stored(fixture.material).as_units(), 3);
        assert_eq!(fixture.inventory.reserved(fixture.material), Quantity::ZERO);
        assert_eq!(fixture.inventory.total_stored().as_units(), 3);
    }
}
