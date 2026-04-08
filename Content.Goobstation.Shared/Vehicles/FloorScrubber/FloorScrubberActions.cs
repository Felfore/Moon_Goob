using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Goobstation.Shared.Vehicles.FloorScrubber;

/// <summary>
///     Event raised to toggle the floor scrubber's cleaning mode.
/// </summary>
public sealed partial class FloorScrubberToggleActionEvent : InstantActionEvent
{
}

/// <summary>
///     Event raised to start the "dump into drain" process.
/// </summary>
public sealed partial class FloorScrubberDumpDrainActionEvent : InstantActionEvent
{
}

/// <summary>
///     Event raised to dump the waste tank directly onto the floor.
/// </summary>
public sealed partial class FloorScrubberDumpFloorActionEvent : InstantActionEvent
{
}

/// <summary>
///     Event raised to refill the clean water tank from a nearby sink.
/// </summary>
public sealed partial class FloorScrubberFillActionEvent : InstantActionEvent
{
}

/// <summary>
///     DoAfter event for the "dump into drain" process.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class FloorScrubberDumpDrainDoAfterEvent : SimpleDoAfterEvent
{
}

[Serializable, NetSerializable]
public sealed partial class FloorScrubberDumpFloorDoAfterEvent : SimpleDoAfterEvent
{
}

[Serializable, NetSerializable]
public sealed partial class FloorScrubberFillDoAfterEvent : SimpleDoAfterEvent
{
}
