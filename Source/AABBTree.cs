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
using Microsoft.Xna.Framework;
using MonoGame.Extended;

namespace Apos.Spatial {
    /// <summary>
    /// An aabb tree is a dynamic partition data structure. It allows you to efficiently query the space in a game world.
    /// </summary>
    /// <typeparam name="T">Type of objects to add to the tree.</typeparam>
    public class AABBTree<T> : IEnumerable<T> {
        /// <summary>
        /// Creates a new tree.
        /// </summary>
        /// <param name="initialCapacity">Amount of nodes the tree can hold before it needs to be resized.</param>
        /// <param name="expandConstant">It expands the items that are added so that they don't need to be updated as often. Defaults to 2f.</param>
        /// <param name="moveConstant">It expands the items that are moved so that they don't need to be updated as much. Defaults to 4f.</param>
        public AABBTree(int initialCapacity = 64, float expandConstant = 2f, float moveConstant = 4f) {
            _aabbTreeExpandConstant = expandConstant;
            _aabbTreeMoveConstant = moveConstant;

            _tree = new AABBTreeT(initialCapacity);
            _queue = new PriorityQueue(new int[AABB_TREE_STACK_QUERY_CAPACITY], new float[AABB_TREE_STACK_QUERY_CAPACITY], AABB_TREE_STACK_QUERY_CAPACITY);
        }

        /// <summary>
        /// Used for the broad phase search.
        /// It expands the items that are added so that they don't need to be updated as often.
        /// </summary>
        public float ExpandConstant {
            get => _aabbTreeExpandConstant;
            set {
                _aabbTreeExpandConstant = value;
            }
        }
        /// <summary>
        /// Used for the broad phase search.
        /// It expands the items that are moved so that they don't need to be updated as much.
        /// </summary>
        public float MoveConstant {
            get => _aabbTreeMoveConstant;
            set {
                _aabbTreeMoveConstant = value;
            }
        }
        /// <summary>
        /// Amount of nodes in the tree.
        /// </summary>
        public int Count => _tree.NodeCount;

        /// <summary>
        /// Adds a new leaf to the tree, and rebalances as necessary.
        /// </summary>
        /// <param name="aabb">An axis aligned bounding box for the item.</param>
        /// <param name="item">The item to add to the tree.</param>
        /// <returns>The item's leaf. Use this to update or remove the item later.</returns>
        public int Add(RectangleF aabb, T item) {
            // Expand aabb before adding.
            return InternalAdd(Expand(aabb, _aabbTreeExpandConstant), item);
        }

        /// <summary>
        /// Removes a leaf from the tree, and rebalances as necessary.
        /// </summary>
        /// <returns>-1 since the leaf is no longer in the tree.</returns>
        public int Remove(int leaf) {
            if (leaf == AABB_TREE_NULL_NODE_INDEX) return AABB_TREE_NULL_NODE_INDEX;

            int index = leaf;
            if (_tree.Root == index) {
                _tree.Root = AABB_TREE_NULL_NODE_INDEX;
            } else {
                ref AABBTreeT.NodeT node = ref _tree.Nodes[index];
                int indexParent = node.IndexParent;
                ref AABBTreeT.NodeT parent = ref _tree.Nodes[indexParent];

                if (indexParent == _tree.Root) {
                    if (parent.IndexA == index) _tree.Root = parent.IndexB;
                    else _tree.Root = parent.IndexA;
                    _tree.Nodes[_tree.Root].IndexParent = AABB_TREE_NULL_NODE_INDEX;
                } else {
                    int indexGrandparent = parent.IndexParent;
                    ref AABBTreeT.NodeT grandparent = ref _tree.Nodes[indexGrandparent];

                    if (parent.IndexA == index) {
                        ref AABBTreeT.NodeT sindexBling = ref _tree.Nodes[parent.IndexB];
                        sindexBling.IndexParent = indexGrandparent;
                        if (grandparent.IndexA == indexParent) grandparent.IndexA = parent.IndexB;
                        else grandparent.IndexB = parent.IndexB;
                    } else {
                        ref AABBTreeT.NodeT sindexBling = ref _tree.Nodes[parent.IndexA];
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

            return AABB_TREE_NULL_NODE_INDEX;
        }
        /// <summary>
        /// Clears the whole tree.
        /// </summary>
        /// <param name="initialCapacity">Amount of nodes the tree can hold before it needs to be resized.</param>
        public void Clear(int initialCapacity = 64) {
            _tree = new AABBTreeT(initialCapacity);
            _version++;
        }

        /// <summary>
        /// Use this function when an aabb needs to be updated. Leafs need to be updated whenever the shape
        /// inside the leaf's aabb moves. Internally there are some optimizations so that the tree is only
        /// adjusted if the aabb is moved enough.
        /// </summary>
        /// <param name="leaf">The leaf to update.</param>
        /// <param name="aabb">The new aabb.</param>
        /// <returns>Returns true if the leaf was updated, false otherwise.</returns>
        public bool Update(int leaf, RectangleF aabb) {
            if (Contains(_tree.AABBs[leaf], aabb)) {
                _tree.AABBs[leaf] = aabb;
                return false;
            }

            T item = _tree.Items[leaf]!;
            Remove(leaf);
            Add(aabb, item);

            _version++;

            return true;
        }

        /// <summary>
        /// Updates a leaf with a new aabb (if needed) with the new `aabb` and an `offset` for how far the new
        /// aabb will be moving.
        /// This function does more optimizations than `aabb_tree_update_leaf` by attempting to use the `offset`
        /// to predict motion and avoid restructuring of the tree.
        /// </summary>
        /// <param name="leaf">The leaf to update.</param>
        /// <param name="aabb">The new aabb.</param>
        /// <param name="offset">An offset that represents the direction the aabb is moving in.</param>
        /// <returns>Returns true if the leaf was updated, false otherwise.</returns>
        public bool Move(int leaf, RectangleF aabb, Vector2 offset) {
            aabb = Expand(aabb, _aabbTreeExpandConstant);
            Vector2 delta = offset * _aabbTreeMoveConstant;

            if (delta.X < 0) {
                aabb.X += delta.X;
                aabb.Width -= delta.X;
            } else {
                aabb.Width += delta.X;
            }

            if (delta.Y < 0) {
                aabb.Y += delta.Y;
                aabb.Height -= delta.Y;
            } else {
                aabb.Height += delta.Y;
            }

            RectangleF oldAABB = _tree.AABBs[leaf];
            if (Contains(oldAABB, aabb)) {
                RectangleF bigAABB = Expand(aabb, _aabbTreeMoveConstant);
                bool oldAABBIsNotWayTooHuge = Contains(bigAABB, oldAABB);
                if (oldAABBIsNotWayTooHuge) {
                    return false;
                }
            }

            T item = _tree.Items[leaf]!;
            Remove(leaf);
            InternalAdd(aabb, item);

            _version++;

            return true;
        }

        /// <summary>
        /// Returns the internal "expanded" aabb. This is useful for when you want to generate all pairs of
        /// potential overlaps for a specific leaf. Just simply use `Query` on the the return value
        /// of this function.
        /// </summary>
        /// <param name="leaf">The leaf to lookup.</param>
        public RectangleF GetAABB(int leaf) {
            return _tree.AABBs[leaf];
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
        public IEnumerable<T> Query(Vector2 v) {
            return new QueryRect(this, new RectangleF(v.X, v.Y, 0, 0));
        }
        /// <summary>
        /// Finds all items overlapping the Vector2 `aabb`.
        /// </summary>
        /// <param name="aabb">The aabb to query.</param>
        public IEnumerable<T> Query(RectangleF aabb) {
            return new QueryRect(this, aabb);
        }
        /// <summary>
        /// Mostly used for debugging. It returns all the `aabb` in the tree including parent nodes.
        /// </summary>
        /// <param name="aabb">The aabb to query.</param>
        public IEnumerable<RectangleF> DebugNodes(RectangleF aabb) {
            return new QueryNodes(this, aabb);
        }
        /// <summary>
        /// Mostly used for debugging. It returns all the `aabb` in the tree including parent nodes.
        /// </summary>
        public IEnumerable<RectangleF> DebugAllNodes() {
            return new QueryAllNodes(this);
        }

        private int InternalAdd(RectangleF aabb, T item) {
            // Make a new node.
            int newIndex = SPopFreelist(aabb, item);
            int searchIndex = _tree.Root;

            // Empty tree, make new root.
            if (searchIndex == AABB_TREE_NULL_NODE_INDEX) {
                _tree.Root = newIndex;
            } else {
                searchIndex = SBranchAndBoundFindOptimalSibling(aabb);

                // Make new branch node.
                int branchIndex = SPopFreelist(RectangleF.Union(aabb, _tree.AABBs[searchIndex]));
                ref AABBTreeT.NodeT branch = ref _tree.Nodes[branchIndex];
                int parentIndex = _tree.Nodes[searchIndex].IndexParent;

                if (parentIndex == AABB_TREE_NULL_NODE_INDEX) {
                    _tree.Root = branchIndex;
                } else {
                    ref AABBTreeT.NodeT parent = ref _tree.Nodes[parentIndex];

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

            ref AABBTreeT.NodeT a = ref _tree.Nodes[indexA];
            int indexB = a.IndexA;
            int indexC = a.IndexB;
            if (a.IndexA == AABB_TREE_NULL_NODE_INDEX || a.Height < 2) return indexA;

            ref AABBTreeT.NodeT b = ref _tree.Nodes[indexB];
            ref AABBTreeT.NodeT c = ref _tree.Nodes[indexC];
            int balance = c.Height - b.Height;

            // Rotate c up.
            if (balance > 1) {
                int indexF = c.IndexA;
                int indexG = c.IndexB;
                ref AABBTreeT.NodeT f = ref _tree.Nodes[indexF];
                ref AABBTreeT.NodeT g = ref _tree.Nodes[indexG];

                // Swap a and c.
                c.IndexA = indexA;
                c.IndexParent = a.IndexParent;
                a.IndexParent = indexC;

                // Hookup a's old parent to c.
                if (c.IndexParent != AABB_TREE_NULL_NODE_INDEX) {
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
                    _tree.AABBs[indexA] = RectangleF.Union(_tree.AABBs[indexB], _tree.AABBs[indexG]);
                    _tree.AABBs[indexC] = RectangleF.Union(_tree.AABBs[indexA], _tree.AABBs[indexF]);

                    a.Height = Math.Max(b.Height, g.Height) + 1;
                    c.Height = Math.Max(a.Height, f.Height) + 1;
                } else {
                    c.IndexB = indexG;
                    a.IndexB = indexF;
                    f.IndexParent = indexA;
                    _tree.AABBs[indexA] = RectangleF.Union(_tree.AABBs[indexB], _tree.AABBs[indexF]);
                    _tree.AABBs[indexC] = RectangleF.Union(_tree.AABBs[indexA], _tree.AABBs[indexG]);

                    a.Height = Math.Max(b.Height, f.Height) + 1;
                    c.Height = Math.Max(a.Height, g.Height) + 1;
                }

                return indexC;
            } else if (balance < -1) {
                // Rotate b up.

                int indexD = b.IndexA;
                int indexE = b.IndexB;
                ref AABBTreeT.NodeT d = ref _tree.Nodes[indexD];
                ref AABBTreeT.NodeT e = ref _tree.Nodes[indexE];

                // Swap a and b.
                b.IndexA = indexA;
                b.IndexParent = a.IndexParent;
                a.IndexParent = indexB;

                // Hookup a's old parent to b.
                if (b.IndexParent != AABB_TREE_NULL_NODE_INDEX) {
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
                    _tree.AABBs[indexA] = RectangleF.Union(_tree.AABBs[indexC], _tree.AABBs[indexE]);
                    _tree.AABBs[indexB] = RectangleF.Union(_tree.AABBs[indexA], _tree.AABBs[indexD]);

                    a.Height = Math.Max(c.Height, e.Height) + 1;
                    b.Height = Math.Max(a.Height, d.Height) + 1;
                } else {
                    b.IndexB = indexE;
                    a.IndexA = indexD;
                    d.IndexParent = indexA;
                    _tree.AABBs[indexA] = RectangleF.Union(_tree.AABBs[indexC], _tree.AABBs[indexD]);
                    _tree.AABBs[indexB] = RectangleF.Union(_tree.AABBs[indexA], _tree.AABBs[indexE]);

                    a.Height = Math.Max(c.Height, d.Height) + 1;
                    b.Height = Math.Max(a.Height, e.Height) + 1;
                }

                return indexB;
            }

            return indexA;
        }

        private void SSyncNode(int index) {
            ref AABBTreeT.NodeT node = ref _tree.Nodes[index];
            int indexA = node.IndexA;
            int indexB = node.IndexB;
            node.Height = Math.Max(_tree.Nodes[indexA].Height, _tree.Nodes[indexB].Height) + 1;
            _tree.AABBs[index] = RectangleF.Union(_tree.AABBs[indexA], _tree.AABBs[indexB]);
        }

        private void SRefitHierarchy(int index) {
            while (index != AABB_TREE_NULL_NODE_INDEX) {
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

        private int SPopFreelist(RectangleF aabb, T item = default!) {
            int newIndex = _tree.Freelist;
            if (newIndex == AABB_TREE_NULL_NODE_INDEX) {
                int newCapacity = _tree.NodeCapacity * 2;
                EnsureSize(ref _tree.Nodes, newCapacity);
                EnsureSize(ref _tree.AABBs, newCapacity);
                EnsureSize(ref _tree.Items, newCapacity);

                // Link up new freelist and attach it to pre-existing freelist.
                int nodeCapacity = _tree.NodeCapacity;
                for (int i = 0; i < _tree.NodeCapacity - 1; i++) {
                    _tree.Nodes[_tree.NodeCapacity + i].IndexA = i + _tree.NodeCapacity + 1;
                }
                _tree.Nodes[newCapacity - 1].IndexA = AABB_TREE_NULL_NODE_INDEX;
                _tree.Freelist = _tree.NodeCapacity;
                newIndex = _tree.Freelist;
                _tree.NodeCapacity = newCapacity;
            }

            _tree.Freelist = _tree.Nodes[newIndex].IndexA;
            _tree.Nodes[newIndex].IndexA = AABB_TREE_NULL_NODE_INDEX;
            _tree.Nodes[newIndex].IndexB = AABB_TREE_NULL_NODE_INDEX;
            _tree.Nodes[newIndex].IndexParent = AABB_TREE_NULL_NODE_INDEX;
            _tree.Nodes[newIndex].Height = 0;
            _tree.AABBs[newIndex] = aabb;
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

        private float SDeltaCost(RectangleF toInsert, RectangleF candidate) {
            return SurfaceArea(RectangleF.Union(toInsert, candidate)) - SurfaceArea(candidate);
        }

        // https://en.wikipedia.org/wiki/Branch_and_bound#Generic_version
        private int SBranchAndBoundFindOptimalSibling(RectangleF toInsert) {
            _queue.Reset();
            _queue.Push(_tree.Root, SDeltaCost(toInsert, _tree.AABBs[_tree.Root]));

            float toInsertSA = SurfaceArea(toInsert);
            float bestCost = float.MaxValue;
            int bestIndex = AABB_TREE_NULL_NODE_INDEX;
            int searchIndex = 0;
            float searchDeltaCost = 0;
            while (_queue.TryPop(ref searchIndex, ref searchDeltaCost)) {
                // Track the best candidate so far.
                RectangleF searchAABB = _tree.AABBs[searchIndex];
                float cost = SurfaceArea(RectangleF.Union(toInsert, searchAABB)) + searchDeltaCost;
                if (cost < bestCost) {
                    bestCost = cost;
                    bestIndex = searchIndex;
                }

                // Consider pushing the candidate's children onto the priority queue.
                // Cull subtrees with lower bound metric.
                float deltaCost = SDeltaCost(toInsert, searchAABB) + searchDeltaCost;
                float lowerBound = toInsertSA + deltaCost;
                if (lowerBound < bestCost) {
                    int indexA = _tree.Nodes[searchIndex].IndexA;
                    int indexB = _tree.Nodes[searchIndex].IndexB;
                    if (indexA != AABB_TREE_NULL_NODE_INDEX) {
                        _queue.Push(indexA, deltaCost);
                        _queue.Push(indexB, deltaCost);
                    }
                }
            }

            return bestIndex;
        }

        private float STreeCost(int index) {
            if (index == AABB_TREE_NULL_NODE_INDEX) return 0;
            float costA = STreeCost(_tree.Nodes[index].IndexA);
            float costB = STreeCost(_tree.Nodes[index].IndexB);
            float myCost = SurfaceArea(_tree.AABBs[index]);
            return costA + costB + myCost;
        }

        private static RectangleF Expand(RectangleF aabb, float v) {
            return new RectangleF(aabb.Left - v, aabb.Top - v, aabb.Width + v * 2f, aabb.Height + v * 2f);
        }
        private static float SurfaceArea(RectangleF bb) {
            return bb.Width * bb.Height;
        }
        private static bool Contains(RectangleF r1, RectangleF r2) {
            return r1.Left <= r2.Left && r2.Right <= r1.Right && r1.Top <= r2.Top && r2.Bottom <= r1.Bottom;
        }
        private static bool Collide(RectangleF a, RectangleF b) {
            bool d0 = b.Right < a.Left;
            bool d1 = a.Right < b.Left;
            bool d2 = b.Bottom < a.Top;
            bool d3 = a.Bottom < b.Top;
            return !(d0 || d1 || d2 || d3);
        }

        private struct AABBTreeT {
            public AABBTreeT(int initialCapacity = 0) {
                if (initialCapacity == 0) initialCapacity = 64;
                Nodes = new NodeT[initialCapacity];
                AABBs = new RectangleF[initialCapacity];
                Items = new T[initialCapacity];
                Root = AABB_TREE_NULL_NODE_INDEX;
                Freelist = 0;
                NodeCapacity = initialCapacity;
                NodeCount = 0;

                for (int i = 0; i < Nodes.Length - 1; i++) Nodes[i].IndexA = i + 1;
                Nodes[Nodes.Length - 1].IndexA = AABB_TREE_NULL_NODE_INDEX;
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
            public NodeT[] Nodes;
            public RectangleF[] AABBs;
            public T[] Items;
        }

        private struct PriorityQueue {
            public PriorityQueue(int[] indices, float[] costs, int capacity) {
                _count = 0;
                _capacity = capacity;
                _indices = indices;
                _costs = costs;
            }

            public void Reset() {
                _count = 0;
            }
            public void Push(int index, float cost) {
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

            private int Predicate(int indexA, int indexB) {
                float costA = _costs[indexA];
                float costB = _costs[indexB];
                return costA < costB ? -1 : costA > costB ? 1 : 0;
            }
            private void Swap(int indexA, int indexB) {
                int ival = _indices[indexA];
                _indices[indexA] = _indices[indexB];
                _indices[indexB] = ival;

                float fval = _costs[indexA];
                _costs[indexA] = _costs[indexB];
                _costs[indexB] = fval;
            }

            private int _count;
            private int _capacity;
            private int[] _indices;
            private float[] _costs;
        }

        private struct QueryRect : IEnumerator<T>, IEnumerable<T> {
            public QueryRect(AABBTree<T> at, RectangleF aabb) {
                _at = at;
                _aabb = aabb;
                if (_at._tree.Root != AABB_TREE_NULL_NODE_INDEX) {
                    _indexStack = new int[AABB_TREE_STACK_QUERY_CAPACITY];
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

            public T Current => _current!;

            object IEnumerator.Current {
                get {
                    if (!_isStarted || _isDone) {
                        throw new InvalidOperationException("Enumeration has either not started or has already finished.");
                    }
                    return _current!;
                }
            }

            public void Dispose() { }

            public bool MoveNext() {
                if (_version != _at._version) {
                    throw new InvalidOperationException("Collection was modified after the enumerator was instantiated.");
                }

                _isStarted = true;

                while (_sp > 0) {
                    int index = _indexStack[--_sp];
                    RectangleF searchAABB = _at._tree.AABBs[index];

                    if (AABBTree<T>.Collide(_aabb, searchAABB)) {
                        if (_at._tree.Nodes[index].IndexA == AABB_TREE_NULL_NODE_INDEX) {
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

            public IEnumerator<T> GetEnumerator() => this;
            IEnumerator IEnumerable.GetEnumerator() => this;

            private AABBTree<T> _at;
            private RectangleF _aabb;
            private int[] _indexStack;
            private int _sp;
            private T? _current;
            private bool _isDone;
            private bool _isStarted;
            private readonly int _version;
        }
        private struct QueryAll : IEnumerator<T>, IEnumerable<T> {
            public QueryAll(AABBTree<T> at) {
                _at = at;
                if (_at._tree.Root != AABB_TREE_NULL_NODE_INDEX) {
                    _indexStack = new int[AABB_TREE_STACK_QUERY_CAPACITY];
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

            public T Current => _current!;

            object IEnumerator.Current {
                get {
                    if (!_isStarted || _isDone) {
                        throw new InvalidOperationException("Enumeration has either not started or has already finished.");
                    }
                    return _current!;
                }
            }

            public void Dispose() { }

            public bool MoveNext() {
                if (_version != _at._version) {
                    throw new InvalidOperationException("Collection was modified after the enumerator was instantiated.");
                }

                _isStarted = true;

                while (_sp > 0) {
                    int index = _indexStack[--_sp];

                    if (_at._tree.Nodes[index].IndexA == AABB_TREE_NULL_NODE_INDEX) {
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

            public IEnumerator<T> GetEnumerator() => this;
            IEnumerator IEnumerable.GetEnumerator() => this;

            private AABBTree<T> _at;
            private int[] _indexStack;
            private int _sp;
            private T? _current;
            private bool _isDone;
            private bool _isStarted;
            private readonly int _version;
        }
        private struct QueryNodes : IEnumerator<RectangleF>, IEnumerable<RectangleF> {
            public QueryNodes(AABBTree<T> at, RectangleF aabb) {
                _at = at;
                _aabb = aabb;
                if (_at._tree.Root != AABB_TREE_NULL_NODE_INDEX) {
                    _indexStack = new int[AABB_TREE_STACK_QUERY_CAPACITY];
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

            public RectangleF Current => _current!.Value;

            object IEnumerator.Current {
                get {
                    if (!_isStarted || _isDone) {
                        throw new InvalidOperationException("Enumeration has either not started or has already finished.");
                    }
                    return _current!;
                }
            }

            public void Dispose() { }

            public bool MoveNext() {
                if (_version != _at._version) {
                    throw new InvalidOperationException("Collection was modified after the enumerator was instantiated.");
                }

                _isStarted = true;

                while (_sp > 0) {
                    int index = _indexStack[--_sp];
                    RectangleF searchAABB = _at._tree.AABBs[index];

                    if (AABBTree<T>.Collide(_aabb, searchAABB)) {
                        if (_at._tree.Nodes[index].IndexA == AABB_TREE_NULL_NODE_INDEX) {
                            _current = _at._tree.AABBs[index];
                            return true;
                        } else {
                            _indexStack[_sp++] = _at._tree.Nodes[index].IndexA;
                            _indexStack[_sp++] = _at._tree.Nodes[index].IndexB;
                            _current = _at._tree.AABBs[index];
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

            public IEnumerator<RectangleF> GetEnumerator() => this;
            IEnumerator IEnumerable.GetEnumerator() => this;

            private AABBTree<T> _at;
            private RectangleF _aabb;
            private int[] _indexStack;
            private int _sp;
            private RectangleF? _current;
            private bool _isDone;
            private bool _isStarted;
            private readonly int _version;
        }
        private struct QueryAllNodes : IEnumerator<RectangleF>, IEnumerable<RectangleF> {
            public QueryAllNodes(AABBTree<T> at) {
                _at = at;
                if (_at._tree.Root != AABB_TREE_NULL_NODE_INDEX) {
                    _indexStack = new int[AABB_TREE_STACK_QUERY_CAPACITY];
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

            public RectangleF Current => _current!.Value;

            object IEnumerator.Current {
                get {
                    if (!_isStarted || _isDone) {
                        throw new InvalidOperationException("Enumeration has either not started or has already finished.");
                    }
                    return _current!;
                }
            }

            public void Dispose() { }

            public bool MoveNext() {
                if (_version != _at._version) {
                    throw new InvalidOperationException("Collection was modified after the enumerator was instantiated.");
                }

                _isStarted = true;

                while (_sp > 0) {
                    int index = _indexStack[--_sp];

                    if (_at._tree.Nodes[index].IndexA == AABB_TREE_NULL_NODE_INDEX) {
                        _current = _at._tree.AABBs[index];
                        return true;
                    } else {
                        _indexStack[_sp++] = _at._tree.Nodes[index].IndexA;
                        _indexStack[_sp++] = _at._tree.Nodes[index].IndexB;
                        _current = _at._tree.AABBs[index];
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

            public IEnumerator<RectangleF> GetEnumerator() => this;
            IEnumerator IEnumerable.GetEnumerator() => this;

            private AABBTree<T> _at;
            private int[] _indexStack;
            private int _sp;
            private RectangleF? _current;
            private bool _isDone;
            private bool _isStarted;
            private readonly int _version;
        }

        private float _aabbTreeExpandConstant;
        private float _aabbTreeMoveConstant;

        private const int AABB_TREE_STACK_QUERY_CAPACITY = 256;
        private const int AABB_TREE_NULL_NODE_INDEX = -1;

        private AABBTreeT _tree;
        private PriorityQueue _queue;
        private int _version = 0;
    }
}
