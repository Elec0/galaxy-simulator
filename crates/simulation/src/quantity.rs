use std::error::Error;
use std::fmt::{self, Display, Formatter};

/// A non-negative integer amount of a material or capacity.
#[derive(Clone, Copy, Debug, Default, Eq, Hash, Ord, PartialEq, PartialOrd)]
pub struct Quantity(u64);

impl Quantity {
    /// A quantity containing no units.
    pub const ZERO: Self = Self(0);

    /// Creates a quantity from integer units.
    #[must_use]
    pub const fn from_units(units: u64) -> Self {
        Self(units)
    }

    /// Returns the contained integer units.
    #[must_use]
    pub const fn as_units(self) -> u64 {
        self.0
    }

    /// Adds quantities without wrapping.
    ///
    /// # Errors
    ///
    /// Returns [`QuantityError::Overflow`] if the result exceeds the numeric
    /// quantity domain.
    pub fn checked_add(self, other: Self) -> Result<Self, QuantityError> {
        self.0
            .checked_add(other.0)
            .map(Self)
            .ok_or(QuantityError::Overflow)
    }

    /// Subtracts a quantity without becoming negative.
    ///
    /// # Errors
    ///
    /// Returns [`QuantityError::Insufficient`] if `other` is greater than this
    /// quantity.
    pub fn checked_sub(self, other: Self) -> Result<Self, QuantityError> {
        self.0
            .checked_sub(other.0)
            .map(Self)
            .ok_or(QuantityError::Insufficient {
                available: self,
                requested: other,
            })
    }
}

/// Errors produced by integer quantity arithmetic.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum QuantityError {
    /// Addition exceeded the numeric quantity domain.
    Overflow,
    /// Subtraction requested more than the available quantity.
    Insufficient {
        /// Quantity available before subtraction.
        available: Quantity,
        /// Quantity requested for subtraction.
        requested: Quantity,
    },
}

impl Display for QuantityError {
    fn fmt(&self, formatter: &mut Formatter<'_>) -> fmt::Result {
        match self {
            Self::Overflow => formatter.write_str("quantity overflow"),
            Self::Insufficient {
                available,
                requested,
            } => write!(
                formatter,
                "insufficient quantity: requested {}, available {}",
                requested.as_units(),
                available.as_units()
            ),
        }
    }
}

impl Error for QuantityError {}

#[cfg(test)]
mod tests {
    use super::{Quantity, QuantityError};

    #[test]
    fn checked_arithmetic_preserves_non_negative_quantities() {
        let total = Quantity::from_units(10)
            .checked_add(Quantity::from_units(5))
            .expect("addition should fit");
        let remainder = total
            .checked_sub(Quantity::from_units(4))
            .expect("subtraction should have enough units");

        assert_eq!(remainder, Quantity::from_units(11));
    }

    #[test]
    fn checked_sub_rejects_negative_result() {
        let result = Quantity::from_units(2).checked_sub(Quantity::from_units(3));

        assert_eq!(
            result,
            Err(QuantityError::Insufficient {
                available: Quantity::from_units(2),
                requested: Quantity::from_units(3),
            })
        );
    }
}
