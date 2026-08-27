# Save slots, autosave, and local preferences

[Project index](../README.md) · [Save format and migration](save-format-and-migration.md) · [Authoritative save boundary](authoritative-save-boundary.md) · [Time and pacing](time-and-pacing.md) · [Internationalization and localization](internationalization-and-localization.md) · [Project task list](task-list.md)

## Purpose and scope

`TASK-050` defines the local application policy around already validated save
files. It does not change authoritative checkpoint contents, compatibility
validation, atomic commit mechanics, or the direct restore contract defined by
`TASK-014` and `TASK-022`. The accepted player-visible display name requires a
separate versioned save-envelope extension in `TASK-067`.

The task also owns persistence, discovery, and reset of local player
preferences. Those preferences are device-local presentation configuration;
they are never authoritative session state or part of a save slot.

**Decision status:** Accepted. Implementation remains separate follow-up work.

## Accepted policy

| Area | Decision |
| --- | --- |
| Manual slots | A manual slot has a stable internal slot ID and a player-editable, non-authoritative display name stored in the save envelope. `TASK-067` owns the required strict-schema extension and migration behavior. Autosaves are distinct from manual slots. |
| Manual saves | A player-initiated save never evicts a manual save. |
| Autosave boundary | An autosave captures only a completed, healthy authoritative commit boundary, using the existing validated file-store contract. |
| Autosave cadence and retention | Local application settings configure the interval and retention. The default interval is five real-time minutes and the default maximum is five autosaves. The interval permits one through 60 minutes and the maximum permits one through 20 autosaves; an explicit disabled setting turns autosaves off. When an interval becomes due, its write waits for the next completed healthy authoritative boundary. It never writes while the simulation is paused. Autosaves rotate within the configured maximum and manual slots are not part of that rotation. |
| Backup | The existing one-generation backup is a recovery choice only after primary validation fails. The application never silently loads it in place of the selected primary. |
| External change during a stale save flow | Discovery returns a non-authoritative revision token for each primary file. A save validates that token and must not automatically overwrite a primary that has changed after discovery. It displays a conflict message and requires the player to reload, save as a new slot, or explicitly overwrite. |
| Cross-device synchronization | Cross-device synchronization is not a current feature. `TASK-050` does not resolve conflicts created by an external synchronization tool. |
| Local preferences | One local, versioned JSON preference store per device retains pacing, presentation, localization, and accessibility preferences separately from slots and authoritative saves. Supported older versions migrate in memory. If the store is unreadable, invalid, or newer than supported, the application preserves it, runs with defaults for that launch, offers reset, and replaces it only after an explicit reset. |
| Pacing preferences | The store includes the default-enabled **Pause when response-required dialogue opens** preference plus the per-category event-responsive actions and selected configured speed caps defined by [Time and pacing](time-and-pacing.md). Grace windows and explanation history remain transient and are not stored. If a selected cap is absent from the current validated speed ladder, the application warns, uses the category's valid default for that launch, and does not silently remap or rewrite the preference. |
| Locale preference | The store retains the explicit locale choice defined by [Internationalization and localization](internationalization-and-localization.md). A development override remains temporary and does not rewrite the stored preference. |
| Preference reset | The application provides reset by preference category and a reset-all-local-preferences action. Neither changes a save slot, session, or installed content. |
| Manual overwrite | Saving over an existing manual slot always requires a confirmation dialog that names the target. Cancel is the safe default. Autosaves are not user-visible manual overwrite targets. |

## Boundaries and player-visible behavior

Save-slot discovery may present only non-authoritative information needed for a
player to identify a save. It must not treat presentation state, local
preferences, or diagnostic history as required to restore the session.

An autosave is a normal validated save written to an autosave slot. Rotation
chooses which autosave slot receives the next successful write; it cannot
weaken validation, publish a partial session, or delete a manual save. A failed
autosave leaves the previously committed file intact under the `TASK-022`
atomic publication contract.

When a player attempts to save from a stale slot view, the application stops
the write before automatic replacement and explains that the slot changed. The
player must choose one of these actions:

- reload the changed slot;
- save the current session as a new manual slot; or
- explicitly overwrite the changed slot.

This policy covers edits to the local primary file. It does not introduce
cloud accounts, synchronization metadata, merge behavior, or a promise to
resolve conflicts produced by external synchronization software.

Failed primary loads and the wording and safe recovery flow for invalid saves
remain `TASK-040` ownership. This task establishes only that a backup is shown
as an explicit recovery choice, never an automatic substitution.

## Local-preference boundary

The local preference store is separate from save slots. It may contain only
player- and device-local choices, including the approved pacing, presentation,
localization, and accessibility categories. A preference cannot alter
authoritative simulation behavior, checkpoint contents, content identity, or
save compatibility unless a later owning gameplay task explicitly changes that
boundary.

`TASK-038` owns the application behavior that consumes pacing preferences.
`TASK-061` owns the definition and acceptance criteria of comprehensive
accessibility modes; `TASK-050` persists and resets any local preferences that
those tasks define.

## Follow-up ownership

- `TASK-022` remains responsible for strict save encoding, validation, atomic
  primary and backup publication, and direct restore mechanics.
- `TASK-067` owns the non-authoritative save-envelope display-name schema
  extension and migration behavior required by this task's accepted slot
  policy.
- `TASK-037` remains responsible for content compatibility and saved
  content-reference migration before general saved sessions are supported.
- `TASK-040` owns player-safe recovery from failed load and invalid session or
  content states.
- `TASK-038` owns pacing implementation and `TASK-061` owns comprehensive
  accessibility behavior. Neither makes their local preferences authoritative.
