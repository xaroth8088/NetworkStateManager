# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
