# Contributing

Bug reports and pull requests are welcome.

Before changing the runtime, read [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md). Changes to Sledders bindings should be tested in game; reflection results alone are not enough to call a binding stable.

Before opening a pull request:

1. Build with `pwsh ./scripts/build.ps1`.
2. Run the Lua syntax checker against `examples` and `tests/manual`.
3. Build the docs with `pwsh ./scripts/serve-docs.ps1` or let CI do it.
4. Update `docs/API.api` if the public Lua API changed.
5. Add or update an example when a new user-facing feature needs one.

Do not commit game assemblies, dependency DLLs, build output, logs, local game paths, or release archives.
