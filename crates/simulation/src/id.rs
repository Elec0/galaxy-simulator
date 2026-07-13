use std::error::Error;
use std::fmt::{self, Display, Formatter};
use std::marker::PhantomData;
use std::num::NonZeroU64;

/// Behavior shared by deterministic, strongly typed entity identifiers.
pub trait TypedId: Copy + Eq + Ord {
    /// Creates an identifier from a non-zero raw value.
    fn from_non_zero(value: NonZeroU64) -> Self;
}

macro_rules! define_typed_id {
    ($name:ident, $description:literal) => {
        #[doc = $description]
        #[derive(Clone, Copy, Debug, Eq, Hash, Ord, PartialEq, PartialOrd)]
        pub struct $name(NonZeroU64);

        impl $name {
            /// Returns the stable numeric representation of this identifier.
            #[must_use]
            pub const fn get(self) -> u64 {
                self.0.get()
            }
        }

        impl TypedId for $name {
            fn from_non_zero(value: NonZeroU64) -> Self {
                Self(value)
            }
        }

        impl Display for $name {
            fn fmt(&self, formatter: &mut Formatter<'_>) -> fmt::Result {
                Display::fmt(&self.0, formatter)
            }
        }
    };
}

define_typed_id!(LocationId, "Stable identifier for a navigable location.");
define_typed_id!(RouteId, "Stable identifier for a directed route.");
define_typed_id!(FacilityId, "Stable identifier for a production facility.");
define_typed_id!(InventoryId, "Stable identifier for an inventory.");
define_typed_id!(MaterialId, "Stable identifier for a material.");
define_typed_id!(ShipId, "Stable identifier for a ship.");
define_typed_id!(OrganizationId, "Stable identifier for an organization.");
define_typed_id!(ShipBlueprintId, "Stable identifier for a ship blueprint.");
define_typed_id!(
    ReservationId,
    "Stable identifier for an inventory reservation."
);
define_typed_id!(
    CapacityReservationId,
    "Stable identifier for an inventory capacity reservation."
);
define_typed_id!(ProductionJobId, "Stable identifier for a production job.");
define_typed_id!(
    ShipConstructionOrderId,
    "Stable identifier for a ship construction order."
);
define_typed_id!(TransportJobId, "Stable identifier for a transport job.");
define_typed_id!(SupplyOfferId, "Stable identifier for a supply offer.");
define_typed_id!(
    DemandRequestId,
    "Stable identifier for a material demand request."
);

/// Deterministic sequential allocator for one typed identifier domain.
#[derive(Clone, Debug)]
pub struct IdSequence<I> {
    next: Option<NonZeroU64>,
    marker: PhantomData<I>,
}

impl<I> Default for IdSequence<I> {
    fn default() -> Self {
        Self::new()
    }
}

impl<I> IdSequence<I> {
    /// Creates an allocator whose first identifier is one.
    #[must_use]
    pub const fn new() -> Self {
        Self {
            next: NonZeroU64::new(1),
            marker: PhantomData,
        }
    }
}

impl<I: TypedId> IdSequence<I> {
    /// Allocates the next identifier in deterministic ascending order.
    ///
    /// # Errors
    ///
    /// Returns [`IdAllocationError::Exhausted`] after allocating the largest
    /// supported identifier.
    pub fn allocate(&mut self) -> Result<I, IdAllocationError> {
        let next = self.next.ok_or(IdAllocationError::Exhausted)?;
        self.next = next.get().checked_add(1).and_then(NonZeroU64::new);
        Ok(I::from_non_zero(next))
    }
}

/// Errors produced by deterministic identifier allocation.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum IdAllocationError {
    /// No identifiers remain in the numeric domain.
    Exhausted,
}

impl Display for IdAllocationError {
    fn fmt(&self, formatter: &mut Formatter<'_>) -> fmt::Result {
        match self {
            Self::Exhausted => formatter.write_str("identifier sequence exhausted"),
        }
    }
}

impl Error for IdAllocationError {}

#[cfg(test)]
mod tests {
    use super::{IdSequence, ShipId};

    #[test]
    fn sequences_allocate_stable_ascending_ids() {
        let mut sequence = IdSequence::<ShipId>::new();

        let first = sequence.allocate().expect("first ID should exist");
        let second = sequence.allocate().expect("second ID should exist");

        assert_eq!(first.get(), 1);
        assert_eq!(second.get(), 2);
    }

    #[test]
    fn separate_typed_sequences_start_independently() {
        use super::LocationId;

        let mut ships = IdSequence::<ShipId>::new();
        let mut locations = IdSequence::<LocationId>::new();

        assert_eq!(ships.allocate().expect("ship ID should exist").get(), 1);
        assert_eq!(
            locations
                .allocate()
                .expect("location ID should exist")
                .get(),
            1
        );
    }
}
