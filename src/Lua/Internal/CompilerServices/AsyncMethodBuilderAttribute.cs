#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable CS0436

namespace System.Runtime.CompilerServices
{
    internal sealed class AsyncMethodBuilderAttribute(Type builderType) : Attribute
    {
        public Type BuilderType { get; } = builderType;
    }
}