# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.0.5] - 2023-06-22

### Changed

#### Breaking Changes
- Package installation now happens via OpenUPM, not installing a .unitypackage
- The package name has been updated, to allow compatibility with OpenUPM.  If you were using a previous version of this library, you'll need to remove it before installing the new version.

- Substantial refactoring of how game state is stored and transmitted.  This includes using [MemoryPack](https://github.com/Cysharp/MemoryPack) to do binary serialization of all NSM-related game state.
- Network traffic is reduced by ~84% in my testing, courtesy of more efficient serialization, binary diffing, and compression
- Inputs are now managed outside the gamestate frames, because it was proving more trouble than it's worth to manage those things together
- Moved NetworkID-related logging to use NSM's verbose logger
- Moved NetworkID initialization into `NetworkIdManager`
- Take full control over random number generation, as external sources kept interfering with Unity's Random
  - This means that you MUST use NSM for synchronization on anything random
  - If it's not important for synchronization (e.g. "randomly shake the camera"), then you should probably still use Unity's RNG
- When state inconsistency is detected, the client will request a full state frame from the server, which greatly improves network reliability on spotty connections
- The `RollbackEvents` event now also passes the `IGameState` object for the frame _after_ the rolled back state, in case it's needed to properly roll back an event
- Internal simulation functions are named more descriptively, hopefully making it easier to understand what the library actually does.
- Verbose logging now includes some stack trace info, to make it easier to understand what's happening from only the log messages

### Added
- Interface requirements for IGameState have changed to now require `GetBinaryRepresentation()` and `RestoreFromBinaryRepresentation()` functions.
  - On the upside, you no longer have to implement `INetworkSerializable`, so there's that.
- Optionally send full state frames periodically
- `RemoveEventAtTick()`, for de-scheduling an event.  Usually this only really comes up when rolling back an event that itself schedules a future event.
- `PredictInputForPlayer()` function, which will predict an `IPlayerInput` for a given player id.
  - This is needed right now to handle the case where not all inputs are present for a frame.  In the future, the plan is to change things to make this unnecessary.

### Removed
- Debug rollback options.  These were really only intended to be used when debugging NSM itself, and are no longer needed.

## [0.0.4] - 2023-03-24
### Changed
- Major refactoring of how game events work internally.  They should be substantially more reliable and predictable now.
- NetworkID management moved to `NetworkIdManager` class, accessible through `networkStateManager.networkIdManager`
- Refactored allocation of network id's, so that they can be more easily reused (and play nicer with rollback scenarios)
- Fixed some bugs in state DTOs where `get` accessors could return an object that wasn't actually used internally
- Rigidbody sleep/wake state are now sync'd (note: this may be removed later, as it's unclear if it's strictly required)
- When StateBuffer is asked for a non-existent frame, it returns the last known state before that frame instead of a blank frame
- Some event delgates have changed their signatures
- Lifecycle events asking NSM for `gameTick` will get the tick associated with the frame in progress, which may be different from the "real" game tick
- Misc code cleanup and organization improvements

### Added
- `Hello, NetworkStateManager` demo project added.
- `OnRollbackEvents` lifecycle event
- Unity's Random number generator is now determistically synchronized
- Various toggles for debugging more complex rollback scenarios
- `networkStateManager.isReplaying` can be used to determine whether a lifecycle event is happening during normal playback or a replay scenario
- Truly verbose verbose logging.  Truly.

## [0.0.3] - 2022-12-10
### Changed
- Fixed problem where `IGameEvent` was used in an RPC, which could cause compilation errors under some circumstances

## [0.0.2] - 2022-10-19
### Added
- Unity package manager support
- `IGameState`, `IGameEvent`, and `IPlayerInput` interfaces, for use in your custom game state objects.

### Changed
- When starting `NetworkStateManager`, you now pass in the `Type`s for your custom game state objects.

### Removed
- Hacky `partial struct` implementation, replaced with somewhat _less_ hacky reflection-based approach.

## [0.0.1] - 2022-07-07
### Added
- Initial version.
