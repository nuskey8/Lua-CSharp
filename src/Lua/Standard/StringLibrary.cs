using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Lua.Internal;
using Lua.Internal.CompilerServices;
using Lua.Runtime;
using Lua.Standard.Internal;

namespace Lua.Standard;

public sealed class StringLibrary
{
    public static readonly StringLibrary Instance = new();

    public StringLibrary()
    {
        var libraryName = "string";
        Functions =
        [
            new(libraryName, "byte", Byte),
            new(libraryName, "char", Char),
            new(libraryName, "dump", Dump),
            new(libraryName, "find", Find),
            new(libraryName, "format", Format),
            new(libraryName, "gmatch", GMatch),
            new(libraryName, "gsub", GSub),
            new(libraryName, "len", Len),
            new(libraryName, "lower", Lower),
            new(libraryName, "match", Match),
            new(libraryName, "rep", Rep),
            new(libraryName, "reverse", Reverse),
            new(libraryName, "sub", Sub),
            new(libraryName, "upper", Upper),
        ];
    }

    public readonly LibraryFunction[] Functions;

    public ValueTask<int> Byte(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var s = context.GetStringArgument(0);
        var i = context.HasArgument(1) ? ReadStringIndex(context, 1) : 1;
        var j = context.HasArgument(2) ? ReadStringIndex(context, 2) : i;

        var span = StringHelper.Slice(s, i, j);
        var buffer = context.GetReturnBuffer(span.Length);
        for (var k = 0; k < span.Length; k++)
        {
            buffer[k] = span[k];
        }

        return new(span.Length);
    }

    public ValueTask<int> Char(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        if (context.ArgumentCount == 0)
        {
            return new(context.Return(""));
        }

        var byteStrings = context.State.Platform.StringMode == LuaStringMode.Bytes;
        ValueStringBuilder builder = new(context.ArgumentCount);
        try
        {
            for (var i = 0; i < context.ArgumentCount; i++)
            {
                var arg = Math.Truncate(context.GetArgument<double>(i));
                LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, i + 1, arg);
                if (byteStrings && (arg < 0 || arg > 255))
                {
                    LuaRuntimeException.BadArgument(context.State, i + 1, "value out of range");
                }

                builder.Append((char)arg);
            }

            return new(context.Return(builder.ToString()));
        }
        finally
        {
            builder.Dispose();
        }
    }

    public ValueTask<int> Dump(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var function = context.GetArgument<LuaFunction>(0);
        if (function is not LuaClosure closure)
            throw new LuaRuntimeException(context.State, "unable to dump given function");
        using var writer = new ArrayPoolBufferWriter<byte>();
        closure.Proto.WriteBytecode(writer);
        return new(context.Return(LuaByteString.FromBytes(writer.WrittenSpan)));
    }

    public ValueTask<int> Find(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        return FindAux(context, true);
    }

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> Format(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var format = context.GetStringArgument(0);
        var stack = context.State.Stack;
        // TODO: pooling StringBuilder
        StringBuilder builder = new(format.Length * 2);
        var parameterIndex = 1;

        for (var i = 0; i < format.Length; i++)
        {
            if (format[i] == '%')
            {
                i++;
                if (i == format.Length)
                    throw new LuaRuntimeException(context.State, "invalid option '%' to 'format'");

                // escape
                if (format[i] == '%')
                {
                    builder.Append('%');
                    continue;
                }

                var leftJustify = false;
                var plusSign = false;
                var zeroPadding = false;
                var alternateForm = false;
                var blank = false;
                var width = 0;
                var precision = -1;

                // Lua 5.2 permits repeated flags, with at most five in total.
                var flagCount = 0;
                while (i < format.Length)
                {
                    switch (format[i])
                    {
                        case '-':
                            leftJustify = true;
                            break;
                        case '+':
                            plusSign = true;
                            break;
                        case '0':
                            zeroPadding = true;
                            break;
                        case '#':
                            alternateForm = true;
                            break;
                        case ' ':
                            blank = true;
                            break;
                        default:
                            goto PROCESS_WIDTH;
                    }
                    if (++flagCount > 5)
                        throw new LuaRuntimeException(
                            context.State,
                            "invalid format (repeated flags)"
                        );
                    i++;
                }

                PROCESS_WIDTH:

                // Process width
                if (i < format.Length && format[i] is >= '0' and <= '9')
                    width = ReadFormatDigits(context.State, format, ref i);

                // Process precision
                if (i < format.Length && format[i] == '.')
                {
                    i++;
                    precision = ReadFormatDigits(context.State, format, ref i);
                }

                if (i == format.Length)
                    throw new LuaRuntimeException(context.State, "invalid option '%' to 'format'");

                // Process conversion specifier
                var specifier = format[i];

                if (context.ArgumentCount <= parameterIndex)
                {
                    throw new LuaRuntimeException(
                        context.State,
                        $"bad argument #{parameterIndex + 1} to 'format' (no value)"
                    );
                }

                var parameter = context.GetArgument(parameterIndex++);

                // TODO: reduce allocation
                string formattedValue = default!;
                switch (specifier)
                {
                    case 'a':
                    case 'A':
                        if (!parameter.TryRead<double>(out var a))
                            LuaRuntimeException.BadArgument(
                                context.State,
                                parameterIndex,
                                LuaValueType.Number,
                                parameter.Type
                            );

                        formattedValue = HexConverter.FormatDouble(
                            a,
                            precision,
                            alternateForm,
                            specifier == 'A',
                            context.State.Platform.NumberFormat
                        );
                        if (BitConverter.DoubleToInt64Bits(a) >= 0)
                        {
                            if (plusSign)
                                formattedValue = "+" + formattedValue;
                            else if (blank)
                                formattedValue = " " + formattedValue;
                        }
                        if (double.IsNaN(a) || double.IsInfinity(a))
                            zeroPadding = false;
                        break;
                    case 'o':
                        if (!parameter.TryRead<double>(out var octal))
                            LuaRuntimeException.BadArgument(
                                context.State,
                                parameterIndex,
                                LuaValueType.Number,
                                parameter.Type
                            );
                        if (!(octal > -1 && octal < 18446744073709551616d))
                            LuaRuntimeException.BadArgument(
                                context.State,
                                parameterIndex,
                                "not a non-negative number in proper range"
                            );

                        var unsigned = (ulong)Math.Truncate(octal);
                        formattedValue =
                            unsigned == 0 && precision == 0
                                ? ""
                                : Convert.ToString(unchecked((long)unsigned), 8);
                        if (precision > formattedValue.Length)
                            formattedValue = formattedValue.PadLeft(precision, '0');
                        if (
                            alternateForm
                            && (formattedValue.Length == 0 || formattedValue[0] != '0')
                        )
                            formattedValue = "0" + formattedValue;
                        if (precision >= 0)
                            zeroPadding = false;
                        break;
                    case 'f':
                    case 'e':
                    case 'E':
                    case 'g':
                    case 'G':
                        if (!parameter.TryRead<double>(out var f))
                            LuaRuntimeException.BadArgument(
                                context.State,
                                parameterIndex,
                                LuaValueType.Number,
                                parameter.Type
                            );
                        formattedValue = FormatFloat(
                            f,
                            specifier,
                            precision,
                            alternateForm,
                            context.State.Platform.NumberFormat
                        );
                        if (double.IsNaN(f) || double.IsInfinity(f))
                            zeroPadding = false;
                        if (BitConverter.DoubleToInt64Bits(f) >= 0)
                        {
                            if (plusSign)
                                formattedValue = "+" + formattedValue;
                            else if (blank)
                                formattedValue = " " + formattedValue;
                        }
                        break;
                    case 's':
                        {
                            await parameter.CallToStringAsync(context, cancellationToken);
                            formattedValue = stack.Pop().Read<string>();
                        }

                        if (precision >= 0 && precision < formattedValue.Length)
                        {
                            formattedValue = formattedValue[..precision];
                        }

                        break;
                    case 'q':
                        switch (parameter.Type)
                        {
                            case LuaValueType.String:
                            case LuaValueType.Number:
                                formattedValue = StringHelper.Quote(
                                    parameter.ToLuaString(context.State.Platform.NumberFormat)
                                );
                                break;
                            default:
                                LuaRuntimeException.BadArgument(
                                    context.State,
                                    parameterIndex,
                                    LuaValueType.String,
                                    parameter.Type
                                );
                                break;
                        }

                        width = 0; // Lua ignores width/precision/flags for %q.
                        break;
                    case 'i':
                    case 'd':
                    case 'u':
                    case 'c':
                    case 'x':
                    case 'X':
                        if (!parameter.TryRead<double>(out var x))
                            LuaRuntimeException.BadArgument(
                                context.State,
                                parameterIndex,
                                LuaValueType.Number,
                                parameter.Type
                            );
                        x = Math.Truncate(x);
                        var signed = specifier is 'i' or 'd';
                        if (
                            !MathEx.IsFinite(x)
                            || (
                                specifier != 'c'
                                && (
                                    signed
                                        ? x < long.MinValue || x >= 9223372036854775808d
                                        : x < 0 || x >= 18446744073709551616d
                                )
                            )
                        )
                            LuaRuntimeException.BadArgument(
                                context.State,
                                parameterIndex,
                                signed || specifier == 'c'
                                    ? "not a number in proper range"
                                    : "not a non-negative number in proper range"
                            );
                        if (specifier == 'c')
                        {
                            formattedValue = (
                                context.State.Platform.StringMode == LuaStringMode.Bytes
                                    ? (char)((int)x & 0xFF)
                                    : (char)(int)x
                            ).ToString();
                        }
                        else
                        {
                            if (signed)
                            {
                                var integer = (long)x;
                                formattedValue =
                                    integer == 0 && precision == 0
                                        ? ""
                                        : integer.ToString(
                                            precision < 0 ? "D" : $"D{precision}",
                                            CultureInfo.InvariantCulture
                                        );
                                if (integer >= 0)
                                {
                                    if (plusSign)
                                        formattedValue = "+" + formattedValue;
                                    else if (blank)
                                        formattedValue = " " + formattedValue;
                                }
                            }
                            else
                            {
                                var integer = (ulong)x;
                                formattedValue =
                                    integer == 0 && precision == 0
                                        ? ""
                                        : integer.ToString(
                                            specifier is 'x' or 'X' ? specifier.ToString() : "D",
                                            CultureInfo.InvariantCulture
                                        );
                                if (precision > formattedValue.Length)
                                    formattedValue = formattedValue.PadLeft(precision, '0');
                                if (alternateForm && integer != 0 && specifier is 'x' or 'X')
                                    formattedValue =
                                        (specifier == 'x' ? "0x" : "0X") + formattedValue;
                            }
                            if (precision >= 0)
                                zeroPadding = false;
                        }
                        break;
                    default:
                        throw new LuaRuntimeException(
                            context.State,
                            $"invalid option '%{specifier}' to 'format'"
                        );
                }

                // Apply width and padding
                if (width > formattedValue.Length)
                {
                    if (leftJustify)
                    {
                        formattedValue = formattedValue.PadRight(width);
                    }
                    else
                    {
                        if (zeroPadding)
                        {
                            var prefix =
                                specifier is not ('c' or 's')
                                && formattedValue.Length > 0
                                && formattedValue[0] is '-' or '+' or ' '
                                    ? 1
                                    : 0;
                            if (
                                specifier is 'a' or 'A' or 'x' or 'X'
                                && formattedValue
                                    .AsSpan(prefix)
                                    .StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                            )
                                prefix += 2;
                            formattedValue = formattedValue.Insert(
                                prefix,
                                new string('0', width - formattedValue.Length)
                            );
                        }
                        else
                            formattedValue = formattedValue.PadLeft(width);
                    }
                }

                builder.Append(formattedValue);
            }
            else
            {
                builder.Append(format[i]);
            }
        }

        return context.Return(builder.ToString());
    }

    public ValueTask<int> GMatch(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var s = context.GetStringArgument(0);
        var pattern = context.GetStringArgument(1);

        return new(
            context.Return(
                new CSharpClosure(
                    "gmatch_iterator",
                    [s, pattern, 0],
                    static (context, cancellationToken) =>
                    {
                        var upValues = context.GetCsClosure()!.UpValues;
                        var s = upValues[0].Read<string>();
                        var pattern = upValues[1].Read<string>();
                        var start = upValues[2].Read<int>();

                        MatchState matchState = new(context.State, s, pattern);
                        var captures = matchState.Captures;

                        // Include the end position so patterns can match an empty string there.
                        for (var sIdx = start; sIdx <= s.Length; sIdx++)
                        {
                            // Reset match state for each attempt
                            matchState.Level = 0;
                            matchState.MatchDepth = MatchState.MaxCalls;

                            var res = matchState.Match(sIdx, 0);

                            if (res >= 0)
                            {
                                // If no captures were made, create one for the whole match
                                if (matchState.Level == 0)
                                {
                                    captures[0].Init = sIdx;
                                    captures[0].Len = res - sIdx;
                                    matchState.Level = 1;
                                }

                                var resultLength = matchState.Level;
                                var buffer = context.GetReturnBuffer(resultLength);
                                for (var i = 0; i < matchState.Level; i++)
                                {
                                    buffer[i] = matchState.GetCapture(i);
                                }

                                // Update start index for next iteration
                                // Handle empty matches by advancing at least 1 position
                                upValues[2] = res > sIdx ? res : sIdx + 1;
                                return new(resultLength);
                            }
                        }

                        return new(context.Return());
                    }
                )
            )
        );
    }

    static void AppendReplacement(
        StringBuilder builder,
        string replacement,
        string source,
        int matchStart,
        int matchEnd,
        MatchState matchState
    )
    {
        for (var i = 0; i < replacement.Length; i++)
        {
            var c = replacement[i];
            if (c != '%')
            {
                builder.Append(c);
                continue;
            }

            i++;
            var next = i < replacement.Length ? replacement[i] : '\0';
            if (next == '%')
            {
                builder.Append('%');
            }
            else if (next == '0')
            {
                builder.Append(source, matchStart, matchEnd - matchStart);
            }
            else if (next >= '1' && next <= '9')
            {
                var index = next - '1';
                if (index >= matchState.Level)
                {
                    throw new LuaRuntimeException(matchState.State, "invalid capture index");
                }

                var capture = matchState.Captures[index];
                if (capture.IsUnfinished)
                {
                    throw new LuaRuntimeException(matchState.State, "unfinished capture");
                }

                if (capture.IsPosition)
                {
                    builder.Append(capture.Init + 1); // 1-based position
                }
                else
                {
                    builder.Append(source, capture.Init, capture.Len);
                }
            }
            else
            {
                throw new LuaRuntimeException(
                    matchState.State,
                    "invalid use of '%' in replacement string"
                );
            }
        }
    }

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> GSub(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var s = context.GetStringArgument(0);
        var pattern = context.GetStringArgument(1);
        var repl = context.GetArgument(2);
        var n_arg = context.HasArgument(3) ? context.GetArgument<double>(3) : s.Length + 1;

        n_arg = Math.Truncate(n_arg);
        if (!MathEx.IsFinite(n_arg))
            LuaRuntimeException.BadArgumentNumberIsNotInteger(context.State, 4);
        if (
            repl.Type
            is not (
                LuaValueType.String
                or LuaValueType.Number
                or LuaValueType.Table
                or LuaValueType.Function
            )
        )
            LuaRuntimeException.BadArgument(context.State, 3, "string/function/table expected");
        // Lua 5.2 stores the limit as size_t; a negative integer means no practical limit.
        var n = n_arg < 0 || n_arg > int.MaxValue ? int.MaxValue : (int)n_arg;
        if (n == 0)
            return context.Return(s, 0);

        context.State.NonYieldableCallCount++;
        try
        {
            MatchState matchState = new(context.State, s, pattern);
            var captures = matchState.Captures;

            StringBuilder builder = new();
            var replacementText =
                repl.Type == LuaValueType.String ? repl.UnsafeReadString()
                : repl.Type == LuaValueType.Number
                    ? repl.ToLuaString(context.State.Platform.NumberFormat)
                : null;
            var replacedBuilder =
                replacementText != null && replacementText.IndexOf('%') >= 0
                    ? new StringBuilder(replacementText.Length)
                    : null;
            var lastIndex = 0;
            var replaceCount = 0;

            // Check for anchor at start
            var anchor = pattern.Length > 0 && pattern[0] == '^';
            var sIdx = 0;

            // Include the end position so patterns can match an empty string there.
            while (sIdx <= s.Length && replaceCount < n)
            {
                // Reset match state for each attempt
                matchState.Level = 0;
                Debug.Assert(matchState.MatchDepth == MatchState.MaxCalls);

                // Always start pattern from beginning (0 or 1 if anchored)
                var pIdx = anchor ? 1 : 0;
                var res = matchState.Match(sIdx, pIdx);

                if (res >= 0)
                {
                    // Found a match
                    builder.Append(s.AsSpan()[lastIndex..sIdx]);

                    // If no captures were made, create one for the whole match
                    if (matchState.Level == 0)
                    {
                        captures[0].Init = sIdx;
                        captures[0].Len = res - sIdx;
                        matchState.Level = 1;
                    }

                    LuaValue result;
                    if (replacementText != null)
                    {
                        if (replacedBuilder == null)
                        {
                            result = replacementText;
                        }
                        else
                        {
                            // String replacement
                            replacedBuilder.Clear();
                            AppendReplacement(
                                replacedBuilder,
                                replacementText,
                                s,
                                sIdx,
                                res,
                                matchState
                            );
                            result = replacedBuilder.ToString();
                        }
                    }
                    else if (repl.TryRead<LuaTable>(out var table))
                    {
                        var key = matchState.GetCapture(0);

                        if (!table.TryGetValue(key, out result) && table.Metatable != null)
                        {
                            result = await LuaVirtualMachine.ExecuteGetTableSlowPath(
                                context.State,
                                table,
                                key,
                                cancellationToken
                            );
                        }
                    }
                    else
                    {
                        var func = repl.UnsafeRead<LuaFunction>();
                        // Function call with captures as arguments
                        var stack = context.State.Stack;

                        for (var k = 0; k < matchState.Level; k++)
                        {
                            stack.Push(matchState.GetCapture(k));
                        }
                        var retCount = await context.State.RunCoreAsync(
                            func,
                            matchState.Level,
                            stack.Count - matchState.Level,
                            cancellationToken
                        );
                        using var results = context.State.ReadStack(retCount);
                        result = results.Count > 0 ? results[0] : LuaValue.Nil;
                    }
                    // Handle replacement result
                    if (result.TryRead<string>(out var rs))
                    {
                        builder.Append(rs);
                    }
                    else if (result.TryRead<double>(out var rd))
                    {
                        AppendNumber(builder, rd, context.State.Platform.NumberFormat);
                    }
                    else if (!result.ToBoolean())
                    {
                        // False or nil means don't replace
                        builder.Append(s.AsSpan(sIdx, res - sIdx));
                    }
                    else
                    {
                        throw new LuaRuntimeException(
                            context.State,
                            $"invalid replacement value (a {result.TypeToString()})"
                        );
                    }

                    replaceCount++;
                    lastIndex = res;

                    // If empty match, advance by 1 to avoid infinite loop
                    if (res == sIdx)
                    {
                        if (sIdx < s.Length)
                        {
                            builder.Append(s[sIdx]);
                            lastIndex = sIdx + 1;
                        }

                        sIdx++;
                    }
                    else
                    {
                        sIdx = res;
                    }

                    // Anchored patterns only match once at the start of the string
                    if (anchor)
                    {
                        break;
                    }
                }
                else
                {
                    // No match at this position
                    if (anchor)
                    {
                        // Anchored pattern only tries at start
                        break;
                    }

                    sIdx++;
                }
            }

            // Append remaining part of string
            if (lastIndex < s.Length)
            {
                builder.Append(s.AsSpan()[lastIndex..]);
            }

            return context.Return(builder.ToString(), replaceCount);
        }
        finally
        {
            context.State.NonYieldableCallCount--;
        }
    }

    public ValueTask<int> Len(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var s = context.GetStringArgument(0);
        return new(context.Return(s.Length));
    }

    public ValueTask<int> Lower(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var s = context.GetStringArgument(0);
        return new(
            context.Return(
                context.State.Platform.StringMode == LuaStringMode.Bytes
                    ? StringHelper.ToLowerAscii(s)
                    : s.ToLower()
            )
        );
    }

    public ValueTask<int> Match(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        return FindAux(context, false);
    }

    public ValueTask<int> FindAux(LuaFunctionExecutionContext context, bool find)
    {
        var s = context.GetStringArgument(0);
        var pattern = context.GetStringArgument(1);
        var init = context.HasArgument(2) ? ReadStringIndex(context, 2) : 1;
        var offset = Math.Max(1, StringHelper.RelativePosition(init, s.Length)) - 1;

        // Check if init is beyond string bounds
        if (offset > s.Length)
        {
            return new(context.Return(LuaValue.Nil));
        }

        // Check for plain search mode (4th parameter = true) or if pattern has no special characters
        if (find && (context.GetArgumentOrDefault(3).ToBoolean() || MatchState.NoSpecials(pattern)))
        {
            return PlainSearch(context, s, pattern, offset);
        }

        return PatternSearch(context, s, pattern, offset, find);
    }

    static ValueTask<int> PlainSearch(
        LuaFunctionExecutionContext context,
        string s,
        string pattern,
        int init
    )
    {
        var index = s.AsSpan(init).IndexOf(pattern);
        if (index == -1)
        {
            return new(context.Return(LuaValue.Nil));
        }

        var actualStart = init + index;
        return new(context.Return(actualStart + 1, actualStart + pattern.Length)); // Convert to 1-based
    }

    static ValueTask<int> PatternSearch(
        LuaFunctionExecutionContext context,
        string s,
        string pattern,
        int init,
        bool find
    )
    {
        MatchState matchState = new(context.State, s, pattern);
        var captures = matchState.Captures;

        // Check for anchor at start
        var anchor = pattern.Length > 0 && pattern[0] == '^';
        var pIdx = anchor ? 1 : 0;

        // Include the end position so patterns can match an empty string there.
        for (var sIdx = init; sIdx <= s.Length; sIdx++)
        {
            // Reset match state for each attempt
            matchState.Level = 0;
            matchState.MatchDepth = MatchState.MaxCalls;

            var res = matchState.Match(sIdx, pIdx);

            if (res >= 0)
            {
                // If no captures were made for string.match, create one for the whole match
                if (!find && matchState.Level == 0)
                {
                    captures[0].Init = sIdx;
                    captures[0].Len = res - sIdx;
                    matchState.Level = 1;
                }

                var resultLength = matchState.Level + (find ? 2 : 0);
                var buffer = context.GetReturnBuffer(resultLength);

                if (find)
                {
                    // Return start and end positions for string.find
                    buffer[0] = sIdx + 1; // Convert to 1-based index
                    buffer[1] = res; // Convert to 1-based index
                    buffer = buffer[2..];
                }

                // Return captures
                for (var i = 0; i < matchState.Level; i++)
                {
                    buffer[i] = matchState.GetCapture(i);
                }

                return new(resultLength);
            }

            // For anchored patterns, only try once
            if (anchor)
            {
                break;
            }
        }

        return new(context.Return(LuaValue.Nil));
    }

    public ValueTask<int> Rep(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var s = context.GetStringArgument(0);
        var n_arg = context.GetArgument<double>(1);
        var sep = context.HasArgument(2) ? context.GetStringArgument(2) : null;

        if (!MathEx.IsFinite(n_arg))
        {
            LuaRuntimeException.BadArgumentNumberIsNotInteger(context.State, 2);
        }

        // Lua 5.2 converts fractional counts to integers instead of rejecting
        // them. Truncate before the range checks and result-length calculation.
        n_arg = Math.Truncate(n_arg);

        if (n_arg <= 0 || (s.Length == 0 && string.IsNullOrEmpty(sep)))
        {
            return new(context.Return(""));
        }

        // Validate before converting: out-of-range conversions can saturate
        // on newer runtimes rather than producing a non-positive count.
        if (n_arg > int.MaxValue)
        {
            throw new LuaRuntimeException(context.State, "resulting string too large");
        }

        var n = (int)n_arg;
        if (n == 1)
        {
            return new(context.Return(s));
        }

        var length = (long)s.Length * n + (long)(sep?.Length ?? 0) * (n - 1);
        if (length > int.MaxValue)
        {
            throw new LuaRuntimeException(context.State, "resulting string too large");
        }

        var result = string.Create(
            (int)length,
            (s, sep),
            static (destination, state) =>
            {
                var (source, separator) = state;
                source.AsSpan().CopyTo(destination);
                separator.AsSpan().CopyTo(destination[source.Length..]);
                var written = source.Length + (separator?.Length ?? 0);
                // Copy the initialized prefix to double the number of repetitions.
                // The final copy stops before the trailing separator.
                while (written < destination.Length)
                {
                    var count = Math.Min(written, destination.Length - written);
                    destination[..count].CopyTo(destination[written..]);
                    written += count;
                }
            }
        );
        return new(context.Return(result));
    }

    public ValueTask<int> Reverse(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var s = context.GetStringArgument(0);
        var result =
            s.Length <= 1
                ? s
                : string.Create(
                    s.Length,
                    s,
                    static (destination, source) =>
                    {
                        source.AsSpan().CopyTo(destination);
                        destination.Reverse();
                    }
                );
        return new(context.Return(result));
    }

    public ValueTask<int> Sub(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var s = context.GetStringArgument(0);
        var i = ReadStringIndex(context, 1);
        var j = context.HasArgument(2) ? ReadStringIndex(context, 2) : -1;

        var slice = StringHelper.Slice(s, i, j);
        return new(context.Return(slice.Length == s.Length ? s : slice.ToString()));
    }

    public ValueTask<int> Upper(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var s = context.GetStringArgument(0);
        return new(
            context.Return(
                context.State.Platform.StringMode == LuaStringMode.Bytes
                    ? StringHelper.ToUpperAscii(s)
                    : s.ToUpper()
            )
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int ReadStringIndex(in LuaFunctionExecutionContext context, int index)
    {
        var number = context.GetArgument<double>(index);
        if (!MathEx.IsFinite(number))
            LuaRuntimeException.BadArgumentNumberIsNotInteger(context.State, index + 1);
        // Indices outside Int32 cannot address a CLR string. Saturate before
        // relative indexing, including on targets predating saturating casts.
#if NET10_0_OR_GREATER
        return (int)number;
#else
        return (int)Math.Min(int.MaxValue, Math.Max(int.MinValue, number));
#endif
    }

    static void AppendNumber(StringBuilder builder, double value, LuaNumberFormat format)
    {
        Span<char> buffer = stackalloc char[32];
        NumberFormatter.TryFormat(value, buffer, out var written, format);
        builder.Append(buffer[..written]);
    }

    static string FormatFloat(
        double value,
        char specifier,
        int precision,
        bool alternateForm,
        LuaNumberFormat numberFormat
    )
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return NumberFormatter.FormatSpecial(value, numberFormat, specifier is 'E' or 'G');
        }
        var general = specifier is 'g' or 'G';
        precision =
            precision < 0 ? 6
            : general ? Math.Max(1, precision)
            : precision;
        var text = value.ToString($"{specifier}{precision}", CultureInfo.InvariantCulture);
        var exponent = text.AsSpan().IndexOfAny('e', 'E');
        // .NET's scientific format has three exponent digits; C requires at least two.
        if (exponent >= 0 && text.Length - exponent == 5 && text[exponent + 2] == '0')
            text = text.Remove(exponent + 2, 1);
        if (!alternateForm)
            return text;
        var end = exponent < 0 ? text.Length : exponent;
        if (text.AsSpan(0, end).IndexOf('.') < 0)
        {
            text = text.Insert(end, ".");
            end++;
        }
        if (general)
        {
            var significant = 0;
            for (var j = 0; j < end; j++)
                if (text[j] is >= '0' and <= '9' && (text[j] != '0' || significant > 0))
                    significant++;
            if (significant == 0)
                significant = 1;
            if (precision > significant)
                text = text.Insert(end, new string('0', precision - significant));
        }
        return text;
    }

    static int ReadFormatDigits(LuaState state, string format, ref int index)
    {
        var start = index;
        var value = 0;
        while (index < format.Length && format[index] is >= '0' and <= '9')
        {
            if (index - start == 2)
                throw new LuaRuntimeException(
                    state,
                    "invalid format (width or precision too long)"
                );
            value = value * 10 + format[index++] - '0';
        }
        return value;
    }
}
