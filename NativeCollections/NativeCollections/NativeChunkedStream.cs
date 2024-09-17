﻿using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if UNITY_2021_3_OR_NEWER || GODOT
using System;
#endif

#pragma warning disable CA2208
#pragma warning disable CS8632

// ReSharper disable ALL

namespace NativeCollections
{
    /// <summary>
    ///     Native chunked stream
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly unsafe struct NativeChunkedStream : IDisposable, IEquatable<NativeChunkedStream>
    {
        /// <summary>
        ///     Handle
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeChunkedStreamHandle
        {
            /// <summary>
            ///     Head
            /// </summary>
            public NativeMemoryChunk* Head;

            /// <summary>
            ///     Tail
            /// </summary>
            public NativeMemoryChunk* Tail;

            /// <summary>
            ///     Free list
            /// </summary>
            public NativeMemoryChunk* FreeList;

            /// <summary>
            ///     Chunks
            /// </summary>
            public int Chunks;

            /// <summary>
            ///     Free chunks
            /// </summary>
            public int FreeChunks;

            /// <summary>
            ///     Max free chunks
            /// </summary>
            public int MaxFreeChunks;

            /// <summary>
            ///     Size
            /// </summary>
            public int Size;

            /// <summary>
            ///     Read offset
            /// </summary>
            public int ReadOffset;

            /// <summary>
            ///     Write offset
            /// </summary>
            public int WriteOffset;

            /// <summary>
            ///     Length
            /// </summary>
            public int Length;
        }

        /// <summary>
        ///     Chunk
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Size = 16)]
        private struct NativeMemoryChunk
        {
            /// <summary>
            ///     Next
            /// </summary>
            [FieldOffset(0)] public NativeMemoryChunk* Next;

            /// <summary>
            ///     Array
            /// </summary>
            [FieldOffset(8)] public fixed byte Array[1];
        }

        /// <summary>
        ///     Handle
        /// </summary>
        private readonly NativeChunkedStreamHandle* _handle;

        /// <summary>
        ///     Structure
        /// </summary>
        /// <param name="size">Size</param>
        /// <param name="maxFreeChunks">Max free chunks</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public NativeChunkedStream(int size, int maxFreeChunks)
        {
            if (size <= 0)
                throw new ArgumentOutOfRangeException(nameof(size), size, "MustBePositive");
            if (maxFreeChunks < 0)
                throw new ArgumentOutOfRangeException(nameof(maxFreeChunks), maxFreeChunks, "MustBeNonNegative");
            _handle = (NativeChunkedStreamHandle*)NativeMemoryAllocator.Alloc((uint)sizeof(NativeChunkedStreamHandle));
            var chunk = (NativeMemoryChunk*)NativeMemoryAllocator.Alloc((uint)(sizeof(NativeMemoryChunk) + size));
            _handle->Head = chunk;
            _handle->Tail = chunk;
            _handle->FreeList = null;
            _handle->Chunks = 1;
            _handle->FreeChunks = 0;
            _handle->MaxFreeChunks = maxFreeChunks;
            _handle->Size = size;
            _handle->ReadOffset = 0;
            _handle->WriteOffset = 0;
            _handle->Length = 0;
        }

        /// <summary>
        ///     Is created
        /// </summary>
        public bool IsCreated => _handle != null;

        /// <summary>
        ///     Chunks
        /// </summary>
        public int Chunks => _handle->Chunks;

        /// <summary>
        ///     Free chunks
        /// </summary>
        public int FreeChunks => _handle->FreeChunks;

        /// <summary>
        ///     Max free chunks
        /// </summary>
        public int MaxFreeChunks => _handle->MaxFreeChunks;

        /// <summary>
        ///     Size
        /// </summary>
        public int Size => _handle->Size;

        /// <summary>
        ///     Length
        /// </summary>
        public int Length => _handle->Length;

        /// <summary>
        ///     Equals
        /// </summary>
        /// <param name="other">Other</param>
        /// <returns>Equals</returns>
        public bool Equals(NativeChunkedStream other) => other == this;

        /// <summary>
        ///     Equals
        /// </summary>
        /// <param name="obj">object</param>
        /// <returns>Equals</returns>
        public override bool Equals(object? obj) => obj is NativeChunkedStream nativeChunkedStream && nativeChunkedStream == this;

        /// <summary>
        ///     Get hashCode
        /// </summary>
        /// <returns>HashCode</returns>
        public override int GetHashCode() => (int)(nint)_handle;

        /// <summary>
        ///     To string
        /// </summary>
        /// <returns>String</returns>
        public override string ToString() => "NativeChunkedStream";

        /// <summary>
        ///     Equals
        /// </summary>
        /// <param name="left">Left</param>
        /// <param name="right">Right</param>
        /// <returns>Equals</returns>
        public static bool operator ==(NativeChunkedStream left, NativeChunkedStream right) => left._handle == right._handle;

        /// <summary>
        ///     Not equals
        /// </summary>
        /// <param name="left">Left</param>
        /// <param name="right">Right</param>
        /// <returns>Not equals</returns>
        public static bool operator !=(NativeChunkedStream left, NativeChunkedStream right) => left._handle != right._handle;

        /// <summary>
        ///     Dispose
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (_handle == null)
                return;
            var node = _handle->Head;
            while (_handle->Chunks > 0)
            {
                _handle->Chunks--;
                var temp = node;
                node = node->Next;
                NativeMemoryAllocator.Free(temp);
            }

            node = _handle->FreeList;
            while (_handle->FreeChunks > 0)
            {
                _handle->FreeChunks--;
                var temp = node;
                node = node->Next;
                NativeMemoryAllocator.Free(temp);
            }

            NativeMemoryAllocator.Free(_handle);
        }

        /// <summary>
        ///     Read
        /// </summary>
        /// <param name="buffer">Buffer</param>
        /// <param name="length">Length</param>
        /// <returns>Bytes</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Read(byte* buffer, int length)
        {
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length), length, "MustBeNonNegative");
            if (length > _handle->Length)
                length = _handle->Length;
            var bytesRead = 0;
            while (bytesRead < length)
            {
                var slice = _handle->Size - _handle->ReadOffset;
                var remaining = length - bytesRead;
                if (slice > remaining)
                {
                    Unsafe.CopyBlockUnaligned(buffer + bytesRead, _handle->Head->Array + _handle->ReadOffset, (uint)remaining);
                    _handle->ReadOffset += remaining;
                    break;
                }

                Unsafe.CopyBlockUnaligned(buffer + bytesRead, _handle->Head->Array + _handle->ReadOffset, (uint)slice);
                _handle->ReadOffset = 0;
                if (_handle->Chunks != 1)
                {
                    bytesRead += slice;
                    var chunk = _handle->Head;
                    _handle->Head = chunk->Next;
                    if (_handle->FreeChunks == _handle->MaxFreeChunks)
                    {
                        NativeMemoryAllocator.Free(chunk);
                    }
                    else
                    {
                        chunk->Next = _handle->FreeList;
                        _handle->FreeList = chunk;
                        _handle->FreeChunks++;
                    }

                    _handle->Chunks--;
                    continue;
                }

                _handle->WriteOffset = 0;
                break;
            }

            _handle->Length -= length;
            return length;
        }

        /// <summary>
        ///     Write
        /// </summary>
        /// <param name="buffer">Buffer</param>
        /// <param name="length">Length</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(byte* buffer, int length)
        {
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length), length, "MustBeNonNegative");
            var bytesWritten = 0;
            while (bytesWritten < length)
            {
                var slice = _handle->Size - _handle->WriteOffset;
                var remaining = length - bytesWritten;
                if (slice >= remaining)
                {
                    Unsafe.CopyBlockUnaligned(_handle->Tail->Array + _handle->WriteOffset, buffer + bytesWritten, (uint)remaining);
                    _handle->WriteOffset += remaining;
                    break;
                }

                Unsafe.CopyBlockUnaligned(_handle->Tail->Array + _handle->WriteOffset, buffer + bytesWritten, (uint)slice);
                bytesWritten += slice;
                NativeMemoryChunk* chunk;
                if (_handle->FreeChunks == 0)
                {
                    chunk = (NativeMemoryChunk*)NativeMemoryAllocator.Alloc((uint)(sizeof(NativeMemoryChunk) + _handle->Size));
                }
                else
                {
                    chunk = _handle->FreeList;
                    _handle->FreeList = chunk->Next;
                    _handle->FreeChunks--;
                }

                _handle->Tail->Next = chunk;
                _handle->Tail = chunk;
                _handle->Chunks++;
                _handle->WriteOffset = 0;
            }

            _handle->Length += length;
        }

        /// <summary>
        ///     Ensure capacity
        /// </summary>
        /// <param name="capacity">Capacity</param>
        /// <returns>New capacity</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int EnsureCapacity(int capacity)
        {
            if (capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "MustBeNonNegative");
            if (capacity > _handle->MaxFreeChunks)
                capacity = _handle->MaxFreeChunks;
            while (_handle->FreeChunks < capacity)
            {
                _handle->FreeChunks++;
                var chunk = (NativeMemoryChunk*)NativeMemoryAllocator.Alloc((uint)(sizeof(NativeMemoryChunk) + _handle->Size));
                chunk->Next = _handle->FreeList;
                _handle->FreeList = chunk;
            }

            return _handle->FreeChunks;
        }

        /// <summary>
        ///     Trim excess
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void TrimExcess()
        {
            var node = _handle->FreeList;
            while (_handle->FreeChunks > 0)
            {
                _handle->FreeChunks--;
                var temp = node;
                node = node->Next;
                NativeMemoryAllocator.Free(temp);
            }

            _handle->FreeList = node;
        }

        /// <summary>
        ///     Trim excess
        /// </summary>
        /// <param name="capacity">Remaining free slabs</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int TrimExcess(int capacity)
        {
            if (capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "MustBeNonNegative");
            var node = _handle->FreeList;
            while (_handle->FreeChunks > capacity)
            {
                _handle->FreeChunks--;
                var temp = node;
                node = node->Next;
                NativeMemoryAllocator.Free(temp);
            }

            _handle->FreeList = node;
            return _handle->FreeChunks;
        }

        /// <summary>
        ///     Empty
        /// </summary>
        public static NativeChunkedStream Empty => new();
    }
}