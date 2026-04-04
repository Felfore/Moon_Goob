using Content.Goobstation.Maths.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Goobstation.Shared.Vehicles.FloorScrubber;

/// <summary>
///     Marker component for the Floor Scrubber borg module.
///     Triggers the dynamic addition of scrubber components to the chassis.
/// </summary>
[RegisterComponent]
public sealed partial class BorgModuleFloorScrubberComponent : Component
{
    /// <summary>
    ///     The pattern in which the scrubber cleans tiles.
    /// </summary>
    [DataField]
    public FloorScrubberShape CleaningShape = FloorScrubberShape.Frontal;

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
    /// </summary>
    [DataField]
    public int ExtraCleaningRange = 1;
}
