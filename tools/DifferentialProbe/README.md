# Lua 5.2 differential checks

Compare deterministic edge cases in fresh Lua-CSharp and native Lua 5.2 states
through NuLua.Lua52 0.1.0. Run from this directory to select the .NET 10 SDK:

```powershell
dotnet restore
dotnet run -c Release -- ../../artifacts/differential/all.jsonl
dotnet run -c Release -- ../../artifacts/differential/compiler.jsonl compiler
dotnet run -c Release -- ../../artifacts/differential/patterns.jsonl pattern
dotnet run -c Release -- ../../artifacts/differential/characters.jsonl format.char utf16
```

Arguments are output path, optional family prefix, and optional `bytes`/`utf16`
mode (default `bytes`). The output directory is created automatically. Exit
codes: 0 means every case agrees; 1 means differences were recorded; 2 means
invalid arguments or no matching family. Each JSONL row retains the source,
case/family, mode, both results and equality flag. Differences are not filtered.

The 14,176 cases cover string slices/searches/quoting, escapes, numerals,
numeric coercion/base conversion/text, math, bit32, patterns and replacements,
table ranges, operators/metamethods/coroutines, closures, varargs, assignment
conflicts, callback results, 3,240 finite printf combinations and 1,000 seeded
expression trees. Numeric replacement cases check direct, callback and table
forms; numeric-input base cases expose number-to-text coercion before parsing.

## Interpreting differences

Current main on Windows x64 records 58 byte-mode differences: 53 retained
managed numeric-text/coercion results, three non-finite `frexp` exponents and
two one-ULP `deg` results. These are visible policy/platform differences, not
new crash or state-corruption findings. The program still returns 1 for them.
The exact count can differ with the native C library. UTF-16 mode adds character
range differences because a .NET character is not a Lua byte. Use an agreeing
family for a focused check, and inspect full results when changing semantics.

Counts, types and primitive values compare exactly, including signed zero;
NaNs compare by numeric category. Strings are encoded as numeric byte/code-unit
lists **inside Lua**, preserving NUL and undecodable bytes before NuLua marshals
them. This avoids the pinned package's Windows string-decoding behavior without
discarding binary data. Exact C NaN payload spellings, undefined integer casts,
huge allocation requests, random sequences and ambiguous table-hole lengths
are outside this corpus.

Runtime calls use `pcall`; error wording and tracebacks are retained only for
top-level failures and are not compared. Unexpected host exception types do not
match native Lua errors. This is a finite behavioral corpus, not proof of full
language or diagnostic compatibility.

To run the same corpus against a frozen engine DLL, build with
`-p:LuaAssemblyPath=C:/absolute/path/Lua.dll`. This changes only the engine
reference; keep the probe source and native package version identical.
