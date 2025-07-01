
# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Lua-CSharp is a high-performance Lua interpreter implemented in C# for .NET and Unity. It provides a Lua 5.2 interpreter with async/await integration, Source Generator support for easy C#-Lua interop, and Unity support.

## Common Development Commands

### Building
```bash
# Build entire solution
dotnet build

# Build in Release mode
dotnet build -c Release

# Build specific project
dotnet build src/Lua/Lua.csproj
```
### Running

Write lua to `sandbox/ConsoleApp1/test.lua`, then:
```bash
# Run simple tests
dotnet run --project sandbox/ConsoleApp1/ConsoleApp1.csproj

# For pattern matching testing, you can run specific Lua scripts like:
# echo 'print(string.gsub("hello", "()l", function(pos) return "[" .. pos .. "]" end))' > sandbox/ConsoleApp1/test.lua
# echo 'print(string.gsub("abc", "", "."))' > sandbox/ConsoleApp1/test.lua  
# echo 'print(string.gsub("(hello) and (world)", "%b()", function(s) return s:upper() end))' > sandbox/ConsoleApp1/test.lua
```


### Testing
```bash
# Run all tests
dotnet test

# Run tests with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run specific test project
dotnet test tests/Lua.Tests/Lua.Tests.csproj
```

### Benchmarking
```bash
# Run performance benchmarks
dotnet run -c Release --project sandbox/Benchmark/Benchmark.csproj
```

### Packaging
```bash
# Create NuGet package
dotnet pack -c Release
```

## Architecture Overview

### Core Components

1. **Lua Runtime (`src/Lua/`)**
   - `LuaState.cs`: Main entry point for Lua execution
   - `LuaValue.cs`: Represents values in Lua (nil, boolean, number, string, table, function, userdata, thread)
   - `Runtime/LuaVirtualMachine.cs`: Core VM implementation that executes Lua bytecode
   - `Runtime/OpCode.cs`, `Runtime/Instruction.cs`: VM instruction definitions
   - `CodeAnalysis/`: Lexer, parser, and compiler that converts Lua source to bytecode

2. **Source Generator (`src/Lua.SourceGenerator/`)**
   - Generates code for classes marked with `[LuaObject]` attribute
   - Enables seamless C#-Lua interop by auto-generating wrapper code
   - Key file: `LuaObjectGenerator.cs`

3. **Standard Libraries (`src/Lua/Standard/`)**
   - Implementations of Lua standard libraries (math, string, table, io, etc.)
   - Entry point: `OpenLibsExtensions.cs`

### Key Design Patterns

1. **Async/Await Integration**
   - All Lua execution methods are async (`DoStringAsync`, `DoFileAsync`)
   - LuaFunction can wrap async C# methods
   - Enables non-blocking execution of Lua scripts

2. **Value Representation**
   - `LuaValue` is a discriminated union struct
   - Implicit conversions between C# and Lua types
   - Zero-allocation for primitive types

3**Memory Management**
   - Heavy use of object pooling (`Pool.cs`, `PooledArray.cs`, `PooledList.cs`)
   - Stack-based value types where possible
   - Careful management of closures and upvalues

### Unity Integration (`src/Lua.Unity/`)

- Custom asset importer for `.lua` files
- Integration with Unity's Resources and Addressables systems
- Works with both Mono and IL2CPP

### Testing Structure

- Unit tests in `tests/Lua.Tests/`
- Lua test suite from official Lua 5.2 in `tests/Lua.Tests/tests-lua/`
- Benchmarks comparing with MoonSharp and NLua in `sandbox/Benchmark/`

## Important Notes

- The project targets .NET Standard 2.1, .NET 6.0, and .NET 8.0
- Uses C# 13 language features
- Heavy use of unsafe code for performance
- Strings are UTF-16 (differs from standard Lua)

## TODO

- **ILuaStream Interface Changes**: The ILuaStream interface has been updated with new methods:
  - Added `IsOpen` property to track stream state
  - Added `ReadNumberAsync()` for reading numeric values (supports formats like "6.0", "-3.23", "15e12", hex numbers)
  - Changed `ReadLineAsync()` to accept a `keepEol` parameter for controlling line ending behavior
  - Renamed `ReadStringAsync()` to `ReadAsync()`
  - Added `CloseAsync()` method for async stream closing
  - ✅ Implemented `ReadNumberAsync()` in all implementations
  - Need to properly implement the `keepEol` parameter in `ReadLineAsync()` for TextLuaStream