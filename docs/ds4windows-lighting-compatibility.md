# Optional DS4Windows lighting integration — engineering contract

Status: implemented read-only diagnostics; lighting ownership remains design and simulation only. No DS4Windows changes, physical HID output or changeledcolor commands are part of this integration.

## Current baseline

NSEdge must work without DS4Windows. While another controller manager owns output, the existing native lighting guard remains intact. Ryochan7 3.3.3 supports read-only Query slot properties and changeledcolor, but the latter only updates an internal custom colour and does not provide mode acquisition, crash-safe leases or profile restoration.

The read-only compatibility probe:
- matches the WM_COPYDATA IPC window to its owning PID and executable, checks the running image version and the known 3.3.3 DLL SHA-256;
- sends only stock Query messages for MacAddress, ConnType and ProfileName;
- matches NSEdge's normalized physical controller identity and transport, rejects duplicate addresses, and rechecks the selected slot and host process;
- identifies the DS4Windows **software profile**, not the Sony onboard profile;
- is advisory and never grants permission for physical output.

Malformed addresses on unrelated slots are counted and skipped so they cannot
hide a valid controller later in the scan. Empty stock responses are ambiguous:
DS4Windows can signal completion after a failed map write, and legitimate empty
values write no bytes. The probe therefore reports an inconclusive absence when
empty address replies occur and refuses a complete match with empty transport or
software-profile data. A reported match is an observation, not an atomic snapshot
or proof that all other slots were successfully read.

The probe creates temporary Windows IPC query objects. It does not mutate controller settings. A query failure or timeout is reported and never treated as authorization.

## Optional GPL-compliant implementation

Implement any extension in a separately sourced, distributed, GPL-3.0-compliant DS4Windows compatibility build with appropriate source, copyright notices and licence obligations. Do not copy DS4Windows' GPL code into NSEdge's MIT application or replace the user's installed binary. NSEdge remains independent.

The compatible DS4Windows build should own all physical device output while it is active. NSEdge sends battery/colour policies, not Sony output reports. Prefer a versioned same-user local IPC channel with explicit acknowledgements, bounded requests and access controls; simply delivering WM_COPYDATA does not prove an LED update.

Proposed protocol v1, NOT present in stock 3.3.3:
- Capabilities: protocol version, exact controller identity and generation, lighting support, current owner and lease support.
- Acquire(identity, generation, clientSession): exclusive, authenticated, controller-specific temporary ownership; returns an unguessable token.
- SetColor(token, generation, RGB): temporary colour only; never writes persistent profile settings.
- Renew(token): extends an unexpired lease, never revives a stale token.
- Release(token): removes the override and recalculates the **current** DS4Windows software profile lighting.
- Expire: server-owned periodic timer removes leases after owner death/timeouts and recalculates current lighting.
- Disconnect/reconnect: invalidate stale generations/tokens and renegotiate capabilities.

Suggested initial lease lifetime is five seconds and client renewal every two seconds; these are proposals, not existing DS4Windows capabilities. Use a monotonic clock and ensure a failed or missing client cannot hold the LEDs indefinitely. The server must also handle profile changes, other forced-lighting priorities, controller removal and service restarts. Establish correct ordering with special actions, charging, distance profile effects, shutdown and other internal DS4Windows lighting behaviour before touching its renderer.

## What the simulation proves (and does not)

SimulatedLightingLeaseHost and SimulatedLightbarSink are test-only in-memory types
under `tests/NSEdge.Tests`; they are not shipped in NSEdge.Core. They have no
DS4Windows IPC, HID, file, process or physical-output functionality. The host
relies on an explicit Expire() call; it does not run a Windows timer or handle a
real crash.

Regression tests cover exclusive ownership, invalid identity and transport matching, wrong tokens, expiry, renewal, latest-profile restoration, disconnect/reconnect generation checks and stale-token rejection.

These tests do not verify actual colour changes, physical HID output, profile persistence, rumble, gyro, Windows IPC behaviour or real crash recovery.

## Windows IPC verification (v0.3 draft)

The Windows Build workflow runs the original protocol suite and `tests/NSEdge.IpcTests`.
This separate Windows-only test executable compiles the same
`Ds4QueryTransport.cs` used by the production read-only Diagnostics path,
but supplies **random test-specific** mutex, result-map and event names.
Its host is a small local WinForms message window. It never publishes the
stock DS4Windows window class, uses no DS4Windows process identity, and never
connects to a Sony device.

Coverage includes real WM_COPYDATA delivery, correct command string/slot,
shared-memory response bytes, named event acknowledgment, abandoned/contended
mutexes, stale events, invalid and non-ASCII results, hung/closed windows,
host shutdown during a response and a bounded overall scan deadline.
None of these tests sends native HID or `changeledcolor` commands.

The read-only probe itself scans at most eight physical DS4Windows slots with
an eight-second query budget (after local process/version fingerprint checks).
A timeout or identity change is treated as failed discovery, not evidence
that output control is available. A native window's owning PID can be read
through GetWindowThreadProcessId, but this does not constitute process
authentication, nor does a DLL checksum guarantee an unmodified loaded image.

The existing Bluetooth disconnect request, which requires user confirmation,
retains its previous IPC transport until that **state-changing** path has its
own hardware acceptance tests. The new transport is used only for read-only
queries.

The owner observed successful read-only discovery and Bluetooth identity matching
against installed Ryochan7 3.3.3 on 2026-10-26. That dated acceptance does not prove
lighting or physical disconnect. The fake test uses an injected
window handle rather than impersonating an installed DS4Windows executable.
It cannot prove actual real-process compatibility.

## Future physical acceptance gate (separate authorization)

1. Recheck read-only identity/transport matching for the controller session used in the output experiment; the owner's earlier read-only acceptance is not output authorization or LED evidence.
2. Prototype a separately licensed compatible DS4Windows build against a fake physical-output sink without changing the installed version.
3. With explicit authorization, test one static RGB value in a controlled environment; observe physical LEDs and preserve all mappings, gyro, rumble and virtual-controller outputs.
4. Release the lease and verify DS4Windows' current active profile colour returns, including after a profile switch.
5. Force-close the client, disconnect/reconnect and restart DS4Windows; prove lease expiry and stale-token invalidation.
6. Only after physical acceptance connect battery bands, gradient and charging policy. Preserve NSEdge's native standalone path when DS4Windows is absent.

Do not publish an NSEdge v0.3 prerelease on simulation results alone.
