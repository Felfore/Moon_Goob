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
[Access(typeof(SharedFloorScrubberSystem))]
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

    // --- Action refs ---

    /// <summary>
    ///     Action for toggling the scrubber state.
    /// </summary>
    [DataField]
    public EntityUid? CleanAction;

    /// <summary>
    ///     Action for dumping the waste tank into a drain.
    /// </summary>
    [DataField]
    public EntityUid? DumpDrainAction;

    /// <summary>
    ///     Action for dumping the waste tank onto the floor.
    /// </summary>
    [DataField]
    public EntityUid? DumpFloorAction;

    /// <summary>
    ///     Action for refilling the clean water tank.
    /// </summary>
    [DataField]
    public EntityUid? FillAction;
}

[Serializable, NetSerializable]
public enum FloorScrubberShape : byte
{
    Square,
    Cross,
    Line
}
