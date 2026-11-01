# Model storage research prototype

This standalone Swift package is not part of TekstilCADViewer and does not read, migrate, or modify its existing model library. It stores an ID, display name, and exact model bytes in one SQLite transaction. It uses the operating system SQLite library and opens a separate connection per operation, with rollback journaling, full synchronous mode, and a bounded busy wait. IDs are inserted exclusively; a duplicate is an error.

The prototype also provides metadata listing, identity-based deletion, and an explicit batch import of caller-supplied byte snapshots. Batch errors or duplicate IDs roll back the whole batch; no source files are removed. This is a migration building block, not a migration tool: it does not enumerate, coordinate or validate legacy files, track provenance, resume imports, reconcile duplicates, or verify a backup. Reads, listing and deletion fail rather than create a missing database.

The tests cover binary and Unicode round trips, rollback on an injected pre-commit error, duplicate IDs, a connection closed without committing, concurrent writers using separate connections, list/delete identity, and interrupted batch import. Closing a connection is not a simulation of process termination or power loss. No Apple device or simulator validation has been performed. The caller must use an app-private directory, treat SQLite's database and journal as a unit, and handle errors; this prototype does not defend against another actor modifying those files. It does not implement user-facing integration, file protection, backup policy, database corruption recovery, storage limits, or a power-loss test.

On a suitable Apple development host, run `swift test` in this directory; integration into the app would require a separate reviewed design and migration plan.

The DEBUG-only DebugPointCopyStore is an isolated point-copy insertion experiment, not wired into the app. It accepts caller-validated snapshot bytes up to 8 MiB and uses the prototype transaction; freshness, app-private database placement and coordination remain caller responsibilities. No Apple build, device, or power-loss validation is claimed.
