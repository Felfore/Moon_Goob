using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Goobstation.Shared.Vehicles.FloorScrubber;

public sealed partial class FloorScrubberToggleActionEvent : InstantActionEvent
{
}

public sealed partial class FloorScrubberDumpDrainActionEvent : InstantActionEvent
{
}

public sealed partial class FloorScrubberDumpFloorActionEvent : InstantActionEvent
{
}

public sealed partial class FloorScrubberFillActionEvent : InstantActionEvent
{
}

[Serializable, NetSerializable]
public sealed partial class FloorScrubberDumpDrainDoAfterEvent : SimpleDoAfterEvent
{
}
