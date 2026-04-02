# Examine
floor-scrubber-examine-clean = [color=cyan]Clean water: {$amount}/{$max}u[/color]
floor-scrubber-examine-waste = [color=yellow]Waste: {$amount}/{$max}u[/color]

# Action names & descriptions
action-name-floor-scrubber-toggle = Toggle Cleaning
action-desc-floor-scrubber-toggle = Toggle the floor scrubber's cleaning mode on or off.

action-name-floor-scrubber-dump-drain = Dump to Drain
action-desc-floor-scrubber-dump-drain = Dump the waste tank into a nearby floor drain.

action-name-floor-scrubber-dump-floor = Dump on Floor
action-desc-floor-scrubber-dump-floor = Pour the waste tank contents onto the floor.

action-name-floor-scrubber-fill = Fill Clean Tank
action-desc-floor-scrubber-fill = Fill the clean water tank from a nearby sink or water source.

action-name-floor-scrubber-clean-gauge = Clean Water
action-desc-floor-scrubber-clean-gauge = The current clean water level. The overlay indicates how full the tank is.

action-name-floor-scrubber-waste-gauge = Waste Level
action-desc-floor-scrubber-waste-gauge = The current waste level. The overlay indicates how full the waste tank is.

# Dump to drain messages
floor-scrubber-dump-drain-empty = The waste tank is empty.
floor-scrubber-dump-drain-no-drain = No drain within reach.
floor-scrubber-dump-drain-success = Waste dumped into the drain.
floor-scrubber-dump-drain-overflow = The drain overflowed — some waste spilled on the floor.

# Dump to floor messages
floor-scrubber-dump-floor-empty = The waste tank is empty.
floor-scrubber-dump-floor-success = Waste dumped on the floor.

# Fill messages
floor-scrubber-fill-full = The clean tank is already full.
floor-scrubber-fill-no-source = No water source nearby.
floor-scrubber-fill-success = Clean tank filled from {$source}.

# Bucket interaction messages
floor-scrubber-bucket-mode-clean = Bucket mode: pouring into clean tank.
floor-scrubber-bucket-mode-waste = Bucket mode: drawing from waste tank.
floor-scrubber-verb-bucket-mode-to-waste = Switch to Draw Waste
floor-scrubber-verb-bucket-mode-to-clean = Switch to Pour Clean Water
floor-scrubber-bucket-poured = Poured {$amount}u into the clean tank.
floor-scrubber-bucket-drawn = Drew {$amount}u of waste into the container.
