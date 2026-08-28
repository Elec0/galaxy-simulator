# Accessibility

[Project index](../README.md) · [Internationalization and localization](internationalization-and-localization.md) · [Application shell and map experience](application-shell-and-map-experience.md) · [Player experience](player-experience.md) · [Presentation snapshots](presentation-snapshots.md) · [Save slots and local preferences](save-slots-and-local-preferences.md) · [Project task list](task-list.md)

## Purpose and decision status

Galaxy Command is a map-driven desktop game whose controls, status displays,
notifications, dialogue, and time controls must remain usable through more
than one input or presentation channel. `TASK-061` owns the comprehensive
accessibility behavior needed by those surfaces. It does not change simulation
authority or invent gameplay outcomes.

This document records the project owner's initial accessibility decisions and
separates them from the remaining choices and later audio, motion, and flashing
work.

**Decision status:** In progress. The platform, input, visual, initial audio,
settings, and evidence-direction decisions below were accepted by the project
owner on 2026-08-27. The named color-vision modes, brightness boundary, basic
test matrix, and final acceptance ownership were accepted on the same date.

## Inherited constraints

The following boundaries are already established by completed design work:

- Accessibility preferences are device-local presentation choices. They do
  not enter authoritative simulation state, deterministic snapshots, content
  identity, checkpoints, or save slots.
- The shared local-preference store owns persistence and category reset for
  approved accessibility preferences. Invalid or unsupported preference data
  follows the safe-default and explicit-reset behavior defined by `TASK-050`.
- Player-facing text uses stable semantic keys and localized formatting.
  Controls must tolerate text expansion and supported text scaling, preserve
  logical focus order, and provide localized accessible labels for icon-only
  actions.
- Gameplay meaning cannot depend only on color, an icon, or untranslated
  typography. Status and action wording must remain available at supported
  text scales.
- The Godot application consumes immutable presentation snapshots and
  observer-visible semantic facts. An accessibility surface may transform or
  describe that permitted presentation, but cannot read hidden simulation
  state or derive authoritative outcomes.
- Map geometry and world direction are not mirrored for right-to-left layout.
  Directional interface controls may mirror where appropriate.
- Accessibility work must preserve the strictly single-player scope.

## Supported platforms and assistive technology

The initial accessibility baseline targets Windows and macOS desktop exports.
The Godot application uses Godot's platform accessibility integration and its
semantic control properties. It does not provide in-game self-voicing.

Standard application surfaces must be validated with one named screen reader
on each supported platform: NVDA on Windows and VoiceOver on macOS. The Godot
4.7 documentation names both for target-platform testing.

The spatial map provides a semantic list-based alternative for permitted map
content. Direct map navigation is not treated as an accessible semantic
control, and complete operation without vision is outside the intended product
scope. The alternative must not disclose hidden simulation data or imply that
the project guarantees a complete no-vision playthrough.

## Input and focus

The supported input families are keyboard and mouse together, and controller.
Each supports complete application and gameplay use within the accepted
product scope.

Gameplay actions are remappable, subject to a lockout-prevention invariant.
The binding that opens the game main menu and the bindings needed to reach and
operate the remapping screen cannot all be removed or made unreachable for an
active input family. The application must validate that invariant before
committing a changed binding set. Other conflicts produce a clear warning but
may be accepted by the player.

Each surface may use the conventional focus model appropriate to its structure
rather than one universal navigation algorithm. Reusable controls still need
predictable ordering within each surface. The initial visible focus treatment
is basic window and control highlighting. Detailed focus restoration and
cross-surface transition behavior remain to be specified without changing
this accepted baseline.

## Visual presentation

The application offers default, high-contrast dark, and high-contrast light
presentation modes. A contrast mode applies to interface chrome, map symbols,
map overlays, and the map itself. It cannot recolor only text while leaving
important map relationships illegible.

The application offers protanopia, deuteranopia, and tritanopia modes in
addition to the invariant non-color cues. A mode must apply consistently to
standard interface, status, symbol, overlay, and map presentation without
turning color into the sole distinction between gameplay states.

UI scale and text scale are independent device-local settings. The supported
text-scale values are 100, 125, 150, 175, and 200 percent. Every application
surface must remain functional at every supported value. The application may
offer a smaller optional text setting, but that value is outside the
accessibility guarantee and must be clearly distinguished from the supported
range.

Layouts reflow as text grows. Panels may expand to an approved maximum size
and then expose scrolling for content that no longer fits. A surface cannot
require clipping, overlap, or inaccessible offscreen controls to remain usable
at a supported scale.

Reduced-motion behavior is deferred until the application has concrete motion
or animation effects to evaluate. A flashing threshold and player-facing
photosensitivity setting are likewise deferred until flashing content is
proposed. No task may add those effects and silently define its own
accessibility policy.

## Initial audio boundary

Audio is part of the initial application scope but supplements the visual
presentation. Initial audio derives primarily from already visible events and
cannot become the sole carrier of gameplay information. Speaker labels,
non-speech classification, directional caption indicators, and a separate
caption history are not part of this initial contract.

The existing system inventory had deferred all audio beyond the roadmap and
assigned it no task. `TASK-078` now owns the design of the initial supplemental
audio foundation, including its volume categories and preference contract.
`TASK-079` owns implementation after that design is accepted. `TASK-080`
separately owns optional supplemental audio cues for visual events after that
foundation exists. `TASK-061` does not invent any of those audio decisions in
advance.

## Settings and onboarding

Before starting or loading a session, the player can reach volume, text size,
contrast, and brightness settings. The application offers editable presets:
selecting a preset establishes initial values without preventing later
per-setting changes.

Settings take effect immediately by default. A setting may require
confirmation, delayed application, or restart only when a documented technical
or user-safety reason requires it, and the interface must explain that boundary
before the player commits the change.

First launch presents an accessibility prompt before the player must navigate
the normal application flow. The same settings remain discoverable later and
persist through the shared device-local preference store.

Brightness changes only the game's rendered presentation. It never requests or
implies an operating-system, physical-display, or monitor brightness change.
The exact supported in-game brightness range remains an application tuning
choice that must be documented before implementation acceptance.

## Acceptance baseline

The project uses the current Xbox Accessibility Guidelines as its design and
test baseline. The XAGs are best-practice guidance rather than a legal
compliance certification. At minimum, TASK-061 maps its accepted behavior to
XAG 101 for text, 102 for contrast, 103 for redundant cues, 105 for audio, 106
for screen narration, 107 for input, 112 for UI navigation, 113 for focus, 115
for errors and destructive actions, 116 for time limits, and 121 for accessible
feature documentation. XAG 117 for motion and XAG 118 for photosensitivity
become active when their deferred content enters scope.

Automated evidence is preferred wherever the property can be observed
reliably. Manual verification is reserved for platform assistive-technology
behavior, perceptual quality, conventional navigation judgment, and other
properties that cannot be established by automation.

### Basic test matrix

The project owner accepted this initial matrix on 2026-08-27.

| Evidence slice | Windows | macOS | Automation boundary |
| --- | --- | --- | --- |
| Standard application surfaces | Current supported Windows release with NVDA | Current supported macOS release with VoiceOver | Automate semantic names, roles, states, order, and action availability; manually verify announcements and navigation with the named reader. |
| Keyboard and mouse | Complete home, settings, new or load, active session, semantic map list, pause, and leave flows | Same flow | Automate action coverage, focus reachability, reserved remapping path, conflict warning, and safe commit. Manually smoke-test platform behavior. |
| Controller | One representative controller through the same complete flow | The same representative controller where supported | Automate action-map coverage and focus traversal. Manually verify device prompts, remapping, and recovery from a conflicting binding. |
| Text scale | 100, 125, 150, 175, and 200 percent at one minimum supported window size and one reference window size | Same values and sizes | Automate overflow, clipping, overlap, focus visibility, reflow, and scrolling assertions. Manually review representative dense surfaces at 200 percent. |
| Contrast | Default, high-contrast dark, and high-contrast light across standard UI and map surfaces | Same modes | Automate token use and measurable contrast where stable. Manually verify symbols, overlays, selection, and focus remain distinguishable. |
| Color vision | Every accepted color-vision mode on representative map and status views | Same modes | Automate that every color-coded state has a non-color distinction. Manually review the selected palettes with simulation tools and, when available, affected users. |
| First launch and preferences | Prompt, immediate preview, editable preset, persistence, category reset, and invalid-store fallback | Same flow | Automate preference transitions and persistence. Manually verify the prompt is reachable and understandable with each named screen reader. |

The minimum and reference window sizes, representative controller, and exact
supported operating-system versions remain release-environment choices and
must be filled in before this matrix becomes executable.

### Acceptance ownership

The project owner supplies final feature
acceptance, based on all automated checks passing on both supported exports and
recorded manual checklists completed by someone other than the implementer.
The manual evidence must cover NVDA on Windows, VoiceOver on macOS, keyboard
and mouse, controller, the semantic map list, 200 percent text, and both
high-contrast modes. Before advertising screen-reader or color-vision support,
the project should also obtain at least one focused usability review from a
player or specialist who uses the relevant accommodation.

## External baselines

- [Xbox Accessibility Guidelines](https://learn.microsoft.com/en-us/xbox/accessibility/guidelines)
- [Godot 4.7 screen-reader integration](https://docs.godotengine.org/en/4.7/tutorials/ui/creating_applications.html#screen-reader-integration)
- [Godot 4.7 accessibility display-server support](https://docs.godotengine.org/en/4.7/classes/class_displayserver.html#enum-displayserver-feature)

Implementation remains with the owning application tasks after this behavior
is approved. Those tasks must consume this contract without moving local
accessibility choices into the simulation or save format.
