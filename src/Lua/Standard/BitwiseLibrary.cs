using Lua.Standard.Internal;

namespace Lua.Standard;

public sealed class BitwiseLibrary
{
    public static readonly BitwiseLibrary Instance = new();

    public BitwiseLibrary()
    {
        var libraryName = "bit32";
        Functions =
        [
            new(libraryName, "arshift", ArShift),
            new(libraryName, "band", BAnd),
            new(libraryName, "bnot", BNot),
            new(libraryName, "bor", BOr),
            new(libraryName, "btest", BTest),
            new(libraryName, "bxor", BXor),
            new(libraryName, "extract", Extract),
            new(libraryName, "lrotate", LRotate),
            new(libraryName, "lshift", LShift),
            new(libraryName, "replace", Replace),
            new(libraryName, "rrotate", RRotate),
            new(libraryName, "rshift", RShift)
        ];
    }

    public readonly LibraryFunction[] Functions;

    public ValueTask<int> ArShift(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var x = context.GetArgument<double>(0);
        var disp = context.GetArgument<double>(1);

        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 1, x);
        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 2, disp);

        var v = Bit32Helper.ToInt32(x);
        var a = (int)disp;

        if (a < 0)
        {
            v <<= -a;
        }
        else
        {
            v >>= a;
        }

        return new(context.Return((uint)v));
    }

    public ValueTask<int> BAnd(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.ArgumentCount == 0)
        {
            context.Return(uint.MaxValue);
            return default;
        }

        var arg0 = context.GetArgument<double>(0);
        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 1, arg0);

        var value = Bit32Helper.ToUInt32(arg0);

        for (var i = 1; i < context.ArgumentCount; i++)
        {
            var arg = context.GetArgument<double>(i);
            LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 1 + i, arg);

            var v = Bit32Helper.ToUInt32(arg);
            value &= v;
        }


        return new(context.Return(value));
    }

    public ValueTask<int> BNot(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<double>(0);
        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 1, arg0);

        var value = Bit32Helper.ToUInt32(arg0);
        return new(context.Return(~value));
    }

    public ValueTask<int> BOr(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.ArgumentCount == 0)
        {
            return new(context.Return(0));
        }

        var arg0 = context.GetArgument<double>(0);
        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 1, arg0);

        var value = Bit32Helper.ToUInt32(arg0);

        for (var i = 1; i < context.ArgumentCount; i++)
        {
            var arg = context.GetArgument<double>(i);
            LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 1 + i, arg);

            var v = Bit32Helper.ToUInt32(arg);
            value |= v;
        }

        return new(context.Return(value));
    }

    public ValueTask<int> BTest(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.ArgumentCount == 0)
        {
            ;
            return new(context.Return(true));
        }

        var arg0 = context.GetArgument<double>(0);
        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 1, arg0);

        var value = Bit32Helper.ToUInt32(arg0);

        for (var i = 1; i < context.ArgumentCount; i++)
        {
            var arg = context.GetArgument<double>(i);
            LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 1 + i, arg);

            var v = Bit32Helper.ToUInt32(arg);
            value &= v;
        }

        return new(context.Return(value != 0));
    }

    public ValueTask<int> BXor(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.ArgumentCount == 0)
        {
            return new(context.Return(0));
        }

        var arg0 = context.GetArgument<double>(0);
        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 1, arg0);

        var value = Bit32Helper.ToUInt32(arg0);

        for (var i = 1; i < context.ArgumentCount; i++)
        {
            var arg = context.GetArgument<double>(i);
            LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 1 + i, arg);

            var v = Bit32Helper.ToUInt32(arg);
            value ^= v;
        }

        return new(context.Return(value));
    }

    public ValueTask<int> Extract(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<double>(0);
        var arg1 = context.GetArgument<double>(1);
        var arg2 = context.HasArgument(2)
            ? context.GetArgument<double>(2)
            : 1;

        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 1, arg0);
        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 2, arg1);
        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 3, arg2);

        var n = Bit32Helper.ToUInt32(arg0);
        var field = (int)arg1;
        var width = (int)arg2;

        Bit32Helper.ValidateFieldAndWidth(context.Thread, "extract", 2, field, width);

        if (field == 0 && width == 32)
        {
            return new(context.Return(n));
        }
        else
        {
            var mask = (uint)((1 << width) - 1);
            return new(context.Return((n >> field) & mask));
        }
    }

    public ValueTask<int> LRotate(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var x = context.GetArgument<double>(0);
        var disp = context.GetArgument<double>(1);

        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 1, x);
        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 2, disp);

        var v = Bit32Helper.ToUInt32(x);
        var a = (int)disp % 32;

        if (a < 0)
        {
            v = (v >> -a) | (v << (32 + a));
        }
        else
        {
            v = (v << a) | (v >> (32 - a));
        }

        ;
        return new(context.Return(v));
    }

    public ValueTask<int> LShift(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var x = context.GetArgument<double>(0);
        var disp = context.GetArgument<double>(1);

        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 1, x);
        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 2, disp);

        var v = Bit32Helper.ToUInt32(x);
        var a = (int)disp;

        if (Math.Abs(a) >= 32)
        {
            v = 0;
        }
        else if (a < 0)
        {
            v >>= -a;
        }
        else
        {
            v <<= a;
        }

        return new(context.Return(v));
    }

    public ValueTask<int> Replace(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<double>(0);
        var arg1 = context.GetArgument<double>(1);
        var arg2 = context.GetArgument<double>(2);
        var arg3 = context.HasArgument(3)
            ? context.GetArgument<double>(3)
            : 1;

        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 1, arg0);
        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 2, arg1);
        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 3, arg2);
        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 4, arg3);

        var n = Bit32Helper.ToUInt32(arg0);
        var v = Bit32Helper.ToUInt32(arg1);
        var field = (int)arg2;
        var width = (int)arg3;

        Bit32Helper.ValidateFieldAndWidth(context.Thread, "replace", 2, field, width);
        uint mask;
        if (width == 32)
        {
            mask = 0xFFFFFFFF;
        }
        else
        {
            mask = (uint)((1 << width) - 1);
        }

        v = v & mask;
        n = (n & ~(mask << field)) | (v << field);
        return new(context.Return(n));
    }

    public ValueTask<int> RRotate(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var x = context.GetArgument<double>(0);
        var disp = context.GetArgument<double>(1);

        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 1, x);
        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 2, disp);

        var v = Bit32Helper.ToUInt32(x);
        var a = (int)disp % 32;

        if (a < 0)
        {
            v = (v << -a) | (v >> (32 + a));
        }
        else
        {
            v = (v >> a) | (v << (32 - a));
        }

        return new(context.Return(v));
    }

    public ValueTask<int> RShift(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var x = context.GetArgument<double>(0);
        var disp = context.GetArgument<double>(1);

        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 1, x);
        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.Thread, 2, disp);

        var v = Bit32Helper.ToUInt32(x);
        var a = (int)disp;

        if (Math.Abs(a) >= 32)
        {
            v = 0;
        }
        else if (a < 0)
        {
            v <<= -a;
        }
        else
        {
            v >>= a;
        }

        return new(context.Return(v));
    }
}