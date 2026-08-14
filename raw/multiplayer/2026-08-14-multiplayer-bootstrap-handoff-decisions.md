# Multiplayer Bootstrap Handoff Decisions

> Source: Project design conversation between the user and Codex
> Collected: 2026-08-14
> Published: Unknown

The project is a Unity 6 FPS Microgame-based Web build intended for Unity Play. Multiplayer uses a client-hosted Host/Client topology with Unity Relay. The target game eventually supports team FPS combat, but the immediate task is only to prove room creation, join-by-code, Relay/NGO connectivity, replicated room state, leaving, and disconnect cleanup through logs.

Installed and confirmed packages:

- Unity Editor 6000.3.18f1
- com.unity.services.multiplayer 2.3.0
- com.unity.netcode.gameobjects 2.13.1
- com.unity.transport 2.7.3 as a transitive dependency
- com.unity.services.authentication 3.7.0 as a transitive dependency
- WebGL Build Support is installed

Decisions:

1. Use the Multiplayer Services Sessions API instead of directly coordinating standalone Lobby and Relay APIs.
2. Use `SessionOptions.WithRelayNetwork()` and do not separately call `NetworkManager.StartHost()` or `NetworkManager.StartClient()`.
3. The host creates the session. The client joins using `MultiplayerService.Instance.JoinSessionByCodeAsync(joinCode)`.
4. For the one-week MVP, support only 1:1 and 2:2. Represent the mode in a data-oriented way so 1:2 can be added later.
5. The mode is selected before session creation. A 1:1 session has `MaxPlayers = 2`; a 2:2 session has `MaxPlayers = 4`. The host is included in this number.
6. Do not create every session with four slots. Doing so would require spectators, late-join policy, kicking, and race handling for extra users during 1:1 matches.
7. Team selection may be added in the room. It is allowed only while waiting, cannot exceed team capacity, and changing team clears Ready. Atomic side swapping is not part of the MVP.
8. Lock the Session before starting gameplay by setting `IHostSession.IsLocked = true` and saving properties.
9. No host migration in the MVP. If the host leaves or disconnects, the room ends and clients return to the menu.
10. UGS authentication remains active after a room disconnect. A network disconnect does not cause UGS to initialize or authenticate again unless authentication itself is no longer valid.
11. Session service data is the low-frequency control plane. NGO NetworkVariables and NetworkObjects are the real-time game data plane.
12. `RoomNetworkState` initially synchronizes only `MatchMode` and `RoomPhase`. PlayerSessionObject and PlayerAvatar are deferred until the in-game implementation.
13. Initial verification is log-only. The host creates a room and prints a join code; a client joins; both observe NGO connection callbacks; the client receives the spawned RoomNetworkState and logs the same mode and phase.
14. Bootstrap responsibilities must remain separated. UgsBootstrap performs UGS/Auth initialization only. SessionFacade wraps session operations. NetworkLifecycle observes NGO callbacks. MultiplayerFlowController orchestrates user intents and cleanup. RoomNetworkState stores replicated room data. RoomAuthority owns host-only room rules.

Implementation should avoid modifying the existing FPS gameplay scripts during this milestone.
