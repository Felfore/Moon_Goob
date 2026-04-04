using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Goobstation.Shared.Vehicles.FloorScrubber;

/// <summary>
///     Borg-only tool items that trigger floor scrubber logic.
///     These items act as hand-held "remote controls" for the scrubber component on the borg itself.
/// </summary>
[RegisterComponent, Access(typeof(SharedFloorScrubberSystem))]
public sealed partial class FloorScrubberToolComponent : Component
{
    /// <summary>
    ///     The type of operation this tool performs.
    /// </summary>
    [DataField("mode", required: true)]
    public FloorScrubberToolType Mode = FloorScrubberToolType.Toggle;
}

/// <summary>
///     The specialized functions of a floor scrubber tool.
/// </summary>
[Serializable, NetSerializable]
public enum FloorScrubberToolType : byte
{
    /// <summary>
    ///     Toggles the clean enabled state.
    /// </summary>
    Toggle,
    /// <summary>
    ///     Performs a proximity refill of the clean tank.
    /// </summary>
    Fill,
    /// <summary>
    ///     Dumps the waste tank into a nearby drain.
    /// </summary>
    DumpDrain,
    /// <summary>
    ///     Spills the waste tank onto the current floor tile.
    /// </summary>
    DumpFloor
}
