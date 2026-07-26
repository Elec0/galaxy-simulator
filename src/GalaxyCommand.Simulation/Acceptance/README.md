# Acceptance harnesses

This directory contains bounded, deterministic harnesses used to prove
integrated simulation behavior. All Phase 1-specific source files are kept
together here while the reusable engine, world, and persistent session
boundaries remain at the project root.

Acceptance harnesses may provide fixture-specific setup controls, explicit
stopping conditions, and exact regression fingerprints. They do not expose the
mutable live world; those affordances remain test infrastructure rather than
the application-facing gameplay API.

Godot and other gameplay callers use `GameSession`. The CLI may run an
acceptance harness explicitly when a repeatable end-to-end proof is desired.
