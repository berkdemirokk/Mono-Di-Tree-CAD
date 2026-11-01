# Model storage research prototype

This standalone Swift package is not part of TekstilCADViewer and does not read, migrate, or modify its existing model library. It stores an ID, display name, and exact model bytes in one SQLite transaction. It uses the operating system SQLite library and opens a separate connection per operation, with rollback journaling, full synchronous mode, and a bounded busy wait. IDs are inserted exclusively; a duplicate is an error.

The prototype also provides metadata listing, identity-based deletion, and an explicit batch import of caller-supplied byte snapshots. Batch errors or duplicate IDs roll back the whole batch; no source files are removed. This is a migration building block, not a migration tool: it does not enumerate, coordinate or validate legacy files, track provenance, resume imports, reconcile duplicates, or verify a backup. Reads, listing and deletion fail rather than create a missing database.

The tests cover binary and Unicode round trips, rollback on an injected pre-commit error, duplicate IDs, a connection closed without committing, concurrent writers using separate connections, list/delete identity, and interrupted batch import. Closing a connection is not a simulation of process termination or power loss. No Apple device or simulator validation has been performed. The caller must use an app-private directory, treat SQLite's database and journal as a unit, and handle errors; this prototype does not defend against another actor modifying those files. It does not implement user-facing integration, file protection, backup policy, database corruption recovery, storage limits, or a power-loss test.

On a suitable Apple development host, run `swift test` in this directory; integration into the app would require a separate reviewed design and migration plan.

The DEBUG-only DebugPointCopyStore is an isolated point-copy insertion experiment, not wired into the app. It accepts caller-validated snapshot bytes up to 8 MiB and uses the prototype transaction; freshness, app-private database placement and coordination remain caller responsibilities. No Apple build, device, or power-loss validation is claimed.

The DEBUG snapshot authorization experiment checks caller-supplied document identity and revision inside the transaction before commit. The caller must hold its own document serialization gate through the complete synchronous call, including commit; the prototype cannot enforce this or coordinate SwiftUI state. It is not connected to the app.

DebugPointCopyCoordinator serializes its own document identity/revision updates and synchronous transaction using one lock. It is not connected to SwiftUI; any state changes bypassing it invalidate the guarantee. It performs blocking I/O and must not be called on the UI thread. Apple build/device validation remains outstanding.

The concurrency test checks the completed save and stale follow-up; scheduling alone does not prove that the competing thread attempted the lock while the transaction was held. The serialization claim rests on code inspection of the shared lock and requires Apple-platform concurrency testing.

The coordinator now owns the snapshot bytes and revision together under its lock, and rejects a forged snapshot whose bytes do not match the current state. Callers must route every real document change into that coordinator; external document state and independent direct prototype APIs are not protected by it.

The coordinator can conditionally install a completed edit against its captured source revision and bytes. This serializes prototype-owned state only; callers must independently validate edited bytes and route all real document changes through the coordinator before any app integration.

A single pending edit request can be cancelled under the coordinator lock. Cancellation prevents application only if it acquires that lock first; after an application wins, cancellation cannot undo it. This does not interrupt computation or cancel a SQLite save, and has no effect on app state outside the prototype.

An oversized result consumes its matching edit request before failing, so retry requires a new request. Cancellation/application tests exercise sequential orderings; they do not prove an actual concurrent race on Apple platforms. Ordering is inferred from the shared-lock implementation.
