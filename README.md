# NetworkStateManager

A standalone, prerelease open-source Unity library for server-authoritative game state, client prediction, rigidbody snapshots, and rollback. Consumers supply their own game rules, player roster, DTOs, and world restoration callbacks. Breaking API and wire-format changes are expected during prerelease development.

## Requirements and installation

The development project uses Unity **6000.6.0f1**. The runtime package is `Assets/Runtime`, version **0.1.0-preview.1**, and requires NGO **2.13.0 or newer** for server-only RPC invocation permissions. See [package.json](Assets/Runtime/package.json) for dependencies and the project lockfile for the tested environment. Install MemoryPack using the scoped registry/dependency setup in this project's [manifest](Packages/manifest.json).

For a Git UPM installation, use `https://github.com/xaroth8088/NetworkStateManager.git?path=Assets/Runtime` and pin the revision you intend to ship. The tests belong to this standalone project, outside the Runtime package.

## Starting a consumer

1. Put `NetworkObject` and `NetworkStateManager` on a registered NGO prefab or scene object. Start NGO and wait for that object to spawn.
2. Supply struct types implementing `IGameState`, `IPlayerInput`, and `IGameEvent`. Capture all simulation state beyond NSM's rigidbody snapshots, including object identity and lifecycle data.
3. Subscribe capture, restore, input, event, and physics callbacks. Configure limits and the server-owned admission policy before startup.
4. Await `StartNetworkStateManager(typeof(MyState), typeof(MyInput), typeof(MyEvent))` on each peer. The server captures frame zero and supplies the random seed; clients begin simulation when the initial snapshot arrives.

Example policy setup (replace `MyInput` and roster values with your types):

```csharp
nsm.ConfigureInputPolicy(policy =>
{
    policy.AssignPlayer(playerId, owningNgoClientId);
    policy.ValidateValue = (_, raw) => raw is MyInput input
        && !float.IsNaN(input.Axis) && !float.IsInfinity(input.Axis)
        && input.Axis >= -1f && input.Axis <= 1f;
});
await nsm.StartNetworkStateManager(typeof(MyState), typeof(MyInput), typeof(MyEvent));
```

A connection can own several player IDs. Player IDs span all 256 byte values and are independent of NGO object ownership and rigidbody network IDs. Only server code should assign/revoke seats using `InputPolicy`; disconnect removes every mapping belonging to that connection. Missing mappings or a missing/throwing validator reject remote input. Locally collected server inputs are trusted application code and must obey the same game rules.

Do not start the same component twice concurrently. Despawn it before starting a new session; reconnect/recreate the session after a fault. Startup exceptions are propagated through the Awaitable.

## Input trust and wire contract

Only the server may invoke snapshot, event, startup, and forwarded-input RPCs. Remote clients may submit inputs and request a full snapshot. Admission checks the NGO sender against the server roster, exact input type, consumer value validator, message count, tick window, duplicate player/tick pairs, and replay budget before modifying history or forwarding. A mixed invalid batch is rejected as a whole. The first accepted value for a player/tick wins, including across updates.

Accepted ticks are strictly after the retained predecessor/confirmed tick and at most `currentTick + futureInputTicks`. Nonpositive, expired, and excessive future ticks are rejected using overflow-safe comparisons. Invalid requests consume rate budget. `OnInputRejected` reports rejection reasons; keep handlers cheap and avoid logging every hostile packet.

The wire format has a ushort player count (maximum 256), then byte player ID, ushort payload length, and that player's serialized payload. A player payload is limited to **1,024 bytes**, and a packet to **65,536 bytes** including headers. Counts, lengths, duplicate IDs, truncation, and trailing payload bytes are checked. Old 0.0.x peers are incompatible; deploy matching versions on every peer and use NGO connection approval/protocol versioning for your application schema.

The library bounds the bytes handed to each custom input deserializer. Your `NetworkSerialize` implementation must also validate any embedded counts before allocating or looping. A small malicious payload can otherwise ask consumer code to allocate a large collection. Keep input/event structs and their nested data immutable after submission; never mutate objects received in callbacks or input-history reads. Structs containing reference fields are not automatically immutable.

## Frame and rollback callbacks

Normal frames advance `GameTick`, collect local inputs, reset the tick's random seed, apply scheduled events and inputs, invoke pre-physics callbacks, simulate physics, invoke post-physics callbacks, and capture the resulting state and applied event set. Gather device input in the appropriate Input System update and coalesce it for `OnGetInputs`. Simulation logic belongs in NSM's physics callbacks; unrelated Unity FixedUpdate logic is not replayed.

`OnApplyInputs` receives changes present at that tick. Use `PredictInputForPlayer` for omitted players; prediction holds the latest authoritative input, including a baseline retained after pruning. `GetInputsForTick` returns an empty dictionary for unavailable ticks without creating history.

During rollback, NSM restores the exact post-frame game and physics state of the frame being undone, invokes `OnRollbackEvents(events, stateAfterEvent)`, then restores its predecessor. The callback's `GameTick` and RNG seed refer to the frame being undone. Forward replay captures the events actually applied on that replay, so a later rewind undoes the replacement event set. `isReplaying` is true throughout restoration and forward replay and is reset in `finally`, even when a callback throws.

Applying a received snapshot re-establishes that frame's event-driven object lifecycle and then restores its captured state. It does **not** reapply the frame's inputs or simulate physics for that frame. `OnApplyState` must restore/reconstruct game objects and registration before NSM applies their physics snapshot. Snapshot application and rollback callbacks may run repeatedly; keep their side effects reversible.

Use `OnFrameConfirmed(int tick, StateFrameDTO frame)` for irreversible effects such as external achievement writes. It fires once per retained frame leaving the mutable rollback window, with the captured frame rather than the current live world. Server confirmation follows server time; client prediction alone cannot confirm frames. A confirmation watermark is session-local: consumers must provide persistence/idempotency if an external operation can be retried. Ending or faulting a session does not flush its remaining speculative frames as confirmed.

## Retention, budgets, and recovery

Set `simulationLimits` before `ConfigureInputPolicy`/startup. NSM validates and copies settings; subsequent Inspector mutation does not change an active session.

| Setting | Default | Meaning |
|---|---:|---|
| `historyTicks` | 256 | Mutable history window; one predecessor retained |
| `futureInputTicks` | 8 | Maximum accepted input lead |
| `maxInputsPerMessage` | 256 | Player entries per input batch |
| `maxQueuedMessages` | 128 | Total pending RPC jobs |
| `maxMessagesPerUpdate` | 16 | Maximum jobs processed per FixedUpdate |
| `maxMessagesPerClientPerUpdate` | 4 | Per-peer input admission quota and pending request cap |
| `maxReplayTicksPerUpdate` | 1024 | Rewind plus forward work; queue processing reserves four history windows per next job |
| `maxFutureEventTicks` | 4096 | Maximum event scheduling horizon |
| `maxEventsPerTick` | 256 | Events scheduled at one tick |
| `maxBufferedEvents` | 4096 | Total scheduled/retained events |

History can be 2-4096 ticks. Replay budget must cover at least four history windows. Delta cadence must be positive and shorter than history. Tick overflow faults the session. State/input/event pruning happens together; prediction keeps a predecessor input per player, and delta reconstruction uses a separate last-received authoritative snapshot. The server likewise preserves its last transmitted delta base when late inputs rewrite simulated history.

Clients stop prediction at one history window beyond their latest snapshot. Normal client retention is therefore at most two history windows plus a predecessor; the server retains one window plus a predecessor. Future inputs and events add their separately bounded horizons. Missing/out-of-order deltas and prediction exhaustion trigger throttled full-state requests. Queue pressure drops excess jobs; a client requests a baseline to recover.

A fresh snapshot too far ahead for bounded replay requires an `OnHistoryReset(previousConfirmedTick, snapshot)` handler. This callback runs in replay context at the snapshot tick. Rebuild the complete world and object registrations from the snapshot and discard obsolete reversible effects; NSM then restores physics, resets history and prediction baselines, and resumes bounded catch-up. Frames skipped by this reset are **not** individually emitted through `OnFrameConfirmed`; the new baseline seals them against rollback. Without a handler, NSM reports an explicit fault requiring reconnection. A snapshot whose own catch-up exceeds the configured window also faults.

`OnSimulationFault(Exception)` stops further simulation and clears queued work after a runtime callback/reconciliation exception. It is a terminal session signal; NSM cannot undo arbitrary external effects from a throwing consumer callback. Applications should leave the match or reconstruct/reconnect deliberately.

## Object identity and current constraints

Add `NetworkId` to each synchronized rigidbody root. Initial IDs are assigned deterministically from GUIDs within the NSM object's scene, including inactive roots. Register dynamically created objects through `NetworkIdManager.RegisterGameObject`. Automatic allocation supplies IDs **1-254**; zero is a sentinel and 255 is not automatically allocated. Player IDs use a separate space.

NSM currently controls Unity's global physics simulation mode and RNG and stores runtime DTO types in a process-wide registry. Use one active simulation/type schema per process; independently configured parallel worlds are not supported. Match timestep, DTO schema, content, and object ordering across peers. Reconciliation does not make Unity physics bitwise deterministic across machines. Snapshot/event deserializers trust the server and remain application-sensitive; this release is not a general sandbox for arbitrary consumer serialization code.

## Standalone verification

From this repository in PowerShell:

```powershell
./scripts/codex/unity-test.ps1 editmode -AssemblyNames NetworkStateManager.Tests
./scripts/codex/unity-test.ps1 playmode -AssemblyNames NetworkStateManager.PlayModeTests
```

For an already-open Editor running Unity MCP, append `-McpPort <port>` to the same runner. It verifies project identity, rejects compiler errors/empty discovery, and records job results plus source fingerprints under `Logs/codex-tests`. EditMode covers admission, malformed input serialization, long retention, callback ordering/context, changing events, delta bases, and recovery. PlayMode exercises real NGO startup/transport and rigidbody stepping. See [HARDENING.md](HARDENING.md) for actual verification evidence and remaining limits.

Contributions and issue reports are welcome. Keep runtime policy reusable, add regression coverage for behavioral fixes, and keep the public contract independent of any private consumer.
