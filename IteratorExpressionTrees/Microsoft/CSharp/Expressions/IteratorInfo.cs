using System;

namespace Microsoft.CSharp.Expressions
{
    public sealed class IteratorInfo
    {
        internal IteratorInfo(Type elementType, Type builderType)
        {
            ElementType = elementType;
            BuilderType = builderType;
        }

        public Type ElementType { get; }
        public Type BuilderType { get; }
    }
}
