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
