using System.Collections.Generic;

namespace System.Runtime.CompilerServices
{
    public abstract class IterableBuilder<T> : IIterable<T>, IIterator<T>
    {
        private int _state;
        private int _initialThreadId;

        public IterableBuilder()
        {
            _state = 1;
            _initialThreadId = Environment.CurrentManagedThreadId;
        }

        public T Current { get; private set; }

        public void Dispose()
        {
            var state = _state;

            if (state < 0)
            {
                return;
            }

            _state = -2;
            TryGetNext(state, shouldBreak: true, out _, out _);
        }

        public IIterator<T> GetEnumerator()
        {
            if (_state != 1 || _initialThreadId != Environment.CurrentManagedThreadId)
            {
                var clone = (IterableBuilder<T>)Clone();
                clone._state = 0;
                return clone;
            }
            else
            {
                _state = 0;
                return this;
            }
        }

        public bool MoveNext()
        {
            if (_state < 0)
            {
                return false;
            }

            Current = TryGetNext(_state, shouldBreak: false, out _state, out var hasNext);
            return hasNext;
        }

        protected abstract IIterable<T> Clone();

        protected abstract T TryGetNext(int state, bool shouldBreak, out int nextState, out bool hasNext);
    }

    public delegate T TryGetNext<T>(int state, bool shouldBreak, out int nextState, out bool hasNext);
}
