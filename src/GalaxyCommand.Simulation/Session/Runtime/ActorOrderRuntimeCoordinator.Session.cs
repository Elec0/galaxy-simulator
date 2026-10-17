namespace GalaxyCommand.Simulation;

internal sealed partial class ActorOrderRuntimeCoordinator
{
    internal void ThrowIfUnhealthy()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_evaluatingInteractions) throw new InvalidOperationException("Interaction evaluation must use stable inputs and buffer its effects.");
        if (_isPoisoned)
        {
            throw new InvalidOperationException(
                "The game session is unhealthy after an invariant failure.");
        }
    }

    /// <summary>Prepares current interests and advances within live controlled intervals, including admissions during the run.</summary>
    internal RunReport AdvanceTo(SimulationTime target)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(target.Milliseconds, CurrentTime.Milliseconds, nameof(target));
        _interactionRegistration?.EnsureEvaluationBindings();
        try
        {
            PrepareStateUpdate(CurrentTime, _agenda);
            return AdvanceWithinControlledMotion(target);
        }
        catch
        {
            // Due envelopes may already have been popped or some effects committed.
            // A failed wave cannot safely resume or publish a checkpoint.
            _isPoisoned = true;
            throw;
        }
    }

    internal GameSnapshot CaptureSnapshot()
    {
        IReadOnlyList<ShipSpatialSnapshot> spatial =
            _movement.CaptureSnapshot(CurrentTime);
        return new GameSnapshot(
            CurrentTime,
            GameSnapshotCollection.Copy(_worldTopology.Systems.Select(system =>
                new GameSystemSnapshot(system.Id, system.Name))),
            GameSnapshotCollection.Copy(_worldTopology.Connectors.Endpoints.Select(endpoint =>
                new ConnectorEndpointSnapshot(
                    endpoint.Id,
                    endpoint.Position))),
            GameSnapshotCollection.Copy(_worldTopology.Connectors.Connections.Select(connection =>
                new TransitConnectionSnapshot(
                    connection.Id,
                    connection.SourceEndpointId,
                    connection.DestinationEndpointId,
                    connection.Duration))),
            _relationships.CaptureSnapshot(),
            GameSnapshotCollection.Copy(spatial.Select(ship =>
            {
                GameSessionShip record = _lifecycle.GetRequiredShip(ship.ShipId);
                Inventory cargo = _lifecycle.GetRequiredCargo(ship.ShipId);
                return new GameShipSnapshot(
                    _lifecycle.Entities.GetEntityId(ship.ShipId)
                        ?? throw new InvalidOperationException(
                            $"Ship {ship.ShipId} has no live entity registration."),
                    ship.ShipId,
                    record.PrincipalId,
                    record.DesignId,
                    record.CargoInventoryId,
                    cargo.Capacity,
                    record.ManeuverCapabilityRevision,
                    ship.State,
                    ship.Velocity,
                    ship.Heading,
                    _control.Capture(ship.ShipId),
                    _orders.CaptureCurrent(ship.ShipId),
                    _orders.CaptureQueue(ship.ShipId),
                    _orders.CaptureSuspended(ship.ShipId));
            })));
    }

    internal GameplayCommandHandlingResult Handle(
        GameplayCommandEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return envelope.Command switch
        {
            MoveShipCommand move => HandleMove(envelope.Source, move),
            MoveShipGroupCommand groupMove => HandleGroupMove(envelope.Source, groupMove),
            CancelShipOrderCommand cancel => HandleCancel(envelope.Source, cancel),
            CancelShipGroupCommand groupCancel => HandleGroupCancel(envelope.Source, groupCancel),
            BeginScriptedOverrideCommand begin =>
                HandleBeginOverride(envelope.Source, begin),
            EndScriptedOverrideCommand end =>
                HandleEndOverride(envelope.Source, end),
            _ => new GameplayCommandHandlingResult(
                CommandResult.Rejected(
                    CommandRejectionCodes.UnsupportedCommand,
                    $"Gameplay command '{envelope.Command.Kind}' is not supported yet.")),
        };
    }

    /// <summary>
    /// Applies a prepared entity removal, invalidates inbound entity-target
    /// orders, and commits their facts before the removal fact. Controlled motion on the
    /// removed actor or an inbound-order owner rejects before any affected owner mutates.
    /// </summary>
    internal EntityRemovalResult RemoveEntity(EntityRemovalRequest request)
    {
        ThrowIfUnhealthy();
        EntityRemovalPreparation preparation = _lifecycle.PrepareRemoval(
            request,
            permitOwnerReleasedCommitments: _economy is not null);
        if (preparation is EntityRemovalPreparation.Resolved resolved)
        {
            return resolved.Value;
        }

        PreparedEntityRemoval removal =
            ((EntityRemovalPreparation.Prepared)preparation).Value;
        // Removal also changes inbound orders. Protect every affected controlled
        // participant before any economy, agenda, order, or lifecycle commit.
        if (_movement.GetState(removal.ShipId) is ShipSpatialState.InteractionControlled ||
            removal.InboundOrders.Any(targeted => _movement.GetState(targeted.ShipId) is ShipSpatialState.InteractionControlled))
            return new EntityRemovalResult.Rejected(request, EntityRemovalRejectionReason.OwnerConflict);

        PreparedEconomyEntityRemoval? economyRemoval = null;
        if (_economy is not null
            && !_economy.TryPrepareEntityRemoval(
                removal.ShipId,
                removal.CargoInventoryId,
                out economyRemoval))
        {
            return new EntityRemovalResult.Rejected(
                request,
                EntityRemovalRejectionReason.OwnerConflict);
        }

        EntityRemovalResult.Rejected? cancellationRejection =
            PrepareMovementCancellations(removal, out PreparedMovementCancellation[] cancellations);
        if (cancellationRejection is not null)
        {
            return cancellationRejection;
        }

        foreach (PreparedMovementCancellation cancellation in cancellations)
        {
            if (_agenda.TryCancelExact(
                cancellation.EventKey,
                cancellation.Generation,
                cancellation.Event))
            {
                continue;
            }

            _isPoisoned = true;
            throw new InvalidOperationException(
                $"Prepared cancellation for {cancellation.EventKey} no longer matches the agenda.");
        }

        if (_economy is not null && economyRemoval is not null)
        {
            try
            {
                _economy.ApplyEntityRemoval(economyRemoval);
            }
            catch
            {
                _isPoisoned = true;
                throw;
            }
        }

        var transitions = new List<ShipOrderTransition>();
        var factProposals = new List<GameFactProposal>();
        TargetedShipOrder[] applyOrder = removal.InboundOrders
            .OrderBy(targeted => targeted.WasCurrentActive)
            .ThenBy(targeted => targeted.ShipId.Value)
            .ThenBy(targeted => targeted.OrderId.Value)
            .ToArray();
        foreach (TargetedShipOrder targeted in applyOrder)
        {
            if (targeted.WasCurrentActive)
            {
                EndActiveLocalMotion(
                    targeted.ShipId,
                    LocalMotionEndReason.TargetRemoved,
                    factProposals);
            }

            _orders.ApplyTargetRemoval(targeted, transitions);
        }

        EntityRemovalResult result = _lifecycle.ApplyRemoval(removal, CurrentTime);
        foreach (ShipId shipId in removal.InboundOrders
                     .Where(targeted => targeted.WasCurrentActive)
                     .Select(targeted => targeted.ShipId)
                     .Distinct()
                     .OrderBy(shipId => shipId.Value))
        {
            StartOrContinueOrders(shipId, transitions, factProposals);
        }

        AddOrderTransitionProposals(
            transitions
                .OrderBy(transition => transition.ShipId.Value)
                .ThenBy(transition => transition.OrderId.Value),
            factProposals);
        var removed = (EntityRemovalResult.Removed)result;
        factProposals.Add(new GameFactProposal(
            new GameFactProposalKey(
                GameFactCommitCategory.EntityLifecycle,
                removed.Request.EntityId.Value,
                removed.ShipId.Value,
                0),
            new EntityRemovedFact(
                removed.Request.EntityId,
                EntityKind.Ship,
                removed.ShipId,
                removed.Request.Reason,
                removed.Request.CargoDisposition)));
        _facts.Commit(
            CurrentTime,
            new EntityRemovalFactCause(removed.Request),
            factProposals);
        return result;
    }

    /// <summary>
    /// Commits prepared relationship state and publishes one fact for each
    /// changed directional pair in stable principal order.
    /// </summary>
    internal StandingChangeBatchResult CommitStandingChanges(
        StandingChangeBatch batch)
    {
        StandingChangePreparation preparation =
            _relationships.PrepareStandingChanges(batch);
        if (preparation is StandingChangePreparation.Resolved resolved)
        {
            return resolved.Result;
        }

        PreparedStandingChange prepared =
            ((StandingChangePreparation.Prepared)preparation).Value;
        if (!_facts.CanCommit(prepared.ChangedOutcomes.Count))
        {
            return new StandingChangeBatchResult.Rejected(
                batch.Id,
                StandingChangeRejectionReason.FactSequenceExhausted);
        }

        StandingChangeBatchResult result =
            _relationships.ApplyStandingChanges(prepared);
        _facts.Commit(
            CurrentTime,
            new StandingChangeFactCause(batch.Id),
            prepared.ChangedOutcomes.Select(outcome => new GameFactProposal(
                new GameFactProposalKey(
                    GameFactCommitCategory.Relationship,
                    outcome.AssessingPrincipalId.Value,
                    outcome.SubjectPrincipalId.Value,
                    0),
                new StandingChangedFact(outcome))));
        return result;
    }

    /// <summary>
    /// Commits one prepared diplomacy and grant batch and publishes changed
    /// outcomes in deterministic relationship order.
    /// </summary>
    internal RelationshipPolicyChangeBatchResult CommitRelationshipPolicyChanges(
        RelationshipPolicyChangeBatch batch)
    {
        RelationshipPolicyChangePreparation preparation =
            _relationships.PreparePolicyChanges(batch);
        if (preparation is RelationshipPolicyChangePreparation.Resolved resolved)
        {
            return resolved.Result;
        }

        PreparedRelationshipPolicyChange prepared =
            ((RelationshipPolicyChangePreparation.Prepared)preparation).Value;
        int factCount = prepared.Result.DiplomaticOutcomes.Count(value => value.Changed)
            + prepared.Result.GrantOutcomes.Count;
        if (!_facts.CanCommit(factCount))
        {
            return new RelationshipPolicyChangeBatchResult.Rejected(
                batch.Id,
                RelationshipPolicyChangeRejectionReason.FactSequenceExhausted);
        }

        RelationshipPolicyChangeBatchResult result =
            _relationships.ApplyPolicyChanges(prepared);
        IEnumerable<GameFactProposal> diplomaticFacts = prepared.Result
            .DiplomaticOutcomes
            .Where(outcome => outcome.Changed)
            .Select(outcome => new GameFactProposal(
                new GameFactProposalKey(
                    GameFactCommitCategory.RelationshipDiplomacy,
                    outcome.LowerPrincipalId.Value,
                    outcome.UpperPrincipalId.Value,
                    0),
                new DiplomaticConditionChangedFact(outcome)));
        IEnumerable<GameFactProposal> grantFacts = prepared.Result.GrantOutcomes
            .Select(outcome => new GameFactProposal(
                new GameFactProposalKey(
                    GameFactCommitCategory.RelationshipGrant,
                    outcome.Id.Value,
                    0,
                    0),
                outcome.ResultingIssued
                    ? new RelationshipGrantIssuedFact(outcome)
                    : new RelationshipGrantRevokedFact(outcome)));
        _facts.Commit(
            CurrentTime,
            new RelationshipPolicyChangeFactCause(batch.Id),
            diplomaticFacts.Concat(grantFacts));
        return result;
    }

    internal DiplomaticCondition GetDiplomaticCondition(
        PrincipalId firstPrincipalId,
        PrincipalId secondPrincipalId) =>
        _relationships.GetDiplomaticCondition(firstPrincipalId, secondPrincipalId);

    internal bool HasEffectiveRelationshipGrant(
        PrincipalId issuerPrincipalId,
        PrincipalId holderPrincipalId,
        RelationshipGrantKind kind) =>
        _relationships.HasEffectiveGrant(issuerPrincipalId, holderPrincipalId, kind);


}
