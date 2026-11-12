using System.Collections.Generic;
using System.Numerics;
using GalaxyCommand.Content;

namespace GalaxyCommand.Simulation;

/// <summary>Frozen current-schema member and token bindings; runtime type discovery never defines save compatibility.</summary>
internal static class CheckpointJsonContracts
{
    /// <summary>Registers only authoritative constructor state and retained init members in fixed wire order.</summary>
    internal static void Register(CheckpointValueCodec codec)
    {
        codec.Object<ActorControlCheckpoint>(["ShipId", "BaseController", "TemporaryOverride", "TemporaryOverrideReason", "Revision"]);
        codec.Object<ActorController>(["Kind", "Id"]);
        codec.Object<ActorControlRegistryCheckpoint>(["Actors"]);
        codec.Scalar<ActorControlRevision>("Value");
        codec.Scalar<ActorOverrideReasonId>("Value");
        codec.Object<AlignedSubCruisePlanSelection>(["Kind", "SettledState", "ImmediateBrakingPlan", "TriangularPlan", "CappedSpeedPlan"]);
        codec.Object<AnalyticManeuverSegment>(["StartsAt", "EndsAt", "Start", "Acceleration", "AngularRate"]);
        codec.Object<AssignStationBuilderCommand>(["RequestingPrincipal", "Station", "Project", "ExpectedStationRevision", "BuilderShip", "ExpectedControlRevision"]);
        codec.Object<AssignStationModuleBuilderCommand>(["RequestingPrincipal", "Station", "ExpectedStationRevision", "Project", "BuilderModule"]);
        codec.Object<BeginScriptedOverrideCommand>(["ShipId", "Reason", "ExpectedRevision"]);
        codec.Object<BrakeThenStationaryDirectionalManeuverPlan>(["Destination", "RequestedHeading", "BrakingPhase", "StationaryPlan", "ReducedThrustPlan"]);
        codec.Object<CancelShipGroupCommand>(["ShipIds"]);
        codec.Object<CancelShipOrderCommand>(["ShipId", "OrderId"]);
        codec.Object<CapacityReservation>(["Id", "InventoryId", "Quantity", "Owner"]);
        codec.Scalar<CapacityReservationId>("Value");
        codec.Object<CappedSpeedManeuverPlan>(["Destination", "Profile", "AccelerationPhase", "CappedTravelPhase", "BrakingPhase"]);
        codec.Object<CappedSpeedManeuverProfile>(["DistanceMillimeters", "InitialSpeedMillimetersPerSecond", "Acceleration", "Braking", "MaximumSpeed", "AccelerationDistanceMillimeters", "CappedTravelDistanceMillimeters", "BrakingDistanceMillimeters"]);
        codec.Object<CargoCollectedFact>(["ShipId", "CollectorEntityId", "OrderId", "AcceptedCommand", "AttemptGeneration", "TargetId", "Outcome"]);
        codec.Object<CargoCollectionAttempt>(["ShipId", "OrderId", "AcceptedCommand", "Generation", "TargetId", "TargetPosition", "DefinitionKey", "UnitCapacityCost", "ReservedQuantity", "CapacityReservationId", "StartedAt", "StartStorage", "DueAt", "CompletionKey", "ExitKey", "MotionRevision", "CapabilityRevision", "PickupRange"]);
        codec.Object<CargoCollectionAttemptOwnerCheckpoint>(["Generations", "Attempts"]);
        codec.Object<CargoCollectionGenerationCheckpoint>(["ShipId", "Generation"]);
        codec.Object<CargoCollectionSettlementOutcome>(["DefinitionKey", "Quantity", "InstanceId", "Retired"]);
        codec.Object<CargoCollectionTarget>(["ShipId", "OrderId", "TargetId", "Anchor", "NeedsSelection"]);
        codec.Object<CargoCollectionWake>(["ShipId", "OrderId", "AcceptedCommand"]);
        codec.Object<CargoDebrisAlias>(["Alias", "Canonical"]);
        codec.Object<CargoDebrisCircle>(["OriginalFieldId", "Center", "RadiusMeters"]);
        codec.Object<CargoDebrisFieldsCheckpoint>(["MergeBehaviorVersion", "Revision", "FieldIds", "Fields", "Aliases", "Tombstones"]);
        codec.Object<CargoDebrisFieldSnapshot>(["Id", "Circles", "Members"]);
        codec.Object<CargoDispositionCommittedFact>(["Key"]);
        codec.Object<CargoDispositionKey>(["Trigger", "SourceEntityId", "OccurrenceOrdinal"]);
        codec.Object<CargoDispositionOwnerCheckpoint>(["PolicyKey", "Objects", "Receipts", "Fields"]);
        codec.Object<CargoDispositionPolicy>(["Key", "GenerationBehaviorVersion", "MergeBehaviorVersion", "OrderingBehaviorVersion", "DurationBehaviorVersion", "RecoveryByCause", "FungibleObjectCount", "ScatterRadiusMeters", "Lifetime", "MinimumCollectionDuration", "MaximumCollectionDuration", "CollectionStorageCap"]);
        codec.Object<CargoDispositionPolicyCheckpoint>(["Key", "GenerationBehaviorVersion", "MergeBehaviorVersion", "OrderingBehaviorVersion", "DurationBehaviorVersion", "RecoveryByCause", "FungibleObjectCount", "ScatterRadiusMeters", "Lifetime", "MinimumCollectionDuration", "MaximumCollectionDuration", "CollectionStorageCap"]);
        codec.Object<CargoDispositionReceipt>(["Request", "CommittedAt", "Outcomes", "ObjectIds", "FieldHandle"]);
        codec.Object<CargoDispositionRequest>(["Key", "SourceInventoryId", "SourcePosition", "Cause", "Policy", "Selected", "Cleanup"]);
        codec.Object<CargoDispositionSelection.Discrete>(["Key", "InstanceId"]);
        codec.Object<CargoDispositionSelection.Fungible>(["Key", "Quantity"]);
        codec.Object<CargoDispositionTrigger.Command>(["Sequence"]);
        codec.Object<CargoDispositionTrigger.Scheduled>(["Key"]);
        codec.Object<CargoDispositionTypeOutcome>(["DefinitionKey", "Kind", "Removed", "Destroyed", "Recoverable", "SurvivingInstances"]);
        codec.Object<CargoLiveObject>(["EntityId", "Origin", "CreatedAt", "ExpiresAt", "PolicyKey", "ExpiryEvent", "Generation", "OriginalFieldId"]);
        codec.Object<CargoObjectAppearedFact>(["Object"]);
        codec.Object<CargoObjectDisappearedFact>(["EntityId", "Position"]);
        codec.Object<CargoObjectExpiredFact>(["EntityId", "Origin"]);
        codec.Object<CargoObjectSnapshot>(["EntityId", "InventoryId", "Position"]);
        codec.Object<CargoObjectView>(["EntityId", "Position", "DefinitionKey", "HoldingKind", "Quantity", "UnitCapacityCost", "OccupiedStorage", "DiscreteItem", "OriginalFieldId"]);
        codec.Object<CargoRecoveryFraction>(["Numerator", "Denominator"]);
        codec.Object<CargoRecoveryFractionCheckpoint>(["Cause", "Numerator", "Denominator"]);
        codec.Object<CargoSourceReservationCleanup.Legacy>(["ReservationId", "Owner"]);
        codec.Object<CargoSourceReservationCleanup.Physical>(["ReservationId", "Owner"]);
        codec.Object<CollectCargoFieldCommand>(["ShipId", "TargetFieldId", "Priorities", "Placement"]);
        codec.Object<CollectCargoObjectCommand>(["ShipId", "TargetEntityId", "Placement"]);
        codec.Object<CommandAcceptedFact>(["CommandSequence", "Source", "CommandKind"]);
        codec.Object<CommandAdmissionCheckpoint>(["Sequences", "LastSubmittedAt"]);
        codec.Object<CommandFactCause>(["Sequence"]);
        codec.Object<CommandRejectedFact>(["CommandSequence", "Source", "CommandKind", "RejectionCode"]);
        codec.Scalar<CommandRejectionCode>("Value");
        codec.Object<CommandResult>(["Status", "RejectionCode", "Reason"]);
        codec.Scalar<CommandSequence>("Value");
        codec.Object<CommandSource>(["Kind", "Id"]);
        codec.Scalar<CommandSourceId>("Value");
        codec.Scalar<ConnectorEndpointId>("Value");
        codec.Scalar<ConnectorTransitId>("Value");
        codec.Object<ConnectorTransitSnapshot>(["Id", "Generation", "ConnectionId", "Source", "Destination", "DepartedAt", "ArrivesAt", "CompletionEventKey"]);
        codec.Scalar<ConstructionDesignId>("Value");
        codec.Object<ConstructionInputPolicyCheckpoint>(["MaterialId", "Quantity"]);
        codec.Object<ConstructionMaterializationEffect>(["FacilityId", "OrderId", "DesignId", "CompletedAt", "Generation", "CompletionEventKey"]);
        codec.Object<ConstructionMaterializationReceiptCheckpoint>(["Effect", "ShipIdentity"]);
        codec.Object<ConstructionOrderCheckpoint>(["Id", "DesignId", "Status", "CompletesAt", "Generation", "Reservations"], ["WorkState"]);
        codec.Scalar<ConstructionOrderId>("Value");
        codec.Object<ConstructionOwnerCheckpoint>(["OrderIds", "Processes"]);
        codec.Object<ConstructionProcessCheckpoint>(["FacilityId", "InventoryId", "Throughput", "ActiveOrderId", "QueuedOrderIds", "Orders", "PendingMaterializations", "MaterializationReceipts"], ["IsProviderAvailable"]);
        codec.Object<ConstructionRecipe>(["Inputs", "RequiredWork"]);
        codec.Object<ConstructionReservationLinkCheckpoint>(["MaterialId", "ReservationId"]);
        codec.Object<ConstructionShipIdentityCheckpoint>(["EntityId", "ShipId", "CargoInventoryId"]);
        codec.Object<ConstructStationModuleCommand>(["RequestingPrincipal", "Station", "ExpectedStationRevision", "DefinitionKey"]);
        codec.Object<ControlStationProjectCommand>(["RequestingPrincipal", "Station", "Project", "ExpectedStationRevision", "Action"]);
        codec.Object<CreateStationSiteCommand>(["RequestingPrincipal", "BuilderShip", "DefinitionKey", "Position", "ExpectedControlRevision"]);
        codec.Object<CruiseTerminalManeuverPlan>(["Destination", "RequestedHeading", "CourseHeading", "CourseTurnPlan", "AccelerationPhase", "MovingSpoolPhase", "CruisePhase", "BrakingPhase", "FinalTurnPlan"]);
        codec.Scalar<DebrisFieldId>("Value");
        // Target speed participates in exact dropout and braking continuation, even when the four-argument overload would default it.
        codec.Factory<DecelerationManeuverSegment>(["Kind", "StartsAt", "Start", "Deceleration", "TargetSpeedMillimetersPerSecond"], values =>
            new DecelerationManeuverSegment((ManeuverDecelerationKind)values[0]!, (SimulationTime)values[1]!,
                (ShipKinematicState)values[2]!, (ManeuverAcceleration)values[3]!, (ulong)values[4]! == 0 ? default : new ManeuverSpeed((ulong)values[4]!)));
        codec.Scalar<DemandPriority>("Value");
        codec.Scalar<DemandRequestId>("Value");
        codec.Object<DeployedSensorSnapshot>(["EntityId", "DefinitionKey", "PrincipalId", "Position"]);
        codec.Object<DeploySensorCommand>(["ShipId", "ItemId", "Position", "Placement"]);
        codec.Object<DeterministicRandomCheckpoint>(["RootSeed", "DerivationAlgorithm", "DerivationVersion", "Streams"]);
        codec.Object<DiplomaticConditionChangeOutcome>(["LowerPrincipalId", "UpperPrincipalId", "PriorCondition", "ResultingCondition", "Reason"]);
        codec.Object<DiscreteItemInstance>(["Id", "DefinitionKey"]);
        codec.Object<DismantleStationBaseCommand>(["RequestingPrincipal", "Station", "ExpectedStationRevision", "RecoveryInventory"]);
        codec.Object<DismantleStationModuleCommand>(["RequestingPrincipal", "Station", "ExpectedStationRevision", "Module", "RecoveryInventory", "UnfinishedUpgradeAddition"]);
        codec.Object<EconomicEvent.ConstructionComplete>(["FacilityId", "OrderId"]);
        codec.Object<EconomicEvent.ProductionComplete>(["FacilityId", "JobId"]);
        codec.Object<EconomicEvent.Transport>(["Event"]);
        codec.Object<EconomicJobWorkState>(["RemainingMilliwork", "ActiveSince"]);
        codec.Object<EconomyFacilityCheckpoint>(["FacilityId", "InventoryId", "LocationId", "Position", "ProductionOutput", "StationBinding", "StationModuleDefinitionKey"]);
        codec.Object<EndScriptedOverrideCommand>(["ShipId", "ReleasePolicy", "ExpectedRevision"]);
        codec.Object<EnqueueStationShipCommand>(["RequestingPrincipal", "Station", "ExpectedStationRevision", "Module", "ExpectedFacility", "Design"]);
        codec.Scalar<EntityId>("Value");
        codec.Object<EntityLifecycleCheckpoint>(["EntityIds", "ShipIds", "InventoryIds", "Inventories", "LiveShips", "MaterializationReceipts", "RemovalReceipts", "SensorDeployables", "CargoObjects"]);
        codec.Object<EntityLifecycleShipCheckpoint>(["EntityId", "ShipId", "PrincipalId", "DesignId", "CargoInventoryId", "ManeuverCapabilityRevision"]);
        codec.Object<EntityMaterializationReceiptCheckpoint>(["Effect", "EntityId", "ShipId", "CargoInventoryId"]);
        codec.Object<EntityMaterializedFact>(["EntityId", "Kind:valueKind", "ShipId", "SourceKind", "PrincipalId", "DesignId", "InitialPosition"], [], ["entityId", "kind", "shipId", "sourceKind", "principalId", "designId", "initialPosition"]);
        codec.Object<EntityRemovalFactCause>(["Request"]);
        codec.Object<EntityRemovalReceiptCheckpoint>(["Request", "ShipId", "CargoInventoryId"]);
        codec.Object<EntityRemovalRequest>(["EntityId", "Reason", "CargoDisposition"]);
        codec.Object<EntityRemovalResult.SensorRemoved>(["Request", "Sensor"]);
        codec.Object<EntityRemovedFact>(["EntityId", "Kind:valueKind", "ShipId", "Reason", "CargoDisposition"], [], ["entityId", "kind", "shipId", "reason", "cargoDisposition"]);
        codec.Object<EventAgendaCheckpoint<GameEvent>>(["CurrentTime", "NextCreationSequence", "PendingEvents"]);
        codec.Scalar<EventGeneration>("Value");
        codec.Object<EventKey>(["Timestamp", "Phase", "CreationSequence"]);
        codec.Object<ExecutableManeuverCheckpoint>(["Kind", "Phases", "Payload"]);
        codec.Object<ExecutableManeuverPayload.BrakeThenDirectional>(["Plan"]);
        codec.Object<ExecutableManeuverPayload.CappedSpeed>(["Plan"]);
        codec.Object<ExecutableManeuverPayload.Cruise>(["Plan"]);
        codec.Object<ExecutableManeuverPayload.Directional>(["Plan"]);
        codec.Object<ExecutableManeuverPayload.ForcedDropout>(["Destination", "RequestedHeading", "DropoutPhase", "Continuation"]);
        codec.Object<ExecutableManeuverPayload.ImmediateBraking>(["Plan"]);
        codec.Object<ExecutableManeuverPayload.MovingAligned>(["Plan"]);
        codec.Object<ExecutableManeuverPayload.MovingCourseTurn>(["Destination", "RequestedHeading", "CourseTurn", "CruiseDropoutPhase", "BrakingPhase", "Continuation"]);
        codec.Object<ExecutableManeuverPayload.PassiveDrag>(["Plan"]);
        codec.Object<ExecutableManeuverPayload.ReducedThrust>(["Plan"]);
        codec.Object<ExecutableManeuverPayload.TerminalSettle>(["At", "State"]);
        codec.Object<ExecutableManeuverPayload.Triangular>(["Plan"]);
        codec.Object<ExecutableManeuverPayload.Turn>(["Plan"]);
        codec.Object<ExecutableManeuverPayload.WaypointRoute>(["Transitions", "Terminal"]);
        codec.Scalar<FacilityId>("Value");
        codec.Object<GameEvent.CargoCollectionCompletion>(["ShipId", "OrderId", "AttemptGeneration"]);
        codec.Object<GameEvent.CargoCollectionRangeExit>(["ShipId", "OrderId", "AttemptGeneration"]);
        codec.Object<GameEvent.CargoCollectionReevaluation>(["Work"]);
        codec.Object<GameEvent.CargoExpiry>(["ObjectId"]);
        codec.Object<GameEvent.Economic>(["Event"]);
        codec.Object<GameEvent.EconomicReevaluation>([]);
        codec.Object<GameEvent.SensorActionReevaluation>(["Work"]);
        codec.Object<GameEvent.SpatialInteraction>(["Forecast"]);
        codec.Object<GameEvent.SpatialMovement>(["Event"]);
        codec.Object<GameEvent.StationTargetContinuation>([]);
        codec.Object<GameEvent.StationWork>(["Station", "Project"]);
        codec.Object<GameFactEnvelope>(["Sequence", "Timestamp", "Cause", "Fact"], ["PlayerDisclosure"]);
        codec.Scalar<GameFactSequence>("Value");
        codec.Object<GameFactStoreCheckpoint>(["Capacity", "Sequences", "RetainedFacts"]);
        codec.Object<GameplayCommandEnvelope>(["Sequence", "SubmittedAt", "Source", "Command"]);
        codec.Object<GameSessionCheckpoint>(["Engine", "RuntimePolicies", "WorldTopology", "Movement", "Control", "Orders", "Lifecycle", "Relationships", "Economy", "Random", "Facts", "CommandAdmission", "InventoryCommit", "SpatialInteractions", "PlayerObservation", "Cargo", "CargoCollectionAttempts", "CargoCollectionTargets", "Stations", "StationTargetContinuation"]);
        codec.Object<IdSequenceCheckpoint>(["NextValue"]);
        codec.Object<InitialStationModuleSetup>(["Id", "DefinitionKey", "Enabled", "FacilityId"]);
        codec.Object<InitialStationSetup>(["EntityId", "Id", "InventoryId", "PrincipalId", "DefinitionKey", "Position", "Modules", "CreatedAt"]);
        codec.Object<InteractionCandidate>(["SystemId", "Interest", "Contact"]);
        codec.Object<InteractionDeliveryCorrelation>(["InterestId", "Generation", "SourceRevision", "TargetRevision"]);
        codec.Object<InteractionForecast>(["Candidate", "Source", "CheckpointTarget:target", "From", "Through"], [], ["candidate", "source", "target", "from", "through"]);
        codec.Scalar<InteractionInterestId>("Value");
        codec.Object<InteractionInterestOwnerCheckpoint>(["DomainKind", "Interests", "Deliveries"]);
        codec.Object<InteractionMotionCompletionReceipt>(["Owner", "Interval", "PreviousRevision", "CompletedRevision", "Timestamp"]);
        codec.Object<InteractionMotionOwner>(["DomainKind", "ActivityId"]);
        codec.Object<InteractionSpatialParticipant>(["Spatial", "Revision", "Completion", "ManeuverPlan", "ManeuverReceipts", "InteractionReceipts"]);
        codec.Object<InventoryCheckpoint>(["Id", "Custody", "Capacity", "StoredMaterials", "Reservations", "CapacityReservations", "FungibleHoldings", "DiscreteItems", "PhysicalReservations", "EnabledIntakeCapacity", "ProtectedIntakeCapacity", "NextIncomingAdmissionSequence", "IncomingAdmissions", "NextOutgoingAdmissionSequence", "OutgoingAdmissions"]);
        codec.Object<InventoryCommitOwnerCheckpoint>(["ItemInstanceIds", "ReservationIds", "Receipts"]);
        codec.Object<InventoryCommitReceiptCheckpoint>(["Proposal", "Outcome"]);
        codec.Object<InventoryCustody.PrincipalControlled>(["PhysicalOwner", "ControllingPrincipalId"]);
        codec.Object<InventoryDiscreteItemCheckpoint>(["Id", "DefinitionKey"]);
        codec.Object<InventoryFungibleCheckpoint>(["DefinitionKey", "Quantity"]);
        codec.Scalar<InventoryId>("Value");
        codec.Object<InventoryIncomingAdmissionCheckpoint>(["Kind", "ReservationValue", "AdmissionSequence"]);
        codec.Object<InventoryMaterialCheckpoint>(["MaterialId", "Quantity"]);
        codec.Object<InventoryMutationOutcome.CreatedDiscrete>(["Key", "InventoryId", "Item"]);
        codec.Object<InventoryMutationOutcome.Rejected>(["Key", "Reason", "StorageReason", "ReservationReason", "TransferReason", "RemovalReason"]);
        codec.Object<InventoryMutationOutcome.ReleasedReservation>(["Key", "Reservation"]);
        codec.Object<InventoryMutationOutcome.Removed>(["Key", "Request"]);
        codec.Object<InventoryMutationOutcome.Reserved>(["Key", "Reservation"]);
        codec.Object<InventoryMutationOutcome.StoredFungible>(["Key", "InventoryId", "DefinitionKey", "Quantity"]);
        codec.Object<InventoryMutationOutcome.Transferred>(["Key", "SourceInventoryId", "DestinationInventoryId", "DefinitionKey", "Subject", "SourceReservationId", "DestinationCapacityReservationId", "ReservationOwner"]);
        codec.Object<InventoryMutationProposalCheckpoint.CreateDiscrete>(["Key", "InventoryId", "DefinitionKey"]);
        codec.Object<InventoryMutationProposalCheckpoint.ReleasePhysicalReservation>(["Key", "InventoryId", "ReservationId", "ExpectedOwner"]);
        codec.Object<InventoryMutationProposalCheckpoint.RemovePhysical>(["Key", "InventoryId", "Disposition"]);
        codec.Object<InventoryMutationProposalCheckpoint.ReservePhysical>(["Key", "InventoryId", "Subject", "Owner"]);
        codec.Object<InventoryMutationProposalCheckpoint.StoreFungible>(["Key", "InventoryId", "DefinitionKey", "Quantity"]);
        codec.Object<InventoryMutationProposalCheckpoint.TransferPhysical>(["Key", "InventoryId", "DestinationInventoryId", "DefinitionKey", "Subject", "SourceReservationId", "DestinationCapacityReservationId", "ReservationOwner"]);
        codec.Object<InventoryOperationKey>(["SourceKind", "Value"]);
        codec.Object<InventoryOutgoingAdmissionCheckpoint>(["Kind", "ReservationValue", "AdmissionSequence"]);
        codec.Object<InventoryOwnerReference.Facility>(["FacilityId"]);
        codec.Object<InventoryOwnerReference.SessionEntity>(["EntityId"]);
        codec.Object<InventoryRegistryCheckpoint>(["Inventories"]);
        codec.Object<InventoryRemovalDisposition.DestroyContents>([]);
        codec.Object<InventoryRemovalDisposition.TransferContents>(["DestinationInventoryId"]);
        codec.Object<InventoryRemovalRequest>(["InventoryId", "Disposition"]);
        codec.Object<IssueRelationshipGrantProposal>(["Id", "IssuerPrincipalId", "HolderPrincipalId", "Kind:valueKind", "MinimumStandingBand", "Reason"], [], ["id", "issuerPrincipalId", "holderPrincipalId", "kind", "minimumStandingBand", "reason"]);
        codec.Scalar<ItemInstanceId>("Value");
        codec.Object<LocalMotionCompletionReceipt>(["Motion", "PreviousRevision", "CompletedRevision", "Timestamp"]);
        codec.Object<LocalMotionSnapshot>(["Id", "Generation", "Origin", "Destination", "DepartedAt", "ArrivesAt", "CompletionEventKey"]);
        codec.Scalar<LocationId>("Value");
        codec.Scalar<ManeuverAcceleration>("MillimetersPerSecondSquared");
        codec.Object<ManeuverBoundaryDiagnostic>(["MotionId", "Generation", "PhaseIndex", "PhaseKind", "Timestamp", "EventKey", "Payload"]);
        codec.Object<ManeuverBoundaryReceipt>(["MotionId", "Generation", "PhaseIndex", "Plan", "PreviousRevision", "CompletedRevision", "Timestamp"]);
        codec.Object<ManeuverScheduledPhase>(["Kind", "StartsAt", "EndsAt"]);
        codec.Object<ManeuverScheduleEvent.Complete>(["MotionId", "Generation"]);
        codec.Object<ManeuverScheduleEvent.PhaseBoundary>(["MotionId", "Generation", "PhaseIndex"]);
        codec.Scalar<ManeuverSpeed>("MillimetersPerSecond");
        codec.Scalar<ManeuverTurnRate>("MillidegreesPerSecond");
        codec.Scalar<MaterialId>("Value");
        codec.Object<MaterialInventoryCompatibilityMap>(["Mappings"]);
        codec.Object<MaterializationPolicyCheckpoint>(["FacilityId", "PrincipalId", "SystemId", "X", "Y", "BaseControllerKind", "BaseControllerId", "InitialOrderPolicy", "AllowedDesignIds"]);
        codec.Scalar<MotionId>("Value");
        codec.Object<MoveShipCommand>(["ShipId", "Destination", "Placement", "RequestedHeading"]);
        codec.Object<MoveShipGroupCommand>(["ShipIds", "Destination"]);
        codec.Object<MovingAlignedTerminalManeuverPlan>(["Destination", "RequestedHeading", "TranslationPlan", "FinalTurnPlan"]);
        codec.Object<NavigationDestination.Entity>(["EntityId"]);
        codec.Object<NavigationDestination.Position>(["Value"]);
        codec.Object<NavigationDestination.System>(["SystemId"]);
        codec.Object<NavigationPolicyCheckpoint>(["Kind", "BehaviorVersion"]);
        codec.Object<ObservedCargoAppearanceFact>(["Cargo"]);
        codec.Object<ObservedCargoDisappearanceFact>(["EntityId", "Position"]);
        codec.Object<ObservedSensorChangeFact>(["EntityId", "DefinitionKey", "PrincipalId", "Position", "Kind:valueKind"], [], ["EntityId", "DefinitionKey", "PrincipalId", "Position", "Kind"]);
        codec.Object<ObservedSensorDeployableSnapshot>(["View", "State"]);
        codec.Object<ObservedShipChangeFact>(["ShipId", "Kind:valueKind", "Position", "EntityId", "PrincipalId", "DesignId"], [], ["ShipId", "Kind", "Position", "EntityId", "PrincipalId", "DesignId"]);
        codec.Object<ObservedStationChangeFact>(["Id", "EntityId", "DefinitionKey", "PrincipalId", "Position", "Kind:valueKind"], [], ["Id", "EntityId", "DefinitionKey", "PrincipalId", "Position", "Kind"]);
        codec.Object<ObservedStationSnapshot>(["View", "State"]);
        codec.Object<PhysicalDefinition>(["Key", "HoldingKind", "CapacityCost"]);
        codec.Object<PhysicalReservation>(["Id", "InventoryId", "Subject", "Owner"]);
        codec.Object<PhysicalReservationSubject.Discrete>(["InstanceId"]);
        codec.Object<PhysicalReservationSubject.Fungible>(["DefinitionKey", "Quantity"]);
        codec.Object<PhysicalReservationSubject.IncomingCapacity>(["Quantity"]);
        codec.Object<PhysicalTransferSubject.Discrete>(["InstanceId"]);
        codec.Object<PhysicalTransferSubject.Fungible>(["DefinitionKey", "Quantity"]);
        codec.Object<PickupSensorCommand>(["ShipId", "TargetEntityId", "Placement"]);
        codec.Object<PlayerCargoCollectedFact>(["ShipId", "OrderId", "DefinitionKey", "Quantity", "InstanceId"]);
        codec.Object<PlayerCommandOutcomeFact>(["CommandSequence", "CommandKind", "Status", "RejectionCode"]);
        codec.Object<PlayerFactDisclosure>(["ObserverPrincipalId", "Fact", "SubjectPrincipalId"]);
        codec.Object<PlayerObservationCheckpoint>(["BehaviorVersion", "ObserverPrincipalId", "PlayerCommandSource", "Deployables", "Stations"]);
        codec.Object<PolicyBatchReceiptCheckpoint>(["BatchId", "Proposals", "Result"]);
        codec.Object<PrecisionSubCruiseManeuverPlan>(["Destination", "PrecisionAcceleration", "TranslationPlan"]);
        codec.Object<PrimarySubCruiseManeuverPlan>(["Destination", "CourseHeading", "PrimaryAcceleration", "BrakingAcceleration", "TranslationPlan"]);
        codec.Scalar<PrincipalId>("Value");
        codec.Object<ProductionJobCheckpoint>(["Id", "Recipe", "IsRepeating", "Status", "CompletesAt", "Generation", "Reservations"], ["WorkState"]);
        codec.Scalar<ProductionJobId>("Value");
        codec.Object<ProductionLineCheckpoint>(["FacilityId", "InventoryId", "Throughput", "ActiveJobId", "QueuedJobIds", "Jobs"], ["IsProviderAvailable"]);
        codec.Object<ProductionOwnerCheckpoint>(["JobIds", "Lines"]);
        codec.Object<ProductionRecipeCheckpoint>(["Inputs", "OutputMaterial", "OutputQuantity", "RequiredWork"]);
        codec.Object<ProductionReservationLinkCheckpoint>(["MaterialId", "ReservationId"]);
        codec.Scalar<Quantity>("Units");
        codec.Object<RandomStreamCheckpoint>(["Key", "GeneratorAlgorithm", "GeneratorVersion", "S0", "S1", "S2", "S3", "NextPosition"]);
        codec.Object<RandomStreamKey>(["Scope", "DomainKind", "OwnerKind", "OwnerId", "PurposeId"]);
        codec.Object<Recipe>(["Inputs", "OutputMaterial", "OutputQuantity", "RequiredWork"]);
        codec.Object<ReducedThrustTerminalManeuverPlan>(["Destination", "RequestedHeading", "AccelerationPhase", "BrakingPhase", "FinalTurnPlan"]);
        codec.Object<RelationshipCheckpoint>(["PlayerPrincipalId", "StandingPolicy", "Principals", "Standings", "DiplomaticConditions", "Grants", "StandingReceipts", "PolicyReceipts"]);
        codec.Object<RelationshipDiplomacyCheckpoint>(["LowerPrincipalId", "UpperPrincipalId", "Condition"]);
        codec.Object<RelationshipGrantChangeOutcome>(["Id", "IssuerPrincipalId", "HolderPrincipalId", "Kind", "MinimumStandingBand", "PriorIssued", "ResultingIssued", "Reason"]);
        codec.Object<RelationshipGrantCheckpoint>(["Id", "IssuerPrincipalId", "HolderPrincipalId", "Kind", "MinimumStandingBand", "IsIssued"]);
        codec.Scalar<RelationshipGrantId>("Value");
        codec.Scalar<RelationshipGrantKind>("Value");
        codec.Object<RelationshipPolicyChangeBatchId>(["SourceKind", "Value"]);
        codec.Object<RelationshipPolicyChangeBatchResult.Applied>(["BatchId", "DiplomaticOutcomes", "GrantOutcomes"]);
        codec.Object<RelationshipPolicyChangeFactCause>(["BatchId"]);
        codec.Object<RelationshipPrincipalCheckpoint>(["Id", "ContentId", "Name"]);
        codec.Object<RelationshipStandingCheckpoint>(["AssessingPrincipalId", "SubjectPrincipalId", "Value"]);
        codec.Object<RelationshipStandingPolicyCheckpoint>(["Id", "Minimum", "Maximum", "Initial", "AdversarialThreshold", "NeutralThreshold", "FavorableThreshold", "AlliedThreshold"]);
        codec.Object<ReleaseStationBuilderCommand>(["RequestingPrincipal", "Station", "Project", "ExpectedStationRevision", "BuilderShip", "ExpectedControlRevision"]);
        codec.Object<ReleaseStationModuleBuilderCommand>(["RequestingPrincipal", "Station", "ExpectedStationRevision", "Project", "BuilderModule"]);
        codec.Object<Reservation>(["Id", "InventoryId", "MaterialId", "Quantity", "Owner"]);
        codec.Scalar<ReservationId>("Value");
        codec.Object<ReservationOwner.CargoCollection>(["ShipId", "OrderId", "AttemptGeneration"]);
        codec.Object<ReservationOwner.ConstructionOrder>(["OrderId"]);
        codec.Object<ReservationOwner.ProductionJob>(["JobId"]);
        codec.Object<ReservationOwner.StationProject>(["StationId", "ProjectId"]);
        codec.Object<ReservationOwner.TransportJob>(["JobId"]);
        codec.Object<RetiredEconomyFacilityCheckpoint>(["Facility", "Production", "Construction"]);
        codec.Object<RevokeRelationshipGrantProposal>(["Id", "Reason"]);
        codec.Object<RuntimePolicyManifestCheckpoint>(["Navigation", "TravelTime", "MaterializationPolicies", "FactRetentionCapacity", "ShipDefinitions", "SensorDeployableDefinitions", "CargoPolicy"]);
        codec.Object<ScheduledEvent<GameEvent>>(["Key", "Generation", "Payload"]);
        codec.Object<ScheduledEventFactCause>(["Key"]);
        codec.Object<SensorActionReceipt>(["Request", "ExecutedAt", "Result"]);
        codec.Object<SensorActionRequest.Deploy>(["Command", "Ship", "Item", "Position"]);
        codec.Object<SensorActionRequest.Pickup>(["Command", "Ship", "Target"]);
        codec.Object<SensorActionResult>(["WasApplied", "Failure", "Deployed", "Item"]);
        codec.Object<SensorActionWake>(["Ship", "Order", "Command"]);
        codec.Object<SensorDeployableDefinitionCheckpoint>(["Key", "Name", "CapacityCost", "DeploymentRangeUnits", "SensorRadiusUnits"]);
        codec.Object<SensorDeployableStateCheckpoint>(["Live", "Receipts", "Removals"]);
        codec.Object<SensorDeployableViewSnapshot>(["EntityId", "DefinitionKey", "PrincipalId", "Position", "ObservedAt"]);
        codec.Object<SensorDeployedFact>(["Sensor", "ActingShipId", "SourceCommand"]);
        codec.Object<SensorPickedUpFact>(["Sensor", "ActingShipId", "SourceCommand"]);
        codec.Object<SensorRemovalReceipt>(["Result", "RemovedAt"]);
        codec.Object<SensorRemovedFact>(["Sensor", "Reason"]);
        codec.Object<SessionEconomyCheckpoint>(["Facilities", "Production", "Construction", "Transport", "RetiredFacilities", "FacilityIds", "LocationIds"], ["PendingWake"]);
        codec.Object<SetDiplomaticConditionProposal>(["LowerPrincipalId", "UpperPrincipalId", "Condition", "Reason"], [], ["firstPrincipalId", "secondPrincipalId", "condition", "reason"]);
        codec.Object<SetStationModuleEnabledCommand>(["RequestingPrincipal", "Station", "ExpectedStationRevision", "Module", "Enabled"]);
        codec.Object<SetStationRecoveryDestinationCommand>(["RequestingPrincipal", "Station", "ExpectedStationRevision", "Project", "RecoveryInventory"]);
        codec.Object<ShipAcceleration>(["MillimetersPerSecondSquaredX", "MillimetersPerSecondSquaredY"]);
        codec.Object<ShipActionIntent.CollectField>(["OriginCommandSequence", "CargoInventoryId", "TargetFieldId", "LastObservedObjectEntityId", "LastObservedPosition", "Priorities"], [], ["command", "inventory", "targetField", "observedObject", "anchor", "priorities"]);
        codec.Object<ShipActionIntent.CollectObject>(["OriginCommandSequence", "CargoInventoryId", "TargetEntityId", "LastObservedPosition"], [], ["command", "inventory", "target", "anchor"]);
        codec.Object<ShipActionIntent.DeploySensor>(["OriginCommandSequence", "CargoInventoryId", "DefinitionKey", "ItemId", "Position"], [], ["command", "inventory", "definition", "item", "position"]);
        codec.Object<ShipActionIntent.PickupSensor>(["OriginCommandSequence", "CargoInventoryId", "DefinitionKey", "TargetEntityId"], [], ["command", "inventory", "definition", "target"]);
        codec.Object<ShipActorOrdersCheckpoint>(["ShipId", "Base", "Override"]);
        codec.Scalar<ShipAngularRate>("MillidegreesPerSecond");
        codec.Object<ShipConnectorTransitCompletedFact>(["ShipId", "Transit", "CompletedAt", "OrderId"]);
        codec.Object<ShipConnectorTransitStartedFact>(["ShipId", "Transit", "OrderId"]);
        codec.Object<ShipCruiseDroppedOutFact>(["ShipId", "MotionId", "Position", "Velocity", "DroppedOutAt", "OrderId"]);
        codec.Object<ShipCruiseEnteredFact>(["ShipId", "MotionId", "Position", "Velocity", "EnteredAt", "OrderId"]);
        codec.Object<ShipDesign>(["Id", "Name", "Recipe", "CargoCapacity", "ManeuverCapability", "SensorRadiusUnits", "PickupRangeUnits", "StationConstructionCapability"]);
        codec.Object<ShipDesignPolicyCheckpoint>(["Id", "Name", "Inputs", "RequiredWork", "CargoCapacity", "ManeuverCapability", "SensorRadiusUnits", "PickupRangeUnits", "StationConstructionCapability"]);
        codec.Scalar<ShipHeading>("Millidegrees");
        codec.Scalar<ShipId>("Value");
        codec.Object<ShipKinematicState>(["Position", "Velocity", "Heading"]);
        codec.Object<ShipLocalMotionEndedFact>(["ShipId", "Motion", "FinalPosition", "EndedAt", "Reason", "OrderId"]);
        codec.Object<ShipLocalMotionStartedFact>(["ShipId", "Motion", "OrderId"]);
        codec.Object<ShipManeuverCapability>(["BaseMassKilograms", "BaseAcceleration", "CustomPassiveDeceleration", "MaximumSubCruiseSpeed", "CruiseSpeed", "TurnRate", "MovingSpoolDuration"]);
        codec.Object<ShipManeuverCapabilityCheckpoint>(["BehaviorVersion", "BaseMassKilograms", "BaseAcceleration", "CustomPassiveDeceleration", "MaximumSubCruiseSpeed", "CruiseSpeed", "TurnRate", "MovingSpoolDuration"]);
        codec.Scalar<ShipManeuverCapabilityRevision>("Value");
        codec.Object<ShipOrderCheckpoint>(["Id", "Source", "Destination", "Status", "Reason", "Plan", "NextLegIndex", "MotionId", "TransitId", "RequestedHeading", "Action", "Kind"]);
        codec.Object<ShipOrderCoordinatorCheckpoint>(["OrderIds", "Actors"]);
        codec.Scalar<ShipOrderId>("Value");
        codec.Object<ShipOrderTransitionFact>(["ShipId", "OrderId", "Source", "Destination", "PreviousStatus", "NextStatus", "Reason", "RequestedHeading", "Action"]);
        codec.Object<ShipOrderWorkSetCheckpoint>(["Active", "Queue", "LastTerminal"]);
        codec.Object<ShipPairInteractionInterest>(["Id", "DomainKind", "PurposeId", "Source", "Target", "RangeUnits", "Generation"]);
        codec.Object<ShipSpatialSnapshot>(["ShipId", "Velocity", "Heading", "State"]);
        codec.Object<ShipSpatialSnapshotState.AnalyticManeuver>(["CurrentState", "Maneuver"]);
        codec.Object<ShipSpatialSnapshotState.AtPosition>(["Position"]);
        codec.Object<ShipSpatialSnapshotState.ConnectorTransit>(["Transit"]);
        codec.Object<ShipSpatialSnapshotState.InteractionControlled>(["Owner", "Interval", "CurrentState"]);
        codec.Object<ShipSpatialSnapshotState.LocalMotion>(["CurrentPosition", "Motion"]);
        codec.Object<ShipSpatialStateCheckpoint.AnalyticManeuver>(["Id", "Generation", "Plan", "Objective", "CurrentPhaseIndex", "WaypointPhaseIndices", "PendingEventKeys"]);
        codec.Object<ShipSpatialStateCheckpoint.AtPosition>(["Position"]);
        codec.Object<ShipSpatialStateCheckpoint.ConnectorTransit>(["Id", "Generation", "ConnectionId", "Source", "Destination", "DepartedAt", "ArrivesAt", "CompletionEventKey"]);
        codec.Object<ShipSpatialStateCheckpoint.InteractionControlled>(["Owner", "Interval"]);
        codec.Object<ShipSpatialStateCheckpoint.LocalMotion>(["Id", "Generation", "Origin", "Destination", "DepartedAt", "ArrivesAt", "CompletionEventKey"]);
        codec.Object<ShipVelocity>(["MillimetersPerSecondX", "MillimetersPerSecondY"]);
        codec.Object<ShipWaypointArrivedFact>(["ShipId", "MotionId", "Waypoint", "ReachedPosition", "ArrivedAt", "OrderId"]);
        codec.Object<ShortMoveImmediateBrakingPlan>(["Destination", "BrakingPhase", "CompletesTerminalArrival"]);
        codec.Object<ShortMoveTriangularPlan>(["Destination", "Profile", "AccelerationPhase", "BrakingPhase"]);
        codec.Object<ShortMoveTriangularProfile>(["DistanceMillimeters", "InitialSpeedMillimetersPerSecond", "Acceleration", "Braking", "MaximumSpeed", "BeginsWithBraking", "SwitchDistanceMillimeters", "SwitchSpeedMillimetersPerSecond"]);
        codec.Scalar<SimulationDuration>("Milliseconds");
        codec.Object<SimulationEngineCheckpoint<GameEvent>>(["IsInitialized", "AccruedThrough", "Agenda"]);
        codec.Scalar<SimulationTime>("Milliseconds");
        codec.Object<SpatialActorCheckpoint>(["ShipId", "Generation", "Velocity", "Heading", "State", "SpatialRevision"]);
        codec.Scalar<SpatialCoordinate>("Units");
        codec.Object<SpatialInteractionRegistrationCheckpoint>(["Ids", "Domains", "EvaluationDomains"]);
        codec.Object<SpatialMovementCheckpoint>(["MotionIds", "TransitIds", "Actors"]);
        codec.Object<SpatialMovementEvent.Arrive>(["ShipId", "MotionId", "Generation"]);
        codec.Object<SpatialMovementEvent.Emerge>(["ShipId", "TransitId", "Generation"]);
        codec.Object<SpatialMovementEvent.Maneuver>(["ShipId", "Event"], [], ["shipId", "maneuverEvent"]);
        codec.Object<SpatialPosition>(["X", "Y"]);
        codec.Scalar<SpatialRevision>("Value");
        codec.Object<StandingBatchReceiptCheckpoint>(["BatchId", "Proposals", "Result"]);
        codec.Object<StandingChangeBatchId>(["SourceKind", "Value"]);
        codec.Object<StandingChangeBatchResult.Applied>(["BatchId", "Outcomes"]);
        codec.Object<StandingChangeContribution>(["Id", "Delta", "Reason"]);
        codec.Scalar<StandingChangeContributionId>("Value");
        codec.Object<StandingChangeFactCause>(["BatchId"]);
        codec.Object<StandingChangeOutcome>(["AssessingPrincipalId", "SubjectPrincipalId", "PriorValue", "PriorBand", "CombinedDelta", "ResultingValue", "ResultingBand", "Contributions"]);
        codec.Object<StandingChangeProposal>(["AssessingPrincipalId", "SubjectPrincipalId", "Contribution"]);
        codec.Scalar<StandingValue>("Value");
        codec.Object<StationaryDirectionalManeuverPlan>(["Kind", "Destination", "RequestedHeading", "PrimarySubCruisePlan", "TurnThenPrimaryPlan", "TurnThenPrecisionPlan", "PrecisionSubCruisePlan", "FinalTurnPlan"]);
        codec.Object<StationaryInteractionInterest>(["Id", "DomainKind", "PurposeId", "Source", "Position", "RangeUnits", "Generation", "EntityId", "Revision"], [], ["id", "domainKind", "purposeId", "source", "position", "rangeUnits", "generation", "entityId", "targetRevision"]);
        codec.Object<StationaryTurnManeuverPlan>(["Destination", "RequestedHeading", "TurnPhase"]);
        codec.Object<StationAssignmentCheckpoint>(["Ship", "Source"]);
        codec.Object<StationCommandAuthority>(["Source", "Principal"]);
        codec.Object<StationCommandReceiptCheckpoint>(["Request", "Result"]);
        codec.Object<StationCommitmentInvalidatedFact>(["StationId", "PrincipalId", "InventoryId", "Kind:valueKind", "ReservationValue", "Owner"], [], ["StationId", "PrincipalId", "InventoryId", "Kind", "ReservationValue", "Owner"]);
        codec.Object<StationConstructionCapability>(["RangeUnits", "Throughput"]);
        codec.Object<StationConstructionCapabilityCheckpoint>(["RangeUnits", "ThroughputUnitsPerSecond"]);
        codec.Object<StationCreatedFact>(["StationId", "EntityId", "PrincipalId", "DefinitionKey", "Position"]);
        codec.Object<StationDefinition>(["Key", "InvariantFallback", "ModuleCapacity", "StagingCapacity", "BaseCapacity", "BaseRecipe", "SensorRadiusUnits", "AllowedModules", "DismantleWork", "DismantleRecovery", "PlacementRules"]);
        codec.Object<StationDefinitionCatalog>(["Stations", "Modules"]);
        codec.Object<StationDestructionReceipt>(["Request", "Before", "InventoryBefore", "WindowBefore", "WindowAfter", "CargoKey", "CommittedAt"]);
        codec.Object<StationDestructionRequest>(["Trigger", "Station", "ExpectedRevision", "Modules", "DestroyBase", "Occurrence"]);
        codec.Object<StationExteriorViewSnapshot>(["Id", "EntityId", "DefinitionKey", "Name", "PrincipalId", "Position", "Phase", "ObservedAt", "Modules", "Services"]);
        codec.Object<StationFacilityBinding>(["StationId", "StationEntityId", "ModuleId"]);
        codec.Scalar<StationId>("Value");
        codec.Object<StationModuleChangedFact>(["StationId", "ModuleId", "DefinitionKey", "Phase", "Enabled"]);
        codec.Object<StationModuleCheckpoint>(["Id", "DefinitionKey", "Phase", "Enabled", "IsCompleted", "OccupiedCapacity", "OriginProject", "ActiveProject", "UnfinishedUpgrade", "UnfinishedUpgradeProject", "FacilityId"]);
        codec.Object<StationModuleDefinition>(["Key", "InvariantFallback", "CapacityCost", "StorageCapacity", "SensorRadiusUnits", "ConstructionCapability", "ConstructionRecipe", "DismantleWork", "DismantleRecovery", "AllowedUpgrades", "UpgradePaths", "ProductionProvider", "ShipConstructionProvider"]);
        codec.Object<StationModuleExteriorSnapshot>(["Handle", "DefinitionKey", "Phase", "Enabled"]);
        codec.Scalar<StationModuleId>("Value");
        codec.Object<StationModuleRemovedFact>(["StationId", "ModuleId"]);
        codec.Object<StationModuleSnapshot>(["Id", "DefinitionKey", "Phase", "Enabled", "OccupiedCapacity", "ActiveProject", "UnfinishedUpgrade", "FacilityId"]);
        codec.Object<StationPlacementRegion>(["Center", "RadiusUnits"]);
        codec.Object<StationPlacementRules>(["AllowedSystems", "AllowedRegions", "ExclusionRegions"]);
        codec.Object<StationProductionProvider>(["Recipe", "Throughput", "Repeat"]);
        codec.Object<StationProjectChangedFact>(["StationId", "ProjectId", "Status"]);
        codec.Object<StationProjectCheckpoint>(["Id", "Status", "Completed", "WorkNumerator", "Rate", "Generation", "Pending", "Consumed", "Assignments"], ["Kind", "ModuleId", "ModuleDefinitionKey", "ProviderFacilityId", "UpgradeTarget", "ModuleBuilders", "RecoveryInventory", "RecoveryReservation", "RecoveryOutputs", "DismantleAddition"]);
        codec.Object<StationProjectCompletedFact>(["StationId", "ProjectId"]);
        codec.Scalar<StationProjectId>("Value");
        codec.Object<StationProjectSnapshot>(["Id", "Kind", "Status", "CompletedWork", "RequiredWork", "ConsumedInputs", "AssignedShips", "ModuleId", "UpgradeTarget", "AssignedModules", "ModuleDefinitionKey", "ProviderFacilityId"]);
        codec.Object<StationRemovalReceipt>(["Station", "CreatedAt", "RemovedAt", "Reason", "Cause"]);
        codec.Object<StationRemovedFact>(["Station", "Reason"]);
        codec.Object<StationRuntimeCheckpoint>(["Setup", "StationIds", "ProjectIds", "ModuleIds", "AccruedThrough", "Stations", "Receipts"], ["Removals", "ShipOrders", "Destructions"]);
        codec.Object<StationRuntimeSetup>(["Definitions", "Materials", "TerritoryPolicy", "CommandAuthorities", "InitialStations", "StorageLossPolicy"]);
        codec.Object<StationServiceDescriptor>(["Type", "IsAvailable"]);
        codec.Object<StationShipConstructionProvider>(["AllowedDesigns", "Throughput", "MaterializedShipController", "InitialOrderPolicy"]);
        codec.Object<StationShipConstructionQueuedFact>(["StationId", "Module", "Facility", "Order", "Design"]);
        codec.Object<StationShipOrderReceipt>(["Command", "Station", "Module", "Facility", "Order", "Design"]);
        codec.Object<StationSnapshot>(["Id", "EntityId", "InventoryId", "PrincipalId", "DefinitionKey", "Position", "Phase", "Revision", "SensorRadiusUnits", "Projects"], ["Modules"]);
        codec.Object<StationStateCheckpoint>(["Id", "Entity", "Inventory", "Principal", "DefinitionKey", "Position", "Phase", "Revision", "Projects"], ["Modules", "CreatedAt", "LossWindow", "LossOccurrence"]);
        codec.Object<StationStorageLossPolicy>(["Key", "BehaviorVersion", "CutoffNumerator", "CutoffDenominator"]);
        codec.Object<StationStorageLossWindow>(["AccumulatedDestroyedCapacity", "OverflowCredits"]);
        codec.Object<StationTargetContinuationCheckpoint>(["Key", "Ships"]);
        codec.Object<StationUpgradePath>(["TargetKey", "Recipe"]);
        codec.Scalar<SupplyOfferId>("Value");
        codec.Object<SweptRangeContact>(["Timestamp", "InRangeAtTimestamp", "AlreadyInRange"]);
        codec.Scalar<SystemId>("Value");
        codec.Object<SystemPosition>(["SystemId", "Position"]);
        codec.Object<TerminalManeuverSnapshot>(["MotionId", "Generation", "PlanKind", "Objective", "CurrentPhaseIndex", "CurrentPhase", "NextBoundary"]);
        codec.Scalar<Throughput>("UnitsPerSecond");
        codec.Scalar<TransitConnectionId>("Value");
        codec.Object<TransportBoardCheckpoint>(["Supplies", "Demands", "Jobs"]);
        codec.Object<TransportDemandCheckpoint>(["Id", "InventoryId", "LocationId", "MaterialId", "Remaining", "Priority", "CreatedAt"]);
        codec.Object<TransportEvent.Arrive>(["JobId", "Generation", "Target"]);
        codec.Object<TransportEvent.FinishLoading>(["JobId", "Generation"]);
        codec.Object<TransportEvent.FinishUnloading>(["JobId", "Generation"]);
        codec.Object<TransportFreighterCheckpoint>(["ShipId", "LocationId", "CargoInventoryId", "ActiveJobId"]);
        codec.Object<TransportIdSequencesCheckpoint>(["OfferIds", "DemandIds", "JobIds"]);
        codec.Object<TransportJobCheckpoint>(["Id", "ShipId", "SupplyOfferId", "DemandRequestId", "SourceInventoryId", "SourceLocationId", "DestinationInventoryId", "DestinationLocationId", "MaterialId", "Quantity", "SourceReservationId", "DestinationCapacityReservationId", "AssignedAt", "Generation", "Status", "TransitionAt"]);
        codec.Scalar<TransportJobId>("Value");
        codec.Object<TransportOwnerCheckpoint>(["Ids", "ReservationIds", "CapacityReservationIds", "Timing", "Board", "Freighters"]);
        codec.Object<TransportSupplyCheckpoint>(["Id", "InventoryId", "LocationId", "MaterialId", "Remaining"]);
        codec.Object<TransportTimingCheckpoint>(["DockingOverhead", "LoadingUnitsPerSecond", "UnloadingUnitsPerSecond"]);
        codec.Object<TravelLeg.Connector>(["ConnectionId", "Origin", "Destination", "Duration"]);
        codec.Object<TravelLeg.Local>(["Origin", "Destination", "Duration"]);
        codec.Object<TravelPlan>(["Destination", "Legs"]);
        codec.Object<TravelTimePolicyCheckpoint>(["Kind", "BehaviorVersion", "MillisecondsPerMapUnit"]);
        codec.Object<TurnThenPrecisionSubCruiseManeuverPlan>(["CourseHeading", "CourseTurnPlan", "PrecisionPlan"]);
        codec.Object<TurnThenSubCruiseManeuverPlan>(["Destination", "CourseHeading", "CourseTurnPlan", "TranslationPlan"]);
        codec.Object<UpgradeStationModuleCommand>(["RequestingPrincipal", "Station", "ExpectedStationRevision", "Module", "UpgradeTarget"]);
        codec.Scalar<Work>("Units");
        codec.Object<WorldConnectorEndpointCheckpoint>(["Id", "SystemId", "X", "Y"]);
        codec.Object<WorldSystemCheckpoint>(["Id", "Name"]);
        codec.Object<WorldTopologyCheckpoint>(["Systems", "Endpoints", "Connections"]);
        codec.Object<WorldTransitConnectionCheckpoint>(["Id", "SourceEndpointId", "DestinationEndpointId", "Duration"]);
        codec.Object<KeyValuePair<QualifiedContentKey, BigInteger>>(["Key", "Value"]);
        codec.Object<KeyValuePair<CargoLossCause, CargoRecoveryFraction>>(["Key", "Value"]);
        codec.Object<KeyValuePair<MaterialId, PhysicalDefinition>>(["Key", "Value"]);
        codec.Object<KeyValuePair<MaterialId, Quantity>>(["Key", "Value"]);

        codec.Union<CargoDispositionSelection>([
            ("discrete", typeof(CargoDispositionSelection.Discrete)),
            ("fungible", typeof(CargoDispositionSelection.Fungible)),
        ]);
        codec.Union<CargoDispositionTrigger>([
            ("command", typeof(CargoDispositionTrigger.Command)),
            ("scheduled", typeof(CargoDispositionTrigger.Scheduled)),
        ]);
        codec.Union<CargoSourceReservationCleanup>([
            ("legacy", typeof(CargoSourceReservationCleanup.Legacy)),
            ("physical", typeof(CargoSourceReservationCleanup.Physical)),
        ]);
        codec.Union<EconomicEvent>([
            ("construction-complete", typeof(EconomicEvent.ConstructionComplete)),
            ("production-complete", typeof(EconomicEvent.ProductionComplete)),
            ("transport", typeof(EconomicEvent.Transport)),
        ]);
        codec.Union<ExecutableManeuverPayload>([
            ("brake-then-directional", typeof(ExecutableManeuverPayload.BrakeThenDirectional)),
            ("capped-speed", typeof(ExecutableManeuverPayload.CappedSpeed)),
            ("cruise", typeof(ExecutableManeuverPayload.Cruise)),
            ("directional", typeof(ExecutableManeuverPayload.Directional)),
            ("forced-dropout", typeof(ExecutableManeuverPayload.ForcedDropout)),
            ("immediate-braking", typeof(ExecutableManeuverPayload.ImmediateBraking)),
            ("moving-aligned", typeof(ExecutableManeuverPayload.MovingAligned)),
            ("moving-course-turn", typeof(ExecutableManeuverPayload.MovingCourseTurn)),
            ("passive-drag", typeof(ExecutableManeuverPayload.PassiveDrag)),
            ("reduced-thrust", typeof(ExecutableManeuverPayload.ReducedThrust)),
            ("terminal-settle", typeof(ExecutableManeuverPayload.TerminalSettle)),
            ("triangular", typeof(ExecutableManeuverPayload.Triangular)),
            ("turn", typeof(ExecutableManeuverPayload.Turn)),
            ("waypoint-route", typeof(ExecutableManeuverPayload.WaypointRoute)),
        ]);
        codec.Union<GameEvent>([
            ("cargo-collection-completion", typeof(GameEvent.CargoCollectionCompletion)),
            ("cargo-collection-range-exit", typeof(GameEvent.CargoCollectionRangeExit)),
            ("cargo-collection-reevaluation", typeof(GameEvent.CargoCollectionReevaluation)),
            ("cargo-expiry", typeof(GameEvent.CargoExpiry)),
            ("economic", typeof(GameEvent.Economic)),
            ("economic-reevaluation", typeof(GameEvent.EconomicReevaluation)),
            ("sensor-action-reevaluation", typeof(GameEvent.SensorActionReevaluation)),
            ("spatial-interaction", typeof(GameEvent.SpatialInteraction)),
            ("spatial-movement", typeof(GameEvent.SpatialMovement)),
            ("station-target-continuation", typeof(GameEvent.StationTargetContinuation)),
            ("station-work", typeof(GameEvent.StationWork)),
        ]);
        codec.Union<GameFact>([
            ("cargo-collected", typeof(CargoCollectedFact)),
            ("cargo-disposition-committed", typeof(CargoDispositionCommittedFact)),
            ("cargo-object-appeared", typeof(CargoObjectAppearedFact)),
            ("cargo-object-disappeared", typeof(CargoObjectDisappearedFact)),
            ("cargo-object-expired", typeof(CargoObjectExpiredFact)),
            ("command-accepted", typeof(CommandAcceptedFact)),
            ("command-rejected", typeof(CommandRejectedFact)),
            ("diplomatic-condition-changed", typeof(DiplomaticConditionChangedFact)),
            ("entity-materialized", typeof(EntityMaterializedFact)),
            ("entity-removed", typeof(EntityRemovedFact)),
            ("observed-cargo-appearance", typeof(ObservedCargoAppearanceFact)),
            ("observed-cargo-disappearance", typeof(ObservedCargoDisappearanceFact)),
            ("observed-sensor-change", typeof(ObservedSensorChangeFact)),
            ("observed-ship-change", typeof(ObservedShipChangeFact)),
            ("observed-station-change", typeof(ObservedStationChangeFact)),
            ("player-cargo-collected", typeof(PlayerCargoCollectedFact)),
            ("player-command-outcome", typeof(PlayerCommandOutcomeFact)),
            ("relationship-grant-issued", typeof(RelationshipGrantIssuedFact)),
            ("relationship-grant-revoked", typeof(RelationshipGrantRevokedFact)),
            ("sensor-deployed", typeof(SensorDeployedFact)),
            ("sensor-picked-up", typeof(SensorPickedUpFact)),
            ("sensor-removed", typeof(SensorRemovedFact)),
            ("ship-connector-transit-completed", typeof(ShipConnectorTransitCompletedFact)),
            ("ship-connector-transit-started", typeof(ShipConnectorTransitStartedFact)),
            ("ship-cruise-dropped-out", typeof(ShipCruiseDroppedOutFact)),
            ("ship-cruise-entered", typeof(ShipCruiseEnteredFact)),
            ("ship-local-motion-ended", typeof(ShipLocalMotionEndedFact)),
            ("ship-local-motion-started", typeof(ShipLocalMotionStartedFact)),
            ("ship-order-transition", typeof(ShipOrderTransitionFact)),
            ("ship-waypoint-arrived", typeof(ShipWaypointArrivedFact)),
            ("standing-changed", typeof(StandingChangedFact)),
            ("station-commitment-invalidated", typeof(StationCommitmentInvalidatedFact)),
            ("station-created", typeof(StationCreatedFact)),
            ("station-module-changed", typeof(StationModuleChangedFact)),
            ("station-module-removed", typeof(StationModuleRemovedFact)),
            ("station-project-changed", typeof(StationProjectChangedFact)),
            ("station-project-completed", typeof(StationProjectCompletedFact)),
            ("station-removed", typeof(StationRemovedFact)),
            ("station-ship-construction-queued", typeof(StationShipConstructionQueuedFact)),
        ]);
        codec.Union<GameFactCause>([
            ("command", typeof(CommandFactCause)),
            ("entity-removal", typeof(EntityRemovalFactCause)),
            ("relationship-policy-change", typeof(RelationshipPolicyChangeFactCause)),
            ("scheduled-event", typeof(ScheduledEventFactCause)),
            ("standing-change", typeof(StandingChangeFactCause)),
        ]);
        codec.Union<GameplayCommand>([
            ("assign-station-builder", typeof(AssignStationBuilderCommand)),
            ("assign-station-module-builder", typeof(AssignStationModuleBuilderCommand)),
            ("begin-scripted-override", typeof(BeginScriptedOverrideCommand)),
            ("cancel-ship-group", typeof(CancelShipGroupCommand)),
            ("cancel-ship-order", typeof(CancelShipOrderCommand)),
            ("collect-cargo-field", typeof(CollectCargoFieldCommand)),
            ("collect-cargo-object", typeof(CollectCargoObjectCommand)),
            ("construct-station-module", typeof(ConstructStationModuleCommand)),
            ("control-station-project", typeof(ControlStationProjectCommand)),
            ("create-station-site", typeof(CreateStationSiteCommand)),
            ("deploy-sensor", typeof(DeploySensorCommand)),
            ("dismantle-station-base", typeof(DismantleStationBaseCommand)),
            ("dismantle-station-module", typeof(DismantleStationModuleCommand)),
            ("end-scripted-override", typeof(EndScriptedOverrideCommand)),
            ("enqueue-station-ship", typeof(EnqueueStationShipCommand)),
            ("move-ship", typeof(MoveShipCommand)),
            ("move-ship-group", typeof(MoveShipGroupCommand)),
            ("pickup-sensor", typeof(PickupSensorCommand)),
            ("release-station-builder", typeof(ReleaseStationBuilderCommand)),
            ("release-station-module-builder", typeof(ReleaseStationModuleBuilderCommand)),
            ("set-station-module-enabled", typeof(SetStationModuleEnabledCommand)),
            ("set-station-recovery-destination", typeof(SetStationRecoveryDestinationCommand)),
            ("upgrade-station-module", typeof(UpgradeStationModuleCommand)),
        ]);
        codec.Union<InventoryCustody>([
            ("principal-controlled", typeof(InventoryCustody.PrincipalControlled)),
            ("unclaimed", typeof(InventoryCustody.Unclaimed)),
        ]);
        codec.Union<InventoryMutationOutcome>([
            ("created-discrete", typeof(InventoryMutationOutcome.CreatedDiscrete)),
            ("rejected", typeof(InventoryMutationOutcome.Rejected)),
            ("released-reservation", typeof(InventoryMutationOutcome.ReleasedReservation)),
            ("removed", typeof(InventoryMutationOutcome.Removed)),
            ("reserved", typeof(InventoryMutationOutcome.Reserved)),
            ("stored-fungible", typeof(InventoryMutationOutcome.StoredFungible)),
            ("transferred", typeof(InventoryMutationOutcome.Transferred)),
        ]);
        codec.Union<InventoryMutationProposalCheckpoint>([
            ("create-discrete", typeof(InventoryMutationProposalCheckpoint.CreateDiscrete)),
            ("release-physical-reservation", typeof(InventoryMutationProposalCheckpoint.ReleasePhysicalReservation)),
            ("remove-physical", typeof(InventoryMutationProposalCheckpoint.RemovePhysical)),
            ("reserve-physical", typeof(InventoryMutationProposalCheckpoint.ReservePhysical)),
            ("store-fungible", typeof(InventoryMutationProposalCheckpoint.StoreFungible)),
            ("transfer-physical", typeof(InventoryMutationProposalCheckpoint.TransferPhysical)),
        ]);
        codec.Union<InventoryOwnerReference>([
            ("facility", typeof(InventoryOwnerReference.Facility)),
            ("session-entity", typeof(InventoryOwnerReference.SessionEntity)),
        ]);
        codec.Union<InventoryRemovalDisposition>([
            ("destroy-contents", typeof(InventoryRemovalDisposition.DestroyContents)),
            ("transfer-contents", typeof(InventoryRemovalDisposition.TransferContents)),
        ]);
        codec.Union<ManeuverScheduleEvent>([
            ("complete", typeof(ManeuverScheduleEvent.Complete)),
            ("phase-boundary", typeof(ManeuverScheduleEvent.PhaseBoundary)),
        ]);
        codec.Union<NavigationDestination>([
            ("entity", typeof(NavigationDestination.Entity)),
            ("position", typeof(NavigationDestination.Position)),
            ("system", typeof(NavigationDestination.System)),
        ]);
        codec.Union<PhysicalReservationSubject>([
            ("discrete", typeof(PhysicalReservationSubject.Discrete)),
            ("fungible", typeof(PhysicalReservationSubject.Fungible)),
            ("incoming-capacity", typeof(PhysicalReservationSubject.IncomingCapacity)),
        ]);
        codec.Union<PhysicalTransferSubject>([
            ("discrete", typeof(PhysicalTransferSubject.Discrete)),
            ("fungible", typeof(PhysicalTransferSubject.Fungible)),
        ]);
        codec.Union<RelationshipPolicyChangeProposal>([
            ("issue-relationship-grant", typeof(IssueRelationshipGrantProposal)),
            ("revoke-relationship-grant", typeof(RevokeRelationshipGrantProposal)),
            ("set-diplomatic-condition", typeof(SetDiplomaticConditionProposal)),
        ]);
        codec.Union<ReservationOwner>([
            ("cargo-collection", typeof(ReservationOwner.CargoCollection)),
            ("construction-order", typeof(ReservationOwner.ConstructionOrder)),
            ("production-job", typeof(ReservationOwner.ProductionJob)),
            ("station-project", typeof(ReservationOwner.StationProject)),
            ("transport-job", typeof(ReservationOwner.TransportJob)),
        ]);
        codec.Union<SensorActionRequest>([
            ("deploy", typeof(SensorActionRequest.Deploy)),
            ("pickup", typeof(SensorActionRequest.Pickup)),
        ]);
        codec.Union<ShipActionIntent>([
            ("collect-field", typeof(ShipActionIntent.CollectField)),
            ("collect-object", typeof(ShipActionIntent.CollectObject)),
            ("deploy-sensor", typeof(ShipActionIntent.DeploySensor)),
            ("pickup-sensor", typeof(ShipActionIntent.PickupSensor)),
        ]);
        codec.Union<ShipSpatialSnapshotState>([
            ("analytic-maneuver", typeof(ShipSpatialSnapshotState.AnalyticManeuver)),
            ("at-position", typeof(ShipSpatialSnapshotState.AtPosition)),
            ("connector-transit", typeof(ShipSpatialSnapshotState.ConnectorTransit)),
            ("interaction-controlled", typeof(ShipSpatialSnapshotState.InteractionControlled)),
            ("local-motion", typeof(ShipSpatialSnapshotState.LocalMotion)),
        ]);
        codec.Union<ShipSpatialStateCheckpoint>([
            ("analytic-maneuver", typeof(ShipSpatialStateCheckpoint.AnalyticManeuver)),
            ("at-position", typeof(ShipSpatialStateCheckpoint.AtPosition)),
            ("connector-transit", typeof(ShipSpatialStateCheckpoint.ConnectorTransit)),
            ("interaction-controlled", typeof(ShipSpatialStateCheckpoint.InteractionControlled)),
            ("local-motion", typeof(ShipSpatialStateCheckpoint.LocalMotion)),
        ]);
        codec.Union<SpatialInteractionInterest>([
            ("ship-pair-interaction-interest", typeof(ShipPairInteractionInterest)),
            ("stationary-interaction-interest", typeof(StationaryInteractionInterest)),
        ]);
        codec.Union<SpatialMovementEvent>([
            ("arrive", typeof(SpatialMovementEvent.Arrive)),
            ("emerge", typeof(SpatialMovementEvent.Emerge)),
            ("maneuver", typeof(SpatialMovementEvent.Maneuver)),
        ]);
        codec.Union<TransportEvent>([
            ("arrive", typeof(TransportEvent.Arrive)),
            ("finish-loading", typeof(TransportEvent.FinishLoading)),
            ("finish-unloading", typeof(TransportEvent.FinishUnloading)),
        ]);
        codec.Union<TravelLeg>([
            ("connector", typeof(TravelLeg.Connector)),
            ("local", typeof(TravelLeg.Local)),
        ]);

        codec.Enum<ActorControllerKind>([
            ("player", ActorControllerKind.Player),
            ("autonomous", ActorControllerKind.Autonomous),
            ("script", ActorControllerKind.Script),
        ]);
        codec.Enum<AlignedSubCruisePlanKind>([
            ("terminal-settle", AlignedSubCruisePlanKind.TerminalSettle),
            ("immediate-braking", AlignedSubCruisePlanKind.ImmediateBraking),
            ("triangular", AlignedSubCruisePlanKind.Triangular),
            ("capped-speed", AlignedSubCruisePlanKind.CappedSpeed),
            ("directional-planner", AlignedSubCruisePlanKind.DirectionalPlanner),
        ]);
        codec.Enum<BoundedTerminalPlanKind>([
            ("terminal-settle", BoundedTerminalPlanKind.TerminalSettle),
            ("stationary-turn", BoundedTerminalPlanKind.StationaryTurn),
            ("stationary-directional", BoundedTerminalPlanKind.StationaryDirectional),
            ("cruise-terminal", BoundedTerminalPlanKind.CruiseTerminal),
            ("forced-cruise-dropout-terminal", BoundedTerminalPlanKind.ForcedCruiseDropoutTerminal),
            ("moving-aligned-terminal", BoundedTerminalPlanKind.MovingAlignedTerminal),
            ("brake-then-stationary-directional", BoundedTerminalPlanKind.BrakeThenStationaryDirectional),
            ("aligned-sub-cruise", BoundedTerminalPlanKind.AlignedSubCruise),
            ("reduced-thrust-terminal", BoundedTerminalPlanKind.ReducedThrustTerminal),
            ("waypoint-route", BoundedTerminalPlanKind.WaypointRoute),
            ("directional-planner", BoundedTerminalPlanKind.DirectionalPlanner),
            ("passive-drag", BoundedTerminalPlanKind.PassiveDrag),
            ("moving-course-turn", BoundedTerminalPlanKind.MovingCourseTurn),
        ]);
        codec.Enum<CargoLossCause>([
            ("entity-destruction", CargoLossCause.EntityDestruction),
            ("storage-module-loss", CargoLossCause.StorageModuleLoss),
            ("functional-ejection", CargoLossCause.FunctionalEjection),
        ]);
        codec.Enum<CommandResultStatus>([
            ("accepted", CommandResultStatus.Accepted),
            ("rejected", CommandResultStatus.Rejected),
        ]);
        codec.Enum<CommandSourceKind>([
            ("player", CommandSourceKind.Player),
            ("autonomous", CommandSourceKind.Autonomous),
            ("dialogue", CommandSourceKind.Dialogue),
            ("script", CommandSourceKind.Script),
        ]);
        codec.Enum<ConstructionOrderStatus>([
            ("waiting-for-inputs", ConstructionOrderStatus.WaitingForInputs),
            ("running", ConstructionOrderStatus.Running),
            ("awaiting-materialization", ConstructionOrderStatus.AwaitingMaterialization),
            ("completed", ConstructionOrderStatus.Completed),
            ("cancelled", ConstructionOrderStatus.Cancelled),
            ("paused", ConstructionOrderStatus.Paused),
        ]);
        codec.Enum<DiplomaticCondition>([
            ("peace", DiplomaticCondition.Peace),
            ("war", DiplomaticCondition.War),
        ]);
        codec.Enum<EntityCargoDisposition>([
            ("discard-cargo", EntityCargoDisposition.DiscardCargo),
        ]);
        codec.Enum<EntityKind>([
            ("ship", EntityKind.Ship),
            ("sensor-deployable", EntityKind.SensorDeployable),
            ("cargo-object", EntityKind.CargoObject),
            ("station", EntityKind.Station),
        ]);
        codec.Enum<EntityMaterializationSourceKind>([
            ("construction", EntityMaterializationSourceKind.Construction),
        ]);
        codec.Enum<EntityRemovalReason>([
            ("despawned", EntityRemovalReason.Despawned),
            ("destroyed", EntityRemovalReason.Destroyed),
        ]);
        codec.Enum<EventPhase>([
            ("physical-completion", EventPhase.PhysicalCompletion),
            ("state-update", EventPhase.StateUpdate),
            ("decision", EventPhase.Decision),
        ]);
        codec.Enum<InitialShipOrderPolicy>([
            ("no-initial-order", InitialShipOrderPolicy.NoInitialOrder),
        ]);
        codec.Enum<InventoryCommitRejectionReason>([
            ("duplicate-operation-key", InventoryCommitRejectionReason.DuplicateOperationKey),
            ("operation-identity-conflict", InventoryCommitRejectionReason.OperationIdentityConflict),
            ("unknown-inventory", InventoryCommitRejectionReason.UnknownInventory),
            ("identifier-capacity-exhausted", InventoryCommitRejectionReason.IdentifierCapacityExhausted),
            ("storage-rejected", InventoryCommitRejectionReason.StorageRejected),
            ("reservation-rejected", InventoryCommitRejectionReason.ReservationRejected),
            ("transfer-rejected", InventoryCommitRejectionReason.TransferRejected),
            ("removal-rejected", InventoryCommitRejectionReason.RemovalRejected),
        ]);
        codec.Enum<InventoryIncomingReservationKind>([
            ("legacy-capacity", InventoryIncomingReservationKind.LegacyCapacity),
            ("physical-capacity", InventoryIncomingReservationKind.PhysicalCapacity),
        ]);
        codec.Enum<InventoryOperationSourceKind>([
            ("explicit", InventoryOperationSourceKind.Explicit),
        ]);
        codec.Enum<InventoryOutgoingReservationKind>([
            ("legacy-material", InventoryOutgoingReservationKind.LegacyMaterial),
            ("physical", InventoryOutgoingReservationKind.Physical),
        ]);
        codec.Enum<InventoryRemovalRejectionReason>([
            ("missing-inventory", InventoryRemovalRejectionReason.MissingInventory),
            ("missing-custody", InventoryRemovalRejectionReason.MissingCustody),
            ("inventory-has-commitments", InventoryRemovalRejectionReason.InventoryHasCommitments),
            ("same-destination", InventoryRemovalRejectionReason.SameDestination),
            ("missing-destination-inventory", InventoryRemovalRejectionReason.MissingDestinationInventory),
            ("insufficient-capacity", InventoryRemovalRejectionReason.InsufficientCapacity),
            ("destination-item-conflict", InventoryRemovalRejectionReason.DestinationItemConflict),
            ("cargo-object-cannot-receive", InventoryRemovalRejectionReason.CargoObjectCannotReceive),
        ]);
        codec.Enum<InventoryReservationRejectionReason>([
            ("missing-custody", InventoryReservationRejectionReason.MissingCustody),
            ("invalid-reservation-id", InventoryReservationRejectionReason.InvalidReservationId),
            ("invalid-owner", InventoryReservationRejectionReason.InvalidOwner),
            ("duplicate-reservation-id", InventoryReservationRejectionReason.DuplicateReservationId),
            ("insufficient-available-quantity", InventoryReservationRejectionReason.InsufficientAvailableQuantity),
            ("missing-item-instance", InventoryReservationRejectionReason.MissingItemInstance),
            ("item-already-reserved", InventoryReservationRejectionReason.ItemAlreadyReserved),
            ("insufficient-capacity", InventoryReservationRejectionReason.InsufficientCapacity),
            ("unknown-reservation", InventoryReservationRejectionReason.UnknownReservation),
            ("owner-mismatch", InventoryReservationRejectionReason.OwnerMismatch),
            ("cargo-object-cannot-reserve", InventoryReservationRejectionReason.CargoObjectCannotReserve),
            ("incoming-admission-sequence-exhausted", InventoryReservationRejectionReason.IncomingAdmissionSequenceExhausted),
            ("outgoing-admission-sequence-exhausted", InventoryReservationRejectionReason.OutgoingAdmissionSequenceExhausted),
        ]);
        codec.Enum<InventoryStorageRejectionReason>([
            ("missing-custody", InventoryStorageRejectionReason.MissingCustody),
            ("holding-kind-mismatch", InventoryStorageRejectionReason.HoldingKindMismatch),
            ("invalid-quantity", InventoryStorageRejectionReason.InvalidQuantity),
            ("invalid-item-instance", InventoryStorageRejectionReason.InvalidItemInstance),
            ("duplicate-item-instance", InventoryStorageRejectionReason.DuplicateItemInstance),
            ("insufficient-capacity", InventoryStorageRejectionReason.InsufficientCapacity),
            ("capacity-overflow", InventoryStorageRejectionReason.CapacityOverflow),
            ("cargo-object-cannot-receive", InventoryStorageRejectionReason.CargoObjectCannotReceive),
        ]);
        codec.Enum<InventoryTransferRejectionReason>([
            ("same-inventory", InventoryTransferRejectionReason.SameInventory),
            ("missing-source-inventory", InventoryTransferRejectionReason.MissingSourceInventory),
            ("missing-destination-inventory", InventoryTransferRejectionReason.MissingDestinationInventory),
            ("missing-custody", InventoryTransferRejectionReason.MissingCustody),
            ("holding-kind-mismatch", InventoryTransferRejectionReason.HoldingKindMismatch),
            ("definition-mismatch", InventoryTransferRejectionReason.DefinitionMismatch),
            ("insufficient-available-quantity", InventoryTransferRejectionReason.InsufficientAvailableQuantity),
            ("missing-item-instance", InventoryTransferRejectionReason.MissingItemInstance),
            ("item-already-reserved", InventoryTransferRejectionReason.ItemAlreadyReserved),
            ("destination-item-conflict", InventoryTransferRejectionReason.DestinationItemConflict),
            ("source-reservation-mismatch", InventoryTransferRejectionReason.SourceReservationMismatch),
            ("destination-reservation-mismatch", InventoryTransferRejectionReason.DestinationReservationMismatch),
            ("invalid-reservation-owner", InventoryTransferRejectionReason.InvalidReservationOwner),
            ("insufficient-capacity", InventoryTransferRejectionReason.InsufficientCapacity),
            ("capacity-overflow", InventoryTransferRejectionReason.CapacityOverflow),
            ("cargo-object-cannot-receive", InventoryTransferRejectionReason.CargoObjectCannotReceive),
            ("invalid-collection-authority", InventoryTransferRejectionReason.InvalidCollectionAuthority),
        ]);
        codec.Enum<LocalMotionEndReason>([
            ("arrived", LocalMotionEndReason.Arrived),
            ("cancelled-by", LocalMotionEndReason.CancelledByCommand),
            ("replaced-by", LocalMotionEndReason.ReplacedByCommand),
            ("suspended-by-scripted-override", LocalMotionEndReason.SuspendedByScriptedOverride),
            ("scripted-override-ended", LocalMotionEndReason.ScriptedOverrideEnded),
            ("target-removed", LocalMotionEndReason.TargetRemoved),
            ("action-completed", LocalMotionEndReason.ActionCompleted),
            ("action-failed", LocalMotionEndReason.ActionFailed),
        ]);
        codec.Enum<ManeuverDecelerationKind>([
            ("passive-drag", ManeuverDecelerationKind.PassiveDrag),
            ("active-brake", ManeuverDecelerationKind.ActiveBrake),
        ]);
        codec.Enum<ManeuverObjective>([
            ("fastest-arrival", ManeuverObjective.FastestArrival),
            ("shortest-path", ManeuverObjective.ShortestPath),
        ]);
        codec.Enum<ManeuverPhaseKind>([
            ("turn", ManeuverPhaseKind.Turn),
            ("accelerate", ManeuverPhaseKind.Accelerate),
            ("moving-spool", ManeuverPhaseKind.MovingSpool),
            ("cruise-travel", ManeuverPhaseKind.CruiseTravel),
            ("cruise-dropout-brake", ManeuverPhaseKind.CruiseDropoutBrake),
            ("capped-speed-travel", ManeuverPhaseKind.CappedSpeedTravel),
            ("coast-under-passive-drag", ManeuverPhaseKind.CoastUnderPassiveDrag),
            ("active-brake", ManeuverPhaseKind.ActiveBrake),
            ("terminal-settle", ManeuverPhaseKind.TerminalSettle),
        ]);
        codec.Enum<ObservedSensorChangeKind>([
            ("appeared", ObservedSensorChangeKind.Appeared),
            ("disappeared", ObservedSensorChangeKind.Disappeared),
        ]);
        codec.Enum<ObservedShipChangeKind>([
            ("motion-changed", ObservedShipChangeKind.MotionChanged),
            ("departed", ObservedShipChangeKind.Departed),
            ("arrived", ObservedShipChangeKind.Arrived),
            ("appeared", ObservedShipChangeKind.Appeared),
            ("disappeared", ObservedShipChangeKind.Disappeared),
        ]);
        codec.Enum<ObservedStationChangeKind>([
            ("appeared", ObservedStationChangeKind.Appeared),
            ("changed", ObservedStationChangeKind.Changed),
            ("disappeared", ObservedStationChangeKind.Disappeared),
        ]);
        codec.Enum<OrderPlacement>([
            ("replace-all", OrderPlacement.ReplaceAll),
            ("append", OrderPlacement.Append),
        ]);
        codec.Enum<PhysicalHoldingKind>([
            ("fungible", PhysicalHoldingKind.Fungible),
            ("discrete", PhysicalHoldingKind.Discrete),
        ]);
        codec.Enum<ProductionJobStatus>([
            ("waiting-for-inputs", ProductionJobStatus.WaitingForInputs),
            ("running", ProductionJobStatus.Running),
            ("completed-awaiting-storage", ProductionJobStatus.CompletedAwaitingStorage),
            ("completed", ProductionJobStatus.Completed),
            ("cancelled", ProductionJobStatus.Cancelled),
            ("paused", ProductionJobStatus.Paused),
        ]);
        codec.Enum<RandomScope>([
            ("new-game-generation", RandomScope.NewGameGeneration),
            ("session-runtime", RandomScope.SessionRuntime),
        ]);
        codec.Enum<RelationshipPolicyChangeReason>([
            ("explicit", RelationshipPolicyChangeReason.Explicit),
        ]);
        codec.Enum<RelationshipPolicyChangeSourceKind>([
            ("explicit", RelationshipPolicyChangeSourceKind.Explicit),
        ]);
        codec.Enum<ScriptedOverrideReleasePolicy>([
            ("cancel-outstanding", ScriptedOverrideReleasePolicy.CancelOutstanding),
        ]);
        codec.Enum<SensorActionFailure>([
            ("missing-actor", SensorActionFailure.MissingActor),
            ("item-unavailable", SensorActionFailure.ItemUnavailable),
            ("item-reserved", SensorActionFailure.ItemReserved),
            ("target-removed", SensorActionFailure.TargetRemoved),
            ("definition-unavailable", SensorActionFailure.DefinitionUnavailable),
            ("unauthorized", SensorActionFailure.Unauthorized),
            ("out-of-range", SensorActionFailure.OutOfRange),
            ("insufficient-capacity", SensorActionFailure.InsufficientCapacity),
            ("identifier-exhausted", SensorActionFailure.IdentifierExhausted),
            ("command-identity-conflict", SensorActionFailure.CommandIdentityConflict),
            ("invalid-spatial-state", SensorActionFailure.InvalidSpatialState),
        ]);
        codec.Enum<SensorObservationState>([
            ("live", SensorObservationState.Live),
            ("stale", SensorObservationState.Stale),
            ("confirmed-missing", SensorObservationState.ConfirmedMissing),
        ]);
        codec.Enum<ShipOrderKind>([
            ("move", ShipOrderKind.Move),
            ("deploy-sensor", ShipOrderKind.DeploySensor),
            ("pickup-sensor", ShipOrderKind.PickupSensor),
            ("collect-cargo-object", ShipOrderKind.CollectCargoObject),
            ("collect-cargo-field", ShipOrderKind.CollectCargoField),
        ]);
        codec.Enum<ShipOrderReason>([
            ("queued-behind-active-order", ShipOrderReason.QueuedBehindActiveOrder),
            ("moving-to-destination", ShipOrderReason.MovingToDestination),
            ("destination-reached", ShipOrderReason.DestinationReached),
            ("cancelled-by", ShipOrderReason.CancelledByCommand),
            ("replaced-by", ShipOrderReason.ReplacedByCommand),
            ("suspended-by-scripted-override", ShipOrderReason.SuspendedByScriptedOverride),
            ("resuming-after-scripted-override", ShipOrderReason.ResumingAfterScriptedOverride),
            ("scripted-override-ended", ShipOrderReason.ScriptedOverrideEnded),
            ("waiting-for-connector-transit-completion", ShipOrderReason.WaitingForConnectorTransitCompletion),
            ("destination-became-unreachable", ShipOrderReason.DestinationBecameUnreachable),
            ("target-removed", ShipOrderReason.TargetRemoved),
            ("awaiting-action-execution", ShipOrderReason.AwaitingActionExecution),
            ("action-completed", ShipOrderReason.ActionCompleted),
            ("action-item-unavailable", ShipOrderReason.ActionItemUnavailable),
            ("action-item-reserved", ShipOrderReason.ActionItemReserved),
            ("action-capacity-unavailable", ShipOrderReason.ActionCapacityUnavailable),
            ("action-unauthorized", ShipOrderReason.ActionUnauthorized),
            ("action-invalid-spatial-state", ShipOrderReason.ActionInvalidSpatialState),
            ("action-definition-unavailable", ShipOrderReason.ActionDefinitionUnavailable),
            ("action-identifier-exhausted", ShipOrderReason.ActionIdentifierExhausted),
            ("action-actor-removed", ShipOrderReason.ActionActorRemoved),
            ("collecting-cargo", ShipOrderReason.CollectingCargo),
        ]);
        codec.Enum<ShipOrderStatus>([
            ("queued", ShipOrderStatus.Queued),
            ("active", ShipOrderStatus.Active),
            ("waiting", ShipOrderStatus.Waiting),
            ("suspended", ShipOrderStatus.Suspended),
            ("completed", ShipOrderStatus.Completed),
            ("cancelled", ShipOrderStatus.Cancelled),
            ("failed", ShipOrderStatus.Failed),
        ]);
        codec.Enum<StandingBand>([
            ("hostile", StandingBand.Hostile),
            ("adversarial", StandingBand.Adversarial),
            ("neutral", StandingBand.Neutral),
            ("favorable", StandingBand.Favorable),
            ("allied", StandingBand.Allied),
        ]);
        codec.Enum<StandingChangeReason>([
            ("explicit", StandingChangeReason.Explicit),
        ]);
        codec.Enum<StandingChangeSourceKind>([
            ("explicit", StandingChangeSourceKind.Explicit),
        ]);
        codec.Enum<StationaryDirectionalPlanKind>([
            ("terminal-settle", StationaryDirectionalPlanKind.TerminalSettle),
            ("primary-sub-cruise", StationaryDirectionalPlanKind.PrimarySubCruise),
            ("turn-then-primary", StationaryDirectionalPlanKind.TurnThenPrimary),
            ("turn-then-precision-sub-cruise", StationaryDirectionalPlanKind.TurnThenPrecisionSubCruise),
            ("precision-sub-cruise", StationaryDirectionalPlanKind.PrecisionSubCruise),
            ("directional-planner", StationaryDirectionalPlanKind.DirectionalPlanner),
        ]);
        codec.Enum<StationCommitmentKind>([
            ("material", StationCommitmentKind.Material),
            ("capacity", StationCommitmentKind.Capacity),
            ("physical", StationCommitmentKind.Physical),
        ]);
        codec.Enum<StationModulePhase>([
            ("under-construction", StationModulePhase.UnderConstruction),
            ("operational", StationModulePhase.Operational),
            ("unfinished", StationModulePhase.Unfinished),
            ("dismantling", StationModulePhase.Dismantling),
        ]);
        codec.Enum<StationPhase>([
            ("site", StationPhase.Site),
            ("operational", StationPhase.Operational),
            ("dismantling", StationPhase.Dismantling),
        ]);
        codec.Enum<StationProjectAction>([
            ("pause", StationProjectAction.Pause),
            ("resume", StationProjectAction.Resume),
            ("cancel", StationProjectAction.Cancel),
            ("restart-base-construction", StationProjectAction.RestartBaseConstruction),
        ]);
        codec.Enum<StationProjectKind>([
            ("base-construction", StationProjectKind.BaseConstruction),
            ("module-construction", StationProjectKind.ModuleConstruction),
            ("upgrade", StationProjectKind.Upgrade),
            ("dismantling", StationProjectKind.Dismantling),
        ]);
        codec.Enum<StationProjectStatus>([
            ("waiting", StationProjectStatus.Waiting),
            ("working", StationProjectStatus.Working),
            ("paused", StationProjectStatus.Paused),
            ("cancelled", StationProjectStatus.Cancelled),
            ("completed", StationProjectStatus.Completed),
        ]);
        codec.Enum<StationRemovalReason>([
            ("dismantled", StationRemovalReason.Dismantled),
            ("destroyed", StationRemovalReason.Destroyed),
        ]);
        codec.Enum<StationServiceType>([
            ("storage", StationServiceType.Storage),
            ("sensors", StationServiceType.Sensors),
            ("production", StationServiceType.Production),
            ("construction", StationServiceType.Construction),
            ("trade", StationServiceType.Trade),
            ("repair", StationServiceType.Repair),
            ("defense", StationServiceType.Defense),
            ("refitting", StationServiceType.Refitting),
        ]);
        codec.Enum<StationTerritoryPolicy>([
            ("absent", StationTerritoryPolicy.Absent),
        ]);
        codec.Enum<TransportJobStatus>([
            ("assigned", TransportJobStatus.Assigned),
            ("waiting-for-route-to-source", TransportJobStatus.WaitingForRouteToSource),
            ("traveling-to-source", TransportJobStatus.TravelingToSource),
            ("loading", TransportJobStatus.Loading),
            ("waiting-for-route-to-destination", TransportJobStatus.WaitingForRouteToDestination),
            ("traveling-to-destination", TransportJobStatus.TravelingToDestination),
            ("waiting-for-destination-capacity", TransportJobStatus.WaitingForDestinationCapacity),
            ("unloading", TransportJobStatus.Unloading),
            ("completed", TransportJobStatus.Completed),
            ("failed-before-loading", TransportJobStatus.FailedBeforeLoading),
            ("cancelled", TransportJobStatus.Cancelled),
        ]);
        codec.Enum<TravelTarget>([
            ("source", TravelTarget.Source),
            ("destination", TravelTarget.Destination),
        ]);

        // These runtime fact constructors flatten an outcome; reconstruction validates the same retained values.
        codec.Factory<StandingChangedFact>(["AssessingPrincipalId", "SubjectPrincipalId", "PriorValue", "PriorBand", "CombinedDelta", "ResultingValue", "ResultingBand", "Contributions"], values =>
            new StandingChangedFact(new StandingChangeOutcome((PrincipalId)values[0]!, (PrincipalId)values[1]!,
                (StandingValue)values[2]!, (StandingBand)values[3]!, (long)values[4]!, (StandingValue)values[5]!,
                (StandingBand)values[6]!, (IEnumerable<StandingChangeContribution>)values[7]!)));
        codec.Factory<DiplomaticConditionChangedFact>(["LowerPrincipalId", "UpperPrincipalId", "PriorCondition", "ResultingCondition", "Reason"], values =>
            new DiplomaticConditionChangedFact(new DiplomaticConditionChangeOutcome((PrincipalId)values[0]!,
                (PrincipalId)values[1]!, (DiplomaticCondition)values[2]!, (DiplomaticCondition)values[3]!,
                (RelationshipPolicyChangeReason)values[4]!)));
        codec.Factory<RelationshipGrantIssuedFact>(["Id", "IssuerPrincipalId", "HolderPrincipalId", "Kind:valueKind", "MinimumStandingBand", "Reason"], values =>
            new RelationshipGrantIssuedFact(new RelationshipGrantChangeOutcome((RelationshipGrantId)values[0]!,
                (PrincipalId)values[1]!, (PrincipalId)values[2]!, (RelationshipGrantKind)values[3]!,
                (StandingBand)values[4]!, false, true, (RelationshipPolicyChangeReason)values[5]!)));
        codec.Object<RelationshipGrantRevokedFact>(["Id", "Reason"]);
        // Unclaimed custody has one exact session-entity owner and deliberately has no controlling principal.
        codec.Factory<InventoryCustody.Unclaimed>(["PhysicalOwner"], values =>
            values[0] is InventoryOwnerReference.SessionEntity owner
                ? new InventoryCustody.Unclaimed(owner.EntityId)
                : throw new ArgumentException("Unclaimed custody requires a session entity."));
    }
}
