// using System;
//
// namespace Lua.Unity
// {
//     public static class LuaStateUnityExtensions
//     {
//         public static void AddStreamingAssetsPath(this LuaState luaState, string path)
//         {
//             if (luaState is null)
//             {
//                 throw new ArgumentNullException(nameof(luaState));
//             }
//
//             if (string.IsNullOrWhiteSpace(path))
//             {
//                 throw new ArgumentException("Path cannot be null or whitespace.", nameof(path));
//             }
//
//             var package = luaState.Environment["package"];
//             if (package == LuaValue.Nil)
//             {
//                 throw new InvalidOperationException("Module 'package' is not available in the Lua state.");
//             }
//
//             package.Read<LuaTable>()["path"] += ";"
//         }
//     }
// }