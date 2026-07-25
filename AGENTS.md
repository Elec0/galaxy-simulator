# Repository agent instructions

## Project task list

Use [`docs/task-list.md`](docs/task-list.md) as the canonical record of project
work and implementation status.

- Review the task list before beginning project work so the current priorities,
  prerequisites, and completed foundations are understood.
- When starting tracked work, keep its existing task ID and update the task in
  place as its scope becomes clearer.
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
