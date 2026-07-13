using GalaxyCommand.Simulation;

var simulation = new PhaseOneScenario();
PhaseOneReport report = simulation.RunUntilFirstShip(new SimulationTime(1_000_000));

Console.WriteLine("Galaxy Command C# Phase 1 headless simulation");
Console.WriteLine($"start_time_ms={report.StartTime.Milliseconds}");
Console.WriteLine($"end_time_ms={report.EndTime.Milliseconds}");
Console.WriteLine($"events_processed={report.EventsProcessed}");
Console.WriteLine($"starting_ship_count={report.StartingShipCount}");
Console.WriteLine($"ending_ship_count={report.EndingShipCount}");
Console.WriteLine($"constructed_ship_id={report.ConstructedShipId?.ToString() ?? "none"}");
Console.WriteLine($"event_records={simulation.EventRecords.Count}");
Console.WriteLine($"decision_records={simulation.DecisionRecords.Count}");
Console.WriteLine($"event_log_digest={report.EventLogDigest:x16}");
Console.WriteLine($"final_state_digest={report.FinalStateDigest:x16}");
Console.WriteLine($"transport_jobs_created={report.Metrics.TransportJobsCreated}");
Console.WriteLine($"transport_jobs_completed={report.Metrics.TransportJobsCompleted}");
Console.WriteLine($"transport_jobs_failed={report.Metrics.TransportJobsFailed}");

foreach (((FacilityId facility, MaterialId material), Quantity quantity) in
    report.Metrics.MaterialProduced.OrderBy(pair => pair.Key.Facility.Value)
        .ThenBy(pair => pair.Key.Material.Value))
{
    Console.WriteLine(
        $"material_produced facility={facility} material={material} units={quantity.Units}");
}

foreach (((FacilityId facility, MaterialId material), Quantity quantity) in
    report.Metrics.MaterialConsumed.OrderBy(pair => pair.Key.Facility.Value)
        .ThenBy(pair => pair.Key.Material.Value))
{
    Console.WriteLine(
        $"material_consumed facility={facility} material={material} units={quantity.Units}");
}

foreach (((InventoryId inventory, MaterialId material), Quantity quantity) in
    report.Metrics.CargoDelivered.OrderBy(pair => pair.Key.Inventory.Value)
        .ThenBy(pair => pair.Key.Material.Value))
{
    Console.WriteLine(
        $"cargo_delivered inventory={inventory} material={material} units={quantity.Units}");
}

foreach ((FacilityId facility, FacilityTimeMetrics timing) in report.Metrics.FacilityTime)
{
    Console.WriteLine(
        $"facility_time facility={facility} active_ms={timing.ActiveMilliseconds} "
        + $"waiting_ms={timing.WaitingMilliseconds} "
        + $"output_blocked_ms={timing.OutputBlockedMilliseconds}");
}

foreach (ShortageRecord shortage in report.CurrentShortages)
{
    Console.WriteLine(
        $"shortage inventory={shortage.InventoryId} location={shortage.LocationId} "
        + $"material={shortage.MaterialId} units={shortage.Missing.Units} "
        + $"cause={shortage.Cause}");
}
