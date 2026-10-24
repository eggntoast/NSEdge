# Optional DS4Windows lighting integration — engineering contract

Status: design and simulation only. No DS4Windows changes, no physical HID output, no changeledcolor commands, and no new controller permissions. Source-only groundwork for NSEdge v0.3.

## Current baseline

NSEdge must work without DS4Windows. While another controller manager owns output, the existing native lighting guard remains intact. Ryochan7 3.3.3 supports read-only Query slot properties and changeledcolor, but the latter only updates an internal custom colour and does not provide mode acquisition, crash-safe leases or profile restoration.

The read-only compatibility probe:
- matches the WM_COPYDATA IPC window to its owning PID and executable, checks the running image version and the known 3.3.3 DLL SHA-256;
- sends only stock Query messages for MacAddress, ConnType and ProfileName;
- matches NSEdge's normalized physical controller identity and transport, rejects duplicate addresses, and rechecks the selected slot and host process;
- identifies the DS4Windows **software profile**, not the Sony onboard profile;
- is advisory and never grants permission for physical output.

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

SimulatedLightingLeaseHost and SimulatedLightbarSink are pure in-memory types. They have no DS4Windows IPC, HID, file, process or physical-output functionality. The host relies on an explicit Expire() call; it does not run a Windows timer or handle a real crash.

Regression tests cover exclusive ownership, invalid identity and transport matching, wrong tokens, expiry, renewal, latest-profile restoration, disconnect/reconnect generation checks and stale-token rejection.

These tests do not verify actual colour changes, physical HID output, profile persistence, rumble, gyro, Windows IPC behaviour or real crash recovery.

## Future physical acceptance gate (separate authorization)

1. Verify the read-only process/identity/profile probe against installed 3.3.3. Capture recognized fingerprint, PID, matching slot, transport and software profile; no lighting commands.
2. Prototype a separately licensed compatible DS4Windows build against a fake physical-output sink without changing the installed version.
3. With explicit authorization, test one static RGB value in a controlled environment; observe physical LEDs and preserve all mappings, gyro, rumble and virtual-controller outputs.
4. Release the lease and verify DS4Windows' current active profile colour returns, including after a profile switch.
5. Force-close the client, disconnect/reconnect and restart DS4Windows; prove lease expiry and stale-token invalidation.
6. Only after physical acceptance connect battery bands, gradient and charging policy. Preserve NSEdge's native standalone path when DS4Windows is absent.

Do not publish an NSEdge v0.3 prerelease on simulation results alone.
