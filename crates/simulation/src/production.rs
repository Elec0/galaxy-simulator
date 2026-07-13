use crate::{
    EventGeneration, FacilityId, IdAllocationError, IdSequence, Inventory, InventoryError,
    MaterialId, ProductionJobId, Quantity, ReservationId, ReservationOwner, SimulationDuration,
    SimulationTime, SimulationTimeError,
};
use std::collections::{BTreeMap, VecDeque};
use std::error::Error;
use std::fmt::{self, Display, Formatter};
use std::num::NonZeroU64;

/// Integer work required to complete production.
#[derive(Clone, Copy, Debug, Default, Eq, Ord, PartialEq, PartialOrd)]
pub struct Work(u64);

impl Work {
    /// Creates an amount of production work.
    #[must_use]
    pub const fn from_units(units: u64) -> Self {
        Self(units)
    }

    /// Returns the integer work units.
    #[must_use]
    pub const fn as_units(self) -> u64 {
        self.0
    }
}

/// Non-zero production work completed per simulated second.
#[derive(Clone, Copy, Debug, Eq, Ord, PartialEq, PartialOrd)]
pub struct Throughput(NonZeroU64);

impl Throughput {
    /// Creates a non-zero throughput.
    #[must_use]
    pub const fn new(units_per_second: NonZeroU64) -> Self {
        Self(units_per_second)
    }

    /// Returns work units completed per simulated second.
    #[must_use]
    pub const fn units_per_second(self) -> u64 {
        self.0.get()
    }

    /// Calculates a millisecond duration, rounding partial milliseconds up.
    ///
    /// # Errors
    ///
    /// Returns [`ProductionError::DurationOverflow`] if the duration cannot be
    /// represented on the simulation timeline.
    pub fn duration_for(self, work: Work) -> Result<SimulationDuration, ProductionError> {
        let numerator = u128::from(work.as_units()) * 1_000;
        let divisor = u128::from(self.units_per_second());
        let milliseconds = numerator.div_ceil(divisor);
        let milliseconds =
            u64::try_from(milliseconds).map_err(|_| ProductionError::DurationOverflow)?;
        Ok(SimulationDuration::from_millis(milliseconds))
    }
}

/// One batch transformation performed by a production line.
#[derive(Clone, Debug, Eq, PartialEq)]
pub struct Recipe {
    inputs: BTreeMap<MaterialId, Quantity>,
    output_material: MaterialId,
    output_quantity: Quantity,
    required_work: Work,
}

impl Recipe {
    /// Creates a production recipe.
    #[must_use]
    pub const fn new(
        inputs: BTreeMap<MaterialId, Quantity>,
        output_material: MaterialId,
        output_quantity: Quantity,
        required_work: Work,
    ) -> Self {
        Self {
            inputs,
            output_material,
            output_quantity,
            required_work,
        }
    }

    /// Returns required input materials and quantities.
    #[must_use]
    pub const fn inputs(&self) -> &BTreeMap<MaterialId, Quantity> {
        &self.inputs
    }

    /// Returns the material created by one batch.
    #[must_use]
    pub const fn output_material(&self) -> MaterialId {
        self.output_material
    }

    /// Returns the amount created by one batch.
    #[must_use]
    pub const fn output_quantity(&self) -> Quantity {
        self.output_quantity
    }

    /// Returns the work required for one batch.
    #[must_use]
    pub const fn required_work(&self) -> Work {
        self.required_work
    }
}

/// Lifecycle of a production job.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum ProductionJobState {
    /// The job is incrementally reserving its recipe inputs.
    WaitingForInputs,
    /// Inputs were consumed and production is underway.
    Running {
        /// Scheduled time at which output becomes complete.
        completes_at: SimulationTime,
    },
    /// Work finished, but output cannot yet enter shared storage.
    CompletedAwaitingStorage,
    /// Output entered storage and the job is finished.
    Completed,
}

/// One finite or repeating production request.
#[derive(Clone, Debug, Eq, PartialEq)]
pub struct ProductionJob {
    id: ProductionJobId,
    recipe: Recipe,
    repeat: bool,
    state: ProductionJobState,
    reservation_ids: BTreeMap<MaterialId, Vec<ReservationId>>,
    generation: EventGeneration,
}

impl ProductionJob {
    /// Returns the stable production job identifier.
    #[must_use]
    pub const fn id(&self) -> ProductionJobId {
        self.id
    }

    /// Returns the batch recipe.
    #[must_use]
    pub const fn recipe(&self) -> &Recipe {
        &self.recipe
    }

    /// Returns whether a replacement batch should be enqueued on completion.
    #[must_use]
    pub const fn is_repeating(&self) -> bool {
        self.repeat
    }

    /// Returns the current lifecycle state.
    #[must_use]
    pub const fn state(&self) -> ProductionJobState {
        self.state
    }

    /// Returns the generation attached to its scheduled completion.
    #[must_use]
    pub const fn generation(&self) -> EventGeneration {
        self.generation
    }

    /// Returns the amount of one input already reserved.
    #[must_use]
    pub fn reserved_input(&self, inventory: &Inventory, material: MaterialId) -> Quantity {
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

/// Global deterministic ID sequences required by production lines.
#[derive(Clone, Debug, Default)]
pub struct ProductionIdSequences {
    jobs: IdSequence<ProductionJobId>,
}

impl ProductionIdSequences {
    /// Creates production ID sequences beginning at one.
    #[must_use]
    pub const fn new() -> Self {
        Self {
            jobs: IdSequence::new(),
        }
    }
}

/// One production capability with shared inventory and a FIFO job queue.
#[derive(Clone, Debug)]
pub struct ProductionLine {
    facility_id: FacilityId,
    inventory_id: crate::InventoryId,
    throughput: Throughput,
    active: Option<ProductionJob>,
    queued: VecDeque<ProductionJob>,
}

impl ProductionLine {
    /// Creates an idle production line.
    #[must_use]
    pub const fn new(
        facility_id: FacilityId,
        inventory_id: crate::InventoryId,
        throughput: Throughput,
    ) -> Self {
        Self {
            facility_id,
            inventory_id,
            throughput,
            active: None,
            queued: VecDeque::new(),
        }
    }

    /// Returns the owning facility.
    #[must_use]
    pub const fn facility_id(&self) -> FacilityId {
        self.facility_id
    }

    /// Returns the world-owned shared inventory used by this line.
    #[must_use]
    pub const fn inventory_id(&self) -> crate::InventoryId {
        self.inventory_id
    }

    /// Returns the active job, including one waiting for inputs.
    #[must_use]
    pub const fn active_job(&self) -> Option<&ProductionJob> {
        self.active.as_ref()
    }

    /// Returns the number of jobs waiting behind the active job.
    #[must_use]
    pub fn queued_job_count(&self) -> usize {
        self.queued.len()
    }

    /// Returns active-job input quantities not yet reserved.
    #[must_use]
    pub fn unmet_inputs(&self, inventory: &Inventory) -> BTreeMap<MaterialId, Quantity> {
        let Some(job) = self.active.as_ref() else {
            return BTreeMap::new();
        };
        if job.state != ProductionJobState::WaitingForInputs {
            return BTreeMap::new();
        }
        job.recipe
            .inputs()
            .iter()
            .filter_map(|(material, required)| {
                let reserved = job.reserved_input(inventory, *material);
                let missing = required.checked_sub(reserved).ok()?;
                (missing > Quantity::ZERO).then_some((*material, missing))
            })
            .collect()
    }

    /// Adds a finite or repeating job to the FIFO queue.
    ///
    /// # Errors
    ///
    /// Returns [`ProductionError::IdAllocation`] if no job ID remains.
    pub fn enqueue(
        &mut self,
        ids: &mut ProductionIdSequences,
        recipe: Recipe,
        repeat: bool,
    ) -> Result<ProductionJobId, ProductionError> {
        let job = Self::allocate_job(ids, recipe, repeat)?;
        let id = job.id;
        if self.active.is_none() {
            self.active = Some(job);
        } else {
            self.queued.push_back(job);
        }
        Ok(id)
    }

    /// Reserves currently available inputs and starts the active job when ready.
    ///
    /// Returns the scheduled completion time when this call starts production.
    ///
    /// # Errors
    ///
    /// Returns a production, reservation, or timeline error without violating
    /// inventory conservation.
    pub fn prepare_active(
        &mut self,
        reservation_ids: &mut IdSequence<ReservationId>,
        inventory: &mut Inventory,
        now: SimulationTime,
    ) -> Result<Option<SimulationTime>, ProductionError> {
        if inventory.id() != self.inventory_id {
            return Err(ProductionError::WrongInventory {
                expected: self.inventory_id,
                actual: inventory.id(),
            });
        }
        let Some(job) = self.active.as_mut() else {
            return Ok(None);
        };
        if job.state != ProductionJobState::WaitingForInputs {
            return Ok(None);
        }

        for (material, required) in job.recipe.inputs() {
            let reserved = job.reserved_input(inventory, *material);
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
                ReservationOwner::ProductionJob(job.id),
            )?;
            job.reservation_ids
                .entry(*material)
                .or_default()
                .push(reservation_id);
        }

        let all_reserved = job
            .recipe
            .inputs()
            .iter()
            .all(|(material, required)| job.reserved_input(inventory, *material) == *required);
        if !all_reserved {
            return Ok(None);
        }

        let reservation_ids: Vec<_> = job.reservation_ids.values().flatten().copied().collect();
        inventory
            .consume_reservations(&reservation_ids, ReservationOwner::ProductionJob(job.id))?;
        job.reservation_ids.clear();

        let duration = self.throughput.duration_for(job.recipe.required_work())?;
        let completes_at = now.checked_add(duration)?;
        job.state = ProductionJobState::Running { completes_at };
        Ok(Some(completes_at))
    }

    /// Completes due work and attempts to place output in storage.
    ///
    /// Returns `true` when output enters storage and the next queued job becomes
    /// active. Returns `false` when the job is not due or output remains blocked.
    ///
    /// # Errors
    ///
    /// Returns a production, inventory, or ID allocation error without losing
    /// completed output.
    pub fn complete_active(
        &mut self,
        ids: &mut ProductionIdSequences,
        inventory: &mut Inventory,
        now: SimulationTime,
    ) -> Result<bool, ProductionError> {
        if inventory.id() != self.inventory_id {
            return Err(ProductionError::WrongInventory {
                expected: self.inventory_id,
                actual: inventory.id(),
            });
        }
        let Some(job) = self.active.as_mut() else {
            return Ok(false);
        };

        match job.state {
            ProductionJobState::Running { completes_at } if now >= completes_at => {
                job.state = ProductionJobState::CompletedAwaitingStorage;
            }
            ProductionJobState::CompletedAwaitingStorage => {}
            _ => return Ok(false),
        }

        let output_result =
            inventory.add(job.recipe.output_material(), job.recipe.output_quantity());
        if matches!(output_result, Err(InventoryError::CapacityExceeded { .. })) {
            return Ok(false);
        }
        output_result?;

        job.state = ProductionJobState::Completed;
        let Some(completed) = self.active.take() else {
            return Ok(false);
        };
        if completed.repeat {
            let repeat_job = Self::allocate_job(ids, completed.recipe, true)?;
            self.queued.push_back(repeat_job);
        }
        self.promote_queued_job();
        Ok(true)
    }

    fn allocate_job(
        ids: &mut ProductionIdSequences,
        recipe: Recipe,
        repeat: bool,
    ) -> Result<ProductionJob, ProductionError> {
        Ok(ProductionJob {
            id: ids.jobs.allocate()?,
            recipe,
            repeat,
            state: ProductionJobState::WaitingForInputs,
            reservation_ids: BTreeMap::new(),
            generation: EventGeneration::new(0),
        })
    }

    fn promote_queued_job(&mut self) {
        if self.active.is_none() {
            self.active = self.queued.pop_front();
        }
    }
}

/// Errors produced by production configuration and execution.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum ProductionError {
    /// A computed production duration does not fit the simulation timeline.
    DurationOverflow,
    /// Checked simulation-time arithmetic failed.
    Time(SimulationTimeError),
    /// Checked quantity arithmetic failed.
    Quantity(crate::QuantityError),
    /// Inventory or reservation behavior failed.
    Inventory(InventoryError),
    /// A deterministic ID sequence was exhausted.
    IdAllocation(IdAllocationError),
    /// A caller supplied an inventory other than the line's configured storage.
    WrongInventory {
        /// Configured line inventory.
        expected: crate::InventoryId,
        /// Inventory supplied to the operation.
        actual: crate::InventoryId,
    },
}

impl From<SimulationTimeError> for ProductionError {
    fn from(error: SimulationTimeError) -> Self {
        Self::Time(error)
    }
}

impl From<crate::QuantityError> for ProductionError {
    fn from(error: crate::QuantityError) -> Self {
        Self::Quantity(error)
    }
}

impl From<InventoryError> for ProductionError {
    fn from(error: InventoryError) -> Self {
        Self::Inventory(error)
    }
}

impl From<IdAllocationError> for ProductionError {
    fn from(error: IdAllocationError) -> Self {
        Self::IdAllocation(error)
    }
}

impl Display for ProductionError {
    fn fmt(&self, formatter: &mut Formatter<'_>) -> fmt::Result {
        match self {
            Self::DurationOverflow => formatter.write_str("production duration overflow"),
            Self::Time(error) => Display::fmt(error, formatter),
            Self::Quantity(error) => Display::fmt(error, formatter),
            Self::Inventory(error) => Display::fmt(error, formatter),
            Self::IdAllocation(error) => Display::fmt(error, formatter),
            Self::WrongInventory { expected, actual } => write!(
                formatter,
                "production line expected inventory {expected}, but received {actual}"
            ),
        }
    }
}

impl Error for ProductionError {}

#[cfg(test)]
mod tests {
    use super::{
        ProductionIdSequences, ProductionJobState, ProductionLine, Recipe, Throughput, Work,
    };
    use crate::{
        FacilityId, IdSequence, Inventory, InventoryId, MaterialId, Quantity, SimulationTime,
    };
    use std::collections::BTreeMap;
    use std::num::NonZeroU64;

    struct Fixture {
        line: ProductionLine,
        inventory: Inventory,
        ids: ProductionIdSequences,
        reservations: IdSequence<crate::ReservationId>,
        input: MaterialId,
        output: MaterialId,
    }

    fn fixture(capacity: u64) -> Fixture {
        let mut facility_ids = IdSequence::<FacilityId>::new();
        let mut inventory_ids = IdSequence::<InventoryId>::new();
        let mut material_ids = IdSequence::<MaterialId>::new();
        let input = material_ids.allocate().expect("input material ID");
        let output = material_ids.allocate().expect("output material ID");
        let inventory = Inventory::new(
            inventory_ids.allocate().expect("inventory ID"),
            Quantity::from_units(capacity),
        );
        Fixture {
            line: ProductionLine::new(
                facility_ids.allocate().expect("facility ID"),
                inventory.id(),
                Throughput::new(NonZeroU64::new(4).expect("non-zero fixture throughput")),
            ),
            inventory,
            ids: ProductionIdSequences::new(),
            reservations: IdSequence::new(),
            input,
            output,
        }
    }

    fn recipe(input: MaterialId, output: MaterialId) -> Recipe {
        Recipe::new(
            BTreeMap::from([(input, Quantity::from_units(4))]),
            output,
            Quantity::from_units(2),
            Work::from_units(5),
        )
    }

    #[test]
    fn throughput_rounds_partial_milliseconds_up() {
        let throughput = Throughput::new(NonZeroU64::new(3).expect("non-zero throughput"));

        let duration = throughput
            .duration_for(Work::from_units(1))
            .expect("duration should fit");

        assert_eq!(duration.as_millis(), 334);
    }

    #[test]
    fn inputs_are_reserved_incrementally_then_consumed_at_start() {
        let mut fixture = fixture(10);
        fixture
            .line
            .enqueue(
                &mut fixture.ids,
                recipe(fixture.input, fixture.output),
                false,
            )
            .expect("job should enqueue");
        fixture
            .inventory
            .add(fixture.input, Quantity::from_units(2))
            .expect("partial input should fit");

        let first_attempt = fixture
            .line
            .prepare_active(
                &mut fixture.reservations,
                &mut fixture.inventory,
                SimulationTime::ZERO,
            )
            .expect("partial preparation should succeed");

        assert_eq!(first_attempt, None);
        assert_eq!(fixture.inventory.stored(fixture.input).as_units(), 2);
        assert_eq!(fixture.inventory.reserved(fixture.input).as_units(), 2);

        fixture
            .inventory
            .add(fixture.input, Quantity::from_units(2))
            .expect("remaining input should fit");
        let completion = fixture
            .line
            .prepare_active(
                &mut fixture.reservations,
                &mut fixture.inventory,
                SimulationTime::ZERO,
            )
            .expect("complete preparation should succeed")
            .expect("job should start");

        assert_eq!(completion.as_millis(), 1_250);
        assert_eq!(fixture.inventory.stored(fixture.input), Quantity::ZERO);
        assert!(matches!(
            fixture.line.active_job().expect("active job").state(),
            ProductionJobState::Running { .. }
        ));
    }

    #[test]
    fn completed_output_waits_for_storage_capacity() {
        let mut fixture = fixture(4);
        fixture
            .inventory
            .add(fixture.input, Quantity::from_units(4))
            .expect("input should fill inventory");
        fixture
            .line
            .enqueue(
                &mut fixture.ids,
                recipe(fixture.input, fixture.output),
                false,
            )
            .expect("job should enqueue");
        let completes_at = fixture
            .line
            .prepare_active(
                &mut fixture.reservations,
                &mut fixture.inventory,
                SimulationTime::ZERO,
            )
            .expect("job should prepare")
            .expect("job should start");
        fixture
            .inventory
            .add(fixture.input, Quantity::from_units(4))
            .expect("new input should refill storage");

        let stored = fixture
            .line
            .complete_active(&mut fixture.ids, &mut fixture.inventory, completes_at)
            .expect("completion should remain valid");

        assert!(!stored);
        assert_eq!(
            fixture.line.active_job().expect("active job").state(),
            ProductionJobState::CompletedAwaitingStorage
        );
    }

    #[test]
    fn repeating_job_rejoins_fifo_after_completion() {
        let mut fixture = fixture(10);
        let batch = recipe(fixture.input, fixture.output);
        fixture
            .inventory
            .add(fixture.input, Quantity::from_units(8))
            .expect("two batches of input should fit");
        fixture
            .line
            .enqueue(&mut fixture.ids, batch, true)
            .expect("job should enqueue");
        let completes_at = fixture
            .line
            .prepare_active(
                &mut fixture.reservations,
                &mut fixture.inventory,
                SimulationTime::ZERO,
            )
            .expect("job should prepare")
            .expect("job should start");

        assert!(
            fixture
                .line
                .complete_active(&mut fixture.ids, &mut fixture.inventory, completes_at)
                .expect("output should store")
        );
        assert!(fixture.line.active_job().is_some());
        assert_eq!(
            fixture.line.active_job().expect("repeat job").state(),
            ProductionJobState::WaitingForInputs
        );
    }
}
