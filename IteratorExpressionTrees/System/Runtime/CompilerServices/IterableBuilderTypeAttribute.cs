namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface)]
    public sealed class IterableBuilderTypeAttribute : Attribute
    {
        public IterableBuilderTypeAttribute(Type type)
        {
            Type = type;
        }

        public Type Type { get; }
    }
}
