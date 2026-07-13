use galaxy_simulation::{PhaseOneConfig, PhaseOneScenario, SimulationTime};
use std::error::Error;

fn main() -> Result<(), Box<dyn Error>> {
    let mut simulation = PhaseOneScenario::new(PhaseOneConfig::default())?;
    let report = simulation.run_until_first_ship(SimulationTime::from_millis(1_000_000))?;

    println!("Galaxy Command Phase 1 headless simulation");
    println!("start_time_ms={}", report.start_time.as_millis());
    println!("end_time_ms={}", report.end_time.as_millis());
    println!("events_processed={}", report.events_processed);
    println!("starting_ship_count={}", report.starting_ship_count);
    println!("ending_ship_count={}", report.ending_ship_count);
    println!(
        "constructed_ship_id={}",
        report
            .constructed_ship_id
            .map_or_else(|| "none".to_owned(), |id| id.to_string())
    );
    println!("event_records={}", simulation.event_records().len());
    println!("decision_records={}", simulation.decision_records().len());
    println!("event_log_digest={:016x}", report.event_log_digest);
    println!("final_state_digest={:016x}", report.final_state_digest);
    println!(
        "transport_jobs_created={}",
        report.metrics.transport_jobs_created
    );
    println!(
        "transport_jobs_completed={}",
        report.metrics.transport_jobs_completed
    );
    println!(
        "transport_jobs_failed={}",
        report.metrics.transport_jobs_failed
    );
    for ((facility_id, material_id), quantity) in &report.metrics.material_produced {
        println!(
            "material_produced facility={} material={} units={}",
            facility_id,
            material_id,
            quantity.as_units()
        );
    }
    for ((facility_id, material_id), quantity) in &report.metrics.material_consumed {
        println!(
            "material_consumed facility={} material={} units={}",
            facility_id,
            material_id,
            quantity.as_units()
        );
    }
    for ((inventory_id, material_id), quantity) in &report.metrics.cargo_delivered {
        println!(
            "cargo_delivered inventory={} material={} units={}",
            inventory_id,
            material_id,
            quantity.as_units()
        );
    }
    for (facility_id, timing) in &report.metrics.facility_time {
        println!(
            "facility_time facility={} active_ms={} waiting_ms={} output_blocked_ms={}",
            facility_id, timing.active_ms, timing.waiting_ms, timing.output_blocked_ms
        );
    }
    for shortage in &report.current_shortages {
        println!(
            "shortage inventory={} location={} material={} units={} cause={:?}",
            shortage.inventory_id,
            shortage.location_id,
            shortage.material_id,
            shortage.missing.as_units(),
            shortage.cause
        );
    }

    Ok(())
}
