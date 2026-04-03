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
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Fluids;
using Content.Shared.Fluids.Components;
using Content.Shared.Alert;
using Content.Goobstation.Common.Footprints;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using System;
using System.Linq;
using System.Numerics;

namespace Content.Goobstation.Shared.Vehicles.FloorScrubber;

public abstract class SharedFloorScrubberSystem : EntitySystem
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

    private const float GaugeUpdateInterval = 1f;

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
        SubscribeLocalEvent<FloorScrubberComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStrapped(Entity<FloorScrubberComponent> ent, ref StrappedEvent args)
    {
        var driver = args.Buckle.Owner;
        _actions.AddAction(driver, ref ent.Comp.CleanAction, "ActionFloorScrubberToggle", ent);
        _actions.AddAction(driver, ref ent.Comp.DumpDrainAction, "ActionFloorScrubberDumpDrain", ent);
        _actions.AddAction(driver, ref ent.Comp.DumpFloorAction, "ActionFloorScrubberDumpFloor", ent);
        _actions.AddAction(driver, ref ent.Comp.FillAction, "ActionFloorScrubberFill", ent);
        
        Dirty(ent);

        // Immediately show alerts for the new driver.
        UpdateAlerts(ent);
    }

    private void OnUnstrapped(Entity<FloorScrubberComponent> ent, ref UnstrappedEvent args)
    {
        var driver = args.Buckle.Owner;
        _actions.RemoveAction(driver, ent.Comp.CleanAction);
        _actions.RemoveAction(driver, ent.Comp.DumpDrainAction);
        _actions.RemoveAction(driver, ent.Comp.DumpFloorAction);
        _actions.RemoveAction(driver, ent.Comp.FillAction);
        
        _alerts.ClearAlert(driver, "FloorScrubberClean");
        _alerts.ClearAlert(driver, "FloorScrubberWaste");

        if (ent.Comp.CleaningEnabled)
            SetCleaningEnabled(ent, false);
    }

    private void OnShutdown(Entity<FloorScrubberComponent> ent, ref ComponentShutdown args)
    {
        UpdateCleaningAudio(ent, false);

        if (TryComp<StrapComponent>(ent, out var strap))
        {
            foreach (var occupant in strap.BuckledEntities)
            {
                _alerts.ClearAlert(occupant, "FloorScrubberClean");
                _alerts.ClearAlert(occupant, "FloorScrubberWaste");
            }
        }
    }

    public void SetCleaningEnabled(Entity<FloorScrubberComponent> ent, bool enabled)
    {
        ent.Comp.CleaningEnabled = enabled;
        UpdateCleaningAudio(ent, enabled);

        // Water Trail Logic: Use the NoFootprintsComponent to stop trails when cleaning is OFF.
        // Wait! User said they want trails ALWAYS, but clean trails when ON and dirty when OFF.
        // So we DON'T toggle NoFootprints anymore, but let's keep the option open for the sync logic.
        
        _movementSpeed.RefreshMovementSpeedModifiers(ent);
        Dirty(ent);
    }

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


    private void OnInsert(Entity<FloorScrubberComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        // Only care about the key slot
        if (args.Container.ID != "key_slot")
            return;

        // Ensure audio state matches for startup.
        UpdateCleaningAudio(ent, ent.Comp.CleaningEnabled);
    }

    private void OnToggleAction(Entity<FloorScrubberComponent> ent, ref FloorScrubberToggleActionEvent args)
    {
        if (args.Handled)
            return;

        // Key Check: Cannot toggle if the key slot is empty.
        if (!_itemSlots.TryGetSlot(ent.Owner, "key_slot", out var slot) || !slot.HasItem)
        {
            if (_timing.IsFirstTimePredicted)
                _popup.PopupEntity(Loc.GetString("floor-scrubber-fill-no-key"), ent.Owner, args.Performer);
            return;
        }

        SetCleaningEnabled(ent, !ent.Comp.CleaningEnabled);
        args.Handled = true;
    }

    private void OnDumpFloor(Entity<FloorScrubberComponent> ent, ref FloorScrubberDumpFloorActionEvent args)
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

        var spill = _solutionContainer.SplitSolution(wasteSolnEnt.Value, waste.Volume);
        _puddle.TrySpillAt(_transform.GetMoverCoordinates(ent.Owner), spill, out _);

        if (_timing.IsFirstTimePredicted)
        {
            _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/Fluids/slosh.ogg"), ent.Owner);
            _popup.PopupEntity(Loc.GetString("floor-scrubber-dump-floor-success"), ent.Owner, user);
        }

        args.Handled = true;
        UpdateAlerts(ent);
    }

    private void OnFill(Entity<FloorScrubberComponent> ent, ref FloorScrubberFillActionEvent args)
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
        Entity<ReagentTankComponent?> nearestSource = default;
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
                nearestSource = (candidate.Owner, candidate.Comp);
            }
        }

        if (nearestSource.Owner == default)
        {
            if (_timing.IsFirstTimePredicted)
                _popup.PopupEntity(Loc.GetString("floor-scrubber-fill-no-source"), ent.Owner, user);
            return;
        }

        // Drain from the source's drainable solution into the clean tank.
        if (!_solutionContainer.TryGetDrainableSolution(nearestSource.Owner, out var sourceSolnEnt, out var sourceSoln))
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
                ("source", Name(nearestSource.Owner))), ent.Owner, user);
        }

        args.Handled = true;
        UpdateAlerts(ent);
    }

    private void OnRefreshSpeed(Entity<FloorScrubberComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.CleaningEnabled)
            args.ModifySpeed(ent.Comp.SpeedMultiplier, ent.Comp.SpeedMultiplier);
    }

    private void OnExamine(Entity<FloorScrubberComponent> ent, ref ExaminedEvent args)
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

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted)
            return;

        var query = EntityQueryEnumerator<FloorScrubberComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var scrubber, out var xform))
        {
            UpdateCleaningAudio((uid, scrubber), scrubber.CleaningEnabled);

            if (!scrubber.CleaningEnabled)
                continue;

            // Key Check: Automatically stop cleaning if the key is removed.
            if (!_itemSlots.TryGetSlot(uid, "key_slot", out var slot) || !slot.HasItem)
            {
                SetCleaningEnabled((uid, scrubber), false);
                continue;
            }

            // HUD Alert Update (throttled)
            scrubber.GaugeUpdateAccumulator += frameTime;
            if (scrubber.GaugeUpdateAccumulator >= GaugeUpdateInterval)
            {
                scrubber.GaugeUpdateAccumulator = 0f;
                UpdateAlerts((uid, scrubber));
            }

            if (!scrubber.CleaningEnabled)
                continue;

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

    protected void UpdateAlerts(Entity<FloorScrubberComponent> ent)
    {
        if (!TryComp<StrapComponent>(ent, out var strap) || strap.BuckledEntities.Count == 0)
            return;

        if (!_solutionContainer.TryGetSolution(ent.Owner, ent.Comp.CleanSolutionName, out _, out var cleanSoln) ||
            !_solutionContainer.TryGetSolution(ent.Owner, ent.Comp.WasteSolutionName, out _, out var wasteSoln))
        {
            return;
        }

        var cleanSeverity = (short) Math.Clamp((float) (cleanSoln.Volume / cleanSoln.MaxVolume) * 10, 0, 10);
        var wasteSeverity = (short) Math.Clamp((float) (wasteSoln.Volume / wasteSoln.MaxVolume) * 10, 0, 10);

        foreach (var occupant in strap.BuckledEntities)
        {
            _alerts.ShowAlert(occupant, "FloorScrubberClean", cleanSeverity);
            _alerts.ShowAlert(occupant, "FloorScrubberWaste", wasteSeverity);
        }
    }

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
            SetCleaningEnabled((ent.Owner, scrubber), false);
            _popup.PopupEntity(Loc.GetString("floor-scrubber-waste-full"), ent.Owner);
            return;
        }

        // 1. Determine which tiles we are cleaning.
        var targetTiles = new HashSet<Vector2i>();
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
                targetTiles.Add(centerTile);
                // Calculate perpendicular direction based on scrubber rotation.
                var rotation = _transform.GetWorldRotation(ent.Owner);
                var worldVec = rotation.ToVec();
                var perpDir = new Vector2(-worldVec.Y, worldVec.X);
                // Snap to best cardinal for logic simplicity on grid.
                var step = Vector2i.Zero;
                if (Math.Abs(perpDir.X) > Math.Abs(perpDir.Y))
                    step = new Vector2i(perpDir.X > 0 ? 1 : -1, 0);
                else
                    step = new Vector2i(0, perpDir.Y > 0 ? 1 : -1);

                for (var i = 1; i <= range; i++)
                {
                    targetTiles.Add(centerTile + step * i);
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

                if (isFootprint)
                {
                    RaiseLocalEvent(entity, new FootprintCleanEvent());
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
                var currentAmount = _solutionContainer.GetTotalPrototypeQuantity(printSolnEnt.Value, "puddle"); // Dummy check or just volume.
                
                if (printSolnEnt.Value.Comp.Solution.Volume < targetAmount)
                {
                    var amount = FixedPoint2.Min(targetAmount - printSolnEnt.Value.Comp.Solution.Volume, cleanSolution.Volume);
                    if (amount > 0)
                    {
                        var move = _solutionContainer.SplitSolution(cleanSolnEnt.Value, amount);
                        _solutionContainer.TryAddSolution(printSolnEnt.Value, move);
                    }
                }
            }

            var decals = _decal.GetDecalsInRange(gridUid, centerTileLocal, lookupRadius);
            var anyCleaned = false;

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

            if (anyCleaned)
            {
                var water = _solutionContainer.SplitSolution(cleanSolnEnt.Value, scrubber.CleaningAmount);
                _puddle.TrySpillAt(xform.Coordinates, water, out _);
            }
        }
    }
}
