# Acceptance harnesses

This directory contains test-only, bounded, deterministic harnesses that prove
integrated simulation behavior. All Phase 1-specific source files are kept
together here while the reusable engine, world, and persistent session
boundaries remain in the production project.

Acceptance harnesses may provide fixture-specific setup controls, explicit
stopping conditions, and exact regression fingerprints. They may use internal
simulation setup capabilities through the test assembly friend boundary; they
do not expose a mutable live world to production callers.

Phase 1 also owns the temporary ship materializer that consumes reusable
construction completion effects. It is test infrastructure and must not become
a production entity-lifecycle path.

Godot and other gameplay callers use `GameSession`. The acceptance tests are
the repeatable end-to-end proof.
