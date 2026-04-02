using Content.Goobstation.Maths.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Goobstation.Shared.Fluids.Components;

/// <summary>
///     Component for an entity that can scrub decals and vacuum puddles.
///     Uses two large internal solution tanks: one for clean water, one for waste.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FloorScrubberComponent : Component
{
    /// <summary>
    ///     Is the scrubber currently active?
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool CleaningEnabled;

    /// <summary>
    ///     The name of the internal clean water solution.
    /// </summary>
    [DataField]
    public string CleanSolutionName = "cleanTank";

    /// <summary>
    ///     The name of the internal waste solution.
    /// </summary>
    [DataField]
    public string WasteSolutionName = "wasteTank";

    /// <summary>
    ///     Controls whether using a bucket on the scrubber pours into the clean tank
    ///     or draws from the waste tank.
    /// </summary>
    [DataField, AutoNetworkedField]
    public FloorScrubberBucketMode BucketMode = FloorScrubberBucketMode.PourIntoClean;

    /// <summary>
    ///     Amount of water used per decal cleaned.
    /// </summary>
    [DataField]
    public FixedPoint2 CleaningAmount = FixedPoint2.New(2);

    /// <summary>
    ///     Maximum amount of fluid vacuumed per operation.
    /// </summary>
    [DataField]
    public FixedPoint2 VacuumAmount = FixedPoint2.New(5);

    /// <summary>
    ///     Speed multiplier applied to the entity while cleaning.
    /// </summary>
    [DataField]
    public float SpeedMultiplier = 0.5f;

    /// <summary>
    ///     The distance in front of the scrubber to clean.
    /// </summary>
    [DataField]
    public float CleaningRange = 1.0f;

    /// <summary>
    ///     The display period in seconds used for the tank gauge cooldown animation.
    ///     Remaining cooldown = fillFraction * GaugeDisplayPeriod → overlay = fill level.
    /// </summary>
    [DataField]
    public float GaugeDisplayPeriod = 100f;

    /// <summary>
    ///     Accumulator for throttling gauge updates (~0.5s intervals).
    /// </summary>
    [DataField]
    public float GaugeUpdateAccumulator;

    // --- Action refs ---

    [DataField]
    public EntityUid? CleanAction;

    [DataField]
    public EntityUid? DumpDrainAction;

    [DataField]
    public EntityUid? DumpFloorAction;

    [DataField]
    public EntityUid? FillAction;

    /// <summary>
    ///     Display-only action showing the clean water tank level via the cooldown sweep animation.
    /// </summary>
    [DataField]
    public EntityUid? CleanGaugeAction;

    /// <summary>
    ///     Display-only action showing the waste tank level via the cooldown sweep animation.
    /// </summary>
    [DataField]
    public EntityUid? WasteGaugeAction;
}

[Serializable, NetSerializable]
public enum FloorScrubberBucketMode : byte
{
    PourIntoClean,
    DrawFromWaste
}
