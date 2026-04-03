using MassLens.Core;
using System.Reflection;

namespace MassLens.Tests;

public class CircularBufferTests
{
    private static CircularBuffer<T> MakeBuffer<T>(int capacity)
    {
        var ctor = typeof(CircularBuffer<T>)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                            null, [typeof(int)], null)!;
        return (CircularBuffer<T>)ctor.Invoke([capacity]);
    }

    [Fact]
    public void Empty_buffer_returns_empty_array()
    {
        var buf = MakeBuffer<int>(10);
        Assert.Empty(buf.ReadAll());
        Assert.Equal(0, buf.Count);
    }

    [Fact]
    public void Write_and_read_preserves_order()
    {
        var buf = MakeBuffer<int>(10);
        buf.Write(1); buf.Write(2); buf.Write(3);
        Assert.Equal([1, 2, 3], buf.ReadAll());
    }

    [Fact]
    public void Overflow_drops_oldest_items()
    {
        var buf = MakeBuffer<int>(3);
        buf.Write(1); buf.Write(2); buf.Write(3); buf.Write(4);
        var result = buf.ReadAll();
        Assert.Equal(3, result.Length);
        Assert.Equal([2, 3, 4], result);
    }

    [Fact]
    public void Count_never_exceeds_capacity()
    {
        var buf = MakeBuffer<int>(5);
        for (int i = 0; i < 20; i++) buf.Write(i);
        Assert.Equal(5, buf.Count);
    }

    [Fact]
    public void ReadAll_is_consistent_with_Count()
    {
        var buf = MakeBuffer<int>(10);
        for (int i = 0; i < 7; i++) buf.Write(i);
        Assert.Equal(buf.Count, buf.ReadAll().Length);
    }

    [Fact]
    public async Task Concurrent_writes_do_not_throw()
    {
        var buf = MakeBuffer<int>(100);
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => { for (int i = 0; i < 100; i++) buf.Write(i); }));
        await Task.WhenAll(tasks);
        Assert.Equal(100, buf.Count);
    }

    [Fact]
    public void Single_item_capacity_always_holds_last_written()
    {
        var buf = MakeBuffer<int>(1);
        buf.Write(42);
        buf.Write(99);
        Assert.Equal([99], buf.ReadAll());
    }
}
