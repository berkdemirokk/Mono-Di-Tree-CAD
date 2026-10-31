
## WearCAD protection — explicit user requirement
WearCAD is read-only reference material. Never modify, delete, rename, overwrite or save any WearCAD installation, configuration or user-data file. Do not use WearCAD UI actions that save, export, change settings or send production jobs. Do not write to C:\WearCadfe, C:\Hpgl, C:\Nester, C:\Nester1 or C:\NST. Perform all development and tests under C:\Projects\TekstilCAD. If fixtures are needed, read originals only and use independent copies in the TekstilCAD workspace. Never overwrite originals.

## Build coordination and UI
Only the root agent runs builds/tests unless explicitly delegated, to avoid shared bin/obj file locks. User requested background work: do not launch, focus or automate visible windows without a new request. WearCAD remains read-only. Record unverified UI behavior honestly.
