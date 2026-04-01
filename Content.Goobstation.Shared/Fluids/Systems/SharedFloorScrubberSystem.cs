using Content.Goobstation.Shared.Fluids.Components;
using Content.Shared.Actions;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Decals;
using Content.Shared.Examine;
using Content.Shared.Fluids.Components;
using Content.Shared.Fluids;
using Content.Shared.Movement.Systems;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Goobstation.Shared.Fluids.Systems;

public abstract class SharedFloorScrubberSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] protected readonly SharedPuddleSystem Puddle = default!;
    [Dependency] private readonly SharedDecalSystem Decals = default!;
    [Dependency] private readonly IGameTiming Timing = default!;
    [Dependency] private readonly SharedTransformSystem Transform = default!;

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

    private void OnExamine(Entity<FloorScrubberComponent> ent, ref ExaminedEvent args)
    {
        if (!_solutionContainer.TryGetSolution(ent.Owner, ent.Comp.TankSolutionName, out _, out var tankSolution))
            return;

        if (!_solutionContainer.TryGetSolution(ent.Owner, ent.Comp.WasteSolutionName, out _, out var wasteSolution))
            return;

        args.PushMarkup(Loc.GetString("floor-scrubber-examine-tank",
            ("name", ent.Comp.TankSolutionName),
            ("amount", tankSolution.Volume),
            ("max", tankSolution.MaxVolume)));

        args.PushMarkup(Loc.GetString("floor-scrubber-examine-waste",
            ("name", ent.Comp.WasteSolutionName),
            ("amount", wasteSolution.Volume),
            ("max", wasteSolution.MaxVolume)));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // We only process cleaning every few ticks to save performance
        if (!_timing.IsFirstTimePredicted)
            return;

        var query = EntityQueryEnumerator<FloorScrubberComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var scrubber, out var xform))
        {
            if (!scrubber.CleaningEnabled)
                continue;

            // TODO: Use a timer to reduce frequency of tile checks if necessary.
            // For now, we'll check every tick but only on the server or predicted client.

            if (xform.GridUid == null)
                continue;

            ProcessTileCleaning((uid, scrubber, xform));
        }
    }

    protected abstract void ProcessTileCleaning(Entity<FloorScrubberComponent, TransformComponent> ent);
}
