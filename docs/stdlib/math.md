# Mathematics

[Reference index](README.md) · [Official Lua 5.2 math library](https://www.lua.org/manual/5.2/manual.html#6.6) · [Implementation](../../src/Lua/Standard/MathematicsLibrary.cs)

Install with `state.OpenMathLibrary()`. Numbers are .NET doubles. Most operations call `System.Math`; decomposition/scaling use managed helpers. Transcendental rounding and exceptional-value details can vary with the .NET runtime/platform, just as C math libraries vary between Lua builds. No C math library or mutable C locale is used.

Angles are radians unless an API explicitly converts degrees. The [numeric text policy](README.md#numbers-and-text) affects printing results, not the calculations.

| API / official reference | Inputs and result | Lua-CSharp differences and reasons |
| --- | --- | --- |
| [`math.abs(x)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.abs) | Absolute value. | `Math.Abs`. |
| [`math.acos(x)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.acos) | Inverse cosine. | `Math.Acos`; managed floating-point behavior applies. |
| [`math.asin(x)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.asin) | Inverse sine. | `Math.Asin`. |
| [`math.atan(x)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.atan) | Inverse tangent. | One argument, as in Lua 5.2; use `atan2` for two coordinates. |
| [`math.atan2(y, x)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.atan2) | Quadrant-aware inverse tangent for `(y, x)`. | `Math.Atan2`. |
| [`math.ceil(x)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.ceil) | Smallest integral double at least `x`. | |
| [`math.cos(x)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.cos) | Cosine. | `Math.Cos`. |
| [`math.cosh(x)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.cosh) | Hyperbolic cosine. | `Math.Cosh`. |
| [`math.deg(x)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.deg) | Radians to degrees. | Multiplies by `180 / Math.PI`. |
| [`math.exp(x)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.exp) | Exponential with base e. | `Math.Exp`. |
| [`math.floor(x)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.floor) | Largest integral double at most `x`. | |
| [`math.fmod(x, y)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.fmod) | Remainder using a quotient truncated toward zero. | Uses the C# double remainder operator. This differs from Lua's `%` operator for some negative inputs, as `fmod` should. |
| [`math.frexp(x)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.frexp) | Returns mantissa and binary exponent, including subnormal decomposition. | Implemented by a managed double-bit helper rather than C `frexp`. |
| [`math.huge`](https://www.lua.org/manual/5.2/manual.html#pdf-math.huge) | Positive infinity. | Backed by `double.PositiveInfinity`. |
| [`math.ldexp(m, e)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.ldexp) | Scales `m` by a power of two; exponent is converted to an integer. | Uses `Math.ScaleB` on newer targets and a managed fallback on .NET Standard 2.1. |
| [`math.log(x [, base])`](https://www.lua.org/manual/5.2/manual.html#pdf-math.log) | Natural log, or log in the specified base. | Uses `Math.Log`, a base-10 specialization, or the ratio of natural logs. |
| [`math.max(x, ...)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.max) | Maximum of at least one numeric argument. | Uses ordered comparisons, preserving Lua 5.2's argument-order behavior for NaN/equal values. |
| [`math.min(x, ...)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.min) | Minimum of at least one numeric argument. | Uses ordered comparisons rather than a different NaN-selecting aggregate. |
| [`math.modf(x)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.modf) | Returns integral and fractional parts. | Managed helper preserves signed zero and handles infinities. |
| [`math.pi`](https://www.lua.org/manual/5.2/manual.html#pdf-math.pi) | The double approximation of pi. | Backed by `Math.PI`. |
| [`math.pow(x, y)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.pow) | `x` raised to `y`. | `Math.Pow`. |
| [`math.rad(x)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.rad) | Degrees to radians. | Multiplies by `Math.PI / 180`. |
| [`math.random([m [, n]])`](https://www.lua.org/manual/5.2/manual.html#pdf-math.random) | Without bounds, a value in `[0,1)`. With bounds, uses Lua 5.2's floor-based interval formula. | Uses `System.Random`; sequences differ from C Lua's C-runtime generator. For fractional bounds, the Lua 5.2 formula is retained; integer-only checks from newer Lua versions are not imposed. The generator is stored in the shared global environment, so coroutines in that state share it. |
| [`math.randomseed(seed)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.randomseed) | Replaces the generator using the converted seed; returns no values. | Uses a 32-bit seed and `System.Random`. Repeatability is tied to the .NET generator/runtime, not C Lua's `srand`/`rand` sequence. |
| [`math.sin(x)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.sin) | Sine. | `Math.Sin`. |
| [`math.sinh(x)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.sinh) | Hyperbolic sine. | `Math.Sinh`. |
| [`math.sqrt(x)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.sqrt) | Square root. | `Math.Sqrt`. |
| [`math.tan(x)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.tan) | Tangent. | `Math.Tan`. |
| [`math.tanh(x)`](https://www.lua.org/manual/5.2/manual.html#pdf-math.tanh) | Hyperbolic tangent. | `Math.Tanh`. |

The deprecated `math.log10` alias is absent; use `math.log(x, 10)`. See [official library changes](https://www.lua.org/manual/5.2/manual.html#8.2). Integer-subtype APIs such as `math.type`, `math.tointeger`, and `math.maxinteger` belong to later Lua versions and are not installed.
