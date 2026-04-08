# Floor Scrubber Feature - Technical Specification & Feature Outline

The **Floor Scrubber** is a specialized maintenance vehicle and cyborg module designed for high-efficiency janitorial operations. It provides a modular system for cleaning decals, vacuuming puddles, and maintaining station hygiene via a data-driven, event-based architecture.

---

## 🚀 Core Features

### 1. Dual-Tank Solution System
The scrubber manages two internal solution containers:
-   **Clean Water Tank (`cleanTank`)**: Used to dilute grime and clean decals. Can be refilled from any water source (sinks/taps).
-   **Waste Tank (`wasteTank`)**: Stores vacuumed fluids. Must be periodically emptied into floor drains or dumped onto the floor.

### 2. Multi-Mode Cleaning Patterns
The cleaning area is configurable via the `CleaningShape` enum:
-   **Square**: A standard radius around the scrubber (e.g., 3x3).
-   **Cross**: Horizontal and vertical tiles in a "+" pattern.
-   **Line**: Cleans in a straight line relative to movement.
-   **Frontal**: Specifically cleans the tile directly in front of the vehicle/borg.

### 3. Cleaning & Vacuuming Logic
-   **Decal Cleaning**: Automatically removes standard cleanable decals (dirt, blood, grime) at a cost of 2u clean water per decal.
-   **Puddle Vacuuming**: Siphons up to 5u of fluid from puddles into the waste tank per interval.
-   **Footprint Sanitization**: Automatically clears "filthy" footprints. If cleaning is active, the scrubber generates a "Clean Water Trail" by filling its own footprint solution with water from the clean tank.
-   **Automatic Throttling**: Logic pulses every **0.33 seconds** (configurable) to ensure high performance even with multiple active units.

### 4. Modular Integration (Vehicles & Borgs)
The system is decoupled from specific entity types using an `ActiveOperators` registration system:
-   **Vehicles**: Automatically registers buckled drivers as operators. Requires a `FloorScrubberKey` in the ignition slot to toggle the engine.
-   **Cyborgs**: Registers the borg as a "Self Operator" upon module installation. Does not require a physical key.

---

## 🛠️ File Structure

### ⚖️ Shared Logic (`Content.Goobstation.Shared`)
-   [SharedFloorScrubberSystem.cs](file:///o:/games/monolith%20server/Goob-Station/Content.Goobstation.Shared/Vehicles/FloorScrubber/SharedFloorScrubberSystem.cs): The "Brain". Handles tile math, solution transfers, and operator state.
-   [FloorScrubberComponent.cs](file:///o:/games/monolith%20server/Goob-Station/Content.Goobstation.Shared/Vehicles/FloorScrubber/FloorScrubberComponent.cs): Data definitions (volumes, rates, patterns).
-   [FloorScrubberActions.cs](file:///o:/games/monolith%20server/Goob-Station/Content.Goobstation.Shared/Vehicles/FloorScrubber/FloorScrubberActions.cs): Net-serializable events for UI/Tool interactions.

### 🔌 Server Logic (`Content.Goobstation.Server`)
-   [BorgFloorScrubberSystem.cs](file:///o:/games/monolith%20server/Goob-Station/Content.Goobstation.Server/Vehicles/FloorScrubber/BorgFloorScrubberSystem.cs): Handles the specific bridge between the Borg module system and the shared scrubber logic.

### 📦 Prototypes (`Resources/Prototypes`)
-   [floorscrubber.yml](file:///o:/games/monolith%20server/Goob-Station/Resources/Prototypes/_Goobstation/_Floorscrubber/Vehicles/floorscrubber.yml): The Janitorial Floor Scrubber vehicle entity.
-   [floorscrubber_actions.yml](file:///o:/games/monolith%20server/Goob-Station/Resources/Prototypes/_Goobstation/_Floorscrubber/Vehicles/floorscrubber_actions.yml): Action bar buttons for Toggle, Dump, and Fill.
-   [floorscrubber_alerts.yml](file:///o:/games/monolith%20server/Goob-Station/Resources/Prototypes/_Goobstation/_Floorscrubber/Alerts/floorscrubber_alerts.yml): HUD status gauges for Clean/Waste levels.

---

## 🎮 User Interface & Controls

### HUD Alerts (Gauges)
Operators see two dynamic status icons on their HUD:
-   **💧 Clean Water**: Shows 11 stages (0-10) of remaining cleaning fluid. Uses the new `watertank.rsi`.
-   **⚠️ Waste Tank**: Shows 11 stages (0-10) of collected waste. Uses the new `wastetank.rsi`.

### Action Buttons
1.  **Toggle Cleaning**: Start/Stop the scrubber engine. Consumes clean water and slows movement speed by 50%.
2.  **Fill Clean Tank**: Interacts with a nearby Sink/Water Source to refill the `cleanTank`.
3.  **Dump to Drain**: Flushes the `wasteTank` into a nearby floor drain.
4.  **Dump on Floor**: (Emergency only) Spills the entire `wasteTank` at the current location.

---

## ⚙️ Requirements & Constraints
-   **Power**: Currently relies on the vehicle's internal battery or the Borg's charge.
-   **Space**: Occupies a 1x1 footprint but can clean up to a 3x3 area.
-   **Access**: Requires janitorial access (or a Janitorial Borg Module) to operate effectively.

---
> [!NOTE]
> This feature was refactored in April 2026 to ensure compatibility with the Goobstation Modular Footprint system, allowing for the "Clean Trail" effect.
