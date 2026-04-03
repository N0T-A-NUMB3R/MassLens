namespace MassLens.Core;

internal sealed class CircularBuffer<T>
{
    private readonly T[] _buffer;
    private readonly int _capacity;
    private int _head;
    private int _count;
    private readonly object _lock = new();

    public CircularBuffer(int capacity)
    {
        _capacity = capacity;
        _buffer = new T[capacity];
    }

    public void Write(T item)
    {
        lock (_lock)
        {
            _buffer[_head] = item;
            _head = (_head + 1) % _capacity;
            if (_count < _capacity) _count++;
        }
    }

    public T[] ReadAll()
    {
        lock (_lock)
        {
            var result = new T[_count];
            var tail = (_head - _count + _capacity) % _capacity;
            for (int i = 0; i < _count; i++)
                result[i] = _buffer[(tail + i) % _capacity];
            return result;
        }
    }

    public int Count { get { lock (_lock) return _count; } }

    public void Clear()
    {
        lock (_lock)
        {
            _head  = 0;
            _count = 0;
            Array.Clear(_buffer, 0, _capacity);
        }
    }
}
