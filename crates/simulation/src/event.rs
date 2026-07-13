use crate::SimulationTime;
use std::collections::BTreeMap;
use std::error::Error;
use std::fmt::{self, Display, Formatter};

/// Deterministic processing phase for events sharing a timestamp.
#[derive(Clone, Copy, Debug, Eq, Hash, Ord, PartialEq, PartialOrd)]
pub enum EventPhase {
    /// Arrivals, transfers, and production completion.
    PhysicalCompletion,
    /// Derived availability, indexes, supply, and demand.
    StateUpdate,
    /// Job selection, production starts, and strategic planning.
    Decision,
}

/// Caller-managed generation used to detect events made stale by cancellation.
#[derive(Clone, Copy, Debug, Default, Eq, Hash, Ord, PartialEq, PartialOrd)]
pub struct EventGeneration(u64);

impl EventGeneration {
    /// Creates a generation token.
    #[must_use]
    pub const fn new(value: u64) -> Self {
        Self(value)
    }

    /// Returns the numeric generation.
    #[must_use]
    pub const fn get(self) -> u64 {
        self.0
    }
}

/// Complete deterministic ordering key for a scheduled event.
#[derive(Clone, Copy, Debug, Eq, Hash, Ord, PartialEq, PartialOrd)]
pub struct EventKey {
    timestamp: SimulationTime,
    phase: EventPhase,
    creation_sequence: u64,
}

impl EventKey {
    /// Returns when the event is scheduled.
    #[must_use]
    pub const fn timestamp(self) -> SimulationTime {
        self.timestamp
    }

    /// Returns the event's processing phase.
    #[must_use]
    pub const fn phase(self) -> EventPhase {
        self.phase
    }

    /// Returns the final deterministic ordering value.
    #[must_use]
    pub const fn creation_sequence(self) -> u64 {
        self.creation_sequence
    }
}

/// An event removed from the pending agenda for processing.
#[derive(Clone, Debug, Eq, PartialEq)]
pub struct ScheduledEvent<E> {
    key: EventKey,
    generation: EventGeneration,
    payload: E,
}

impl<E> ScheduledEvent<E> {
    /// Returns the deterministic event key.
    #[must_use]
    pub const fn key(&self) -> EventKey {
        self.key
    }

    /// Returns the caller-managed validity generation.
    #[must_use]
    pub const fn generation(&self) -> EventGeneration {
        self.generation
    }

    /// Returns a shared reference to the domain event payload.
    #[must_use]
    pub const fn payload(&self) -> &E {
        &self.payload
    }

    /// Consumes the scheduled event and returns its payload.
    #[must_use]
    pub fn into_payload(self) -> E {
        self.payload
    }
}

/// Ordered agenda of future domain events.
#[derive(Clone, Debug)]
pub struct EventAgenda<E> {
    current_time: SimulationTime,
    current_phase: Option<EventPhase>,
    next_creation_sequence: u64,
    pending: BTreeMap<EventKey, (EventGeneration, E)>,
}

impl<E> Default for EventAgenda<E> {
    fn default() -> Self {
        Self::new()
    }
}

impl<E> EventAgenda<E> {
    /// Creates an empty agenda at simulation time zero.
    #[must_use]
    pub const fn new() -> Self {
        Self {
            current_time: SimulationTime::ZERO,
            current_phase: None,
            next_creation_sequence: 0,
            pending: BTreeMap::new(),
        }
    }

    /// Returns the authoritative scheduling cursor time.
    #[must_use]
    pub const fn current_time(&self) -> SimulationTime {
        self.current_time
    }

    /// Returns the most recently processed phase at the current timestamp.
    #[must_use]
    pub const fn current_phase(&self) -> Option<EventPhase> {
        self.current_phase
    }

    /// Returns the number of pending events.
    #[must_use]
    pub fn len(&self) -> usize {
        self.pending.len()
    }

    /// Returns whether the agenda contains no pending events.
    #[must_use]
    pub fn is_empty(&self) -> bool {
        self.pending.is_empty()
    }

    /// Schedules a domain event.
    ///
    /// # Errors
    ///
    /// Returns [`ScheduleError::InPast`] for an earlier timestamp,
    /// [`ScheduleError::EarlierPhaseAtCurrentTime`] for a phase that has
    /// already passed at the current timestamp, or
    /// [`ScheduleError::CreationSequenceExhausted`] if no ordering values
    /// remain.
    pub fn schedule(
        &mut self,
        timestamp: SimulationTime,
        phase: EventPhase,
        generation: EventGeneration,
        payload: E,
    ) -> Result<EventKey, ScheduleError> {
        if timestamp < self.current_time {
            return Err(ScheduleError::InPast {
                current: self.current_time,
                requested: timestamp,
            });
        }

        if timestamp == self.current_time
            && let Some(current) = self.current_phase
            && phase < current
        {
            return Err(ScheduleError::EarlierPhaseAtCurrentTime {
                current,
                requested: phase,
            });
        }

        let creation_sequence = self.next_creation_sequence;
        self.next_creation_sequence = creation_sequence
            .checked_add(1)
            .ok_or(ScheduleError::CreationSequenceExhausted)?;

        let key = EventKey {
            timestamp,
            phase,
            creation_sequence,
        };
        let replaced = self.pending.insert(key, (generation, payload));
        debug_assert!(
            replaced.is_none(),
            "creation sequence must make keys unique"
        );

        Ok(key)
    }

    /// Removes the next event scheduled no later than `target`.
    ///
    /// If no event is ready, the scheduling cursor advances to `target` and
    /// its phase resets. Callers may then schedule any phase at that timestamp.
    ///
    /// # Errors
    ///
    /// Returns [`ScheduleError::InPast`] when `target` is earlier than the
    /// scheduling cursor.
    pub fn pop_next_through(
        &mut self,
        target: SimulationTime,
    ) -> Result<Option<ScheduledEvent<E>>, ScheduleError> {
        if target < self.current_time {
            return Err(ScheduleError::InPast {
                current: self.current_time,
                requested: target,
            });
        }

        let Some((&key, _)) = self.pending.first_key_value() else {
            self.current_time = target;
            self.current_phase = None;
            return Ok(None);
        };

        if key.timestamp > target {
            self.current_time = target;
            self.current_phase = None;
            return Ok(None);
        }

        let Some((removed_key, (generation, payload))) = self.pending.pop_first() else {
            self.current_time = target;
            self.current_phase = None;
            return Ok(None);
        };
        debug_assert_eq!(removed_key, key);
        self.current_time = key.timestamp;
        self.current_phase = Some(key.phase);

        Ok(Some(ScheduledEvent {
            key,
            generation,
            payload,
        }))
    }
}

/// Errors produced while scheduling or advancing the event agenda.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum ScheduleError {
    /// A timestamp precedes the current scheduling cursor.
    InPast {
        /// Current scheduling time.
        current: SimulationTime,
        /// Requested scheduling or target time.
        requested: SimulationTime,
    },
    /// A same-timestamp event requested a phase that has already passed.
    EarlierPhaseAtCurrentTime {
        /// Phase currently being processed.
        current: EventPhase,
        /// Earlier requested phase.
        requested: EventPhase,
    },
    /// The stable creation-sequence domain has been exhausted.
    CreationSequenceExhausted,
}

impl Display for ScheduleError {
    fn fmt(&self, formatter: &mut Formatter<'_>) -> fmt::Result {
        match self {
            Self::InPast { current, requested } => write!(
                formatter,
                "timestamp {} ms precedes current simulation time {} ms",
                requested.as_millis(),
                current.as_millis()
            ),
            Self::EarlierPhaseAtCurrentTime { current, requested } => write!(
                formatter,
                "cannot schedule phase {requested:?} after phase {current:?} at the current timestamp"
            ),
            Self::CreationSequenceExhausted => {
                formatter.write_str("event creation sequence exhausted")
            }
        }
    }
}

impl Error for ScheduleError {}

#[cfg(test)]
mod tests {
    use super::{EventAgenda, EventGeneration, EventPhase, ScheduleError};
    use crate::SimulationTime;

    #[test]
    fn events_are_ordered_by_time_phase_and_creation_sequence() {
        let mut agenda = EventAgenda::new();
        let generation = EventGeneration::new(0);

        agenda
            .schedule(
                SimulationTime::from_millis(10),
                EventPhase::Decision,
                generation,
                "decision",
            )
            .expect("decision should schedule");
        agenda
            .schedule(
                SimulationTime::from_millis(10),
                EventPhase::PhysicalCompletion,
                generation,
                "first completion",
            )
            .expect("first completion should schedule");
        agenda
            .schedule(
                SimulationTime::from_millis(10),
                EventPhase::PhysicalCompletion,
                generation,
                "second completion",
            )
            .expect("second completion should schedule");

        let mut payloads = Vec::new();
        while let Some(event) = agenda
            .pop_next_through(SimulationTime::from_millis(10))
            .expect("agenda should advance")
        {
            payloads.push(event.into_payload());
        }

        assert_eq!(
            payloads,
            vec!["first completion", "second completion", "decision"]
        );
    }

    #[test]
    fn current_timestamp_rejects_an_earlier_phase() {
        let mut agenda = EventAgenda::new();
        agenda
            .schedule(
                SimulationTime::from_millis(10),
                EventPhase::Decision,
                EventGeneration::new(0),
                (),
            )
            .expect("decision should schedule");
        agenda
            .pop_next_through(SimulationTime::from_millis(10))
            .expect("agenda should advance")
            .expect("decision should be ready");

        let result = agenda.schedule(
            SimulationTime::from_millis(10),
            EventPhase::PhysicalCompletion,
            EventGeneration::new(0),
            (),
        );

        assert_eq!(
            result,
            Err(ScheduleError::EarlierPhaseAtCurrentTime {
                current: EventPhase::Decision,
                requested: EventPhase::PhysicalCompletion,
            })
        );
    }

    #[test]
    fn advancing_without_an_event_resets_the_phase() {
        let mut agenda = EventAgenda::new();
        agenda
            .pop_next_through(SimulationTime::from_millis(10))
            .expect("empty agenda should advance");

        agenda
            .schedule(
                SimulationTime::from_millis(10),
                EventPhase::PhysicalCompletion,
                EventGeneration::new(0),
                (),
            )
            .expect("all phases should be available after plain time advancement");

        assert_eq!(agenda.len(), 1);
    }

    #[test]
    fn event_exposes_caller_managed_generation() {
        let mut agenda = EventAgenda::new();
        let generation = EventGeneration::new(7);
        agenda
            .schedule(
                SimulationTime::from_millis(10),
                EventPhase::StateUpdate,
                generation,
                "refresh",
            )
            .expect("event should schedule");

        let event = agenda
            .pop_next_through(SimulationTime::from_millis(10))
            .expect("agenda should advance")
            .expect("event should be ready");

        assert_eq!(event.generation(), generation);
    }
}
