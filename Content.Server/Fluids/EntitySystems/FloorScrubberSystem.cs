using Content.Goobstation.Maths.FixedPoint;
using Content.Goobstation.Shared.Fluids;
using Content.Goobstation.Shared.Fluids.Components;
using Content.Goobstation.Shared.Fluids.Systems;
using Content.Server.DoAfter;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.DoAfter;
using Content.Shared.Fluids.Components;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Server.Fluids.EntitySystems;

public sealed class FloorScrubberSystem : SharedFloorScrubberSystem
{
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly TagSystem _tag = default!;

    private static readonly ProtoId<TagPrototype> ReagentTankTag = "ReagentTank";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FloorScrubberComponent, FloorScrubberDumpDrainActionEvent>(OnDumpDrain);
        SubscribeLocalEvent<FloorScrubberComponent, FloorScrubberDumpFloorActionEvent>(OnDumpFloor);
        SubscribeLocalEvent<FloorScrubberComponent, FloorScrubberFillActionEvent>(OnFill);
        SubscribeLocalEvent<FloorScrubberComponent, FloorScrubberDumpDrainDoAfterEvent>(OnDumpDrainDoAfter);
        SubscribeLocalEvent<FloorScrubberComponent, GetVerbsEvent<AlternativeVerb>>(OnGetBucketVerb);
        SubscribeLocalEvent<FloorScrubberComponent, AfterInteractUsingEvent>(OnBucketInteract);
    }

    // ── Dump to drain ──────────────────────────────────────────────────────────

    private void OnDumpDrain(Entity<FloorScrubberComponent> ent, ref FloorScrubberDumpDrainActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;

        // Pre-check 1: waste tank must have something to dump.
        if (!SolutionContainer.TryGetSolution(ent.Owner, ent.Comp.WasteSolutionName, out _, out var waste)
            || waste.Volume <= 0)
        {
            _popup.PopupEntity(Loc.GetString("floor-scrubber-dump-drain-empty"), ent.Owner, user);
            return;
        }

        // Pre-check 2: a drain must be nearby before we start the timer.
        if (!TryGetNearestDrain(ent.Owner, out _))
        {
            _popup.PopupEntity(Loc.GetString("floor-scrubber-dump-drain-no-drain"), ent.Owner, user);
            return;
        }

        // Both checks passed — start doAfter on the scrubber entity itself.
        var doAfterArgs = new DoAfterArgs(EntityManager, user, 2f,
            new FloorScrubberDumpDrainDoAfterEvent(), ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
        args.Handled = true;
    }

    private void OnDumpDrainDoAfter(Entity<FloorScrubberComponent> ent, ref FloorScrubberDumpDrainDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        var user = args.User;

        // Re-validate drain still exists (entity may have moved or drain removed).
        if (!TryGetNearestDrain(ent.Owner, out var drain))
        {
            _popup.PopupEntity(Loc.GetString("floor-scrubber-dump-drain-no-drain"), ent.Owner, user);
            return;
        }

        if (!SolutionContainer.TryGetSolution(ent.Owner, ent.Comp.WasteSolutionName, out var wasteSolnEnt, out var waste)
            || waste.Volume <= 0)
        {
            _popup.PopupEntity(Loc.GetString("floor-scrubber-dump-drain-empty"), ent.Owner, user);
            return;
        }

        if (!SolutionContainer.ResolveSolution(drain.Owner, DrainComponent.SolutionName,
                ref drain.Comp.Solution, out var drainSolution))
            return;

        var toTransfer = FixedPoint2.Min(waste.Volume, drainSolution.AvailableVolume);
        var overflow = waste.Volume - toTransfer;

        // Transfer what fits.
        if (toTransfer > 0)
        {
            var transferred = SolutionContainer.SplitSolution(wasteSolnEnt.Value, toTransfer);
            SolutionContainer.TryAddSolution(drain.Comp.Solution.Value, transferred);
            _audio.PlayPvs(drain.Comp.ManualDrainSound, drain.Owner);
            _ambientSound.SetAmbience(drain.Owner, true);
        }

        // Spill the overflow next to the drain.
        if (overflow > 0)
        {
            var spill = SolutionContainer.SplitSolution(wasteSolnEnt.Value, overflow);
            PuddleSystem.TrySpillAt(Transform.GetMoverCoordinates(drain.Owner), spill, out _);
            _popup.PopupEntity(Loc.GetString("floor-scrubber-dump-drain-overflow"), ent.Owner, user);
        }
        else
        {
            _popup.PopupEntity(Loc.GetString("floor-scrubber-dump-drain-success"), ent.Owner, user);
        }

        args.Handled = true;
    }

    // ── Dump to floor ──────────────────────────────────────────────────────────

    private void OnDumpFloor(Entity<FloorScrubberComponent> ent, ref FloorScrubberDumpFloorActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;

        if (!SolutionContainer.TryGetSolution(ent.Owner, ent.Comp.WasteSolutionName, out var wasteSolnEnt, out var waste)
            || waste.Volume <= 0)
        {
            _popup.PopupEntity(Loc.GetString("floor-scrubber-dump-floor-empty"), ent.Owner, user);
            return;
        }

        var spill = SolutionContainer.SplitSolution(wasteSolnEnt.Value, waste.Volume);
        PuddleSystem.TrySpillAt(Transform.GetMoverCoordinates(ent.Owner), spill, out _);
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/Fluids/slosh.ogg"), ent.Owner);
        _popup.PopupEntity(Loc.GetString("floor-scrubber-dump-floor-success"), ent.Owner, user);
        args.Handled = true;
    }

    // ── Fill from source ───────────────────────────────────────────────────────

    private void OnFill(Entity<FloorScrubberComponent> ent, ref FloorScrubberFillActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;

        if (!SolutionContainer.TryGetSolution(ent.Owner, ent.Comp.CleanSolutionName, out var cleanSolnEnt, out var cleanSoln))
            return;

        if (cleanSoln.AvailableVolume <= 0)
        {
            _popup.PopupEntity(Loc.GetString("floor-scrubber-fill-full"), ent.Owner, user);
            return;
        }

        // Find the nearest drainable water source (sink, water tank, etc.)
        var scrubberPos = Transform.GetMapCoordinates(ent.Owner);
        Entity<DrainComponent>? nearestSource = null;
        var nearestDist = float.MaxValue;

        foreach (var candidate in Lookup.GetEntitiesInRange<DrainComponent>(scrubberPos, 1.5f))
        {
            // Must be marked as a ReagentTank (sinks, water tanks — not floor drains which are waste receivers)
            if (!_tag.HasTag(candidate.Owner, ReagentTankTag))
                continue;

            var dist = (Transform.GetWorldPosition(candidate.Owner) - Transform.GetWorldPosition(ent.Owner)).LengthSquared();
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearestSource = candidate;
            }
        }

        if (nearestSource == null)
        {
            _popup.PopupEntity(Loc.GetString("floor-scrubber-fill-no-source"), ent.Owner, user);
            return;
        }

        // Drain from the source's drainable solution into the clean tank.
        if (!SolutionContainer.TryGetDrainableSolution(nearestSource.Value.Owner, out var sourceSolnEnt, out var sourceSoln))
        {
            _popup.PopupEntity(Loc.GetString("floor-scrubber-fill-no-source"), ent.Owner, user);
            return;
        }

        var toFill = FixedPoint2.Min(cleanSoln.AvailableVolume, sourceSoln.Volume);
        if (toFill <= 0)
        {
            _popup.PopupEntity(Loc.GetString("floor-scrubber-fill-no-source"), ent.Owner, user);
            return;
        }

        var water = SolutionContainer.SplitSolution(sourceSolnEnt.Value, toFill);
        SolutionContainer.TryAddSolution(cleanSolnEnt.Value, water);
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/Fluids/glug.ogg"), ent.Owner);
        _popup.PopupEntity(Loc.GetString("floor-scrubber-fill-success",
            ("source", Name(nearestSource.Value.Owner))), ent.Owner, user);
        args.Handled = true;
    }

    // ── Bucket verb + interaction ──────────────────────────────────────────────

    private void OnGetBucketVerb(Entity<FloorScrubberComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var comp = ent.Comp;
        var user = args.User;
        var scrubber = ent.Owner;

        var verb = new AlternativeVerb
        {
            Text = comp.BucketMode == FloorScrubberBucketMode.PourIntoClean
                ? Loc.GetString("floor-scrubber-verb-bucket-mode-to-waste")
                : Loc.GetString("floor-scrubber-verb-bucket-mode-to-clean"),
            Act = () =>
            {
                comp.BucketMode = comp.BucketMode == FloorScrubberBucketMode.PourIntoClean
                    ? FloorScrubberBucketMode.DrawFromWaste
                    : FloorScrubberBucketMode.PourIntoClean;
                Dirty(scrubber, comp);

                var msg = comp.BucketMode == FloorScrubberBucketMode.PourIntoClean
                    ? Loc.GetString("floor-scrubber-bucket-mode-clean")
                    : Loc.GetString("floor-scrubber-bucket-mode-waste");
                _popup.PopupEntity(msg, scrubber, user);
            },
            Priority = 1
        };
        args.Verbs.Add(verb);
    }

    private void OnBucketInteract(Entity<FloorScrubberComponent> ent, ref AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        var used = args.Used;

        // Only handle entities that can drain (e.g. buckets, beakers).
        if (!SolutionContainer.TryGetDrainableSolution(used, out var bucketSolnEnt, out var bucketSoln))
            return;

        var user = args.User;

        if (ent.Comp.BucketMode == FloorScrubberBucketMode.PourIntoClean)
        {
            // Pour bucket contents into clean tank.
            if (!SolutionContainer.TryGetSolution(ent.Owner, ent.Comp.CleanSolutionName, out var cleanSolnEnt, out var cleanSoln))
                return;

            var toTransfer = FixedPoint2.Min(bucketSoln.Volume, cleanSoln.AvailableVolume);
            if (toTransfer <= 0)
            {
                _popup.PopupEntity(Loc.GetString("floor-scrubber-fill-full"), ent.Owner, user);
                return;
            }

            var poured = SolutionContainer.SplitSolution(bucketSolnEnt.Value, toTransfer);
            SolutionContainer.TryAddSolution(cleanSolnEnt.Value, poured);
            _popup.PopupEntity(Loc.GetString("floor-scrubber-bucket-poured",
                ("amount", toTransfer)), ent.Owner, user);
        }
        else
        {
            // Draw waste into bucket.
            if (!SolutionContainer.TryGetSolution(ent.Owner, ent.Comp.WasteSolutionName, out var wasteSolnEnt, out var wasteSoln))
                return;

            // Bucket needs to be refillable to receive waste.
            if (!SolutionContainer.TryGetRefillableSolution(used, out var bucketRefillEnt, out var bucketRefill))
                return;

            var toTransfer = FixedPoint2.Min(wasteSoln.Volume, bucketRefill.AvailableVolume);
            if (toTransfer <= 0)
            {
                _popup.PopupEntity(Loc.GetString("floor-scrubber-dump-drain-empty"), ent.Owner, user);
                return;
            }

            var drawn = SolutionContainer.SplitSolution(wasteSolnEnt.Value, toTransfer);
            SolutionContainer.TryAddSolution(bucketRefillEnt.Value, drawn);
            _popup.PopupEntity(Loc.GetString("floor-scrubber-bucket-drawn",
                ("amount", toTransfer)), ent.Owner, user);
        }

        args.Handled = true;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private bool TryGetNearestDrain(EntityUid scrubber, out Entity<DrainComponent> drain)
    {
        drain = default;
        var scrubberPos = Transform.GetMapCoordinates(scrubber);
        var nearestDist = float.MaxValue;
        var found = false;

        foreach (var candidate in Lookup.GetEntitiesInRange<DrainComponent>(scrubberPos, 1.5f))
        {
            // Exclude sinks/reagent tanks — those are sources, not waste receivers.
            if (_tag.HasTag(candidate.Owner, ReagentTankTag))
                continue;

            var dist = (Transform.GetWorldPosition(candidate.Owner) - Transform.GetWorldPosition(scrubber)).LengthSquared();
            if (dist < nearestDist)
            {
                nearestDist = dist;
                drain = candidate;
                found = true;
            }
        }

        return found;
    }
}
