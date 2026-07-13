use std::error::Error;
use std::fmt::{self, Display, Formatter};

/// An absolute millisecond timestamp on the authoritative simulation timeline.
#[derive(Clone, Copy, Debug, Default, Eq, Hash, Ord, PartialEq, PartialOrd)]
pub struct SimulationTime(u64);

impl SimulationTime {
    /// The beginning of simulation time.
    pub const ZERO: Self = Self(0);

    /// Creates a timestamp from simulated milliseconds.
    #[must_use]
    pub const fn from_millis(milliseconds: u64) -> Self {
        Self(milliseconds)
    }

    /// Returns the timestamp as simulated milliseconds.
    #[must_use]
    pub const fn as_millis(self) -> u64 {
        self.0
    }

    /// Adds a duration without wrapping.
    ///
    /// # Errors
    ///
    /// Returns [`SimulationTimeError::Overflow`] if the result exceeds the
    /// supported timeline.
    pub fn checked_add(self, duration: SimulationDuration) -> Result<Self, SimulationTimeError> {
        self.0
            .checked_add(duration.as_millis())
            .map(Self)
            .ok_or(SimulationTimeError::Overflow)
    }
}

/// A non-negative duration measured in simulated milliseconds.
#[derive(Clone, Copy, Debug, Default, Eq, Hash, Ord, PartialEq, PartialOrd)]
pub struct SimulationDuration(u64);

impl SimulationDuration {
    /// A duration of zero simulated milliseconds.
    pub const ZERO: Self = Self(0);

    /// Creates a duration from simulated milliseconds.
    #[must_use]
    pub const fn from_millis(milliseconds: u64) -> Self {
        Self(milliseconds)
    }

    /// Returns the duration as simulated milliseconds.
    #[must_use]
    pub const fn as_millis(self) -> u64 {
        self.0
    }

    /// Adds two durations without wrapping.
    ///
    /// # Errors
    ///
    /// Returns [`SimulationTimeError::Overflow`] if the combined duration
    /// exceeds the supported timeline.
    pub fn checked_add(self, other: Self) -> Result<Self, SimulationTimeError> {
        self.0
            .checked_add(other.0)
            .map(Self)
            .ok_or(SimulationTimeError::Overflow)
    }
}

/// Errors produced by simulation-time arithmetic.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum SimulationTimeError {
    /// An operation would exceed the integer timeline.
    Overflow,
}

impl Display for SimulationTimeError {
    fn fmt(&self, formatter: &mut Formatter<'_>) -> fmt::Result {
        match self {
            Self::Overflow => formatter.write_str("simulation time overflow"),
        }
    }
}

impl Error for SimulationTimeError {}

#[cfg(test)]
mod tests {
    use super::{SimulationDuration, SimulationTime, SimulationTimeError};

    #[test]
    fn checked_add_advances_time() {
        let result = SimulationTime::from_millis(10)
            .checked_add(SimulationDuration::from_millis(25))
            .expect("small addition should fit");

        assert_eq!(result, SimulationTime::from_millis(35));
    }

    #[test]
    fn checked_add_rejects_overflow() {
        let result =
            SimulationTime::from_millis(u64::MAX).checked_add(SimulationDuration::from_millis(1));

        assert_eq!(result, Err(SimulationTimeError::Overflow));
    }

    #[test]
    fn duration_addition_rejects_overflow() {
        let result = SimulationDuration::from_millis(u64::MAX)
            .checked_add(SimulationDuration::from_millis(1));

        assert_eq!(result, Err(SimulationTimeError::Overflow));
    }
}
