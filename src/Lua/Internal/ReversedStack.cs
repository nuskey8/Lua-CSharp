namespace Lua.Internal;

sealed class ReversedStack<T>(int capacity = 16)
{
    T[] buffer = new T[capacity];
    int tail = capacity;

    public int Length => buffer.Length - tail;

    public Span<T> AsSpan()
    {
        return buffer.AsSpan(tail, buffer.Length - tail);
    }

    public void Push(T element)
    {
        EnsureAdditionalCapacity(1);
        buffer[--tail] = element;
    }

    public void Push(ReadOnlySpan<T> elements)
    {
        if (elements.Length == 0)
        {
            return;
        }

        EnsureAdditionalCapacity(elements.Length);
        tail -= elements.Length;
        elements.CopyTo(buffer.AsSpan(tail));
    }

    public T Pop()
    {
        if (IsEmpty)
        {
            ThrowEmpty();
        }

        static void ThrowEmpty()
        {
            throw new InvalidOperationException("Cannot pop an empty stack");
        }

        return buffer[tail++];
    }

    public void Pop(int count)
    {
        if (count > buffer.Length - tail)
        {
            ThrowEmpty();
        }

        static void ThrowEmpty()
        {
            throw new InvalidOperationException("Cannot pop more elements than exist in the stack");
        }

        tail += count;
    }

    public void TryPop(out T? element)
    {
        if (IsEmpty)
        {
            element = default!;
            return;
        }

        element = buffer[tail++];
    }

    public void Clear()
    {
        tail = buffer.Length;
    }

    public bool IsEmpty => tail == buffer.Length;

    public T Peek()
    {
        if (IsEmpty)
        {
            ThrowEmpty();
        }

        static void ThrowEmpty()
        {
            throw new InvalidOperationException("Cannot peek an empty stack");
        }

        return buffer[tail];
    }


    void EnsureAdditionalCapacity(int required)
    {
        if (tail >= required)
        {
            return;
        }

        Resize(required - tail + buffer.Length);

        void Resize(int requiredCapacity)
        {
            var newCapacity = buffer.Length * 2;
            while (newCapacity < requiredCapacity)
            {
                newCapacity *= 2;
            }

            var newBuffer = new T[newCapacity];
            AsSpan().CopyTo(newBuffer.AsSpan(newCapacity - buffer.Length));
            tail = newCapacity - (buffer.Length - tail);
            buffer = newBuffer;
        }
    }
}