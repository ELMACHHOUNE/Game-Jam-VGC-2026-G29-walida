// Divert execution to the opening scene
-> ch1_apartment

=== ch1_apartment ===
#bg:background
You wake up to rain hitting the glass.
-> coffee_routine

// A stitch (single '=') belongs to the knot above it
= coffee_routine
#char:arthur:neutral
Arthur: You're up early.
-> ch1_street // Jumps out to a completely different knot

=== ch1_street ===
#clear
#bg:irithyll
The pavement is still wet.

-> examine_desk
=== examine_desk ===
What do you inspect on the desk?

// Disappears after being chosen once
+ [Read the torn letter]
    The ink is smudged, except for a date: November 14th.
    -> examine_desk

+ [Check the locked drawer]
    It won't budge without a key.
    -> examine_desk

// Always remains visible so the player can leave anytime
+ [Step away from the desk]
    -> ch1_apartment
-> END