using System.Runtime.CompilerServices;
using System.Diagnostics;

// ReSharper disable InconsistentNaming

namespace Lua.Runtime;

static partial class LuaVirtualMachine
{
    static class Markers
    {
        static Markers()
        {
            InitializeMarkers();
        }

        /// <summary>
        /// This method is used to call the marker method once for the JIT decompiler.
        /// ClrMd, used for the JIT decompiler, cannot identify a method unless that method is called at least once.
        /// </summary>
        [Conditional("CASE_MARKER")]
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void InitializeMarkers()
        {
            Move();
            LoadK();
            LoadKX();
            LoadBool();
            LoadNil();
            GetUpVal();
            GetTabUp();
            GetTable();
            SetTabUp();
            SetUpVal();
            SetTable();
            NewTable();
            Self();
            Add();
            Sub();
            Mul();
            Div();
            Mod();
            Pow();
            Unm();
            Not();
            Len();
            Concat();
            Jmp();
            Eq();
            Lt();
            Le();
            Test();
            TestSet();
            Call();
            TailCall();
            Return();
            ForLoop();
            ForPrep();
            TForCall();
            TForLoop();
            SetList();
            Closure();
            VarArg();
            ExtraArg();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Move()
        {
        }


        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LoadK()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LoadKX()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LoadBool()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void LoadNil()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void GetUpVal()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void GetTabUp()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void GetTable()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void SetTabUp()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void SetUpVal()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void SetTable()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void NewTable()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Self()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Add()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Sub()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Mul()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Div()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Mod()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Pow()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Unm()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Not()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Len()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Concat()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Jmp()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Eq()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Lt()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Le()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Test()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void TestSet()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Call()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void TailCall()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Return()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void ForLoop()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void ForPrep()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void TForCall()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void TForLoop()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void SetList()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void Closure()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void VarArg()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("CASE_MARKER")]
        public static void ExtraArg()
        {
        }
    }
}