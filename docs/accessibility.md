# Accessibility

[Project index](../README.md) · [Internationalization and localization](internationalization-and-localization.md) · [Application shell and map experience](application-shell-and-map-experience.md) · [Player experience](player-experience.md) · [Presentation snapshots](presentation-snapshots.md) · [Save slots and local preferences](save-slots-and-local-preferences.md) · [Project task list](task-list.md)

## Purpose and decision status

Galaxy Command is a map-driven desktop game whose controls, status displays,
notifications, dialogue, and time controls must remain usable through more
than one input or presentation channel. `TASK-061` defined the comprehensive
accessibility behavior needed by those surfaces. It does not change simulation
authority or invent gameplay outcomes.

This document completes the design work in `TASK-061`. It records the project
owner's accepted cross-cutting accessibility behavior and separates it from
later audio, motion, flashing, and application implementation work.

**Decision status:** Accepted by the project owner on 2026-08-27.

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

## Task ownership

`TASK-061` defined the cross-cutting accessibility behavior and its acceptance
contract. It does not implement Godot controls, audio, motion effects, or
flashing effects.

| Work | Owner |
| --- | --- |
| Standard-surface semantics, focus, semantic map list, input and remapping behavior, visual-setting interactions, local preference fields, and acceptance contract | `TASK-061` |
| Godot application implementation of the accepted accessibility contract | `TASK-077` |
| Initial supplemental audio design and implementation | `TASK-078` and `TASK-079` |
| Optional supplemental accessibility audio cues | `TASK-080` |
| Reduced-motion behavior after concrete effects exist | `TASK-081` |
| Flashing and photosensitivity behavior before relevant effects ship | `TASK-082` |

The effect-owning implementation task consumes `TASK-081` or `TASK-082` once
its design is accepted. Neither deferred task creates a second presentation
owner.

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
is basic window and control highlighting. The detailed focus restoration and
cross-surface transition contract appears below.

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
The supported in-game brightness range is 80 through 120 percent in 10 percent
steps, with 100 percent as the default.

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

The executable contract below defines the minimum and reference window sizes,
representative controller, and operating-system release policy. Each release
records the exact tested operating-system versions in its evidence.

### Acceptance ownership

The project owner supplies final feature acceptance, based on all automated
checks passing on both supported exports and
recorded manual checklists completed by someone other than the implementer.
The manual evidence must cover NVDA on Windows, VoiceOver on macOS, keyboard
and mouse, controller, the semantic map list, 200 percent text, and both
high-contrast modes. Before advertising screen-reader or color-vision support,
the project should also obtain at least one focused usability review from a
player or specialist who uses the relevant accommodation.

## Detailed behavioral contract

The project owner accepted the following behavior on 2026-08-27.

### Screen-reader semantics and announcements

- Use Godot's native semantic roles and actions. Every interactive control has
  a localized name, role, current state or value, and every action available
  without a screen reader.
- Keep semantic reading order consistent with the visible layout after reflow.
  Decorative controls and duplicated labels remain outside the accessibility
  tree.
- Announce a newly opened modal surface, validation errors, explicit command
  results, and material state changes for the focused item. Do not announce
  every simulation tick, interpolated movement update, or unfocused map change.
  The accessible activity surface remains the review path for retained events.
- If platform accessibility support is unavailable, keep visual and input
  operation functional, emit a local diagnostic, and do not claim screen-reader
  support for that platform configuration. Do not fall back to self-voicing.

### Focus and surface navigation

- When a surface opens, focus its first logical, enabled, non-destructive
  control. A modal traps focus within itself until it closes.
- Closing a surface restores focus to the control that opened it. If that
  control no longer exists, use the next logical sibling, then the previous
  sibling, then the containing surface's first logical control.
- If the focused record disappears during a refresh, preserve focus by stable
  identity when possible. Otherwise use the same next, previous, then container
  fallback and announce that the prior record is unavailable.
- Use a persistent outline plus a non-color style change for focus. The outline
  is at least two logical pixels and maintains at least 3:1 contrast with
  adjacent colors in every supported visual mode.

### Semantic map list

- Group permitted records by system, then by record type. Order groups and
  records by localized display name with stable identity as the tie-breaker so
  the list is understandable and repeatable within one locale.
- Include only information already permitted by the observer-scoped
  presentation snapshot. Mark stale, confirmed-missing, and unavailable records
  explicitly without exposing hidden authoritative state.
- Synchronize stable selection and focused identity in both directions between
  the list, visual map, and inspector. Preserve the focused identity across
  refreshes without reordering records because their live state changed.
- Offer the same entity-based inspection and commands available from the map.
  Arbitrary coordinate pointing remains outside the semantic-list alternative
  and the accepted no-vision scope.
- Announce focused-record removal and explicit selection or command outcomes.
  Route other changes through the accessible activity surface instead of live
  narration.

### Input remapping and controller lifecycle

- Keep permanent fallback bindings for opening the main menu and operating the
  remapping screen: Escape, arrow keys, and Enter on keyboard; Menu, directional
  pad, A, and B on the representative Xbox controller. Players may add
  alternatives but cannot remove every fallback for an active input family.
- Show every affected action when a non-reserved binding conflicts. Warn and
  allow the conflict only after explicit confirmation. Reserved fallback
  conflicts cannot replace the fallback.
- Provide reset for one action, one input family, and all bindings. Resetting
  bindings does not reset unrelated accessibility preferences.
- If the active controller disconnects, acquire a local application pause and
  show a keyboard-and-mouse-operable reconnect or continue prompt. Restoring
  the controller does not automatically resume simulation.

### Settings and first launch

- Provide `Default`, `High Contrast Dark`, `High Contrast Light`, and `Large
  Text` editable presets. Protanopia, deuteranopia, and tritanopia remain an
  independent selector so they compose with contrast and text choices.
- Any manual change after choosing a preset labels the current configuration
  `Custom`; it never rewrites the named preset.
- Apply contrast first, color-vision remapping second, and brightness last. All
  supported combinations retain non-color cues and the accepted contrast
  thresholds.
- Support UI scale at 100, 125, and 150 percent, independently of text scale.
  Every combination with the accepted 100 through 200 percent text scales must
  remain functional. An optional 75 percent UI scale may be offered outside the
  accessibility guarantee.
- Support in-game brightness from 80 through 120 percent in 10 percent steps,
  with 100 percent as default. Brightness cannot reduce any required UI, text,
  focus, symbol, or overlay contrast below its accepted threshold.
- Apply settings immediately. Contrast, brightness, UI scale, and text scale
  use a 15-second `Keep` or `Revert` confirmation that automatically restores
  the prior usable configuration if no confirmation arrives.
- On first launch, focus an accessibility heading followed by `Open
  Accessibility Settings` and `Continue with Defaults`. After either explicit
  choice, the prompt does not recur unless local onboarding preferences are
  reset. The full settings remain available from the main menu.

### Executable acceptance matrix

- On every release, test the current generally available Windows 11 and macOS
  releases. Pin the exact versions in release evidence rather than freezing
  them in this architecture document. Run automated smoke coverage on any
  older operating-system version the project still advertises as supported.
- Use an Xbox Wireless Controller as the representative controller on both
  platforms. It is a common Windows controller and is officially supported on
  macOS.
- Use 1280 by 720 logical pixels as the minimum viewport and 1920 by 1080
  logical pixels as the reference viewport. Manually smoke-test native
  high-density display scaling on macOS and 200 percent operating-system scale
  on Windows.
- Require at least 4.5:1 contrast for standard text and important standard-size
  visual elements, 3:1 for large text and important large elements, 3:1 for
  inactive-element text, and 7:1 for high-contrast-mode elements.
- Record automated results by surface and setting combination. Record manual
  evidence as a versioned checklist naming platform version, build, assistive
  technology version, controller, viewport, settings, tester, date, and result.
  Retain one screenshot or short recording for each perceptual failure or
  exception, but do not require media for every passing check.

## External baselines

- [Xbox Accessibility Guidelines](https://learn.microsoft.com/en-us/xbox/accessibility/guidelines)
- [Xbox Accessibility Guideline 102: Contrast](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/102)
- [Xbox Accessibility Guideline 113: UI focus handling](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/113)
- [Godot 4.7 screen-reader integration](https://docs.godotengine.org/en/4.7/tutorials/ui/creating_applications.html#screen-reader-integration)
- [Godot 4.7 accessibility display-server support](https://docs.godotengine.org/en/4.7/classes/class_displayserver.html#enum-displayserver-feature)
- [Apple support for Xbox Wireless Controller on Mac](https://support.apple.com/en-euro/111101)

Implementation remains with the owning application tasks. Those tasks must
consume this contract without moving local accessibility choices into the
simulation or save format.
