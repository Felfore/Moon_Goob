using Content.Goobstation.Shared.Fluids.Components;
using Content.Goobstation.Shared.Fluids.Systems;
using Content.Server.Chemistry.EntitySystems;
using Content.Server.Decals;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Decals;
using Content.Shared.Fluids.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using System.Numerics;

namespace Content.Server.Fluids.EntitySystems;

public sealed class FloorScrubberSystem : SharedFloorScrubberSystem
{
    [Dependency] private readonly DecalSystem _decals = default!;
    [Dependency] private readonly SolutionContainerSystem _solutionContainer = default!;
    [Dependency] private readonly PuddleSystem _puddle = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;

    protected override void ProcessTileCleaning(Entity<FloorScrubberComponent, TransformComponent> ent)
    {
        var (uid, scrubber, xform) = ent;

        if (xform.GridUid == null)
            return;

        var gridUid = xform.GridUid.Value;
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        // Get the tile in front of the scrubber.
        // We use the rotation of the scrubber to determine "front".
        var offset = xform.LocalRotation.ToWorldVec();
        var frontPos = xform.LocalPosition + offset;
        var tileIndices = grid.LocalToTile(frontPos);
        var tileCenter = grid.TileToLocal(tileIndices).Position;

        // 1. Vacuum Logic (Current Tile or Front Tile)
        // We'll vacuum the tile the scrubber is ON for best feel, or maybe both?
        // Let's do the tile in front for consistent behavior.
        VacuumTile(ent, gridUid, tileCenter);

        // 2. Scrubbing Logic
        ScrubTile(ent, gridUid, tileCenter);
    }

    private void VacuumTile(Entity<FloorScrubberComponent, TransformComponent> ent, EntityUid gridUid, Vector2 tileCenter)
    {
        var (uid, scrubber, _) = ent;

        if (!_solutionContainer.TryGetSolution(uid, scrubber.WasteSolutionName, out var wasteSolnEnt, out var wasteSolution))
            return;

        if (wasteSolution.AvailableVolume <= 0)
            return;

        // Find puddles on this tile.
        var worldPos = _transform.GetWorldPosition(ent.Comp2); // We can just use world pos for lookup if we want, or tile indices.
        // But GetDecalsInRange uses coordinates.

        // Actually, let's use lookup for puddles.
        var frontCoords = new EntityCoordinates(gridUid, tileCenter);
        var frontMap = _transform.ToMapCoordinates(frontCoords);

        foreach (var puddleUid in _lookup.GetEntitiesInRange<PuddleComponent>(frontMap, 0.5f))
        {
            if (!_solutionContainer.TryGetSolution(puddleUid, "puddle", out var puddleSolnEnt, out var puddleSolution))
                continue;

            var drawAmount = FixedPoint2.Min(scrubber.VacuumAmount, puddleSolution.Volume, wasteSolution.AvailableVolume);
            if (drawAmount <= 0)
                continue;

            var removed = _solutionContainer.SplitSolution(puddleSolnEnt.Value, drawAmount);
            _solutionContainer.TryAddSolution(wasteSolnEnt.Value, removed);

            // If the puddle is now empty, it will be deleted by PuddleSystem when we call update or similar.
            // Actually PuddleSystem usually handles empty puddles.
        }
    }

    private void ScrubTile(Entity<FloorScrubberComponent, TransformComponent> ent, EntityUid gridUid, Vector2 tileCenter)
    {
        var (uid, scrubber, _) = ent;

        if (!_solutionContainer.TryGetSolution(uid, scrubber.TankSolutionName, out var tankSolnEnt, out var tankSolution))
            return;

        if (tankSolution.Volume < scrubber.CleaningAmount)
            return;

        // Find cleanable decals.
        var decals = _decals.GetDecalsInRange(gridUid, tileCenter, 0.5f, d => d.Cleanable);
        if (decals.Count == 0)
            return;

        // We found something to clean!
        foreach (var (decalId, _) in decals)
        {
            _decals.RemoveDecal(gridUid, decalId);
        }

        // Consume water.
        var water = _solutionContainer.SplitSolution(tankSolnEnt.Value, scrubber.CleaningAmount);

        // Spill tiny water puddle.
        var coords = new EntityCoordinates(gridUid, tileCenter);
        _puddle.TrySpillAt(coords, water, out _, canRefill: true);
    }
}
