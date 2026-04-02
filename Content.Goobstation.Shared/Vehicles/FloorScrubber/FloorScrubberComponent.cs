using Content.Goobstation.Maths.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Goobstation.Shared.Vehicles.FloorScrubber;

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
    ///     The number of additional tiles to clean around the scrubber's current tile.
    ///     0 = current tile only. 1 = +1 expansion (e.g. 3x3 square for Square shape).
    /// </summary>
    [DataField, AutoNetworkedField]
    public int ExtraCleaningRange = 1;

    /// <summary>
    ///     The pattern in which the scrubber cleans tiles.
    /// </summary>
    [DataField, AutoNetworkedField]
    public FloorScrubberShape CleaningShape = FloorScrubberShape.Cross;

    /// <summary>
    ///    Sound to play while the scrubber is actively cleaning.
    /// </summary>
    [DataField]
    public SoundSpecifier? CleaningSound = new SoundPathSpecifier("/Audio/Ambience/Objects/server_fans.ogg");

    /// <summary>
    ///     Volume of the cleaning sound.
    /// </summary>
    [DataField]
    public float CleaningSoundVolume = -6f;

    /// <summary>
    ///     Range of the cleaning sound.
    /// </summary>
    [DataField]
    public float CleaningSoundRange = 5f;

    /// <summary>
    ///     The active audio stream for the cleaning sound.
    /// </summary>
    [NonSerialized]
    public EntityUid? CleaningAudioStream;

    /// <summary>
    ///     How often (in seconds) the scrubber pulses its cleaning logic.
    ///     Higher values improve performance.
    /// </summary>
    [DataField]
    public float CleaningInterval = 0.33f;

    /// <summary>
    ///     Accumulator for throttling the cleaning logic.
    /// </summary>
    [DataField]
    public float CleaningAccumulator;

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

[Serializable, NetSerializable]
public enum FloorScrubberShape : byte
{
    Square,
    Cross,
    Line
}
