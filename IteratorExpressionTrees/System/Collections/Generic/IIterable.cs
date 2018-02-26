using System.Runtime.CompilerServices;

namespace System.Collections.Generic
{
    [IterableBuilderType(typeof(IterableBuilder<>))]
    public interface IIterable<out T>
    {
        IIterator<T> GetEnumerator();
    }
}
