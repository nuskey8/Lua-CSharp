// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// CommunityToolkit.HighPerformance.Streams

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
namespace Lua.Tests.Helpers;

internal static class IOThrowHelpers
{
    /// <summary>
    /// Validates the <see cref="Stream.Position"/> argument (it needs to be in the [0, length]) range.
    /// </summary>
    /// <param name="position">The new <see cref="Stream.Position"/> value being set.</param>
    /// <param name="length">The maximum length of the target <see cref="Stream"/>.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidatePosition(long position, int length)
    {
        if ((ulong)position > (ulong)length)
        {
            ThrowArgumentOutOfRangeExceptionForPosition();
        }
    }

    /// <summary>
    /// Validates the <see cref="Stream.Position"/> argument (it needs to be in the [0, length]) range.
    /// </summary>
    /// <param name="position">The new <see cref="Stream.Position"/> value being set.</param>
    /// <param name="length">The maximum length of the target <see cref="Stream"/>.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidatePosition(long position, long length)
    {
        if ((ulong)position > (ulong)length)
        {
            ThrowArgumentOutOfRangeExceptionForPosition();
        }
    }

    /// <summary>
    /// Validates the <see cref="Stream.Read(byte[],int,int)"/> or <see cref="Stream.Write(byte[],int,int)"/> arguments.
    /// </summary>
    /// <param name="buffer">The target array.</param>
    /// <param name="offset">The offset within <paramref name="buffer"/>.</param>
    /// <param name="count">The number of elements to process within <paramref name="buffer"/>.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidateBuffer([NotNull] byte[]? buffer, int offset, int count)
    {
        if (buffer is null)
        {
            ThrowArgumentNullExceptionForBuffer();
        }

        if (offset < 0)
        {
            ThrowArgumentOutOfRangeExceptionForOffset();
        }

        if (count < 0)
        {
            ThrowArgumentOutOfRangeExceptionForCount();
        }

        if (offset + count > buffer!.Length)
        {
            ThrowArgumentExceptionForLength();
        }
    }

    /// <summary>
    /// Validates the CanWrite property.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidateCanWrite(bool canWrite)
    {
        if (!canWrite)
        {
            ThrowNotSupportedException();
        }
    }

    /// <summary>
    /// Validates that a given <see cref="Stream"/> instance hasn't been disposed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidateDisposed(bool disposed)
    {
        if (disposed)
        {
            ThrowObjectDisposedException();
        }
    }
    /// <summary>
    /// Gets a standard <see cref="NotSupportedException"/> instance for a stream.
    /// </summary>
    /// <returns>A <see cref="NotSupportedException"/> with the standard text.</returns>
    public static Exception GetNotSupportedException()
    {
        return new NotSupportedException("The requested operation is not supported for this stream.");
    }

    /// <summary>
    /// Throws a <see cref="NotSupportedException"/> when trying to perform a not supported operation.
    /// </summary>
    [DoesNotReturn]
    public static void ThrowNotSupportedException()
    {
        throw GetNotSupportedException();
    }

    /// <summary>
    /// Throws an <see cref="ArgumentException"/> when trying to write too many bytes to the target stream.
    /// </summary>
    [DoesNotReturn]
    public static void ThrowArgumentExceptionForEndOfStreamOnWrite()
    {
        throw new ArgumentException("The current stream can't contain the requested input data.");
    }

    /// <summary>
    /// Throws an <see cref="ArgumentException"/> when using an invalid seek mode.
    /// </summary>
    /// <returns>Nothing, as this method throws unconditionally.</returns>
    public static long ThrowArgumentExceptionForSeekOrigin()
    {
        throw new ArgumentException("The input seek mode is not valid.", "origin");
    }

    /// <summary>
    /// Throws an <see cref="ArgumentOutOfRangeException"/> when setting the <see cref="Stream.Position"/> property.
    /// </summary>
    private static void ThrowArgumentOutOfRangeExceptionForPosition()
    {
        throw new ArgumentOutOfRangeException(nameof(Stream.Position), "The value for the property was not in the valid range.");
    }

    /// <summary>
    /// Throws an <see cref="ArgumentNullException"/> when an input buffer is <see langword="null"/>.
    /// </summary>
    [DoesNotReturn]
    private static void ThrowArgumentNullExceptionForBuffer()
    {
        throw new ArgumentNullException("buffer", "The buffer is null.");
    }
    
    /// <summary>
    /// Throws an <see cref="ArgumentOutOfRangeException"/> when the input count is negative.
    /// </summary>
    [DoesNotReturn]
    private static void ThrowArgumentOutOfRangeExceptionForOffset()
    {
        throw new ArgumentOutOfRangeException("offset", "Offset can't be negative.");
    }

    /// <summary>
    /// Throws an <see cref="ArgumentOutOfRangeException"/> when the input count is negative.
    /// </summary>
    [DoesNotReturn]
    private static void ThrowArgumentOutOfRangeExceptionForCount()
    {
        throw new ArgumentOutOfRangeException("count", "Count can't be negative.");
    }

    /// <summary>
    /// Throws an <see cref="ArgumentException"/> when the sum of offset and count exceeds the length of the target buffer.
    /// </summary>
    [DoesNotReturn]
    private static void ThrowArgumentExceptionForLength()
    {
        throw new ArgumentException("The sum of offset and count can't be larger than the buffer length.", "buffer");
    }

    /// <summary>
    /// Throws an <see cref="ObjectDisposedException"/> when using a disposed <see cref="Stream"/> instance.
    /// </summary>
    [DoesNotReturn]
    private static void ThrowObjectDisposedException()
    {
        throw new ObjectDisposedException("source", "The current stream has already been disposed");
    }
}