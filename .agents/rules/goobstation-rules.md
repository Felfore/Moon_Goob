---
trigger: always_on
---

# Goobstation Coding Conventions

This document provides a concise summary of the project's coding and YAML conventions.

## Goobstation Specifics
- **Organization**: Place all new prototypes, localization, assets, and code in `Goobstation` folders/modules.
- **Feature Folders**: Keep code related to a feature in its specific folder (e.g., `Changeling/Rules/` instead of `GameTicking/Rules/`).
- **Core Edits**:
    - Mark every core modification with `// Goobstation - <Description>`.
    - Use a single-line call to an extension method in a custom module for core logic additions.
- **Localization**: Localize **all** player-facing strings. Use `kebab-case` for IDs and be specific to avoid clashes.

## File & Class Layout
- **Namespaces**: Use file-scoped namespaces matching the folder structure.
- **Ordering**: Fields and auto-properties must come before any methods.
- **Inheritance**: Classes must be `sealed`, `abstract`, `static`, or marked `[Virtual]`.
- **Using Directives**: Place at the very top of the file.

## Documentation & Naming
- **Comments**: Focus on the **Why**, not the What. Use XML docs for top-level members.
- **Strings**: Never use human-readable text as identifiers.
- **Events**: Suffix with `Event` (e.g., `DamagedEvent`). Handlers should be `On<X>Event`.
- **Shared Types**: Only prefix with `Shared` if server/client counterparts exist with the same name.

## Components & Systems
- **Separation**: Game logic goes in **Systems**; **Components** only hold data.
- **Access**: Component data should be `public`. Use `[Friend]` or `[Access]` to restrict modifications to specific systems.
- **System Methods**:
    - Use `Resolve(entity, ref component)` at the start of public system methods.
    - Public methods should follow: `Entity<T?> entity, arg1, arg2...`.
- **Dependencies**: Use `[Dependency]` instead of `IoCManager.Resolve`.
- **Proxy Methods**: Use `EntitySystem` proxy methods (e.g., `Name(uid)`, `Transform(uid)`) instead of `EntityManager`.

## Events & Async
- **Method Events**: Prohibited for performing actions. Use System methods instead.
- **Event Types**: Use `struct` for events, raise them by `ref`, and use the `[ByRefEvent]` attribute.
- **Async**: Avoid async for game simulation; use events (e.g., for `DoAfter`).
- **EventBus**: Prefer EventBus over standard C# events for simulation.

## Performance
- **Updates**: Use events instead of `Update` ticks whenever possible.
- **Collections**: Use iterator methods (`yield return`) instead of returning new collections.
- **Lambdas**: Avoid variable captures in lambdas or local functions to minimize allocations.

## YAML Conventions
- **Ordering**: `type` > `abstract` > `parent` > `id` > `name` > `description` > `components`.
- **Formatting**:
    - No empty lines between components.
    - One empty newline between different prototypes.
    - No indentation for items in the `components:` list.
- **Naming**: `PascalCase` for IDs and Component types; `camelCase` for fields/data-fields.
- **Textures**: Don't specify textures in abstract parents.

## Prototypes & Resources
- **Prototypes**: Use `ProtoId<T>` for data-fields. Don't cache; use `prototypeManager`.
- **Enums**: Heavily discouraged for in-game types; use prototypes instead.
- **Specifiers**: Use `SoundSpecifier` for audio and `SpriteSpecifier` for textures/sprites.
- **EntityUid**: Use `ToPrettyString(uid)` for admin logs. Use nullable `EntityUid?` for optional entities (never `EntityUid.Invalid`).
