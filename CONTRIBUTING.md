# Contributing

Thanks for helping improve Lua-CSharp. This project is a Lua interpreter and C# interop library for .NET and Unity, so changes should keep correctness, performance, and AOT-friendly behavior in mind.

## Pull Requests

1. Fork the repository and create your branch from `main`.
2. Keep changes focused. Separate unrelated fixes or refactors into separate pull requests.
3. Add or update tests when changing runtime behavior, source generation, standard libraries, or public APIs.
4. Run the relevant test suite locally before opening a pull request:

   ```sh
   dotnet test
   ```

   For focused changes, you can run a narrower filter, for example:

   ```sh
   dotnet test tests/Lua.Tests/Lua.Tests.csproj --filter LuaObjectTests
   ```

5. Update documentation when changing public behavior or user-facing APIs.
6. Address review feedback promptly.

## Bug Fixes

When fixing a bug, please include:

- A short description of the problem.
- A test that fails before the fix and passes after it, when practical.
- Notes about any compatibility or performance impact.

## Issues

Use GitHub issues for bug reports, feature requests, and design discussions. Please include enough detail to reproduce the problem:

- Lua-CSharp version or commit.
- .NET/runtime version.
- Unity version, if applicable.
- Minimal C# and Lua code that demonstrates the issue.
- Expected behavior and actual behavior.

## License

By contributing, you agree that your contributions will be licensed under the MIT license in the root `LICENSE` file.

## Development

The repository uses the .NET SDK and can be built from the repo root.

### Build

```sh
dotnet build Lua.slnx -c Release
```

If you only want to build the NuGet package project:

```sh
dotnet build src/Lua/Lua.csproj -c Release
```

### Run Tests

```sh
dotnet test tests/Lua.Tests/Lua.Tests.csproj -c Release
```

### Inherited Lua Tests

The test project includes a set of `.lua` conformance-style tests inherited from the official Lua repository. Most of them are expected to pass, but a few known cases are currently marked with the `ExpectedFailure` category in the test suite.

To run the test project while excluding those known failures:

```sh
dotnet test tests/Lua.Tests/Lua.Tests.csproj -c Release --filter "TestCategory!=ExpectedFailure"
```

This is the same filter used in GitHub Actions when packing and publishing the NuGet package.

### Pack for NuGet

```sh
dotnet pack src/Lua/Lua.csproj -c Release -o ./artifacts/nuget
```

The package version is defined in `Directory.Build.props`. After packing, the `.nupkg` file will be written to `artifacts/nuget/`.
