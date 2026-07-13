use crate::{
    Freighter, InventoryId, LocationId, OrganizationId, Quantity, ShipBlueprintId, ShipId,
};
use std::collections::BTreeMap;
use std::error::Error;
use std::fmt::{self, Display, Formatter};

/// Minimal Phase 1 definition of a constructible ship.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct ShipBlueprint {
    id: ShipBlueprintId,
    cargo_capacity: Quantity,
}

impl ShipBlueprint {
    /// Creates a blueprint with its cargo capacity.
    #[must_use]
    pub const fn new(id: ShipBlueprintId, cargo_capacity: Quantity) -> Self {
        Self { id, cargo_capacity }
    }

    /// Returns the stable blueprint ID.
    #[must_use]
    pub const fn id(self) -> ShipBlueprintId {
        self.id
    }

    /// Returns the ship cargo capacity created by this blueprint.
    #[must_use]
    pub const fn cargo_capacity(self) -> Quantity {
        self.cargo_capacity
    }
}

/// Persistent ship created by construction or scenario setup.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct Ship {
    id: ShipId,
    organization_id: OrganizationId,
    blueprint_id: ShipBlueprintId,
    location_id: LocationId,
    cargo_inventory_id: InventoryId,
}

impl Ship {
    /// Creates a persistent ship record.
    #[must_use]
    pub const fn new(
        id: ShipId,
        organization_id: OrganizationId,
        blueprint_id: ShipBlueprintId,
        location_id: LocationId,
        cargo_inventory_id: InventoryId,
    ) -> Self {
        Self {
            id,
            organization_id,
            blueprint_id,
            location_id,
            cargo_inventory_id,
        }
    }

    /// Returns the stable ship ID.
    #[must_use]
    pub const fn id(self) -> ShipId {
        self.id
    }

    /// Returns the owning organization.
    #[must_use]
    pub const fn organization_id(self) -> OrganizationId {
        self.organization_id
    }

    /// Returns the blueprint used to construct the ship.
    #[must_use]
    pub const fn blueprint_id(self) -> ShipBlueprintId {
        self.blueprint_id
    }

    /// Returns the current location.
    #[must_use]
    pub const fn location_id(self) -> LocationId {
        self.location_id
    }

    /// Returns the ship cargo inventory.
    #[must_use]
    pub const fn cargo_inventory_id(self) -> InventoryId {
        self.cargo_inventory_id
    }
}

/// Deterministic ownership of persistent ships and Phase 1 freighter state.
#[derive(Clone, Debug, Default)]
pub struct ShipRegistry {
    ships: BTreeMap<ShipId, Ship>,
    freighters: BTreeMap<ShipId, Freighter>,
}

impl ShipRegistry {
    /// Creates an empty ship registry.
    #[must_use]
    pub const fn new() -> Self {
        Self {
            ships: BTreeMap::new(),
            freighters: BTreeMap::new(),
        }
    }

    /// Registers a ship as an idle Phase 1 freighter.
    ///
    /// # Errors
    ///
    /// Returns [`ShipError::DuplicateShip`] if the ID is already present.
    pub fn insert_freighter(&mut self, ship: Ship) -> Result<(), ShipError> {
        if self.ships.contains_key(&ship.id()) {
            return Err(ShipError::DuplicateShip(ship.id()));
        }
        self.freighters.insert(
            ship.id(),
            Freighter::new(ship.id(), ship.location_id(), ship.cargo_inventory_id()),
        );
        self.ships.insert(ship.id(), ship);
        Ok(())
    }

    /// Returns a persistent ship.
    #[must_use]
    pub fn ship(&self, ship_id: ShipId) -> Option<Ship> {
        self.ships.get(&ship_id).copied()
    }

    /// Returns a freighter's current logistics state.
    #[must_use]
    pub fn freighter(&self, ship_id: ShipId) -> Option<Freighter> {
        self.freighters.get(&ship_id).copied()
    }

    /// Returns mutable freighter logistics state.
    pub fn freighter_mut(&mut self, ship_id: ShipId) -> Option<&mut Freighter> {
        self.freighters.get_mut(&ship_id)
    }

    /// Returns freighter IDs in deterministic order.
    pub fn freighter_ids(&self) -> impl Iterator<Item = ShipId> + '_ {
        self.freighters.keys().copied()
    }

    /// Returns the number of persistent ships.
    #[must_use]
    pub fn len(&self) -> usize {
        self.ships.len()
    }

    /// Returns whether no ships are registered.
    #[must_use]
    pub fn is_empty(&self) -> bool {
        self.ships.is_empty()
    }
}

/// Errors produced by persistent ship registration.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum ShipError {
    /// A ship ID is already registered.
    DuplicateShip(ShipId),
}

impl Display for ShipError {
    fn fmt(&self, formatter: &mut Formatter<'_>) -> fmt::Result {
        match self {
            Self::DuplicateShip(id) => write!(formatter, "duplicate ship {id}"),
        }
    }
}

impl Error for ShipError {}
