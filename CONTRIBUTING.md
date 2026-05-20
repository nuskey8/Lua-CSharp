# Contributing

## Dependencies

The following tools are used for Lua-CSharp development:

- [Visual Studio Code](https://code.visualstudio.com/) / [Rider](https://www.jetbrains.com/rider/)
- [Unity Editor](https://unity3d.com/unity/editor) (optional)
- .NET SDK and runtimes
- [CSharpier](https://csharpier.com/) (optional, C# code formatter)

## Build

```sh
dotnet build Lua.slnx -c Release
```

If you only want to build the NuGet package project:

```sh
dotnet build src/Lua/Lua.csproj -c Release
```

## Run Tests

```sh
dotnet test tests/Lua.Tests/Lua.Tests.csproj -c Release
```

The test project includes a set of `.lua` conformance-style tests inherited from the official Lua repository. Most of them are expected to pass, but a few known cases are currently marked with the `ExpectedFailure` category in the test suite.

To run the test project while excluding those known failures:

```sh
dotnet test tests/Lua.Tests/Lua.Tests.csproj -c Release --filter "TestCategory!=ExpectedFailure"
```

This is the same filter used in GitHub Actions when packing and publishing the NuGet package.

## Unity

The extension package for Unity is located in the `Assets/Lua.Unity` directory within the Unity project, which is located in `src/Lua.Unity`.

The Unity package does not include the source code of the core package. During development, a local package containing the debug DLL is added as `com.nuskey8.lua.unity.internal`.

## Publishing Packages

Publishing to NuGet is handled by a workflow via GitHub Actions. This will run whenever you push a `v.*.*.*` tag.

Before this runs, make sure that the versions of `Directory.Build.props` and the `package.json` file inside your Unity project match. Otherwise, the CI will fail.
