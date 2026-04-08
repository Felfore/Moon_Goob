using Content.Goobstation.Shared.Vehicles.FloorScrubber;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.GameObjects;

namespace Content.Goobstation.Server.Vehicles.FloorScrubber;

/// <summary>
///     Handles the installation and uninstallation of the Floor Scrubber borg module.
///     Dynamically injects the required scrubber components and solution tanks into the chassis.
/// </summary>
public sealed class BorgFloorScrubberSystem : EntitySystem
{
    [Dependency] private readonly SharedFloorScrubberSystem _scrubber = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainer = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BorgModuleFloorScrubberComponent, BorgModuleInstalledEvent>(OnModuleInstalled);
        SubscribeLocalEvent<BorgModuleFloorScrubberComponent, BorgModuleUninstalledEvent>(OnModuleUninstalled);
        SubscribeLocalEvent<BorgModuleFloorScrubberComponent, BorgModuleUnselectedEvent>(OnModuleUnselected);
    }

    private void OnModuleInstalled(EntityUid uid, BorgModuleFloorScrubberComponent module, ref BorgModuleInstalledEvent args)
    {
        var chassis = args.ChassisEnt;
        
        // Add core scrubber component
        // EnsureComp will add or get.
        var scrub = EnsureComp<FloorScrubberComponent>(chassis);
        
        // Apply config using proxy method due to Access restrictions on the component
        _scrubber.SetupBorgMode((chassis, scrub), module);
        
        // Ensure solution tanks exist on the chassis. 
        // Parity with VehicleScrubber.
        if (_solutionContainer.EnsureSolutionEntity(chassis, scrub.CleanSolutionName, out var cleanSol))
            _solutionContainer.SetCapacity(cleanSol.Value, 500);

        if (_solutionContainer.EnsureSolutionEntity(chassis, scrub.WasteSolutionName, out var wasteSol))
            _solutionContainer.SetCapacity(wasteSol.Value, 1000);

        if (_solutionContainer.EnsureSolutionEntity(chassis, "print", out var printSol))
            _solutionContainer.SetCapacity(printSol.Value, 10);
        
        // Register the borg itself as the operator for HUD alerts
        _scrubber.UpdateOperators((chassis, scrub));
    }

    private void OnModuleUninstalled(EntityUid uid, BorgModuleFloorScrubberComponent component, ref BorgModuleUninstalledEvent args)
    {
        var chassis = args.ChassisEnt;
        
        if (TryComp<FloorScrubberComponent>(chassis, out var scrub))
        {
            // Force disable cleaning if was active
            _scrubber.SetCleaningEnabled(chassis, false);
            
            // Remove operator alerts for the borg
            _scrubber.TeardownBorgMode((chassis, scrub));
            _scrubber.UpdateOperators((chassis, scrub));
            
            // Remove component to prevent further logic
            RemComp<FloorScrubberComponent>(chassis);
        }
    }

    private void OnModuleUnselected(EntityUid uid, BorgModuleFloorScrubberComponent component, ref BorgModuleUnselectedEvent args)
    {
        var chassis = args.Chassis;
        // Automatically stop cleaning when module is unselected from the active slot
        _scrubber.SetCleaningEnabled(chassis, false);
    }
}
