using Content.Goobstation.Maths.FixedPoint;
using Content.Shared.Actions;
using Content.Shared.Audio;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Decals;
using Content.Shared.Examine;
using Content.Shared.Fluids.Components;
using Content.Shared.Fluids;
using Content.Shared.Movement.Events;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using System.Numerics;

namespace Content.Goobstation.Shared.Vehicles.FloorScrubber;

public abstract class SharedFloorScrubberSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] protected readonly SharedAmbientSoundSystem _ambientSound = default!;
    [Dependency] protected readonly SharedDecalSystem DecalSystem = default!;
    [Dependency] protected readonly EntityLookupSystem Lookup = default!;
    [Dependency] protected readonly SharedMapSystem MapSystem = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] protected readonly SharedPuddleSystem PuddleSystem = default!;
    [Dependency] protected readonly SharedSolutionContainerSystem SolutionContainer = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] protected readonly SharedAudioSystem Audio = default!;
    [Dependency] protected readonly SharedPopupSystem Popup = default!;
    [Dependency] protected readonly SharedTransformSystem _transform = default!;

    private static readonly EntProtoId CleanActionId = "ActionFloorScrubberToggle";
    private static readonly EntProtoId DumpDrainActionId = "ActionFloorScrubberDumpDrain";
    private static readonly EntProtoId DumpFloorActionId = "ActionFloorScrubberDumpFloor";
    private static readonly EntProtoId FillActionId = "ActionFloorScrubberFill";
    private static readonly EntProtoId CleanGaugeActionId = "ActionFloorScrubberCleanGauge";
    private static readonly EntProtoId WasteGaugeActionId = "ActionFloorScrubberWasteGauge";

    private const float GaugeUpdateInterval = 0.5f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FloorScrubberComponent, FloorScrubberToggleActionEvent>(OnToggleAction);
        SubscribeLocalEvent<FloorScrubberComponent, FloorScrubberDumpFloorActionEvent>(OnDumpFloor);
        SubscribeLocalEvent<FloorScrubberComponent, FloorScrubberFillActionEvent>(OnFill);
        SubscribeLocalEvent<FloorScrubberComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<FloorScrubberComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<FloorScrubberComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<FloorScrubberComponent, UnstrappedEvent>(OnUnstrapped);
        SubscribeLocalEvent<FloorScrubberComponent, EntInsertedIntoContainerMessage>(OnInsert);
    }

    private void OnStrapped(Entity<FloorScrubberComponent> ent, ref StrappedEvent args)
    {
        var driver = args.Buckle.Owner;
        _actions.AddAction(driver, ref ent.Comp.CleanAction, CleanActionId, ent);
        _actions.AddAction(driver, ref ent.Comp.DumpDrainAction, DumpDrainActionId, ent);
        _actions.AddAction(driver, ref ent.Comp.DumpFloorAction, DumpFloorActionId, ent);
        _actions.AddAction(driver, ref ent.Comp.FillAction, FillActionId, ent);
        _actions.AddAction(driver, ref ent.Comp.CleanGaugeAction, CleanGaugeActionId, ent);
        _actions.AddAction(driver, ref ent.Comp.WasteGaugeAction, WasteGaugeActionId, ent);
        Dirty(ent);

        // Immediately sync gauge state for the new driver.
        UpdateGauges(ent.Owner, ent.Comp);
    }

    private void OnUnstrapped(Entity<FloorScrubberComponent> ent, ref UnstrappedEvent args)
    {
        _actions.RemoveAction(args.Buckle.Owner, ent.Comp.CleanAction);
        _actions.RemoveAction(args.Buckle.Owner, ent.Comp.DumpDrainAction);
        _actions.RemoveAction(args.Buckle.Owner, ent.Comp.DumpFloorAction);
        _actions.RemoveAction(args.Buckle.Owner, ent.Comp.FillAction);
        _actions.RemoveAction(args.Buckle.Owner, ent.Comp.CleanGaugeAction);
        _actions.RemoveAction(args.Buckle.Owner, ent.Comp.WasteGaugeAction);
    }

    private void OnInsert(Entity<FloorScrubberComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        // Only care about the key slot
        if (args.Container.ID != "key_slot")
            return;

        // SharedVehicleSystem turns on the hum. If cleaning is off, we turn it back off.
        _ambientSound.SetAmbience(ent.Owner, ent.Comp.CleaningEnabled);
    }

    private void OnToggleAction(Entity<FloorScrubberComponent> ent, ref FloorScrubberToggleActionEvent args)
    {
        if (args.Handled)
            return;

        ent.Comp.CleaningEnabled = !ent.Comp.CleaningEnabled;
        Dirty(ent);

        _movementSpeed.RefreshMovementSpeedModifiers(ent);
        _ambientSound.SetAmbience(ent.Owner, ent.Comp.CleaningEnabled);
        args.Handled = true;
    }

    private void OnDumpFloor(Entity<FloorScrubberComponent> ent, ref FloorScrubberDumpFloorActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;

        if (!SolutionContainer.TryGetSolution(ent.Owner, ent.Comp.WasteSolutionName, out var wasteSolnEnt, out var waste)
            || waste.Volume <= 0)
        {
            if (_timing.IsFirstTimePredicted)
                Popup.PopupEntity(Loc.GetString("floor-scrubber-dump-floor-empty"), ent.Owner, user);
            return;
        }

        var spill = SolutionContainer.SplitSolution(wasteSolnEnt.Value, waste.Volume);
        PuddleSystem.TrySpillAt(_transform.GetMoverCoordinates(ent.Owner), spill, out _);

        if (_timing.IsFirstTimePredicted)
        {
            Audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/Fluids/slosh.ogg"), ent.Owner);
            Popup.PopupEntity(Loc.GetString("floor-scrubber-dump-floor-success"), ent.Owner, user);
        }

        args.Handled = true;
    }

    private void OnFill(Entity<FloorScrubberComponent> ent, ref FloorScrubberFillActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;

        if (!SolutionContainer.TryGetSolution(ent.Owner, ent.Comp.CleanSolutionName, out var cleanSolnEnt, out var cleanSoln))
            return;

        if (cleanSoln.AvailableVolume <= 0)
        {
            if (_timing.IsFirstTimePredicted)
                Popup.PopupEntity(Loc.GetString("floor-scrubber-fill-full"), ent.Owner, user);
            return;
        }

        // Find the nearest drainable water source (sink)
        var scrubberPos = _transform.GetMapCoordinates(ent.Owner);
        Entity<ReagentTankComponent>? nearestSource = null;
        var nearestDist = float.MaxValue;

        foreach (var candidate in Lookup.GetEntitiesInRange<ReagentTankComponent>(scrubberPos, 1.5f))
        {
            // Only refill from sinks (which have both a ReagentTank and a Drain component)
            if (!HasComp<DrainComponent>(candidate.Owner))
                continue;

            var dist = (_transform.GetWorldPosition(candidate.Owner) - _transform.GetWorldPosition(ent.Owner)).LengthSquared();
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearestSource = candidate;
            }
        }

        if (nearestSource == null)
        {
            if (_timing.IsFirstTimePredicted)
                Popup.PopupEntity(Loc.GetString("floor-scrubber-fill-no-source"), ent.Owner, user);
            return;
        }

        // Drain from the source's drainable solution into the clean tank.
        if (!SolutionContainer.TryGetDrainableSolution(nearestSource.Value.Owner, out var sourceSolnEnt, out var sourceSoln))
        {
            if (_timing.IsFirstTimePredicted)
                Popup.PopupEntity(Loc.GetString("floor-scrubber-fill-no-source"), ent.Owner, user);
            return;
        }

        var toFill = FixedPoint2.Min(cleanSoln.AvailableVolume, sourceSoln.Volume);
        if (toFill <= 0)
        {
            if (_timing.IsFirstTimePredicted)
                Popup.PopupEntity(Loc.GetString("floor-scrubber-fill-no-source"), ent.Owner, user);
            return;
        }

        var water = SolutionContainer.SplitSolution(sourceSolnEnt.Value, toFill);
        SolutionContainer.TryAddSolution(cleanSolnEnt.Value, water);

        if (_timing.IsFirstTimePredicted)
        {
            Audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/Fluids/glug.ogg"), ent.Owner);
            Popup.PopupEntity(Loc.GetString("floor-scrubber-fill-success",
                ("source", EntityManager.GetComponent<MetaDataComponent>(nearestSource.Value.Owner).EntityName)), ent.Owner, user);
        }

        args.Handled = true;
    }

    private void OnRefreshSpeed(Entity<FloorScrubberComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.CleaningEnabled)
            args.ModifySpeed(ent.Comp.SpeedMultiplier, ent.Comp.SpeedMultiplier);
    }

    private void OnExamine(Entity<FloorScrubberComponent> ent, ref ExaminedEvent args)
    {
        if (SolutionContainer.TryGetSolution(ent.Owner, ent.Comp.CleanSolutionName, out _, out var cleanSoln))
        {
            args.PushMarkup(Loc.GetString("floor-scrubber-examine-clean",
                ("amount", cleanSoln.Volume),
                ("max", cleanSoln.MaxVolume)));
        }

        if (SolutionContainer.TryGetSolution(ent.Owner, ent.Comp.WasteSolutionName, out _, out var wasteSoln))
        {
            args.PushMarkup(Loc.GetString("floor-scrubber-examine-waste",
                ("amount", wasteSoln.Volume),
                ("max", wasteSoln.MaxVolume)));
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted)
            return;

        var query = EntityQueryEnumerator<FloorScrubberComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var scrubber, out var xform))
        {
            // Gauge update (throttled)
            if (scrubber.CleanGaugeAction != null || scrubber.WasteGaugeAction != null)
            {
                scrubber.GaugeUpdateAccumulator += frameTime;
                if (scrubber.GaugeUpdateAccumulator >= GaugeUpdateInterval)
                {
                    scrubber.GaugeUpdateAccumulator = 0f;
                    UpdateGauges(uid, scrubber);
                }
            }

            if (!scrubber.CleaningEnabled)
                continue;

            if (xform.GridUid == null)
                continue;

            ProcessTileCleaning((uid, scrubber, xform));
        }
    }

    /// <summary>
    ///     Updates both tank gauge actions to reflect current fill levels.
    ///     Cooldown remaining = fillFraction * displayPeriod, so the sweep overlay represents fill level.
    /// </summary>
    private void UpdateGauges(EntityUid uid, FloorScrubberComponent scrubber)
    {
        UpdateGauge(uid, scrubber.CleanGaugeAction, scrubber.CleanSolutionName, scrubber.GaugeDisplayPeriod);
        UpdateGauge(uid, scrubber.WasteGaugeAction, scrubber.WasteSolutionName, scrubber.GaugeDisplayPeriod);
    }

    private void UpdateGauge(EntityUid uid, EntityUid? gaugeAction, string solutionName, float displayPeriod)
    {
        if (gaugeAction == null)
            return;

        if (!SolutionContainer.TryGetSolution(uid, solutionName, out _, out var soln))
            return;

        var fillFraction = soln.MaxVolume == 0
            ? 0f
            : (float)(soln.Volume / soln.MaxVolume);
        var emptyFraction = 1f - fillFraction;

        var now = _timing.CurTime;
        // start offset so that remaining = fillFraction * displayPeriod
        var start = now - TimeSpan.FromSeconds(emptyFraction * displayPeriod);
        var end = start + TimeSpan.FromSeconds(displayPeriod);
        _actions.SetCooldown(gaugeAction, start, end);
    }

    protected virtual void ProcessTileCleaning(Entity<FloorScrubberComponent, TransformComponent> ent)
    {
        var (uid, scrubber, xform) = ent;

        if (xform.GridUid == null)
            return;

        var gridUid = xform.GridUid.Value;
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        // Get the center of the tile the scrubber is currently on.
        var tileIndices = MapSystem.LocalToTile(gridUid, grid, xform.Coordinates);
        var tileCenter = MapSystem.GridTileToLocal(gridUid, grid, tileIndices).Position;

        // A radius of 1.1f hits the current tile (dist 0) and cardinals (dist 1.0) 
        // while skipping diagonals (dist 1.41).
        var crossRadius = 1.1f;

        // 1. Vacuum Logic — suck puddles into the waste tank
        VacuumTile(ent, gridUid, tileCenter, crossRadius);

        // 2. Scrubbing Logic — clean decals using water from the clean tank
        ScrubTile(ent, gridUid, tileCenter, crossRadius);
    }

    private void VacuumTile(Entity<FloorScrubberComponent, TransformComponent> ent, EntityUid gridUid, Vector2 tileCenter, float radius)
    {
        var (uid, scrubber, _) = ent;

        if (!SolutionContainer.TryGetSolution(uid, scrubber.WasteSolutionName, out var wasteSolnEnt, out var wasteSolution))
            return;

        if (wasteSolution.AvailableVolume <= 0)
            return;

        var frontCoords = new EntityCoordinates(gridUid, tileCenter);
        var frontMap = _transform.ToMapCoordinates(frontCoords);

        foreach (var puddleUid in Lookup.GetEntitiesInRange<PuddleComponent>(frontMap, radius))
        {
            if (!SolutionContainer.TryGetSolution(puddleUid.Owner, "puddle", out var puddleSolnEnt, out var puddleSolution))
                continue;

            var drawAmount = FixedPoint2.Min(scrubber.VacuumAmount, puddleSolution.Volume, wasteSolution.AvailableVolume);
            if (drawAmount <= 0)
                continue;

            var removed = SolutionContainer.SplitSolution(puddleSolnEnt.Value, drawAmount);
            SolutionContainer.TryAddSolution(wasteSolnEnt.Value, removed);
        }
    }

    private void ScrubTile(Entity<FloorScrubberComponent, TransformComponent> ent, EntityUid gridUid, Vector2 tileCenter, float radius)
    {
        var (uid, scrubber, _) = ent;

        if (!SolutionContainer.TryGetSolution(uid, scrubber.CleanSolutionName, out var cleanSolnEnt, out var cleanSolution))
            return;

        if (cleanSolution.Volume < scrubber.CleaningAmount)
            return;

        // Find cleanable decals.
        var decals = DecalSystem.GetDecalsInRange(gridUid, tileCenter, radius, d => d.Cleanable);
        if (decals.Count == 0)
            return;

        // We found something to clean!
        foreach (var (decalId, _) in decals)
        {
            DecalSystem.RemoveDecal(gridUid, decalId);
        }

        // Consume water from the clean tank.
        var water = SolutionContainer.SplitSolution(cleanSolnEnt.Value, scrubber.CleaningAmount);

        // Spill tiny water puddle.
        var coords = new EntityCoordinates(gridUid, tileCenter);
        PuddleSystem.TrySpillAt(coords, water, out _);
    }
}
