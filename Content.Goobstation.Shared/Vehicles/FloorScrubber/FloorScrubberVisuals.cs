using Robust.Shared.Serialization;

namespace Content.Goobstation.Shared.Vehicles.FloorScrubber;

[Serializable, NetSerializable]
public enum FloorScrubberVisuals : byte
{
    /// <summary>
    ///     Whether the scrubbing animation overlay is active.
    /// </summary>
    Cleaning
}

[Serializable, NetSerializable]
public enum FloorScrubberCleaningVisualState : byte
{
    Off,
    On
}
