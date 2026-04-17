using Content.Goobstation.Maths.FixedPoint;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Audio;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Decals;
using Content.Shared.Examine;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Fluids;
using Content.Shared.Fluids.Components;
using Content.Shared.Alert;
using Content.Goobstation.Common.Footprints;
using Content.Goobstation.Shared.Vehicles;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Content.Shared.Interaction.Events;
using Content.Shared.Interaction;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Content.Goobstation.Shared.Vehicles.FloorScrubber;

/// <summary>
///     Handles the core logic for floor scrubbers, including vacuuming puddles, cleaning decals,
///     and managing internal solution tanks.
/// </summary>
/// <remarks>
///     Goobstation - Refactor for modularity: Decoupled from vehicles to support Borgs and Automated Drones.
/// </remarks>
public abstract partial class SharedFloorScrubberSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambientSound = default!;
    [Dependency] private readonly SharedDecalSystem _decal = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly SharedPuddleSystem _puddle = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    /// <summary>
    ///     Pooled collection to prevent allocations during tile cleaning logic.
    /// </summary>
    private readonly HashSet<Vector2i> _targetTiles = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FloorScrubberComponent, FloorScrubberToggleActionEvent>(OnFloorScrubberToggleActionEvent);
        SubscribeLocalEvent<FloorScrubberComponent, FloorScrubberDumpFloorActionEvent>(OnFloorScrubberDumpFloorActionEvent);
        SubscribeLocalEvent<FloorScrubberComponent, FloorScrubberFillActionEvent>(OnFloorScrubberFillActionEvent);
        SubscribeLocalEvent<FloorScrubberComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeedModifiersEvent);
        SubscribeLocalEvent<FloorScrubberComponent, ExaminedEvent>(OnExaminedEvent);
        SubscribeLocalEvent<FloorScrubberComponent, StrappedEvent>(OnStrappedEvent);
        SubscribeLocalEvent<FloorScrubberComponent, UnstrappedEvent>(OnUnstrappedEvent);
        SubscribeLocalEvent<FloorScrubberComponent, EntInsertedIntoContainerMessage>(OnEntInsertedIntoContainerMessage);
        SubscribeLocalEvent<FloorScrubberComponent, ComponentShutdown>(OnComponentShutdown);

        // DoAfter Events
        SubscribeLocalEvent<FloorScrubberComponent, FloorScrubberDumpFloorDoAfterEvent>(OnDumpFloorDoAfter);
        SubscribeLocalEvent<FloorScrubberComponent, FloorScrubberFillDoAfterEvent>(OnFillDoAfter);

        SubscribeLocalEvent<FloorScrubberToolComponent, UseInHandEvent>(OnUseInHandEvent);
        SubscribeLocalEvent<FloorScrubberComponent, InteractUsingEvent>(OnInteractUsing);
    }

    /// <summary>
    ///     Handles a driver buckling into the scrubber.
    /// </summary>
    private void OnStrappedEvent(Entity<FloorScrubberComponent> ent, ref StrappedEvent args)
    {
        UpdateOperators((ent.Owner, ent.Comp));
    }

    /// <summary>
    ///     Handles a driver unbuckling.
    /// </summary>
    private void OnUnstrappedEvent(Entity<FloorScrubberComponent> ent, ref UnstrappedEvent args)
    {
        UpdateOperators((ent.Owner, ent.Comp));

        if (ent.Comp.CleaningEnabled && ent.Comp.RequiresOperator && ent.Comp.ActiveOperators.Count == 0)
            SetCleaningEnabled(ent.Owner, false);
    }

    /// <summary>
    ///     Updates the list of active operators who should receive actions and alerts.
    /// </summary>
    public void UpdateOperators(Entity<FloorScrubberComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        var oldOperators = new HashSet<EntityUid>(ent.Comp.ActiveOperators);
        ent.Comp.ActiveOperators.Clear();

        // 1. Add buckled entities
        if (TryComp<StrapComponent>(ent, out var strap))
        {
            foreach (var occupant in strap.BuckledEntities)
            {
                ent.Comp.ActiveOperators.Add(occupant);
            }
        }

        // 1.5 Add self if SelfOperator is true
        if (ent.Comp.SelfOperator)
        {
            ent.Comp.ActiveOperators.Add(ent.Owner);
        }

        // 2. Add handlers for removed operators
        foreach (var oldOp in oldOperators)
        {
            if (!ent.Comp.ActiveOperators.Contains(oldOp))
                RemoveOperatorEffects((ent.Owner, ent.Comp), oldOp);
        }

        // 3. Apply effects to new operators
        foreach (var newOp in ent.Comp.ActiveOperators)
        {
            if (!oldOperators.Contains(newOp))
                AddOperatorEffects((ent.Owner, ent.Comp), newOp);
        }

        Dirty(ent);
        UpdateAlerts(ent.Owner);
    }

    /// <summary>
    ///     Grants actions to an operator if not suppressed.
    /// </summary>
    private void AddOperatorEffects(Entity<FloorScrubberComponent> ent, EntityUid operatorEnt)
    {
        if (ent.Comp.SuppressActions)
            return;

        _actions.AddAction(operatorEnt, ref ent.Comp.CleanAction, "ActionFloorScrubberToggle", ent);
        _actions.AddAction(operatorEnt, ref ent.Comp.DumpDrainAction, "ActionFloorScrubberDumpDrain", ent);
        _actions.AddAction(operatorEnt, ref ent.Comp.DumpFloorAction, "ActionFloorScrubberDumpFloor", ent);
        _actions.AddAction(operatorEnt, ref ent.Comp.FillAction, "ActionFloorScrubberFill", ent);
    }

    /// <summary>
    ///     Clears alerts and actions for an operator.
    /// </summary>
    private void RemoveOperatorEffects(Entity<FloorScrubberComponent> ent, EntityUid operatorEnt)
    {
        _actions.RemoveAction(operatorEnt, ent.Comp.CleanAction);
        _actions.RemoveAction(operatorEnt, ent.Comp.DumpDrainAction);
        _actions.RemoveAction(operatorEnt, ent.Comp.DumpFloorAction);
        _actions.RemoveAction(operatorEnt, ent.Comp.FillAction);

        _alerts.ClearAlert(operatorEnt, "FloorScrubberClean");
        _alerts.ClearAlert(operatorEnt, "FloorScrubberWaste");
    }

    /// <summary>
    ///     Ensures clean-up of audio and alerts on component shutdown.
    /// </summary>
    private void OnComponentShutdown(Entity<FloorScrubberComponent> ent, ref ComponentShutdown args)
    {
        UpdateCleaningAudio(ent, false);

        foreach (var occupant in ent.Comp.ActiveOperators)
        {
            _alerts.ClearAlert(occupant, "FloorScrubberClean");
            _alerts.ClearAlert(occupant, "FloorScrubberWaste");
        }
    }

    /// <summary>
    ///     Configures the scrubber for borg operation, bypassing vehicle requirements.
    /// </summary>
    public void SetupBorgMode(Entity<FloorScrubberComponent?> ent, BorgModuleFloorScrubberComponent module)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        ent.Comp.RequiresKey = false;
        ent.Comp.RequiresOperator = false;
        ent.Comp.SelfOperator = true;
        ent.Comp.SuppressActions = true;

        ent.Comp.CleaningShape = module.CleaningShape;
        ent.Comp.CleaningAmount = module.CleaningAmount;
        ent.Comp.VacuumAmount = module.VacuumAmount;
        ent.Comp.SpeedMultiplier = module.SpeedMultiplier;
        ent.Comp.ExtraCleaningRange = module.ExtraCleaningRange;

        Dirty(ent);
    }

    /// <summary>
    ///     Removes borg-specific operational flags from the scrubber before removal.
    /// </summary>
    public void TeardownBorgMode(Entity<FloorScrubberComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        ent.Comp.SelfOperator = false;
        Dirty(ent);
    }

    /// <summary>
    ///     Toggles the cleaning state and associated effects (audio, speed).
    /// </summary>
    public void SetCleaningEnabled(Entity<FloorScrubberComponent?> ent, bool enabled)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        if (ent.Comp.CleaningEnabled == enabled)
            return;

        ent.Comp.CleaningEnabled = enabled;
        UpdateCleaningAudio((ent.Owner, ent.Comp), enabled);

        _movementSpeed.RefreshMovementSpeedModifiers(ent.Owner);
        Dirty(ent);

        // Drive the cleaning animation overlay.
        if (TryComp<VehicleComponent>(ent.Owner, out var vehicle) && vehicle.ActiveOverlay.HasValue)
        {
            var state = enabled ? FloorScrubberCleaningVisualState.On : FloorScrubberCleaningVisualState.Off;
            _appearance.SetData(vehicle.ActiveOverlay.Value, FloorScrubberVisuals.Cleaning, state);
        }

        // Update alerts to show/reset severity if we stopped due to a full tank etc.
        UpdateAlerts(ent.Owner);
    }

    /// <summary>
    ///     Starts or stops the looping cleaning audio stream.
    /// </summary>
    private void UpdateCleaningAudio(Entity<FloorScrubberComponent> ent, bool enabled)
    {
        if (enabled)
        {
            if (ent.Comp.CleaningAudioStream != null || ent.Comp.CleaningSound == null)
                return;

            var audioParams = ent.Comp.CleaningSound.Params
                .WithLoop(true)
                .WithVolume(ent.Comp.CleaningSoundVolume)
                .WithMaxDistance(ent.Comp.CleaningSoundRange);

            ent.Comp.CleaningAudioStream = _audio.PlayPvs(ent.Comp.CleaningSound, ent.Owner, audioParams)?.Entity;
        }
        else
        {
            if (ent.Comp.CleaningAudioStream == null)
                return;

            _audio.Stop(ent.Comp.CleaningAudioStream);
            ent.Comp.CleaningAudioStream = null;
        }
    }


    /// <summary>
    ///     Synchronizes cleaning audio state when a key is inserted.
    /// </summary>
    private void OnEntInsertedIntoContainerMessage(Entity<FloorScrubberComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        // Only care about the key slot
        if (args.Container.ID != "key_slot")
            return;

        // Ensure audio state matches for startup.
        UpdateCleaningAudio(ent, ent.Comp.CleaningEnabled);
    }

    /// <summary>
    ///     Handles the usage of borg-held scrubber tools.
    /// </summary>
    private void OnUseInHandEvent(Entity<FloorScrubberToolComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        var user = args.User;
        if (!TryComp<FloorScrubberComponent>(user, out var scrub))
            return;

        switch (ent.Comp.Mode)
        {
            case FloorScrubberToolType.Toggle:
                SetCleaningEnabled(user, !scrub.CleaningEnabled);
                break;
            case FloorScrubberToolType.Fill:
                var fillEv = new FloorScrubberFillActionEvent { Performer = user };
                OnFloorScrubberFillActionEvent((user, scrub), ref fillEv);
                break;
            case FloorScrubberToolType.DumpDrain:
                var drainEv = new FloorScrubberDumpDrainActionEvent { Performer = user };
                RaiseLocalEvent(user, drainEv);
                break;
            case FloorScrubberToolType.DumpFloor:
                var dumpEv = new FloorScrubberDumpFloorActionEvent { Performer = user };
                OnFloorScrubberDumpFloorActionEvent((user, scrub), ref dumpEv);
                break;
        }

        args.Handled = true;
    }

    /// <summary>
    ///     Toggles the cleaning mode via action.
    /// </summary>
    private void OnFloorScrubberToggleActionEvent(Entity<FloorScrubberComponent> ent, ref FloorScrubberToggleActionEvent args)
    {
        if (args.Handled)
            return;

        // Key Check: Cannot toggle if the key slot is empty and requirements are met.
        if (ent.Comp.RequiresKey)
        {
            if (!_itemSlots.TryGetSlot(ent.Owner, "key_slot", out var slot) || !slot.HasItem)
            {
                if (_timing.IsFirstTimePredicted)
                    _popup.PopupEntity(Loc.GetString("floor-scrubber-fill-no-key"), ent.Owner, args.Performer);
                return;
            }
        }

        SetCleaningEnabled(ent.Owner, !ent.Comp.CleaningEnabled);
        args.Handled = true;
    }

    /// <summary>
    ///     Initiates a do-after to dump the waste solution onto the floor.
    /// </summary>
    private void OnFloorScrubberDumpFloorActionEvent(Entity<FloorScrubberComponent> ent, ref FloorScrubberDumpFloorActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;

        if (!_solutionContainer.TryGetSolution(ent.Owner, ent.Comp.WasteSolutionName, out var wasteSolnEnt, out var waste)
            || waste.Volume <= 0)
        {
            if (_timing.IsFirstTimePredicted)
                _popup.PopupEntity(Loc.GetString("floor-scrubber-dump-floor-empty"), ent.Owner, user);
            return;
        }

        var doAfterArgs = new DoAfterArgs(EntityManager, user, 3f, new FloorScrubberDumpFloorDoAfterEvent(), ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
        args.Handled = true;
    }

    /// <summary>
    ///     Executes the dump floor action after the duration.
    /// </summary>
    private void OnDumpFloorDoAfter(Entity<FloorScrubberComponent> ent, ref FloorScrubberDumpFloorDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        var user = args.User;

        if (!_solutionContainer.TryGetSolution(ent.Owner, ent.Comp.WasteSolutionName, out var wasteSolnEnt, out var waste)
            || waste.Volume <= 0)
        {
            if (_timing.IsFirstTimePredicted)
                _popup.PopupEntity(Loc.GetString("floor-scrubber-dump-floor-empty"), ent.Owner, user);
            return;
        }

        var spill = _solutionContainer.SplitSolution(wasteSolnEnt.Value, waste.Volume);
        _puddle.TrySpillAt(_transform.GetMoverCoordinates(ent.Owner), spill, out _);

        if (_timing.IsFirstTimePredicted)
        {
            _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/Fluids/slosh.ogg"), ent.Owner);
            _popup.PopupEntity(Loc.GetString("floor-scrubber-dump-floor-success"), ent.Owner, user);
        }

        args.Handled = true;
        UpdateAlerts(ent.Owner);
    }

    /// <summary>
    ///     Initiates a do-after to refill the clean water tank from a nearby source.
    /// </summary>
    private void OnFloorScrubberFillActionEvent(Entity<FloorScrubberComponent> ent, ref FloorScrubberFillActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;

        if (!_solutionContainer.TryGetSolution(ent.Owner, ent.Comp.CleanSolutionName, out var cleanSolnEnt, out var cleanSoln))
            return;

        if (cleanSoln.AvailableVolume <= 0)
        {
            if (_timing.IsFirstTimePredicted)
                _popup.PopupEntity(Loc.GetString("floor-scrubber-fill-full"), ent.Owner, user);
            return;
        }

        // Find the nearest drainable water source (sink)
        var scrubberPos = _transform.GetMapCoordinates(ent.Owner);
        EntityUid nearestSource = default;
        var nearestDist = float.MaxValue;

        foreach (var candidate in _lookup.GetEntitiesInRange<ReagentTankComponent>(scrubberPos, 1.5f))
        {
            // Only refill from sinks (which have both a ReagentTank and a Drain component)
            if (!HasComp<DrainComponent>(candidate.Owner))
                continue;

            var dist = (_transform.GetWorldPosition(candidate.Owner) - _transform.GetWorldPosition(ent.Owner)).LengthSquared();
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearestSource = candidate.Owner;
            }
        }

        if (nearestSource == default)
        {
            if (_timing.IsFirstTimePredicted)
                _popup.PopupEntity(Loc.GetString("floor-scrubber-fill-no-source"), ent.Owner, user);
            return;
        }

        var doAfterArgs = new DoAfterArgs(EntityManager, user, 3f, new FloorScrubberFillDoAfterEvent(), ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
        args.Handled = true;
    }

    /// <summary>
    ///     Executes the fill tank action after the duration.
    /// </summary>
    private void OnFillDoAfter(Entity<FloorScrubberComponent> ent, ref FloorScrubberFillDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        var user = args.User;

        if (!_solutionContainer.TryGetSolution(ent.Owner, ent.Comp.CleanSolutionName, out var cleanSolnEnt, out var cleanSoln))
            return;

        if (cleanSoln.AvailableVolume <= 0)
        {
            if (_timing.IsFirstTimePredicted)
                _popup.PopupEntity(Loc.GetString("floor-scrubber-fill-full"), ent.Owner, user);
            return;
        }

        // Find the nearest drainable water source (sink) AGAIN since time passed
        var scrubberPos = _transform.GetMapCoordinates(ent.Owner);
        EntityUid nearestSource = default;
        var nearestDist = float.MaxValue;

        foreach (var candidate in _lookup.GetEntitiesInRange<ReagentTankComponent>(scrubberPos, 1.5f))
        {
            if (!HasComp<DrainComponent>(candidate.Owner))
                continue;

            var dist = (_transform.GetWorldPosition(candidate.Owner) - _transform.GetWorldPosition(ent.Owner)).LengthSquared();
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearestSource = candidate.Owner;
            }
        }

        if (nearestSource == default)
        {
            if (_timing.IsFirstTimePredicted)
                _popup.PopupEntity(Loc.GetString("floor-scrubber-fill-no-source"), ent.Owner, user);
            return;
        }

        // Drain from the source's drainable solution into the clean tank.
        if (!_solutionContainer.TryGetDrainableSolution(nearestSource, out var sourceSolnEnt, out var sourceSoln))
        {
            if (_timing.IsFirstTimePredicted)
                _popup.PopupEntity(Loc.GetString("floor-scrubber-fill-no-source"), ent.Owner, user);
            return;
        }

        var toFill = FixedPoint2.Min(cleanSoln.AvailableVolume, sourceSoln.Volume);
        if (toFill <= 0)
        {
            if (_timing.IsFirstTimePredicted)
                _popup.PopupEntity(Loc.GetString("floor-scrubber-fill-no-source"), ent.Owner, user);
            return;
        }

        var water = _solutionContainer.SplitSolution(sourceSolnEnt.Value, toFill);
        _solutionContainer.TryAddSolution(cleanSolnEnt.Value, water);

        if (_timing.IsFirstTimePredicted)
        {
            _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/Fluids/glug.ogg"), ent.Owner);
            _popup.PopupEntity(Loc.GetString("floor-scrubber-fill-success",
                ("source", Name(nearestSource))), ent.Owner, user);
        }

        args.Handled = true;
        UpdateAlerts(ent.Owner);
    }

    /// <summary>
    ///     Intercepts interactions with containers to provide useful customized popups
    ///     when attempting to pour or failing to draw. Let native DrainableSolution handle the core drawing.
    /// </summary>
    private void OnInteractUsing(Entity<FloorScrubberComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        var used = args.Used;
        var user = args.User;

        if (!_solutionContainer.TryGetDrainableSolution(ent.Owner, out _, out var wasteSoln))
            return;

        _solutionContainer.TryGetDrainableSolution(used, out _, out var bucketDrainSoln);
        _solutionContainer.TryGetRefillableSolution(used, out _, out var bucketRefillSoln);

        // Not a fluid container, ignore.
        if (bucketDrainSoln == null && bucketRefillSoln == null)
            return;

        // Will the native system successfully draw waste into the bucket?
        bool canDraw = wasteSoln.Volume > 0 && bucketRefillSoln != null && bucketRefillSoln.AvailableVolume > 0;
        if (canDraw)
            return; // Let the native system handle the successful interaction undisturbed.

        // Native system will fail to draw. 
        // Provide custom popups instead of generic ones.
        
        // If the container has capacity but the waste is empty, they are trying to draw.
        if (wasteSoln.Volume <= 0 && bucketRefillSoln != null && bucketRefillSoln.AvailableVolume > 0)
        {
            if (_timing.IsFirstTimePredicted)
                _popup.PopupEntity(Loc.GetString("floor-scrubber-waste-empty"), ent.Owner, user);
            args.Handled = true;
            return;
        }

        // If the container has liquid and they are using it on the scrubber, they are trying to pour.
        if (bucketDrainSoln != null && bucketDrainSoln.Volume > 0)
        {
            if (_timing.IsFirstTimePredicted)
                _popup.PopupEntity(Loc.GetString("floor-scrubber-refill-sink"), ent.Owner, user);
            args.Handled = true;
            return;
        }
    }

    /// <summary>
    ///     Modifies movement speed based on whether the scrubber is cleaning.
    /// </summary>
    private void OnRefreshMovementSpeedModifiersEvent(Entity<FloorScrubberComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.CleaningEnabled)
            args.ModifySpeed(ent.Comp.SpeedMultiplier, ent.Comp.SpeedMultiplier);
    }

    /// <summary>
    ///     Adds information about tank levels to the examine tooltip.
    /// </summary>
    private void OnExaminedEvent(Entity<FloorScrubberComponent> ent, ref ExaminedEvent args)
    {
        if (_solutionContainer.TryGetSolution(ent.Owner, ent.Comp.CleanSolutionName, out _, out var cleanSoln))
        {
            args.PushMarkup(Loc.GetString("floor-scrubber-examine-clean",
                ("amount", cleanSoln.Volume),
                ("max", cleanSoln.MaxVolume)));
        }

        if (_solutionContainer.TryGetSolution(ent.Owner, ent.Comp.WasteSolutionName, out _, out var wasteSoln))
        {
            args.PushMarkup(Loc.GetString("floor-scrubber-examine-waste",
                ("amount", wasteSoln.Volume),
                ("max", wasteSoln.MaxVolume)));
        }
    }

    /// <summary>
    ///     Updates active scrubbers. Logic is throttled and only cleans when enabled.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted)
            return;

        var query = EntityQueryEnumerator<FloorScrubberComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var scrubber, out var xform))
        {
            if (!scrubber.CleaningEnabled)
                continue;

            // Key Check: Automatically stop cleaning if the key is removed and required.
            if (scrubber.RequiresKey && (!_itemSlots.TryGetSlot(uid, "key_slot", out var slot) || !slot.HasItem))
            {
                SetCleaningEnabled(uid, false);
                continue;
            }

            // Operator Check: Automatically stop if no operator is active and required.
            if (scrubber.RequiresOperator && scrubber.ActiveOperators.Count == 0)
            {
                SetCleaningEnabled(uid, false);
                continue;
            }

            if (xform.GridUid == null)
                continue;

            scrubber.CleaningAccumulator += frameTime;
            if (scrubber.CleaningAccumulator >= scrubber.CleaningInterval)
            {
                // Reset but keep remainder for smooth timings
                scrubber.CleaningAccumulator -= scrubber.CleaningInterval;
                ProcessTileCleaning((uid, scrubber, xform));
            }
        }
    }

    /// <summary>
    ///     Updates the HUD status alerts for buckled occupants.
    /// </summary>
    protected void UpdateAlerts(Entity<FloorScrubberComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        if (ent.Comp.ActiveOperators.Count == 0)
            return;

        if (!_solutionContainer.TryGetSolution(ent.Owner, ent.Comp.CleanSolutionName, out _, out var cleanSoln) ||
            !_solutionContainer.TryGetSolution(ent.Owner, ent.Comp.WasteSolutionName, out _, out var wasteSoln))
        {
            return;
        }

        var cleanSeverity = (short) Math.Clamp((float) (cleanSoln.Volume / cleanSoln.MaxVolume) * 10, 0, 10);
        var wasteSeverity = (short) Math.Clamp((float) (wasteSoln.Volume / wasteSoln.MaxVolume) * 10, 0, 10);

        foreach (var occupant in ent.Comp.ActiveOperators)
        {
            _alerts.ShowAlert(occupant, "FloorScrubberClean", cleanSeverity);
            _alerts.ShowAlert(occupant, "FloorScrubberWaste", wasteSeverity);
        }
    }

    /// <summary>
    ///     Performs the actual tile cleaning logic (decals and puddles).
    /// </summary>
    protected virtual void ProcessTileCleaning(Entity<FloorScrubberComponent?, TransformComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp1, ref ent.Comp2))
            return;

        var scrubber = ent.Comp1;
        var xform = ent.Comp2;
        if (xform.GridUid == null)
            return;

        var gridUid = xform.GridUid.Value;
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        // Verify we aren't full. We don't auto-stop on clean-empty as it can still vacuum.
        if (!_solutionContainer.TryGetSolution(ent.Owner, scrubber.WasteSolutionName, out var wasteSolnEnt, out var wasteSolution) ||
            wasteSolution.AvailableVolume <= 0)
        {
            SetCleaningEnabled(ent.Owner, false);
            _popup.PopupEntity(Loc.GetString("floor-scrubber-waste-full"), ent.Owner);
            return;
        }

        // 1. Determine which tiles we are cleaning.
        _targetTiles.Clear();
        var targetTiles = _targetTiles;
        var centerTile = _map.LocalToTile(gridUid, grid, xform.Coordinates);
        var range = scrubber.ExtraCleaningRange;

        switch (scrubber.CleaningShape)
        {
            case FloorScrubberShape.Square:
                for (var x = -range; x <= range; x++)
                {
                    for (var y = -range; y <= range; y++)
                    {
                        targetTiles.Add(centerTile + new Vector2i(x, y));
                    }
                }
                break;

            case FloorScrubberShape.Cross:
                targetTiles.Add(centerTile);
                for (var i = 1; i <= range; i++)
                {
                    targetTiles.Add(centerTile + new Vector2i(i, 0));
                    targetTiles.Add(centerTile + new Vector2i(-i, 0));
                    targetTiles.Add(centerTile + new Vector2i(0, i));
                    targetTiles.Add(centerTile + new Vector2i(0, -i));
                }
                break;

            case FloorScrubberShape.Line:
            case FloorScrubberShape.Frontal:
                targetTiles.Add(centerTile);
                // Calculate direction relative to the grid rotation.
                var gridRot = _transform.GetWorldRotation(gridUid);
                var entRot = _transform.GetWorldRotation(ent.Owner);
                var relativeRot = entRot - gridRot;

                if (scrubber.CleaningShape == FloorScrubberShape.Frontal)
                    relativeRot -= Angle.FromDegrees(90);

                var dirVec = relativeRot.ToVec();
                var step = Vector2i.Zero;
                if (Math.Abs(dirVec.X) > Math.Abs(dirVec.Y))
                    step = new Vector2i(dirVec.X > 0 ? 1 : -1, 0);
                else
                    step = new Vector2i(0, dirVec.Y > 0 ? 1 : -1);

                for (var i = 1; i <= range; i++)
                {
                    targetTiles.Add(centerTile + step * i);
                    if (scrubber.CleaningShape == FloorScrubberShape.Line)
                        targetTiles.Add(centerTile - step * i);
                }
                break;
        }

        // 2. Perform spatial lookups for affected tiles.
        // We use GetEntitiesInRange with a radius covering the grid-aligned square,
        // then filter results precisely by the targetTiles set.
        var lookupRadius = range * 1.5f + 0.1f;
        var centerTileLocal = _map.GridTileToLocal(gridUid, grid, centerTile).Position;
        var centerMap = _transform.ToMapCoordinates(new EntityCoordinates(gridUid, centerTileLocal));

        _solutionContainer.TryGetSolution(ent.Owner, scrubber.CleanSolutionName, out var cleanSolnEnt, out var cleanSolution);

        var changed = false;

        // --- Vacuum Logic ---
        if (wasteSolution.AvailableVolume > 0)
        {
            var entities = _lookup.GetEntitiesInRange(centerMap, lookupRadius);
            foreach (var entity in entities)
            {
                if (Deleted(entity) || !TryComp<TransformComponent>(entity, out var entXform))
                    continue;

                var pos = _map.LocalToTile(gridUid, grid, entXform.Coordinates);
                if (!targetTiles.Contains(pos))
                    continue;

                // 1. Filter: If it's a footprint, check if it's "filthy" relative to our own cleaning fluid.
                // If it only contains things that are in our clean tank (mostly water), we skip it.
                var isFootprint = HasComp<FootprintComponent>(entity);
                if (isFootprint)
                {
                    if (_solutionContainer.TryGetSolution(entity, "print", out _, out var printSol))
                    {
                        var isFilthy = false;
                        foreach (var reagent in printSol.Contents)
                        {
                            if (cleanSolution == null || !cleanSolution.Contents.Any(r => r.Reagent.Prototype == reagent.Reagent.Prototype))
                            {
                                isFilthy = true;
                                break;
                            }
                        }

                        if (!isFilthy)
                            continue;
                    }
                }

                // Apply changes to unpredicted entities ONLY on the server to prevent visual desync/flicker.
                if (_net.IsServer)
                {
                    if (isFootprint)
                    {
                        RaiseLocalEvent(entity, new FootprintCleanEvent());
                        changed = true;
                    }

                    // 2. Vacuum Puddle solutions
                    if (TryComp<PuddleComponent>(entity, out var puddle) &&
                        _solutionContainer.TryGetSolution(entity, "puddle", out var puddleSolnEnt, out var puddleSolution))
                    {
                        var drawAmount = FixedPoint2.Min(scrubber.VacuumAmount, puddleSolution.Volume, wasteSolution.AvailableVolume);
                        if (drawAmount > 0)
                        {
                            var removed = _solutionContainer.SplitSolution(puddleSolnEnt.Value, drawAmount);
                            _solutionContainer.TryAddSolution(wasteSolnEnt.Value, removed);
                            changed = true;

                            if (puddleSolution.Volume <= 0)
                                QueueDel(entity);
                        }
                    }
                }
            }
        }

        // --- Scrubbing Logic ---
        if (cleanSolnEnt != null && cleanSolution != null && cleanSolution.Volume >= scrubber.CleaningAmount)
        {
            // Sync logic for the Water Trail:
            // While cleaning is active and clean water is available, we force the 'print' solution to be filled with water.
            // This dilutes/clears any other grime we've picked up, generating the "clean" trail requested by the user.
            if (scrubber.CleaningEnabled && _solutionContainer.TryGetSolution(ent.Owner, "print", out var printSolnEnt))
            {
                // We keep a consistent small amount (e.g., 2u) in the print tank to draw from.
                var targetAmount = FixedPoint2.New(2);

                if (printSolnEnt.Value.Comp.Solution.Volume < targetAmount)
                {
                    var amount = FixedPoint2.Min(targetAmount - printSolnEnt.Value.Comp.Solution.Volume, cleanSolution.Volume);
                    if (amount > 0)
                    {
                        var move = _solutionContainer.SplitSolution(cleanSolnEnt.Value, amount);
                        _solutionContainer.TryAddSolution(printSolnEnt.Value, move);
                        changed = true;
                    }
                }
            }

            var decals = _decal.GetDecalsInRange(gridUid, centerTileLocal, lookupRadius);
            var anyCleaned = false;

            if (_net.IsServer)
            {
                foreach (var (decalId, decal) in decals)
                {
                    if (!decal.Cleanable)
                        continue;

                    var decalPos = _map.LocalToTile(gridUid, grid, new EntityCoordinates(gridUid, decal.Coordinates));
                    if (!targetTiles.Contains(decalPos))
                        continue;

                    _decal.RemoveDecal(gridUid, decalId);
                    anyCleaned = true;
                }
            }

            if (anyCleaned)
            {
                var water = _solutionContainer.SplitSolution(cleanSolnEnt.Value, scrubber.CleaningAmount);
                _puddle.TrySpillAt(xform.Coordinates, water, out _, sound: false);
                changed = true;
            }
        }

        if (changed)
            UpdateAlerts(ent.Owner);
    }
}
