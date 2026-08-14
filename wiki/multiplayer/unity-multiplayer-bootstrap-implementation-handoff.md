# Unity Multiplayer Bootstrap Implementation Handoff

> Sources: Project design conversation, 2026-08-14; Unity Multiplayer Services SDK 2.3.0 local package source, 2026-08-14
> Raw: [Multiplayer Bootstrap Handoff Decisions](../../raw/multiplayer/2026-08-14-multiplayer-bootstrap-handoff-decisions.md)

## Overview

This document is the implementation contract for the first multiplayer milestone of the FPSbasic Unity project. Implement a small, production-shaped bootstrap layer that proves UGS anonymous authentication, Multiplayer Services Session creation, join-by-code, Relay-backed NGO connection, replicated room state, graceful leave, and unexpected disconnect cleanup. Verification is console-log based; gameplay networking, teams, Ready UI, PlayerSessionObject, PlayerAvatar, combat, and scene transitions are intentionally deferred.

## Project Facts

Project root: `C:/UnityProject/FPSbasic`

Confirmed environment:

| Item | Version/status |
|---|---|
| Unity | 6000.3.18f1 |
| Multiplayer Services | 2.3.0 |
| Netcode for GameObjects | 2.13.1 |
| Unity Transport | 2.7.3, transitive |
| Authentication | 3.7.0, transitive |
| WebGL Build Support | Installed |

Do not add standalone Relay, Lobby, Transport, or Authentication packages. Multiplayer Services already resolves the required dependencies.

## Scope Decision

The one-week MVP supports:

| Mode | Team A capacity | Team B capacity | Session MaxPlayers |
|---|---:|---:|---:|
| OneVsOne | 1 | 1 | 2 |
| TwoVsTwo | 2 | 2 | 4 |

The host selects the mode before creating the Session. Do not make every Session a four-player Session. That design introduces spectators, late join, extra-player eviction, and start/lock races that are outside this milestone.

Model mode capacity through one mapping function or immutable configuration so `OneVsTwo = (1, 2, 3)` can be added later without restructuring the system. Do not implement OneVsTwo now.

## Non-Goals

Do not implement the following in this milestone:

- FPS player prefab networking
- PlayerSessionObject or PlayerAvatar
- Team selection or Ready UI
- Match scene loading
- Health, shooting, kills, gold, monsters, shop, respawn, or score
- Host migration or reconnection recovery
- Spectators or late join during gameplay
- Matchmaker, public lobby browsing, passwords, or dedicated servers
- Changes to existing `Assets/FPS` gameplay code

## Architectural Rules

1. Use `Unity.Services.Multiplayer.MultiplayerService` Sessions API.
2. Use `SessionOptions.WithRelayNetwork()`.
3. Do not also call `NetworkManager.StartHost()` or `StartClient()`; the Sessions network module owns startup.
4. The Session service is the control plane: membership, code, capacity, lock, and low-frequency metadata.
5. NGO is the real-time data plane: NetworkObjects, NetworkVariables, RPCs, and connection callbacks.
6. No UI class may call UGS, Sessions, or NetworkManager APIs directly.
7. All async public operations must be single-flight: repeated Create/Join/Leave requests while one is in progress are rejected or reuse the current task.
8. Always unsubscribe SDK and NGO event handlers in teardown/OnDestroy.
9. Do not use `Task.Run`, blocking waits, or thread-dependent logic because the target is Web.
10. Expected failures should return typed results and useful log messages rather than relying only on thrown exceptions.

## Bootstrap GameObjects

Create one bootstrap root in the initial scene. Suggested hierarchy:

```text
MultiplayerBootstrap
|- NetworkManager
|  `- UnityTransport
|- UgsBootstrap
|- SessionFacade
|- NetworkLifecycle
|- MultiplayerFlowController
`- MultiplayerDebugHarness
```

The `NetworkManager` may be the same GameObject as `MultiplayerBootstrap` if preferred. Keep the component responsibilities unchanged. Preserve this root across future scene changes. Enable NGO scene management even though scene switching is not part of this milestone.

Create a registered network prefab:

```text
RoomNetworkState
|- NetworkObject
`- RoomNetworkState
```

Register this prefab in the NetworkManager Network Prefabs list. The host spawns it after the Session/NGO host connection is ready. Clients must receive it through NGO and must never instantiate or spawn their own copy.

## Shared Domain Types

Create small, dependency-free types under a multiplayer namespace such as `FPSBasic.Multiplayer`.

```csharp
public enum MatchMode : byte
{
    OneVsOne,
    TwoVsTwo
}

public enum RoomPhase : byte
{
    Waiting,
    Starting,
    InGame,
    Finished
}

public enum SessionRole : byte
{
    None,
    Host,
    Client
}

public enum SessionOperationState : byte
{
    Idle,
    Creating,
    Joining,
    InSession,
    Leaving
}

public enum MultiplayerFlowState : byte
{
    Offline,
    Initializing,
    Menu,
    CreatingRoom,
    JoiningRoom,
    InRoom,
    LeavingRoom,
    Error
}
```

Provide a pure mapping for capacities:

```csharp
public readonly struct MatchModeRules
{
    public int TeamACapacity { get; }
    public int TeamBCapacity { get; }
    public int MaxPlayers => TeamACapacity + TeamBCapacity;

    public static MatchModeRules For(MatchMode mode);
}
```

`For` must return `(1,1)` for OneVsOne and `(2,2)` for TwoVsTwo and throw for unknown enum values. Do not scatter mode switches across SessionFacade and RoomNetworkState.

## Class Contract: UgsBootstrap

Purpose: idempotently initialize Unity Services and anonymous Authentication.

Suggested public contract:

```csharp
public enum UgsInitializationState : byte
{
    Uninitialized,
    Initializing,
    Ready,
    Failed
}

public sealed class UgsBootstrap : MonoBehaviour
{
    public UgsInitializationState State { get; }
    public bool IsReady { get; }
    public string PlayerId { get; }
    public string LastError { get; }

    public Task EnsureReadyAsync();
}
```

Responsibilities:

- Call `UnityServices.InitializeAsync()` once.
- If `AuthenticationService.Instance.IsSignedIn` is false, call `SignInAnonymouslyAsync()`.
- Reuse the same in-progress initialization task when multiple callers invoke `EnsureReadyAsync()`.
- Set `Ready` only after both initialization and authentication succeed.
- Preserve Authentication after Session/Relay/NGO disconnects.
- Log initialization start, successful PlayerId, and failure details.

Must not:

- Create, join, leave, or delete Sessions.
- Start or stop NGO.
- React to room kick/disconnect by reinitializing UGS.
- Manage room state, scenes, teams, or UI.

## Class Contract: SessionFacade

Purpose: provide the only project-facing wrapper around the Multiplayer Services Sessions API.

Suggested public contract:

```csharp
public sealed class SessionFacade : MonoBehaviour
{
    public SessionRole Role { get; }
    public SessionOperationState State { get; }
    public bool HasSession { get; }
    public bool IsHost { get; }
    public string SessionId { get; }
    public string JoinCode { get; }
    public string LastError { get; }

    public event Action SessionChanged;
    public event Action<string> OperationFailed;

    public Task<SessionOperationResult> CreateSessionAsync(MatchMode mode);
    public Task<SessionOperationResult> JoinSessionByCodeAsync(string joinCode);
    public Task<SessionOperationResult> LockSessionAsync();
    public Task<SessionOperationResult> LeaveSessionAsync();
}
```

Dependencies:

- Serialized/reference dependency on `UgsBootstrap`.
- It may read NetworkManager state for diagnostics but must not directly start Host/Client when using Sessions networking.

Host create flow:

```csharp
await ugsBootstrap.EnsureReadyAsync();
var rules = MatchModeRules.For(mode);

var options = new SessionOptions
{
    Name = $"fps-{Guid.NewGuid():N}",
    MaxPlayers = rules.MaxPlayers,
    IsPrivate = true,
    IsLocked = false,
    SessionProperties = /* mode and build version */
}.WithRelayNetwork();

IHostSession hostSession =
    await MultiplayerService.Instance.CreateSessionAsync(options);
```

Store the returned handle as the current Session, set `Role = Host`, set `State = InSession`, and expose `hostSession.Code`. Session properties should include at least:

- `mode`: stable string such as `1v1` or `2v2`
- `build`: `Application.version`

Client join flow:

```csharp
await ugsBootstrap.EnsureReadyAsync();
string normalized = joinCode?.Trim().ToUpperInvariant();
ISession session = await MultiplayerService.Instance
    .JoinSessionByCodeAsync(normalized);
```

Reject an empty code before calling the SDK. Store the returned Session, set `Role = Client`, and set `State = InSession`.

Lock flow, Host only:

```csharp
IHostSession host = currentSession.AsHost();
host.IsLocked = true;
await host.SavePropertiesAsync();
```

Leave flow:

- Client calls `currentSession.LeaveAsync()`.
- Host calls `currentSession.AsHost().DeleteAsync()` so the MVP room deterministically ends.
- In `finally`, unsubscribe Session events and clear local references/role/state.
- Let the Sessions network module perform normal network teardown. Add a narrowly scoped NGO shutdown fallback only for a failed/half-open operation, not as a second normal owner of teardown.

SessionFacade should translate common SDK failures into a small project error enum or result message, including:

- invalid/empty code
- Session not found
- Session locked
- Session full
- rate limited
- already in a Session or operation already running
- authentication/service failure
- unknown failure with original exception logged

Must not:

- Implement team/Ready rules.
- Spawn RoomNetworkState.
- Load scenes.
- Store combat or real-time player state.
- Expose mutable Session handles to UI classes.

## Class Contract: NetworkLifecycle

Purpose: observe NGO lifecycle and publish neutral connection events.

Suggested contract:

```csharp
public sealed class NetworkLifecycle : MonoBehaviour
{
    public bool IsListening { get; }
    public bool IsLocalClientConnected { get; }

    public event Action<ulong> ClientConnected;
    public event Action<ulong, string> ClientDisconnected;
    public event Action<string> LocalClientDisconnected;
}
```

Responsibilities:

- Subscribe to `NetworkManager.OnClientConnectedCallback`.
- Subscribe to `NetworkManager.OnClientDisconnectCallback`.
- Distinguish the local Client ID from remote Client IDs.
- Include `NetworkManager.DisconnectReason` where applicable.
- Produce clear log messages for host/client and local/remote events.
- Subscribe once and unsubscribe deterministically.

Must not:

- Call Session APIs.
- Automatically reconnect.
- Initialize UGS.
- Load the menu or destroy Session state.

## Class Contract: MultiplayerFlowController

Purpose: orchestrate user intent across UgsBootstrap, SessionFacade, NetworkLifecycle, and RoomNetworkState spawning.

Suggested contract:

```csharp
public sealed class MultiplayerFlowController : MonoBehaviour
{
    public MultiplayerFlowState State { get; }
    public string LastError { get; }

    public event Action<MultiplayerFlowState> StateChanged;

    public Task CreateRoomAsync(MatchMode mode);
    public Task JoinRoomAsync(string code);
    public Task LeaveRoomAsync();
}
```

Create flow:

1. Reject if another flow operation is active.
2. Set state to `Initializing` and await UGS readiness.
3. Set state to `CreatingRoom` and call SessionFacade create.
4. Wait for/verify NGO local host connection.
5. On the server, instantiate and `Spawn()` the RoomNetworkState prefab.
6. Initialize its mode and phase through RoomAuthority.
7. Set state to `InRoom` and log Session ID and join code.

Join flow:

1. Reject if another flow operation is active.
2. Ensure UGS readiness.
3. Set state to `JoiningRoom` and call SessionFacade join.
4. Wait for/verify NGO local client connection.
5. Wait for the host-spawned RoomNetworkState `OnNetworkSpawn()` event.
6. Set state to `InRoom` and log synchronized values.

Leave flow:

1. Set `LeavingRoom`.
2. Call SessionFacade leave/delete according to role.
3. Clear local RoomNetworkState references.
4. Return to `Menu`; leave Authentication intact.

Unexpected local disconnect flow:

1. NetworkLifecycle reports local disconnect.
2. Ignore it if an expected Leave flow is already running.
3. Otherwise ask SessionFacade to clean up any remaining Session membership/reference.
4. Clear room state and transition to `Menu` or `Error` with a readable reason.

Must not contain direct MultiplayerService calls or business rules for team capacities.

## Class Contract: RoomNetworkState

Purpose: hold server-written, NGO-replicated room state.

Suggested contract:

```csharp
public sealed class RoomNetworkState : NetworkBehaviour
{
    public NetworkVariable<MatchMode> Mode { get; } =
        new(default, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    public NetworkVariable<RoomPhase> Phase { get; } =
        new(RoomPhase.Waiting, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn();
}
```

Responsibilities in this milestone:

- Be spawned only by the host/server.
- Allow everyone to read and only the server to write.
- Subscribe to `Mode.OnValueChanged` and `Phase.OnValueChanged` in `OnNetworkSpawn`.
- Unsubscribe in `OnNetworkDespawn`.
- Log initial and changed values on host and clients.
- Offer server-only initialization methods rather than public unrestricted setters.

Must not:

- Call UGS or Session APIs.
- Store join codes, Session IDs, or authentication tokens.
- Implement team selection, Ready, player spawn, or gameplay.

## Class Contract: RoomAuthority

Purpose: own host-only room rules and initialize RoomNetworkState.

For this milestone it may be intentionally small:

```csharp
public sealed class RoomAuthority : NetworkBehaviour
{
    public void InitializeRoom(MatchMode mode);
}
```

`InitializeRoom` must reject non-server callers, set the selected mode, and set phase to `Waiting`. It may live on the same prefab as RoomNetworkState.

Future responsibilities, not implemented now:

- Team change validation
- Ready validation
- Team capacity checks
- Start-condition checks
- Calling SessionFacade lock through an orchestration interface
- Moving phase from Waiting to Starting

RoomAuthority must not directly use `MultiplayerService.Instance`; external service calls stay in SessionFacade.

## Result Types

Avoid returning only `bool`. Provide enough information for logs and later UI:

```csharp
public enum SessionFailureReason : byte
{
    None,
    Busy,
    InvalidCode,
    NotFound,
    Full,
    Locked,
    RateLimited,
    AuthenticationFailed,
    ServiceError,
    Unknown
}

public readonly struct SessionOperationResult
{
    public bool Success { get; }
    public SessionFailureReason FailureReason { get; }
    public string Message { get; }
}
```

Keep the original exception in logs, but do not expose raw stack traces as future player-facing messages.

## Debug Harness

Create a temporary `MultiplayerDebugHarness` MonoBehaviour to exercise the system without final UI. It may use context-menu methods, inspector fields, or minimal development-only buttons.

Required operations:

- Create OneVsOne room
- Create TwoVsTwo room
- Join using an inspector-provided code
- Leave current room
- Print current UGS, Session, NGO, and Room state

The harness may call only MultiplayerFlowController. It must not call UGS, SessionFacade, or NetworkManager directly.

## Expected Logs

Host success:

```text
[UGS] Initialization started
[UGS] Anonymous sign-in succeeded. PlayerId=<id>
[Session] Creating private OneVsOne session. MaxPlayers=2
[Session] Host session created. SessionId=<id> Code=<code>
[NGO] Local host connected. ClientId=0
[Room] RoomNetworkState spawned by server
[Room] Initialized. Mode=OneVsOne Phase=Waiting
[Flow] State=InRoom
```

Client success:

```text
[UGS] Anonymous sign-in succeeded. PlayerId=<different-id>
[Session] Joining by code=<normalized-code>
[Session] Join succeeded. SessionId=<id>
[NGO] Local client connected. ClientId=<id>
[Room] RoomNetworkState received. Mode=OneVsOne Phase=Waiting
[Flow] State=InRoom
```

Host exit:

```text
[Session] Host deleting session
[NGO] Local client disconnected
[Flow] Session cleanup complete. State=Menu
```

Remaining clients must log the disconnect reason and return to Menu without re-running UnityServices initialization.

## Acceptance Criteria

Implementation is complete only when all of these pass:

1. Project compiles with no new warnings attributable to the implementation.
2. Repeated `EnsureReadyAsync()` calls perform only one initialization/sign-in operation.
3. Host creates a private OneVsOne Session with MaxPlayers 2 and receives a join code.
4. A second authenticated player joins by code.
5. Host and client receive NGO local connection callbacks.
6. Host spawns exactly one RoomNetworkState.
7. Client receives RoomNetworkState and logs identical Mode and Phase.
8. A third player cannot join the OneVsOne Session because it is full.
9. TwoVsTwo creation uses MaxPlayers 4.
10. Client Leave removes its membership and returns its flow state to Menu.
11. Host Leave/Delete terminates the room; connected clients handle disconnect and return to Menu.
12. Disconnect cleanup does not sign the player out or initialize UGS a second time.
13. Create/Join button spam cannot launch overlapping SDK requests.
14. Invalid, missing, full, and locked Session failures produce distinguishable result messages.
15. No existing FPS gameplay scripts are modified.

## Implementation Order

Implement and verify in this order:

1. Shared enums, MatchModeRules, and result types
2. UgsBootstrap with idempotency tests/logs
3. SessionFacade create/join/leave/delete and error mapping
4. NetworkLifecycle callback observation
5. RoomNetworkState prefab and server-only initialization
6. MultiplayerFlowController orchestration
7. MultiplayerDebugHarness
8. Two-player Multiplayer Play Mode test
9. Capacity and leave/disconnect tests
10. Web build versus Editor join-code smoke test

## Instructions for the Terra Implementation Chat

Work directly in `C:/UnityProject/FPSbasic` and implement this milestone. Before editing, inspect existing project conventions and any `AGENTS.md`. Preserve unrelated user changes. Use the installed Multiplayer Services 2.3.0 and NGO 2.13.1 APIs rather than older standalone Relay/Lobby tutorials. Do not add packages unless compilation proves a missing dependency. Create scripts in a new multiplayer-specific folder and avoid editing `Assets/FPS` gameplay scripts.

After implementation:

- Refresh Unity and wait for compilation.
- Report all created/modified files.
- Run proportionate EditMode tests for pure mode/result logic if practical.
- Verify Editor host/client behavior through Multiplayer Play Mode or equivalent instances.
- Include console evidence for create, join, replicated RoomNetworkState, client leave, and host termination.
- Stop if Unity Dashboard project linkage or service configuration requires user credentials or an external decision; report the exact blocker rather than inventing configuration.

## Future Extension Boundary

The next milestone should add PlayerSessionObject, team/Ready synchronization, and Room start locking without changing UgsBootstrap. SessionFacade should only gain narrowly scoped host property/lock operations. RoomNetworkState should expand with room data, while PlayerSessionObject becomes the persistent per-player data owner. PlayerAvatar remains a separate per-life in-game object.
