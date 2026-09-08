# NetworkStateManager

NSM is a standalone prerelease open-source Unity networking library intended for other developers' games. Breaking APIs and substantial architectural changes are permitted during prerelease development.

Keep game-specific concepts out of NSM.

- Runtime/package code is in `Assets/Runtime`; standalone test coverage is in `Assets/Tests`.
- Preserve asset `.meta` files. Do not edit generated Library, Logs, obj, solution, or csproj files.
- Read `HARDENING.md` for the completed hardening scope and evidence limits; read the README for public API contracts.
- Use `./scripts/codex/unity-test.ps1 editmode -AssemblyNames NetworkStateManager.Tests` for tests. Use PlayMode for physics and actual networking integration.
- Record actual discovered counts and results, including failures. Empty discovery is not a passing suite.
- Keep public migration guidance and standalone verification usable without access to the private consumer.
