using Content.Goobstation.Maths.FixedPoint;
using Content.Goobstation.Shared.Vehicles.FloorScrubber;
using Content.Server.DoAfter;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.DoAfter;
using Content.Shared.Fluids.Components;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Content.Shared.Audio;
using Content.Shared.Fluids;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Server.Vehicles.FloorScrubber;

public sealed class FloorScrubberSystem : SharedFloorScrubberSystem
{
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambientSound = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedPuddleSystem _puddle = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;


    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FloorScrubberComponent, FloorScrubberDumpDrainActionEvent>(OnDumpDrain);
        SubscribeLocalEvent<FloorScrubberComponent, FloorScrubberDumpDrainDoAfterEvent>(OnDumpDrainDoAfter);
        SubscribeLocalEvent<FloorScrubberComponent, AfterInteractUsingEvent>(OnBucketInteract);
    }

    // ── Dump to drain ──────────────────────────────────────────────────────────

    private void OnDumpDrain(Entity<FloorScrubberComponent> ent, ref FloorScrubberDumpDrainActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;

        // Pre-check 1: waste tank must have something to dump.
        if (!_solutionContainer.TryGetSolution(ent.Owner, ent.Comp.WasteSolutionName, out _, out var waste)
            || waste.Volume <= 0)
        {
            _popup.PopupEntity(Loc.GetString("floor-scrubber-dump-drain-empty"), ent.Owner, user);
            return;
        }

        // Pre-check 2: a drain must be nearby before we start the timer.
        if (!TryGetNearestDrain(ent.Owner, out _, ent.Comp))
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
        if (!TryGetNearestDrain(ent.Owner, out var drain, ent.Comp))
        {
            _popup.PopupEntity(Loc.GetString("floor-scrubber-dump-drain-no-drain"), ent.Owner, user);
            return;
        }

        if (!_solutionContainer.TryGetSolution(ent.Owner, ent.Comp.WasteSolutionName, out var wasteSolnEnt, out var waste)
            || waste.Volume <= 0)
        {
            _popup.PopupEntity(Loc.GetString("floor-scrubber-dump-drain-empty"), ent.Owner, user);
            return;
        }

        if (!_solutionContainer.ResolveSolution(drain.Owner, DrainComponent.SolutionName,
                ref drain.Comp.Solution, out var drainSolution))
            return;

        var toTransfer = FixedPoint2.Min(waste.Volume, drainSolution.AvailableVolume);
        var overflow = waste.Volume - toTransfer;

        // Transfer what fits.
        if (toTransfer > 0 && drain.Comp.Solution.HasValue)
        {
            var transferred = _solutionContainer.SplitSolution(wasteSolnEnt.Value, toTransfer);
            _solutionContainer.TryAddSolution(drain.Comp.Solution.Value, transferred);
            _audio.PlayPvs(drain.Comp.ManualDrainSound, drain.Owner);
        }

        // Spill the overflow next to the drain.
        if (overflow > 0)
        {
            var spill = _solutionContainer.SplitSolution(wasteSolnEnt.Value, overflow);
            _puddle.TrySpillAt(_transform.GetMoverCoordinates(drain.Owner), spill, out _);
            _popup.PopupEntity(Loc.GetString("floor-scrubber-dump-drain-overflow"), ent.Owner, user);
        }
        else
        {
            _popup.PopupEntity(Loc.GetString("floor-scrubber-dump-drain-success"), ent.Owner, user);
        }

        args.Handled = true;
        UpdateAlerts(ent);
    }

    private void OnBucketInteract(Entity<FloorScrubberComponent> ent, ref AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        var used = args.Used;

        // Only handle entities that can drain (e.g. buckets, beakers).
        if (!_solutionContainer.TryGetDrainableSolution(used, out var bucketSolnEnt, out var bucketSoln))
            return;

        var user = args.User;

        if (bucketSoln.Volume > 0)
        {
            // --- Refill / Dump Mode ---

            // Edge Case: Check if it's "Pure Water" (nothing but Water).
            // This ensures the clean tank never gets contaminated.
            var isPureWater = true;
            foreach (var reagent in bucketSoln.Contents)
            {
                if (reagent.Reagent.Prototype != "Water")
                {
                    isPureWater = false;
                    break;
                }
            }

            if (isPureWater)
            {
                // Refill Clean Tank
                if (!_solutionContainer.TryGetSolution(ent.Owner, ent.Comp.CleanSolutionName, out var cleanSolnEnt, out var cleanSoln))
                    return;

                var toTransfer = FixedPoint2.Min(bucketSoln.Volume, cleanSoln.AvailableVolume);
                if (toTransfer <= 0)
                {
                    _popup.PopupEntity(Loc.GetString("floor-scrubber-fill-full"), ent.Owner, user);
                }
                else
                {
                    var poured = _solutionContainer.SplitSolution(bucketSolnEnt.Value, toTransfer);
                    _solutionContainer.TryAddSolution(cleanSolnEnt.Value, poured);
                    _popup.PopupEntity(Loc.GetString("floor-scrubber-bucket-poured", ("amount", toTransfer)), ent.Owner, user);
                }
            }
            else
            {
                // Dump into Waste Tank
                if (!_solutionContainer.TryGetSolution(ent.Owner, ent.Comp.WasteSolutionName, out var wasteSolnEnt, out var wasteSoln))
                    return;

                var toTransfer = FixedPoint2.Min(bucketSoln.Volume, wasteSoln.AvailableVolume);
                if (toTransfer <= 0)
                {
                    _popup.PopupEntity(Loc.GetString("floor-scrubber-dump-waste-full"), ent.Owner, user);
                }
                else
                {
                    var poured = _solutionContainer.SplitSolution(bucketSolnEnt.Value, toTransfer);
                    _solutionContainer.TryAddSolution(wasteSolnEnt.Value, poured);
                    _popup.PopupEntity(Loc.GetString("floor-scrubber-bucket-poured-waste", ("amount", toTransfer)), ent.Owner, user);
                }
            }
        }
        else
        {
            // --- Draw Mode (Empty container) ---

            // Draw waste into bucket.
            if (!_solutionContainer.TryGetSolution(ent.Owner, ent.Comp.WasteSolutionName, out var wasteSolnEnt, out var wasteSoln))
                return;

            // Bucket needs to be refillable to receive waste.
            if (!_solutionContainer.TryGetRefillableSolution(used, out var bucketRefillEnt, out var bucketRefill))
                return;

            var toTransfer = FixedPoint2.Min(wasteSoln.Volume, bucketRefill.AvailableVolume);
            if (toTransfer <= 0)
            {
                if (wasteSoln.Volume <= 0)
                    _popup.PopupEntity(Loc.GetString("floor-scrubber-dump-drain-empty"), ent.Owner, user);
                else
                    _popup.PopupEntity(Loc.GetString("bucket-full"), used, user);
                return;
            }

            var drawn = _solutionContainer.SplitSolution(wasteSolnEnt.Value, toTransfer);
            _solutionContainer.TryAddSolution(bucketRefillEnt.Value, drawn);
            _popup.PopupEntity(Loc.GetString("floor-scrubber-bucket-drawn", ("amount", toTransfer)), ent.Owner, user);
        }

        args.Handled = true;
        UpdateAlerts(ent);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private bool TryGetNearestDrain(EntityUid uid, out Entity<DrainComponent> drain, FloorScrubberComponent? scrubber = null)
    {
        drain = default;
        if (!Resolve(uid, ref scrubber))
            return false;

        var scrubberPos = _transform.GetMapCoordinates(uid);
        var nearestDist = float.MaxValue;
        var found = false;

        foreach (var candidate in _lookup.GetEntitiesInRange<DrainComponent>(scrubberPos, 1.5f))
        {
            // Exclude sinks/reagent tanks — those are sources, not waste receivers.
            if (HasComp<ReagentTankComponent>(candidate.Owner))
                continue;

            var dist = (_transform.GetWorldPosition(candidate.Owner) - _transform.GetWorldPosition(uid)).LengthSquared();
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
