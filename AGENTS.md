# Repository agent instructions

## Project task list

Use [`docs/task-list.md`](docs/task-list.md) as the canonical record of project
work and implementation status.

- Review the task list before beginning project work so the current priorities,
  prerequisites, and completed foundations are understood.
- When starting tracked work, keep its existing task ID and update the task in
  place as its scope becomes clearer.
- If implementation reveals a scope gap, stop work and call out the gap
  explicitly so the project owner can decide the direction. Do not silently
  expand the current task, create an architectural workaround, or defer the
  decision inside the implementation.
- Add newly discovered work to the task list even when it should not be
  addressed during the current task. Put it in **Future parking lot** unless it
  is clearly current or near-term work.
- Use the next unused `TASK-NNN` identifier for new work. Do not renumber or
  reuse existing task IDs.
- Move completed tasks to **Completed foundations** and change the checkbox to
  `[x]`. Keep the existing task ID stable, preserve a concise record of what
  was completed, and note any remaining follow-up task IDs. Existing
  `DONE-NNN` entries describe foundations that predate the canonical tracker;
  do not convert newer `TASK-NNN` entries to that format.
- Keep design rationale, behavioral contracts, and detailed decisions in the
  appropriate design document. The task list should contain a concise action
  and link to that context rather than duplicate substantial notes.
- Do not create separate ad hoc TODO files or scatter project-status checklists
  through design documents. Add or link the work in the canonical task list.
- Preserve the project's strictly single-player direction. Do not add
  multiplayer, networking, replication, remote-authority, client-prediction,
  rollback-netcode, lobby, or related affordances to the task list or
  implementation.

## Architecture documentation

Architecture documents are reviewed directly by the project owner. Write them
for a human reader rather than as agent-to-agent notes: establish the problem
and context, explain the chosen boundaries and terminology, make decisions and
deferred choices explicit, and keep the narrative understandable without
requiring a code search.

Include a diagram when it materially clarifies relationships, hierarchy,
ownership, state transitions, or a multi-step flow. Prefer a small Mermaid
diagram that is readable alongside the surrounding prose. Do not add decorative
diagrams or use a diagram as a substitute for the written behavioral contract,
and keep diagrams synchronized when the documented design changes.

## Performance and concurrency

Parallel readiness is a project-wide architectural requirement. Keep
authoritative ownership explicit; separate stable read/evaluate work from
buffered effects and deterministic commit; and make substantial workloads
batchable without tying one entity, system, or subsystem to one thread.

Simulation results must not depend on worker count, work-stealing order, or
which worker finishes first. Do not introduce hidden cross-owner mutation,
task-per-entity designs, pervasive locks, or sequence allocation based on
concurrent completion order. Retain a single-thread reference path and add
concurrent execution only with focused deterministic tests and benchmark
evidence. Follow
[`docs/concurrency-and-performance.md`](docs/concurrency-and-performance.md)
for the full contract.
