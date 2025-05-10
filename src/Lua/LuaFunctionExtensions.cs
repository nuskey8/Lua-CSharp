// using Lua.Runtime;
//
// namespace Lua;
//
// public static class LuaFunctionExtensions
// {
//     
//     public static async ValueTask<int> InvokeAsync(this LuaFunction function, LuaThread thread, int argumentCount, CancellationToken cancellationToken = default)
//     {
//         var returnFrameBase = thread.Stack.Count-argumentCount;
//         var varArgumentCount = function.GetVariableArgumentCount(argumentCount);
//         if (varArgumentCount != 0)
//         {
//             if (varArgumentCount < 0)
//             {
//                 thread.Stack.SetTop(thread.Stack.Count - varArgumentCount);
//                 argumentCount -= varArgumentCount;
//                 varArgumentCount = 0;
//             }
//             else
//             {
//                 LuaVirtualMachine.PrepareVariableArgument(thread.Stack, argumentCount, varArgumentCount);
//             }
//         }
//
//         LuaFunctionExecutionContext context = new() { Thread = thread, ArgumentCount = argumentCount , ReturnFrameBase = returnFrameBase, };
//         var frame = new CallStackFrame { Base = context.FrameBase, VariableArgumentCount = varArgumentCount, Function = function, ReturnBase = context.ReturnFrameBase };
//         context.Thread.PushCallStackFrame(frame);
//         try
//         {
//             if (context.Thread.CallOrReturnHookMask.Value != 0 && !context.Thread.IsInHook)
//             {
//                 return await LuaVirtualMachine.ExecuteCallHook(context, cancellationToken);
//             }
//
//             return await function.Func(context, cancellationToken);
//         }
//         finally
//         {
//             context.Thread.PopCallStackFrame();
//         }
//     }
// }