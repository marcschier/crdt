// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;

namespace Crdt;

/// <summary>
/// A growable <see cref="IBufferWriter{T}"/> backed by pooled arrays, used by the
/// convenience serialization helpers to materialise a payload into a single buffer. Rent
/// the writer, fill it, read <see cref="WrittenSpan"/>, then dispose to return the array.
/// </summary>
internal sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
{
    private byte[] _buffer;
    private int _index;

    public PooledBufferWriter(int initialCapacity = 256)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity < 1 ? 1 : initialCapacity);
        _index = 0;
    }

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _index);

    public int WrittenCount => _index;

    public void Advance(int count)
    {
        Throw.IfNegative(count);
        if (_index + count > _buffer.Length)
        {
            Throw.ArgumentOutOfRange(nameof(count), "Advanced past the end of the buffer.");
        }

        _index += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsMemory(_index);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsSpan(_index);
    }

    public void Dispose()
    {
        if (_buffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = [];
        }
    }

    private void EnsureCapacity(int sizeHint)
    {
        if (sizeHint < 1)
        {
            sizeHint = 1;
        }

        if (_index + sizeHint > _buffer.Length)
        {
            Grow(_index + sizeHint);
        }
    }

    private void Grow(int required)
    {
        int newSize = Math.Max(required, _buffer.Length * 2);
        byte[] next = ArrayPool<byte>.Shared.Rent(newSize);
        Array.Copy(_buffer, next, _index);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = next;
    }
}
