# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
