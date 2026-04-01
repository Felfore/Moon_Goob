using Content.Goobstation.Maths.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Goobstation.Shared.Fluids.Components;

/// <summary>
///     Component for an entity that can scrub decals and vacuum puddles.
///     Uses two item slots for modular fluid containers (e.g., buckets, beakers).
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
    ///     The item slot ID for the clean water container.
    /// </summary>
    [DataField]
    public string TankSlotId = "tank_slot";

    /// <summary>
    ///     The item slot ID for the waste fluid container.
    /// </summary>
    [DataField]
    public string WasteSlotId = "waste_slot";

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
    ///     The action used to toggle cleaning mode.
    /// </summary>
    [DataField]
    public EntityUid? CleanAction;
}
