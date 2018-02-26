namespace System.Collections.Generic
{
    public interface IIterator<out T> : IDisposable
    {
        bool MoveNext();
        T Current { get; }
    }
}
