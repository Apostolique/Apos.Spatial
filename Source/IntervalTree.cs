/*
    Cute Framework
    Copyright (C) 2021 Randy Gaul https://randygaul.net
    This software is provided 'as-is', without any express or implied
    warranty.  In no event will the authors be held liable for any damages
    arising from the use of this software.
    Permission is granted to anyone to use this software for any purpose,
    including commercial applications, and to alter it and redistribute it
    freely, subject to the following restrictions:
    1. The origin of this software must not be misrepresented; you must not
    claim that you wrote the original software. If you use this software
    in a product, an acknowledgment in the product documentation would be
    appreciated but is not required.
    2. Altered source versions must be plainly marked as such, and must not be
    misrepresented as being the original software.
    3. This notice may not be removed or altered from any source distribution.
*/

using System;
using System.Collections;
using System.Collections.Generic;

namespace Apos.Spatial {
    /// <summary>
    /// An interval tree is a dynamic partition data structure. It allows you to efficiently query the space in a game world.
    /// </summary>
    /// <typeparam name="T">Type of objects to add to the tree.</typeparam>
    /// <remarks>
    /// Creates a new tree.
    /// </remarks>
    /// <param name="initialCapacity">Amount of nodes the tree can hold before it needs to be resized.</param>
    /// <param name="expandConstant">It expands the items that are added so that they don't need to be updated as often. Defaults to 2f.</param>
    /// <param name="moveConstant">It expands the items that are moved so that they don't need to be updated as much. Defaults to 4f.</param>
    public class IntervalTree<T>(int initialCapacity = 64, float expandConstant = 2f, float moveConstant = 4f) : IEnumerable<T> {
        /// <summary>
        /// Used for the broad phase search.
        /// It expands the items that are added so that they don't need to be updated as often.
        /// </summary>
        public float ExpandConstant {
            get => _intervalTreeExpandConstant;
            set {
                _intervalTreeExpandConstant = value;
            }
        }
        /// <summary>
        /// Used for the broad phase search.
        /// It expands the items that are moved so that they don't need to be updated as much.
        /// </summary>
        public float MoveConstant {
            get => _intervalTreeMoveConstant;
            set {
                _intervalTreeMoveConstant = value;
            }
        }
        /// <summary>
        /// Amount of nodes in the tree.
        /// </summary>
        public int NodeCount => _tree.NodeCount;
        /// <summary>
        /// Amount of items in the tree.
        /// </summary>
        public int Count => _tree.Count;

        /// <summary>
        /// Bounds of all the items in the tree.
        /// </summary>
        public Interval? Bounds => _tree.Root != INTERVAL_TREE_NULL_NODE_INDEX ? _tree.Intervals[_tree.Root] : null;

        /// <summary>
        /// Adds a new leaf to the tree, and rebalances as necessary.
        /// </summary>
        /// <param name="interval">An interval for the item.</param>
        /// <param name="item">The item to add to the tree.</param>
        /// <returns>The item's leaf. Use this to update or remove the item later.</returns>
        public int Add(Interval interval, T item) {
            _tree.Count++;
            // Expand interval before adding.
            return InternalAdd(Expand(interval, _intervalTreeExpandConstant), item);
        }

        /// <summary>
        /// Removes a leaf from the tree, and rebalances as necessary.
        /// </summary>
        /// <returns>-1 since the leaf is no longer in the tree.</returns>
        public int Remove(int leaf) {
            if (leaf == INTERVAL_TREE_NULL_NODE_INDEX) return INTERVAL_TREE_NULL_NODE_INDEX;

            _tree.Count--;
            int index = leaf;
            if (_tree.Root == index) {
                _tree.Root = INTERVAL_TREE_NULL_NODE_INDEX;
            } else {
                ref IntervalTreeT.NodeT node = ref _tree.Nodes[index];
                int indexParent = node.IndexParent;
                ref IntervalTreeT.NodeT parent = ref _tree.Nodes[indexParent];

                if (indexParent == _tree.Root) {
                    if (parent.IndexA == index) _tree.Root = parent.IndexB;
                    else _tree.Root = parent.IndexA;
                    _tree.Nodes[_tree.Root].IndexParent = INTERVAL_TREE_NULL_NODE_INDEX;
                } else {
                    int indexGrandparent = parent.IndexParent;
                    ref IntervalTreeT.NodeT grandparent = ref _tree.Nodes[indexGrandparent];

                    if (parent.IndexA == index) {
                        ref IntervalTreeT.NodeT sindexBling = ref _tree.Nodes[parent.IndexB];
                        sindexBling.IndexParent = indexGrandparent;
                        if (grandparent.IndexA == indexParent) grandparent.IndexA = parent.IndexB;
                        else grandparent.IndexB = parent.IndexB;
                    } else {
                        ref IntervalTreeT.NodeT sindexBling = ref _tree.Nodes[parent.IndexA];
                        sindexBling.IndexParent = indexGrandparent;
                        if (grandparent.IndexA == indexParent) grandparent.IndexA = parent.IndexA;
                        else grandparent.IndexB = parent.IndexA;
                    }
                    SRefitHierarchy(indexGrandparent);
                }
                SPushFreelist(indexParent);
            }
            SPushFreelist(index);

            _version++;

            return INTERVAL_TREE_NULL_NODE_INDEX;
        }
        /// <summary>
        /// Clears the whole tree.
        /// </summary>
        /// <param name="initialCapacity">Amount of nodes the tree can hold before it needs to be resized.</param>
        public void Clear(int initialCapacity = 64) {
            _tree = new IntervalTreeT(initialCapacity);
            _version++;
        }

        /// <summary>
        /// Use this function when an interval needs to be updated. Leafs need to be updated whenever the shape
        /// inside the leaf's interval moves. Internally there are some optimizations so that the tree is only
        /// adjusted if the interval is moved enough.
        /// </summary>
        /// <param name="leaf">The leaf to update.</param>
        /// <param name="interval">The new interval.</param>
        /// <returns>Returns true if the leaf was updated, false otherwise.</returns>
        public bool Update(int leaf, Interval interval) {
            if (Contains(_tree.Intervals[leaf], interval)) {
                _tree.Intervals[leaf] = interval;
                return false;
            }

            T item = _tree.Items[leaf]!;
            Remove(leaf);
            Add(interval, item);

            _version++;

            return true;
        }

        /// <summary>
        /// Updates a leaf with a new interval (if needed) with the new `interval` and an `offset` for how far the new
        /// interval will be moving.
        /// This function does more optimizations than `Update` by attempting to use the `offset`
        /// to predict motion and avoid restructuring of the tree.
        /// </summary>
        /// <param name="leaf">The leaf to update.</param>
        /// <param name="interval">The new interval.</param>
        /// <param name="offset">An offset that represents the direction the interval is moving in.</param>
        /// <returns>Returns true if the leaf was updated, false otherwise.</returns>
        public bool Move(int leaf, Interval interval, float offset) {
            interval = Expand(interval, _intervalTreeExpandConstant);
            float delta = offset * _intervalTreeMoveConstant;

            if (delta < 0) {
                interval.X += delta;
                interval.Length -= delta;
            } else {
                interval.Length += delta;
            }

            Interval oldInterval = _tree.Intervals[leaf];
            if (Contains(oldInterval, interval)) {
                Interval bigInterval = Expand(interval, _intervalTreeMoveConstant);
                bool oldIntervalIsNotWayTooHuge = Contains(bigInterval, oldInterval);
                if (oldIntervalIsNotWayTooHuge) {
                    return false;
                }
            }

            T item = _tree.Items[leaf]!;
            Remove(leaf);
            InternalAdd(interval, item);

            _version++;

            return true;
        }

        /// <summary>
        /// Returns the internal "expanded" interval. This is useful for when you want to generate all pairs of
        /// potential overlaps for a specific leaf. Just simply use `Query` on the the return value
        /// of this function.
        /// </summary>
        /// <param name="leaf">The leaf to lookup.</param>
        public Interval GetInterval(int leaf) {
            return _tree.Intervals[leaf];
        }
        /// <summary>
        /// Returns the `item` from `Add`.
        /// </summary>
        /// <param name="leaf">The leaf to lookup.</param>
        public T GetItem(int leaf) {
            return _tree.Items[leaf];
        }

        /// <summary>
        /// Returns every item in the tree. Used implicitly by foreach loops.
        /// </summary>
        public IEnumerator<T> GetEnumerator() {
            return new QueryAll(this);
        }
        IEnumerator IEnumerable.GetEnumerator() {
            return new QueryAll(this);
        }

        /// <summary>
        /// Finds all items overlapping the Vector2 `v`.
        /// </summary>
        /// <param name="v">The position to query.</param>
        public IEnumerable<T> Query(float v) {
            return new QueryInterval(this, new Interval(v, 0f));
        }
        /// <summary>
        /// Finds all items overlapping the Vector2 `interval`.
        /// </summary>
        /// <param name="interval">The interval to query.</param>
        public IEnumerable<T> Query(Interval interval) {
            return new QueryInterval(this, interval);
        }
        /// <summary>
        /// Mostly used for debugging. It returns all the `interval` in the tree including parent nodes.
        /// </summary>
        /// <param name="interval">The interval to query.</param>
        public IEnumerable<Interval> DebugNodes(Interval interval) {
            return new QueryNodes(this, interval);
        }
        /// <summary>
        /// Mostly used for debugging. It returns all the `interval` in the tree including parent nodes.
        /// </summary>
        public IEnumerable<Interval> DebugAllNodes() {
            return new QueryAllNodes(this);
        }

        private int InternalAdd(Interval interval, T item) {
            // Make a new node.
            int newIndex = SPopFreelist(interval, item);
            int searchIndex = _tree.Root;

            // Empty tree, make new root.
            if (searchIndex == INTERVAL_TREE_NULL_NODE_INDEX) {
                _tree.Root = newIndex;
            } else {
                searchIndex = SBranchAndBoundFindOptimalSibling(interval);

                // Make new branch node.
                int branchIndex = SPopFreelist(Interval.Union(interval, _tree.Intervals[searchIndex]));
                ref IntervalTreeT.NodeT branch = ref _tree.Nodes[branchIndex];
                int parentIndex = _tree.Nodes[searchIndex].IndexParent;

                if (parentIndex == INTERVAL_TREE_NULL_NODE_INDEX) {
                    _tree.Root = branchIndex;
                } else {
                    ref IntervalTreeT.NodeT parent = ref _tree.Nodes[parentIndex];

                    // Hookup parent to the new branch.
                    if (parent.IndexA == searchIndex) {
                        parent.IndexA = branchIndex;
                    } else {
                        parent.IndexB = branchIndex;
                    }
                }

                // Assign branch children and parent.
                branch.IndexA = searchIndex;
                branch.IndexB = newIndex;
                branch.IndexParent = parentIndex;
                branch.Height = _tree.Nodes[searchIndex].Height + 1;

                // Assign parent pointers for new the leaf pair, and heights.
                _tree.Nodes[searchIndex].IndexParent = branchIndex;
                _tree.Nodes[newIndex].IndexParent = branchIndex;

                SRefitHierarchy(parentIndex);
            }

            _version++;

            return newIndex;
        }
        private int SBalance(int indexA) {
            //      a
            //    /   \
            //   b     c
            //  / \   / \
            // d   e f   g

            ref IntervalTreeT.NodeT a = ref _tree.Nodes[indexA];
            int indexB = a.IndexA;
            int indexC = a.IndexB;
            if (a.IndexA == INTERVAL_TREE_NULL_NODE_INDEX || a.Height < 2) return indexA;

            ref IntervalTreeT.NodeT b = ref _tree.Nodes[indexB];
            ref IntervalTreeT.NodeT c = ref _tree.Nodes[indexC];
            int balance = c.Height - b.Height;

            // Rotate c up.
            if (balance > 1) {
                int indexF = c.IndexA;
                int indexG = c.IndexB;
                ref IntervalTreeT.NodeT f = ref _tree.Nodes[indexF];
                ref IntervalTreeT.NodeT g = ref _tree.Nodes[indexG];

                // Swap a and c.
                c.IndexA = indexA;
                c.IndexParent = a.IndexParent;
                a.IndexParent = indexC;

                // Hookup a's old parent to c.
                if (c.IndexParent != INTERVAL_TREE_NULL_NODE_INDEX) {
                    if (_tree.Nodes[c.IndexParent].IndexA == indexA) {
                        _tree.Nodes[c.IndexParent].IndexA = indexC;
                    } else {
                        _tree.Nodes[c.IndexParent].IndexB = indexC;
                    }
                } else {
                    _tree.Root = indexC;
                }

                // Rotation, picking f or g to go under a or c respectively.
                //       c
                //      / \
                //     a   ? (f or g)
                //    / \
                //   b   ? (f or g)
                //  / \
                // d   e
                if (f.Height > g.Height) {
                    c.IndexB = indexF;
                    a.IndexB = indexG;
                    g.IndexParent = indexA;
                    _tree.Intervals[indexA] = Interval.Union(_tree.Intervals[indexB], _tree.Intervals[indexG]);
                    _tree.Intervals[indexC] = Interval.Union(_tree.Intervals[indexA], _tree.Intervals[indexF]);

                    a.Height = Math.Max(b.Height, g.Height) + 1;
                    c.Height = Math.Max(a.Height, f.Height) + 1;
                } else {
                    c.IndexB = indexG;
                    a.IndexB = indexF;
                    f.IndexParent = indexA;
                    _tree.Intervals[indexA] = Interval.Union(_tree.Intervals[indexB], _tree.Intervals[indexF]);
                    _tree.Intervals[indexC] = Interval.Union(_tree.Intervals[indexA], _tree.Intervals[indexG]);

                    a.Height = Math.Max(b.Height, f.Height) + 1;
                    c.Height = Math.Max(a.Height, g.Height) + 1;
                }

                return indexC;
            } else if (balance < -1) {
                // Rotate b up.

                int indexD = b.IndexA;
                int indexE = b.IndexB;
                ref IntervalTreeT.NodeT d = ref _tree.Nodes[indexD];
                ref IntervalTreeT.NodeT e = ref _tree.Nodes[indexE];

                // Swap a and b.
                b.IndexA = indexA;
                b.IndexParent = a.IndexParent;
                a.IndexParent = indexB;

                // Hookup a's old parent to b.
                if (b.IndexParent != INTERVAL_TREE_NULL_NODE_INDEX) {
                    if (_tree.Nodes[b.IndexParent].IndexA == indexA) {
                        _tree.Nodes[b.IndexParent].IndexA = indexB;
                    } else {
                        _tree.Nodes[b.IndexParent].IndexB = indexB;
                    }
                } else {
                    _tree.Root = indexB;
                }

                // Rotation, picking d or e to go under a or b respectively.
                //            b
                //           / \
                // (d or e) ?   a
                //             / \
                //   (d or e) ?   c
                //               / \
                //              f   g
                if (d.Height > e.Height) {
                    b.IndexB = indexD;
                    a.IndexA = indexE;
                    e.IndexParent = indexA;
                    _tree.Intervals[indexA] = Interval.Union(_tree.Intervals[indexC], _tree.Intervals[indexE]);
                    _tree.Intervals[indexB] = Interval.Union(_tree.Intervals[indexA], _tree.Intervals[indexD]);

                    a.Height = Math.Max(c.Height, e.Height) + 1;
                    b.Height = Math.Max(a.Height, d.Height) + 1;
                } else {
                    b.IndexB = indexE;
                    a.IndexA = indexD;
                    d.IndexParent = indexA;
                    _tree.Intervals[indexA] = Interval.Union(_tree.Intervals[indexC], _tree.Intervals[indexD]);
                    _tree.Intervals[indexB] = Interval.Union(_tree.Intervals[indexA], _tree.Intervals[indexE]);

                    a.Height = Math.Max(c.Height, d.Height) + 1;
                    b.Height = Math.Max(a.Height, e.Height) + 1;
                }

                return indexB;
            }

            return indexA;
        }

        private void SSyncNode(int index) {
            ref IntervalTreeT.NodeT node = ref _tree.Nodes[index];
            int indexA = node.IndexA;
            int indexB = node.IndexB;
            node.Height = Math.Max(_tree.Nodes[indexA].Height, _tree.Nodes[indexB].Height) + 1;
            _tree.Intervals[index] = Interval.Union(_tree.Intervals[indexA], _tree.Intervals[indexB]);
        }

        private void SRefitHierarchy(int index) {
            while (index != INTERVAL_TREE_NULL_NODE_INDEX) {
                index = SBalance(index);
                SSyncNode(index);
                index = _tree.Nodes[index].IndexParent;
            }
        }

        private void EnsureSize<K>(ref K[] array, int newCapacity) {
            if (array.Length < newCapacity) {
                Array.Resize(ref array, newCapacity);
            }
        }

        private int SPopFreelist(Interval interval, T item = default!) {
            int newIndex = _tree.Freelist;
            if (newIndex == INTERVAL_TREE_NULL_NODE_INDEX) {
                int newCapacity = _tree.NodeCapacity * 2;
                EnsureSize(ref _tree.Nodes, newCapacity);
                EnsureSize(ref _tree.Intervals, newCapacity);
                EnsureSize(ref _tree.Items, newCapacity);

                // Link up new freelist and attach it to pre-existing freelist.
                for (int i = 0; i < _tree.NodeCapacity - 1; i++) {
                    _tree.Nodes[_tree.NodeCapacity + i].IndexA = i + _tree.NodeCapacity + 1;
                }
                _tree.Nodes[newCapacity - 1].IndexA = INTERVAL_TREE_NULL_NODE_INDEX;
                _tree.Freelist = _tree.NodeCapacity;
                newIndex = _tree.Freelist;
                _tree.NodeCapacity = newCapacity;
            }

            _tree.Freelist = _tree.Nodes[newIndex].IndexA;
            _tree.Nodes[newIndex].IndexA = INTERVAL_TREE_NULL_NODE_INDEX;
            _tree.Nodes[newIndex].IndexB = INTERVAL_TREE_NULL_NODE_INDEX;
            _tree.Nodes[newIndex].IndexParent = INTERVAL_TREE_NULL_NODE_INDEX;
            _tree.Nodes[newIndex].Height = 0;
            _tree.Intervals[newIndex] = interval;
            _tree.Items[newIndex] = item;

            _tree.NodeCount++;

            return newIndex;
        }

        private void SPushFreelist(int index) {
            _tree.Nodes[index].IndexA = _tree.Freelist;
            _tree.Items[index] = default!;
            _tree.Freelist = index;
            _tree.NodeCount--;
        }

        private float SDeltaCost(Interval toInsert, Interval candidate) {
            return SurfaceArea(Interval.Union(toInsert, candidate)) - SurfaceArea(candidate);
        }

        // https://en.wikipedia.org/wiki/Branch_and_bound#Generic_version
        private int SBranchAndBoundFindOptimalSibling(Interval toInsert) {
            _queue.Reset();
            _queue.Push(_tree.Root, SDeltaCost(toInsert, _tree.Intervals[_tree.Root]));

            float toInsertSA = SurfaceArea(toInsert);
            float bestCost = float.MaxValue;
            int bestIndex = INTERVAL_TREE_NULL_NODE_INDEX;
            int searchIndex = 0;
            float searchDeltaCost = 0;
            while (_queue.TryPop(ref searchIndex, ref searchDeltaCost)) {
                // Track the best candidate so far.
                Interval searchInterval = _tree.Intervals[searchIndex];
                float cost = SurfaceArea(Interval.Union(toInsert, searchInterval)) + searchDeltaCost;
                if (cost < bestCost) {
                    bestCost = cost;
                    bestIndex = searchIndex;
                }

                // Consider pushing the candidate's children onto the priority queue.
                // Cull subtrees with lower bound metric.
                float deltaCost = SDeltaCost(toInsert, searchInterval) + searchDeltaCost;
                float lowerBound = toInsertSA + deltaCost;
                if (lowerBound < bestCost) {
                    int indexA = _tree.Nodes[searchIndex].IndexA;
                    int indexB = _tree.Nodes[searchIndex].IndexB;
                    if (indexA != INTERVAL_TREE_NULL_NODE_INDEX) {
                        _queue.Push(indexA, deltaCost);
                        _queue.Push(indexB, deltaCost);
                    }
                }
            }

            return bestIndex;
        }

        private float STreeCost(int index) {
            if (index == INTERVAL_TREE_NULL_NODE_INDEX) return 0;
            float costA = STreeCost(_tree.Nodes[index].IndexA);
            float costB = STreeCost(_tree.Nodes[index].IndexB);
            float myCost = SurfaceArea(_tree.Intervals[index]);
            return costA + costB + myCost;
        }

        private static Interval Expand(Interval interval, float v) {
            return new Interval(interval.X - v, interval.Length + v * 2f);
        }
        private static float SurfaceArea(Interval a) {
            return a.Length;
        }
        private static bool Contains(Interval a, Interval b) {
            return a.X <= b.X && b.X + b.Length <= a.X + a.Length;
        }
        private static bool Collide(Interval a, Interval b) {
            bool d0 = b.X + b.Length < a.X;
            bool d1 = a.X + a.Length < b.X;
            return !(d0 || d1);
        }

        private struct IntervalTreeT {
            public IntervalTreeT(int initialCapacity = 0) {
                if (initialCapacity == 0) initialCapacity = 64;
                Nodes = new NodeT[initialCapacity];
                Intervals = new Interval[initialCapacity];
                Items = new T[initialCapacity];
                Root = INTERVAL_TREE_NULL_NODE_INDEX;
                Freelist = 0;
                NodeCapacity = initialCapacity;
                NodeCount = 0;
                Count = 0;

                for (int i = 0; i < Nodes.Length - 1; i++) Nodes[i].IndexA = i + 1;
                Nodes[Nodes.Length - 1].IndexA = INTERVAL_TREE_NULL_NODE_INDEX;
            }

            public struct NodeT {
                public int IndexA;
                public int IndexB;
                public int IndexParent;
                public int Height;
            }

            public int Root;
            public int Freelist;
            public int NodeCapacity;
            public int NodeCount;
            public int Count;
            public NodeT[] Nodes;
            public Interval[] Intervals;
            public T[] Items;
        }

        private struct PriorityQueue(int[] indices, float[] costs) {
            public void Reset() {
                _count = 0;
            }
            public void Push(int index, float cost) {
                EnsureSizeOrDouble(ref _indices, _count);
                EnsureSizeOrDouble(ref _costs, _count);

                _indices[_count] = index;
                _costs[_count] = cost;
                ++_count;

                int i = _count;
                while (i > 1 && Predicate(i - 1, i / 2 - 1) > 0) {
                    Swap(i - 1, i / 2 - 1);
                    i /= 2;
                }
            }
            public bool TryPop(ref int index, ref float cost) {
                if (_count == 0) return false;
                index = _indices[0];
                cost = _costs[0];

                _count--;
                _indices[0] = _indices[_count];
                _costs[0] = _costs[_count];

                int u = 0;
                int v = 1;
                while (u != v) {
                    u = v;
                    if (2 * u + 1 <= _count) {
                        if (Predicate(u - 1, 2 * u - 1) <= 0) v = 2 * u;
                        if (Predicate(v - 1, 2 * u) <= 0) v = 2 * u + 1;
                    } else if (2 * u <= _count) {
                        if (Predicate(u - 1, 2 * u - 1) <= 0) v = 2 * u;
                    }

                    if (u != v) {
                        Swap(u - 1, v - 1);
                    }
                }

                return true;
            }

            private readonly int Predicate(int indexA, int indexB) {
                float costA = _costs[indexA];
                float costB = _costs[indexB];
                return costA < costB ? -1 : costA > costB ? 1 : 0;
            }
            private readonly void Swap(int indexA, int indexB) {
                (_indices[indexB], _indices[indexA]) = (_indices[indexA], _indices[indexB]);
                (_costs[indexB], _costs[indexA]) = (_costs[indexA], _costs[indexB]);
            }
            private readonly void EnsureSizeOrDouble<K>(ref K[] array, int neededCapacity) {
                if (array.Length < neededCapacity) {
                    Array.Resize(ref array, array.Length * 2);
                }
            }

            private int _count = 0;
            private int[] _indices = indices;
            private float[] _costs = costs;
        }

        private struct QueryInterval : IEnumerator<T>, IEnumerable<T> {
            public QueryInterval(IntervalTree<T> at, Interval interval) {
                _at = at;
                _interval = interval;
                if (_at._tree.Root != INTERVAL_TREE_NULL_NODE_INDEX) {
                    _indexStack = new int[INTERVAL_TREE_STACK_QUERY_CAPACITY];
                    _sp = 1;
                    _indexStack[0] = _at._tree.Root;
                    _isDone = false;
                } else {
                    _indexStack = null!;
                    _sp = 0;
                    _isDone = true;
                }
                _isStarted = false;
                _version = _at._version;
                _current = default;
            }

            public readonly T Current => _current!;

            readonly object IEnumerator.Current {
                get {
                    if (!_isStarted || _isDone) {
                        throw new InvalidOperationException("Enumeration has either not started or has already finished.");
                    }
                    return _current!;
                }
            }

            public readonly void Dispose() { }

            public bool MoveNext() {
                if (_version != _at._version) {
                    throw new InvalidOperationException("Collection was modified after the enumerator was instantiated.");
                }

                _isStarted = true;

                while (_sp > 0) {
                    int index = _indexStack[--_sp];
                    Interval searchInterval = _at._tree.Intervals[index];

                    if (IntervalTree<T>.Collide(_interval, searchInterval)) {
                        if (_at._tree.Nodes[index].IndexA == INTERVAL_TREE_NULL_NODE_INDEX) {
                            _current = _at._tree.Items[index];
                            return true;
                        } else {
                            _indexStack[_sp++] = _at._tree.Nodes[index].IndexA;
                            _indexStack[_sp++] = _at._tree.Nodes[index].IndexB;
                        }
                    }
                }
                _isDone = true;
                _current = default;
                return false;
            }

            public void Reset() {
                if (_version != _at._version) {
                    throw new InvalidOperationException("Collection was modified after the enumerator was instantiated.");
                }

                _sp = 1;
                _isDone = false;
                _isStarted = false;
                _current = default;
            }

            public readonly IEnumerator<T> GetEnumerator() => this;
            readonly IEnumerator IEnumerable.GetEnumerator() => this;

            private readonly IntervalTree<T> _at;
            private Interval _interval;
            private readonly int[] _indexStack;
            private int _sp;
            private T? _current;
            private bool _isDone;
            private bool _isStarted;
            private readonly int _version;
        }
        private struct QueryAll : IEnumerator<T>, IEnumerable<T> {
            public QueryAll(IntervalTree<T> at) {
                _at = at;
                if (_at._tree.Root != INTERVAL_TREE_NULL_NODE_INDEX) {
                    _indexStack = new int[INTERVAL_TREE_STACK_QUERY_CAPACITY];
                    _sp = 1;
                    _indexStack[0] = _at._tree.Root;
                    _isDone = false;
                } else {
                    _indexStack = null!;
                    _sp = 0;
                    _isDone = true;
                }
                _isStarted = false;
                _version = _at._version;
                _current = default;
            }

            public readonly T Current => _current!;

            readonly object IEnumerator.Current {
                get {
                    if (!_isStarted || _isDone) {
                        throw new InvalidOperationException("Enumeration has either not started or has already finished.");
                    }
                    return _current!;
                }
            }

            public readonly void Dispose() { }

            public bool MoveNext() {
                if (_version != _at._version) {
                    throw new InvalidOperationException("Collection was modified after the enumerator was instantiated.");
                }

                _isStarted = true;

                while (_sp > 0) {
                    int index = _indexStack[--_sp];

                    if (_at._tree.Nodes[index].IndexA == INTERVAL_TREE_NULL_NODE_INDEX) {
                        _current = _at._tree.Items[index];
                        return true;
                    } else {
                        _indexStack[_sp++] = _at._tree.Nodes[index].IndexA;
                        _indexStack[_sp++] = _at._tree.Nodes[index].IndexB;
                    }
                }
                _isDone = true;
                _current = default;
                return false;
            }

            public void Reset() {
                if (_version != _at._version) {
                    throw new InvalidOperationException("Collection was modified after the enumerator was instantiated.");
                }

                _sp = 1;
                _isDone = false;
                _isStarted = false;
                _current = default;
            }

            public readonly IEnumerator<T> GetEnumerator() => this;
            readonly IEnumerator IEnumerable.GetEnumerator() => this;

            private readonly IntervalTree<T> _at;
            private readonly int[] _indexStack;
            private int _sp;
            private T? _current;
            private bool _isDone;
            private bool _isStarted;
            private readonly int _version;
        }
        private struct QueryNodes : IEnumerator<Interval>, IEnumerable<Interval> {
            public QueryNodes(IntervalTree<T> at, Interval interval) {
                _at = at;
                _Interval = interval;
                if (_at._tree.Root != INTERVAL_TREE_NULL_NODE_INDEX) {
                    _indexStack = new int[INTERVAL_TREE_STACK_QUERY_CAPACITY];
                    _sp = 1;
                    _indexStack[0] = _at._tree.Root;
                    _isDone = false;
                } else {
                    _indexStack = null!;
                    _sp = 0;
                    _isDone = true;
                }
                _isStarted = false;
                _version = _at._version;
                _current = default;
            }

            public Interval Current => _current!.Value;

            readonly object IEnumerator.Current {
                get {
                    if (!_isStarted || _isDone) {
                        throw new InvalidOperationException("Enumeration has either not started or has already finished.");
                    }
                    return _current!;
                }
            }

            public readonly void Dispose() { }

            public bool MoveNext() {
                if (_version != _at._version) {
                    throw new InvalidOperationException("Collection was modified after the enumerator was instantiated.");
                }

                _isStarted = true;

                while (_sp > 0) {
                    int index = _indexStack[--_sp];
                    Interval searchInterval = _at._tree.Intervals[index];

                    if (IntervalTree<T>.Collide(_Interval, searchInterval)) {
                        if (_at._tree.Nodes[index].IndexA == INTERVAL_TREE_NULL_NODE_INDEX) {
                            _current = _at._tree.Intervals[index];
                            return true;
                        } else {
                            _indexStack[_sp++] = _at._tree.Nodes[index].IndexA;
                            _indexStack[_sp++] = _at._tree.Nodes[index].IndexB;
                            _current = _at._tree.Intervals[index];
                            return true;
                        }
                    }
                }
                _isDone = true;
                _current = default;
                return false;
            }

            public void Reset() {
                if (_version != _at._version) {
                    throw new InvalidOperationException("Collection was modified after the enumerator was instantiated.");
                }

                _sp = 1;
                _isDone = false;
                _isStarted = false;
                _current = default;
            }

            public readonly IEnumerator<Interval> GetEnumerator() => this;
            readonly IEnumerator IEnumerable.GetEnumerator() => this;

            private readonly IntervalTree<T> _at;
            private Interval _Interval;
            private readonly int[] _indexStack;
            private int _sp;
            private Interval? _current;
            private bool _isDone;
            private bool _isStarted;
            private readonly int _version;
        }
        private struct QueryAllNodes : IEnumerator<Interval>, IEnumerable<Interval> {
            public QueryAllNodes(IntervalTree<T> at) {
                _at = at;
                if (_at._tree.Root != INTERVAL_TREE_NULL_NODE_INDEX) {
                    _indexStack = new int[INTERVAL_TREE_STACK_QUERY_CAPACITY];
                    _sp = 1;
                    _indexStack[0] = _at._tree.Root;
                    _isDone = false;
                } else {
                    _indexStack = null!;
                    _sp = 0;
                    _isDone = true;
                }
                _isStarted = false;
                _version = _at._version;
                _current = default;
            }

            public Interval Current => _current!.Value;

            readonly object IEnumerator.Current {
                get {
                    if (!_isStarted || _isDone) {
                        throw new InvalidOperationException("Enumeration has either not started or has already finished.");
                    }
                    return _current!;
                }
            }

            public readonly void Dispose() { }

            public bool MoveNext() {
                if (_version != _at._version) {
                    throw new InvalidOperationException("Collection was modified after the enumerator was instantiated.");
                }

                _isStarted = true;

                while (_sp > 0) {
                    int index = _indexStack[--_sp];

                    if (_at._tree.Nodes[index].IndexA == INTERVAL_TREE_NULL_NODE_INDEX) {
                        _current = _at._tree.Intervals[index];
                        return true;
                    } else {
                        _indexStack[_sp++] = _at._tree.Nodes[index].IndexA;
                        _indexStack[_sp++] = _at._tree.Nodes[index].IndexB;
                        _current = _at._tree.Intervals[index];
                        return true;
                    }
                }
                _isDone = true;
                _current = default;
                return false;
            }

            public void Reset() {
                if (_version != _at._version) {
                    throw new InvalidOperationException("Collection was modified after the enumerator was instantiated.");
                }

                _sp = 1;
                _isDone = false;
                _isStarted = false;
                _current = default;
            }

            public readonly IEnumerator<Interval> GetEnumerator() => this;
            readonly IEnumerator IEnumerable.GetEnumerator() => this;

            private readonly IntervalTree<T> _at;
            private readonly int[] _indexStack;
            private int _sp;
            private Interval? _current;
            private bool _isDone;
            private bool _isStarted;
            private readonly int _version;
        }

        private float _intervalTreeExpandConstant = expandConstant;
        private float _intervalTreeMoveConstant = moveConstant;

        private const int INTERVAL_TREE_STACK_QUERY_CAPACITY = 256;
        private const int INTERVAL_TREE_NULL_NODE_INDEX = -1;

        private IntervalTreeT _tree = new(initialCapacity);
        private PriorityQueue _queue = new(new int[INTERVAL_TREE_STACK_QUERY_CAPACITY], new float[INTERVAL_TREE_STACK_QUERY_CAPACITY]);
        private int _version = 0;
    }
}
