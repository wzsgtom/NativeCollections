﻿using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if UNITY_2021_3_OR_NEWER || GODOT
using System;
using System.Threading;
#endif

#pragma warning disable CA2208
#pragma warning disable CS8632

// ReSharper disable ConvertToAutoProperty
// ReSharper disable ConvertToAutoPropertyWhenPossible
// ReSharper disable ConvertToAutoPropertyWithPrivateSetter
// ReSharper disable ConvertIfStatementToSwitchStatement
// ReSharper disable PossibleNullReferenceException
// ReSharper disable MemberHidesStaticFromOuterClass
// ReSharper disable RedundantAssignment
// ReSharper disable StaticMemberInGenericType
// ReSharper disable ByRefArgumentIsVolatileField
// ReSharper disable ConvertSwitchStatementToSwitchExpression

namespace NativeCollections
{
    /// <summary>
    ///     Native concurrentHashSet
    ///     (Slower than ConcurrentHashSet)
    /// </summary>
    /// <typeparam name="T">Type</typeparam>
    [StructLayout(LayoutKind.Sequential)]
    public readonly unsafe struct NativeConcurrentHashSet<T> : IDisposable, IEquatable<NativeConcurrentHashSet<T>> where T : unmanaged, IEquatable<T>
    {
        /// <summary>
        ///     Handle
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeConcurrentHashSetHandle
        {
            /// <summary>
            ///     Tables
            /// </summary>
            public volatile Tables* Tables;

            /// <summary>
            ///     Budget
            /// </summary>
            public int Budget;

            /// <summary>
            ///     Grow lock array
            /// </summary>
            public bool GrowLockArray;

            /// <summary>
            ///     Node pool
            /// </summary>
            public NativeMemoryPool NodePool;

            /// <summary>
            ///     Node lock
            /// </summary>
            public NativeConcurrentSpinLock NodeLock;
        }

        /// <summary>
        ///     Handle
        /// </summary>
        private readonly NativeConcurrentHashSetHandle* _handle;

        /// <summary>
        ///     Structure
        /// </summary>
        /// <param name="size">Size</param>
        /// <param name="maxFreeSlabs">Max free slabs</param>
        /// <param name="concurrencyLevel">Concurrency level</param>
        /// <param name="capacity">Capacity</param>
        /// <param name="growLockArray">Grow lock array</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public NativeConcurrentHashSet(int size, int maxFreeSlabs, int concurrencyLevel, int capacity, bool growLockArray)
        {
            var nodePool = new NativeMemoryPool(size, sizeof(Node), maxFreeSlabs);
            if (concurrencyLevel <= 0)
                concurrencyLevel = Environment.ProcessorCount;
            if (capacity < concurrencyLevel)
                capacity = concurrencyLevel;
            capacity = HashHelpers.GetPrime(capacity);
            var locks = new NativeArray<NativeMonitorLock>(concurrencyLevel);
            locks[0] = new NativeMonitorLock(locks);
            for (var i = 1; i < locks.Length; ++i)
                locks[i] = new NativeMonitorLock(new object());
            var countPerLock = new NativeArray<int>(locks.Length, true);
            var buckets = new NativeArray<VolatileNode>(capacity, true);
            _handle = (NativeConcurrentHashSetHandle*)NativeMemoryAllocator.Alloc(sizeof(NativeConcurrentHashSetHandle));
            _handle->Tables = (Tables*)NativeMemoryAllocator.Alloc(sizeof(Tables));
            _handle->Tables->Initialize(buckets, locks, countPerLock);
            _handle->GrowLockArray = growLockArray;
            _handle->Budget = buckets.Length / locks.Length;
            _handle->NodePool = nodePool;
            _handle->NodeLock = new NativeConcurrentSpinLock(-1);
        }

        /// <summary>
        ///     Is created
        /// </summary>
        public bool IsCreated => _handle != null;

        /// <summary>
        ///     Is created
        /// </summary>
        public bool IsEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (!AreAllBucketsEmpty())
                    return false;
                var locksAcquired = 0;
                try
                {
                    AcquireAllLocks(ref locksAcquired);
                    return AreAllBucketsEmpty();
                }
                finally
                {
                    ReleaseLocks(locksAcquired);
                }
            }
        }

        /// <summary>
        ///     Count
        /// </summary>
        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                var locksAcquired = 0;
                try
                {
                    AcquireAllLocks(ref locksAcquired);
                    return GetCountNoLocks();
                }
                finally
                {
                    ReleaseLocks(locksAcquired);
                }
            }
        }

        /// <summary>
        ///     Equals
        /// </summary>
        /// <param name="other">Other</param>
        /// <returns>Equals</returns>
        public bool Equals(NativeConcurrentHashSet<T> other) => other == this;

        /// <summary>
        ///     Equals
        /// </summary>
        /// <param name="obj">object</param>
        /// <returns>Equals</returns>
        public override bool Equals(object? obj) => obj is NativeConcurrentHashSet<T> nativeConcurrentHashSet && nativeConcurrentHashSet == this;

        /// <summary>
        ///     Get hashCode
        /// </summary>
        /// <returns>HashCode</returns>
        public override int GetHashCode() => (int)(nint)_handle;

        /// <summary>
        ///     To string
        /// </summary>
        /// <returns>String</returns>
        public override string ToString() => $"NativeConcurrentHashSet<{typeof(T).Name}>";

        /// <summary>
        ///     Equals
        /// </summary>
        /// <param name="left">Left</param>
        /// <param name="right">Right</param>
        /// <returns>Equals</returns>
        public static bool operator ==(NativeConcurrentHashSet<T> left, NativeConcurrentHashSet<T> right) => left._handle == right._handle;

        /// <summary>
        ///     Not equals
        /// </summary>
        /// <param name="left">Left</param>
        /// <param name="right">Right</param>
        /// <returns>Not equals</returns>
        public static bool operator !=(NativeConcurrentHashSet<T> left, NativeConcurrentHashSet<T> right) => left._handle != right._handle;

        /// <summary>
        ///     Dispose
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (_handle == null)
                return;
            _handle->Tables->Dispose();
            _handle->NodePool.Dispose();
            _handle->NodeLock.Dispose();
            NativeMemoryAllocator.Free(_handle);
        }

        /// <summary>
        ///     Clear
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            var locksAcquired = 0;
            try
            {
                AcquireAllLocks(ref locksAcquired);
                if (AreAllBucketsEmpty())
                    return;
                foreach (var bucket in _handle->Tables->Buckets)
                {
                    var node = (Node*)bucket.Node;
                    while (node != null)
                    {
                        var temp = node;
                        node = node->Next;
                        _handle->NodePool.Return(temp);
                    }
                }

                var length = HashHelpers.GetPrime(31);
                if (_handle->Tables->Buckets.Length != length)
                {
                    _handle->Tables->Buckets.Dispose();
                    _handle->Tables->Buckets = new NativeArray<VolatileNode>(length, true);
                }
                else
                {
                    _handle->Tables->Buckets.Clear();
                }

                _handle->Tables->CountPerLock.Clear();
                var budget = _handle->Tables->Buckets.Length / _handle->Tables->Locks.Length;
                _handle->Budget = budget >= 1 ? budget : 1;
            }
            finally
            {
                ReleaseLocks(locksAcquired);
            }
        }

        /// <summary>
        ///     Add
        /// </summary>
        /// <param name="key">Key</param>
        /// <returns>Added</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Add(in T key) => TryAddInternal(_handle->Tables, key);

        /// <summary>
        ///     Remove
        /// </summary>
        /// <param name="key">Key</param>
        /// <returns>Removed</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove(in T key)
        {
            var tables = _handle->Tables;
            var hashCode = key.GetHashCode();
            while (true)
            {
                var locks = tables->Locks;
                ref var bucket = ref GetBucketAndLock(tables, hashCode, out var lockNo);
                if (tables->CountPerLock[lockNo] != 0)
                {
                    locks[lockNo].Enter();
                    try
                    {
                        if (tables != _handle->Tables)
                        {
                            tables = _handle->Tables;
                            continue;
                        }

                        Node* prev = null;
                        for (var curr = (Node*)bucket; curr != null; curr = curr->Next)
                        {
                            if (hashCode == curr->HashCode && curr->Key.Equals(key))
                            {
                                if (prev == null)
                                    Volatile.Write(ref bucket, (nint)curr->Next);
                                else
                                    prev->Next = curr->Next;
                                _handle->NodeLock.Enter();
                                _handle->NodePool.Return(curr);
                                _handle->NodeLock.Exit();
                                tables->CountPerLock[lockNo]--;
                                return true;
                            }

                            prev = curr;
                        }
                    }
                    finally
                    {
                        locks[lockNo].Exit();
                    }
                }

                return false;
            }
        }

        /// <summary>
        ///     Contains key
        /// </summary>
        /// <param name="key">Key</param>
        /// <returns>Contains key</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(in T key)
        {
            var tables = _handle->Tables;
            var hashCode = key.GetHashCode();
            for (var node = (Node*)GetBucket(tables, hashCode); node != null; node = node->Next)
            {
                if (hashCode == node->HashCode && node->Key.Equals(key))
                    return true;
            }

            return false;
        }

        /// <summary>
        ///     Try to get the actual value
        /// </summary>
        /// <param name="equalValue">Equal value</param>
        /// <param name="actualValue">Actual value</param>
        /// <returns>Got</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(in T equalValue, out T actualValue)
        {
            var tables = _handle->Tables;
            var hashCode = equalValue.GetHashCode();
            for (var node = (Node*)GetBucket(tables, hashCode); node != null; node = node->Next)
            {
                if (hashCode == node->HashCode && node->Key.Equals(equalValue))
                {
                    actualValue = node->Key;
                    return true;
                }
            }

            actualValue = default;
            return false;
        }

        /// <summary>
        ///     Check all buckets are empty
        /// </summary>
        /// <returns>All buckets are empty</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool AreAllBucketsEmpty()
        {
#if NET8_0_OR_GREATER
            return !_handle->Tables->CountPerLock.AsSpan().ContainsAnyExcept(0);
#elif NET7_0_OR_GREATER
            return !(_handle->Tables->CountPerLock.AsSpan().IndexOfAnyExcept(0) >= 0);
#else
            for (var i = 0; i < _handle->Tables->CountPerLock.Length; ++i)
            {
                if (_handle->Tables->CountPerLock[i] != 0)
                    return false;
            }

            return true;
#endif
        }

        /// <summary>
        ///     Grow table
        /// </summary>
        /// <param name="tables">Tables</param>
        /// <param name="resizeDesired">Resize desired</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void GrowTable(Tables* tables, bool resizeDesired)
        {
            var locksAcquired = 0;
            try
            {
                AcquireFirstLock(ref locksAcquired);
                if (tables != _handle->Tables)
                    return;
                var newLength = tables->Buckets.Length;
                if (resizeDesired)
                {
                    if (GetCountNoLocks() < tables->Buckets.Length / 4)
                    {
                        _handle->Budget = 2 * _handle->Budget;
                        if (_handle->Budget < 0)
                            _handle->Budget = int.MaxValue;
                        return;
                    }

                    if ((newLength = tables->Buckets.Length * 2) < 0 || (newLength = HashHelpers.GetPrime(newLength)) > 2147483591)
                    {
                        newLength = 2147483591;
                        _handle->Budget = int.MaxValue;
                    }
                }

                var newLocks = tables->Locks;
                if (_handle->GrowLockArray && tables->Locks.Length < 1024)
                {
                    newLocks = new NativeArray<NativeMonitorLock>(tables->Locks.Length * 2);
                    Unsafe.CopyBlockUnaligned(newLocks.Array, tables->Locks.Array, (uint)(tables->Locks.Length * sizeof(NativeMonitorLock)));
                    for (var i = tables->Locks.Length; i < newLocks.Length; ++i)
                        newLocks[i] = new NativeMonitorLock(new object());
                }

                var newBuckets = new NativeArray<VolatileNode>(newLength, true);
                var newCountPerLock = new NativeArray<int>(newLocks.Length, true);
                var newTables = (Tables*)NativeMemoryAllocator.Alloc(sizeof(Tables));
                newTables->Initialize(newBuckets, newLocks, newCountPerLock);
                AcquirePostFirstLock(tables, ref locksAcquired);
                foreach (var bucket in tables->Buckets)
                {
                    var current = (Node*)bucket.Node;
                    while (current != null)
                    {
                        var hashCode = current->HashCode;
                        var next = current->Next;
                        ref var newBucket = ref GetBucketAndLock(newTables, hashCode, out var newLockNo);
                        var newNode = current;
                        newNode->Initialize(current->Key, hashCode, (Node*)newBucket);
                        newBucket = (nint)newNode;
                        checked
                        {
                            newCountPerLock[newLockNo]++;
                        }

                        current = next;
                    }
                }

                var budget = newBuckets.Length / newLocks.Length;
                _handle->Budget = budget >= 1 ? budget : 1;
                _handle->Tables->Buckets.Dispose();
                if (_handle->Tables->Locks != newLocks)
                    _handle->Tables->Locks.Dispose();
                _handle->Tables->CountPerLock.Dispose();
                NativeMemoryAllocator.Free(_handle->Tables);
                _handle->Tables = newTables;
            }
            finally
            {
                ReleaseLocks(locksAcquired);
            }
        }

        /// <summary>
        ///     Acquire all locks
        /// </summary>
        /// <param name="locksAcquired">Locks acquired</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AcquireAllLocks(ref int locksAcquired)
        {
            AcquireFirstLock(ref locksAcquired);
            AcquirePostFirstLock(_handle->Tables, ref locksAcquired);
        }

        /// <summary>
        ///     Acquire first lock
        /// </summary>
        /// <param name="locksAcquired">Locks acquired</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AcquireFirstLock(ref int locksAcquired)
        {
            var locks = _handle->Tables->Locks;
            locks[0].Enter();
            locksAcquired = 1;
        }

        /// <summary>
        ///     Acquire post first locks
        /// </summary>
        /// <param name="tables">Tables</param>
        /// <param name="locksAcquired">Locks acquired</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AcquirePostFirstLock(Tables* tables, ref int locksAcquired)
        {
            var locks = tables->Locks;
            for (var i = 1; i < locks.Length; ++i)
            {
                locks[i].Enter();
                locksAcquired++;
            }
        }

        /// <summary>
        ///     Release locks
        /// </summary>
        /// <param name="locksAcquired">Locks acquired</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReleaseLocks(int locksAcquired)
        {
            var locks = _handle->Tables->Locks;
            for (var i = 0; i < locksAcquired; ++i)
                locks[i].Exit();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryAddInternal(Tables* tables, in T key)
        {
            var hashCode = key.GetHashCode();
            while (true)
            {
                var locks = tables->Locks;
                ref var bucket = ref GetBucketAndLock(tables, hashCode, out var lockNo);
                var resizeDesired = false;
                var lockTaken = false;
                try
                {
                    locks[lockNo].Enter(ref lockTaken);
                    if (tables != _handle->Tables)
                    {
                        tables = _handle->Tables;
                        continue;
                    }

                    for (var node = (Node*)bucket; node != null; node = node->Next)
                    {
                        if (hashCode == node->HashCode && node->Key.Equals(key))
                            return false;
                    }

                    _handle->NodeLock.Enter();
                    var resultNode = (Node*)_handle->NodePool.Rent();
                    _handle->NodeLock.Exit();
                    resultNode->Initialize(key, hashCode, (Node*)bucket);
                    Volatile.Write(ref bucket, (nint)resultNode);
                    checked
                    {
                        tables->CountPerLock[lockNo]++;
                    }

                    if (tables->CountPerLock[lockNo] > _handle->Budget)
                        resizeDesired = true;
                }
                finally
                {
                    if (lockTaken)
                        locks[lockNo].Exit();
                }

                if (resizeDesired)
                    GrowTable(tables, resizeDesired);
                return true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetCountNoLocks()
        {
            var count = 0;
            foreach (var value in _handle->Tables->CountPerLock)
            {
                checked
                {
                    count += value;
                }
            }

            return count;
        }

        /// <summary>
        ///     Get bucket
        /// </summary>
        /// <param name="tables">Tables</param>
        /// <param name="hashCode">HashCode</param>
        /// <returns>Bucket</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nint GetBucket(Tables* tables, int hashCode)
        {
            var buckets = tables->Buckets;
            return IntPtr.Size == 8 ? buckets[HashHelpers.FastMod((uint)hashCode, (uint)buckets.Length, tables->FastModBucketsMultiplier)].Node : buckets[(uint)hashCode % (uint)buckets.Length].Node;
        }

        /// <summary>
        ///     Get bucket and lock
        /// </summary>
        /// <param name="tables">Tables</param>
        /// <param name="hashCode">HashCode</param>
        /// <param name="lockNo">Lock no</param>
        /// <returns>Bucket</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ref nint GetBucketAndLock(Tables* tables, int hashCode, out uint lockNo)
        {
            var buckets = tables->Buckets;
            var bucketNo = IntPtr.Size == 8 ? HashHelpers.FastMod((uint)hashCode, (uint)buckets.Length, tables->FastModBucketsMultiplier) : (uint)hashCode % (uint)buckets.Length;
            lockNo = bucketNo % (uint)tables->Locks.Length;
            return ref buckets[bucketNo].Node;
        }

        /// <summary>
        ///     Volatile node
        /// </summary>
        private struct VolatileNode
        {
            /// <summary>
            ///     Node
            /// </summary>
            public volatile nint Node;
        }

        /// <summary>
        ///     Node
        /// </summary>
        private struct Node
        {
            /// <summary>
            ///     Key
            /// </summary>
            public T Key;

            /// <summary>
            ///     Next
            /// </summary>
            public volatile Node* Next;

            /// <summary>
            ///     HashCode
            /// </summary>
            public int HashCode;

            /// <summary>
            ///     Initialize
            /// </summary>
            /// <param name="key">Key</param>
            /// <param name="hashCode">HashCode</param>
            /// <param name="next">Next</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Initialize(in T key, int hashCode, Node* next)
            {
                Key = key;
                Next = next;
                HashCode = hashCode;
            }
        }

        /// <summary>
        ///     Tables
        /// </summary>
        private struct Tables
        {
            /// <summary>
            ///     Buckets
            /// </summary>
            public NativeArray<VolatileNode> Buckets;

            /// <summary>
            ///     Fast mod buckets multiplier
            /// </summary>
            public ulong FastModBucketsMultiplier;

            /// <summary>
            ///     Locks
            /// </summary>
            public NativeArray<NativeMonitorLock> Locks;

            /// <summary>
            ///     Count per lock
            /// </summary>
            public NativeArray<int> CountPerLock;

            /// <summary>
            ///     Initialize
            /// </summary>
            /// <param name="buckets">Buckets</param>
            /// <param name="locks">Locks</param>
            /// <param name="countPerLock">Count per lock</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Initialize(in NativeArray<VolatileNode> buckets, in NativeArray<NativeMonitorLock> locks, in NativeArray<int> countPerLock)
            {
                Buckets = buckets;
                Locks = locks;
                CountPerLock = countPerLock;
                FastModBucketsMultiplier = IntPtr.Size == 8 ? HashHelpers.GetFastModMultiplier((uint)buckets.Length) : 0;
            }

            /// <summary>
            ///     Dispose
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Dispose()
            {
                Buckets.Dispose();
                for (var i = 0; i < Locks.Length; ++i)
                    Locks[i].Dispose();
                Locks.Dispose();
                CountPerLock.Dispose();
            }
        }

        /// <summary>
        ///     Get enumerator
        /// </summary>
        /// <returns>Enumerator</returns>
        public Enumerator GetEnumerator() => new(this);

        /// <summary>
        ///     Enumerator
        /// </summary>
        public struct Enumerator
        {
            /// <summary>
            ///     NativeConcurrentHashSet
            /// </summary>
            private readonly NativeConcurrentHashSet<T> _nativeConcurrentHashSet;

            /// <summary>
            ///     Buckets
            /// </summary>
            private NativeArray<VolatileNode> _buckets;

            /// <summary>
            ///     Node
            /// </summary>
            private Node* _node;

            /// <summary>
            ///     Index
            /// </summary>
            private int _index;

            /// <summary>
            ///     State
            /// </summary>
            private int _state;

            /// <summary>
            ///     State uninitialized
            /// </summary>
            private const int STATE_UNINITIALIZED = 0;

            /// <summary>
            ///     State outer loop
            /// </summary>
            private const int STATE_OUTER_LOOP = 1;

            /// <summary>
            ///     State inner loop
            /// </summary>
            private const int STATE_INNER_LOOP = 2;

            /// <summary>
            ///     State done
            /// </summary>
            private const int STATE_DONE = 3;

            /// <summary>
            ///     Current
            /// </summary>
            private T _current;

            /// <summary>
            ///     Structure
            /// </summary>
            /// <param name="nativeConcurrentHashSet">NativeConcurrentHashSet</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal Enumerator(NativeConcurrentHashSet<T> nativeConcurrentHashSet)
            {
                _nativeConcurrentHashSet = nativeConcurrentHashSet;
                _index = -1;
                _buckets = default;
                _node = null;
                _state = 0;
                _current = default;
            }

            /// <summary>
            ///     Move next
            /// </summary>
            /// <returns>Moved</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                switch (_state)
                {
                    case STATE_UNINITIALIZED:
                        _buckets = _nativeConcurrentHashSet._handle->Tables->Buckets;
                        _index = -1;
                        goto case STATE_OUTER_LOOP;
                    case STATE_OUTER_LOOP:
                        var buckets = _buckets;
                        var i = ++_index;
                        if ((uint)i < (uint)buckets.Length)
                        {
                            _node = (Node*)buckets[i].Node;
                            _state = STATE_INNER_LOOP;
                            goto case STATE_INNER_LOOP;
                        }

                        goto default;
                    case STATE_INNER_LOOP:
                        if (_node != null)
                        {
                            var node = _node;
                            _current = node->Key;
                            _node = node->Next;
                            return true;
                        }

                        goto case STATE_OUTER_LOOP;
                    default:
                        _state = STATE_DONE;
                        return false;
                }
            }

            /// <summary>
            ///     Current
            /// </summary>
            public T Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _current;
            }
        }
    }

    /// <summary>
    ///     Native concurrentHashSet type props
    /// </summary>
    /// <typeparam name="T">Type</typeparam>
    internal static class NativeConcurrentHashSetTypeProps<T> where T : unmanaged, IEquatable<T>
    {
        /// <summary>
        ///     Is write atomic
        /// </summary>
        public static readonly bool IsWriteAtomic = IsWriteAtomicPrivate();

        /// <summary>
        ///     Is write atomic
        /// </summary>
        /// <returns>Is write atomic</returns>
        private static bool IsWriteAtomicPrivate()
        {
            if (typeof(T) == typeof(IntPtr) || typeof(T) == typeof(UIntPtr))
                return true;
            switch (Type.GetTypeCode(typeof(T)))
            {
                case TypeCode.Boolean:
                case TypeCode.Byte:
                case TypeCode.Char:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.SByte:
                case TypeCode.Single:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                    return true;
                case TypeCode.Double:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    return IntPtr.Size == 8;
                default:
                    return false;
            }
        }
    }
}