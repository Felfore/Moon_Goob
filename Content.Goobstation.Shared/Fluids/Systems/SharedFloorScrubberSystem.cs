using Content.Goobstation.Shared.Fluids;
using Content.Goobstation.Maths.FixedPoint;
using Content.Goobstation.Shared.Fluids.Components;
using Content.Shared.Actions;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Decals;
using Content.Shared.Examine;
using Content.Shared.Fluids.Components;
using Content.Shared.Fluids;
using Content.Shared.Movement.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using System.Numerics;

namespace Content.Goobstation.Shared.Fluids.Systems;

public abstract class SharedFloorScrubberSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] protected readonly SharedDecalSystem DecalSystem = default!;
    [Dependency] protected readonly EntityLookupSystem Lookup = default!;
    [Dependency] protected readonly ItemSlotsSystem ItemSlots = default!;
    [Dependency] protected readonly SharedMapSystem MapSystem = default!;
    [Dependency] protected readonly SharedPuddleSystem PuddleSystem = default!;
    [Dependency] protected readonly SharedSolutionContainerSystem SolutionContainer = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] protected readonly SharedTransformSystem Transform = default!;

    private static readonly EntProtoId CleanActionId = "ActionFloorScrubberToggle";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FloorScrubberComponent, FloorScrubberToggleActionEvent>(OnToggleAction);
        SubscribeLocalEvent<FloorScrubberComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<FloorScrubberComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<FloorScrubberComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<FloorScrubberComponent, UnstrappedEvent>(OnUnstrapped);
    }

    private void OnStrapped(Entity<FloorScrubberComponent> ent, ref StrappedEvent args)
    {
        _actions.AddAction(args.Buckle.Owner, ref ent.Comp.CleanAction, CleanActionId, ent);
    }

    private void OnUnstrapped(Entity<FloorScrubberComponent> ent, ref UnstrappedEvent args)
    {
        _actions.RemoveAction(args.Buckle.Owner, ent.Comp.CleanAction);
    }

    private void OnToggleAction(Entity<FloorScrubberComponent> ent, ref FloorScrubberToggleActionEvent args)
    {
        if (args.Handled)
            return;

        ent.Comp.CleaningEnabled = !ent.Comp.CleaningEnabled;
        Dirty(ent);

        RaiseLocalEvent(ent, new RefreshMovementSpeedModifiersEvent());
        args.Handled = true;
    }

    private void OnRefreshSpeed(Entity<FloorScrubberComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.CleaningEnabled)
        {
            args.ModifySpeed(ent.Comp.SpeedMultiplier, ent.Comp.SpeedMultiplier);
        }
    }

    /// <summary>
    ///     Tries to get the entity in the given item slot.
    /// </summary>
    private bool TryGetSlotEntity(EntityUid uid, string slotId, out EntityUid slotEntity)
    {
        slotEntity = default;

        if (!ItemSlots.TryGetSlot(uid, slotId, out var slot))
            return false;

        if (slot.Item is not { } item)
            return false;

        slotEntity = item;
        return true;
    }

    private void OnExamine(Entity<FloorScrubberComponent> ent, ref ExaminedEvent args)
    {
        // Show clean water tank status
        if (TryGetSlotEntity(ent, ent.Comp.TankSlotId, out var tankEntity)
            && SolutionContainer.TryGetDrainableSolution(tankEntity, out _, out var tankSolution))
        {
            args.PushMarkup(Loc.GetString("floor-scrubber-examine-tank",
                ("name", Name(tankEntity)),
                ("amount", tankSolution.Volume),
                ("max", tankSolution.MaxVolume)));
        }
        else
        {
            args.PushMarkup(Loc.GetString("floor-scrubber-examine-tank-empty"));
        }

        // Show waste tank status
        if (TryGetSlotEntity(ent, ent.Comp.WasteSlotId, out var wasteEntity)
            && SolutionContainer.TryGetRefillableSolution(wasteEntity, out _, out var wasteSolution))
        {
            args.PushMarkup(Loc.GetString("floor-scrubber-examine-waste",
                ("name", Name(wasteEntity)),
                ("amount", wasteSolution.Volume),
                ("max", wasteSolution.MaxVolume)));
        }
        else
        {
            args.PushMarkup(Loc.GetString("floor-scrubber-examine-waste-empty"));
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
            if (!scrubber.CleaningEnabled)
                continue;

            if (xform.GridUid == null)
                continue;

            ProcessTileCleaning((uid, scrubber, xform));
        }
    }

    protected virtual void ProcessTileCleaning(Entity<FloorScrubberComponent, TransformComponent> ent)
    {
        var (uid, scrubber, xform) = ent;

        if (xform.GridUid == null)
            return;

        var gridUid = xform.GridUid.Value;
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        // Get the tile in front of the scrubber.
        var rotation = xform.LocalRotation;
        var frontPos = xform.LocalPosition + rotation.ToVec() * scrubber.CleaningRange;
        var frontCoords = new EntityCoordinates(gridUid, frontPos);
        var tileIndices = MapSystem.LocalToTile(gridUid, grid, frontCoords);
        var tileCenter = MapSystem.GridTileToLocal(gridUid, grid, tileIndices).Position;

        // 1. Vacuum Logic — suck puddles into the waste container
        VacuumTile(ent, gridUid, tileCenter);

        // 2. Scrubbing Logic — clean decals using water from the tank container
        ScrubTile(ent, gridUid, tileCenter);
    }

    private void VacuumTile(Entity<FloorScrubberComponent, TransformComponent> ent, EntityUid gridUid, Vector2 tileCenter)
    {
        var (uid, scrubber, _) = ent;

        // Get the waste container entity from the item slot
        if (!TryGetSlotEntity(uid, scrubber.WasteSlotId, out var wasteEntity))
            return;

        // Get the refillable solution on the waste container (we're filling it with waste)
        if (!SolutionContainer.TryGetRefillableSolution(wasteEntity, out var wasteSolnEnt, out var wasteSolution))
            return;

        if (wasteSolution.AvailableVolume <= 0)
            return;

        var frontCoords = new EntityCoordinates(gridUid, tileCenter);
        var frontMap = Transform.ToMapCoordinates(frontCoords);

        foreach (var puddleUid in Lookup.GetEntitiesInRange<PuddleComponent>(frontMap, 0.5f))
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

    private void ScrubTile(Entity<FloorScrubberComponent, TransformComponent> ent, EntityUid gridUid, Vector2 tileCenter)
    {
        var (uid, scrubber, _) = ent;

        // Get the tank container entity from the item slot
        if (!TryGetSlotEntity(uid, scrubber.TankSlotId, out var tankEntity))
            return;

        // Get the drainable solution on the tank container (we're draining clean water from it)
        if (!SolutionContainer.TryGetDrainableSolution(tankEntity, out var tankSolnEnt, out var tankSolution))
            return;

        if (tankSolution.Volume < scrubber.CleaningAmount)
            return;

        // Find cleanable decals.
        var decals = DecalSystem.GetDecalsInRange(gridUid, tileCenter, scrubber.CleaningRange, d => d.Cleanable);
        if (decals.Count == 0)
            return;

        // We found something to clean!
        foreach (var (decalId, _) in decals)
        {
            DecalSystem.RemoveDecal(gridUid, decalId);
        }

        // Consume water from the tank container.
        var water = SolutionContainer.SplitSolution(tankSolnEnt.Value, scrubber.CleaningAmount);

        // Spill tiny water puddle.
        var coords = new EntityCoordinates(gridUid, tileCenter);
        PuddleSystem.TrySpillAt(coords, water, out _);
    }
}
