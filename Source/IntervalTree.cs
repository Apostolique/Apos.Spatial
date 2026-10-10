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

/*
    The node layout, insertion, rotation, and rebuild algorithms are adapted from Box2D.
    https://github.com/erincatto/box2d
    Copyright (c) 2026 Erin Catto
    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:
    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.
    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Apos.Spatial {
    /// <summary>
    /// An interval tree is a dynamic partition data structure. It allows you to efficiently query the space in a game world.
    /// </summary>
    /// <typeparam name="T">Type of objects to add to the tree.</typeparam>
    public class IntervalTree<T> : IEnumerable<T> where T : notnull {
        /// <summary>
        /// Creates a new tree.
        /// </summary>
        /// <param name="initialCapacity">Amount of items the tree can hold before it needs to be resized.</param>
        /// <param name="expandConstant">It expands the items that are added so that they don't need to be updated as often. Defaults to 2f.</param>
        /// <param name="moveConstant">It expands the items that are moved so that they don't need to be updated as much. Defaults to 4f.</param>
        public IntervalTree(int initialCapacity = 64, float expandConstant = 2f, float moveConstant = 4f) {
            _expandConstant = expandConstant;
            _moveConstant = moveConstant;
            Initialize(initialCapacity);
        }

        /// <summary>
        /// Used for the broad phase search.
        /// It expands the items that are added so that they don't need to be updated as often.
        /// </summary>
        public float ExpandConstant {
            get => _expandConstant;
            set => _expandConstant = value;
        }
        /// <summary>
        /// Used for the broad phase search.
        /// It expands the items that are moved so that they don't need to be updated as much.
        /// </summary>
        public float MoveConstant {
            get => _moveConstant;
            set => _moveConstant = value;
        }
        /// <summary>
        /// Amount of nodes in the tree.
        /// </summary>
        public int NodeCount => _count == 0 ? 0 : 2 * _count - 1;
        /// <summary>
        /// Amount of items in the tree.
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Bounds of all the items in the tree.
        /// </summary>
        public Interval? Bounds => _count > 0 ? _nodes[Root].Span.ToInterval() : null;

        /// <summary>
        /// Adds a new leaf to the tree, and rebalances as necessary.
        /// An item can only be added once to the same tree.
        /// </summary>
        /// <param name="interval">An interval for the item.</param>
        /// <param name="item">The item to add to the tree.</param>
        /// <returns>
        /// The item's leaf. You can use it to update or remove the item later without a lookup.
        /// You can also ignore it and use the item instead.
        /// </returns>
        public int Add(Interval interval, T item) {
            ArgumentNullException.ThrowIfNull(item);
            if (_leaves.ContainsKey(item)) {
                throw new ArgumentException("The item is already in the tree.", nameof(item));
            }

            int leaf = AllocateProxy();
            _leaves.Add(item, leaf);
            _items[leaf] = item;
            InsertLeaf(leaf, Span.From(interval).Expand(_expandConstant));

            _count++;
            _version++;

            return leaf;
        }

        /// <summary>
        /// Removes an item from the tree, and rebalances as necessary.
        /// </summary>
        /// <param name="item">The item to remove.</param>
        /// <returns>True if the item was in the tree, false otherwise.</returns>
        public bool Remove(T item) {
            if (!_leaves.TryGetValue(item, out int leaf)) return false;

            Remove(leaf);

            return true;
        }
        /// <summary>
        /// Removes a leaf from the tree, and rebalances as necessary.
        /// </summary>
        /// <param name="leaf">The leaf to remove.</param>
        /// <returns>-1 since the leaf is no longer in the tree.</returns>
        public int Remove(int leaf) {
            if (leaf == NullNode) return NullNode;
            ThrowIfNotLeaf(leaf);

            _leaves.Remove(_items[leaf]);
            RemoveLeaf(leaf);
            FreeProxy(leaf);

            _count--;
            _version++;

            return NullNode;
        }
        /// <summary>
        /// Clears the whole tree.
        /// </summary>
        /// <param name="initialCapacity">Amount of items the tree can hold before it needs to be resized.</param>
        public void Clear(int initialCapacity = 64) {
            Initialize(initialCapacity);
            _leaves.Clear();
            _version++;
        }

        /// <summary>
        /// Returns true if the item is in the tree.
        /// </summary>
        /// <param name="item">The item to look for.</param>
        public bool Contains(T item) {
            return _leaves.ContainsKey(item);
        }

        /// <summary>
        /// Use this function when an item's interval needs to be updated. Items need to be updated whenever the shape
        /// inside the item's interval moves. Internally there are some optimizations so that the tree is only
        /// adjusted if the interval is moved enough.
        /// </summary>
        /// <param name="item">The item to update.</param>
        /// <param name="interval">The new interval.</param>
        /// <returns>Returns true if the tree was restructured, false otherwise.</returns>
        public bool Update(T item, Interval interval) {
            return Move(GetLeaf(item), interval, 0f);
        }
        /// <summary>
        /// Use this function when an interval needs to be updated. Leafs need to be updated whenever the shape
        /// inside the leaf's interval moves. Internally there are some optimizations so that the tree is only
        /// adjusted if the interval is moved enough.
        /// </summary>
        /// <param name="leaf">The leaf to update.</param>
        /// <param name="interval">The new interval.</param>
        /// <returns>Returns true if the tree was restructured, false otherwise.</returns>
        public bool Update(int leaf, Interval interval) {
            return Move(leaf, interval, 0f);
        }

        /// <summary>
        /// Updates an item with a new interval (if needed) and an `offset` for how far the new
        /// interval will be moving.
        /// This function does more optimizations than `Update` by attempting to use the `offset`
        /// to predict motion and avoid restructuring of the tree.
        /// </summary>
        /// <param name="item">The item to update.</param>
        /// <param name="interval">The new interval.</param>
        /// <param name="offset">An offset that represents the direction the interval is moving in.</param>
        /// <returns>Returns true if the tree was restructured, false otherwise.</returns>
        public bool Move(T item, Interval interval, float offset) {
            return Move(GetLeaf(item), interval, offset);
        }
        /// <summary>
        /// Updates a leaf with a new interval (if needed) and an `offset` for how far the new
        /// interval will be moving.
        /// This function does more optimizations than `Update` by attempting to use the `offset`
        /// to predict motion and avoid restructuring of the tree.
        /// </summary>
        /// <param name="leaf">The leaf to update.</param>
        /// <param name="interval">The new interval.</param>
        /// <param name="offset">An offset that represents the direction the interval is moving in.</param>
        /// <returns>Returns true if the tree was restructured, false otherwise.</returns>
        public bool Move(int leaf, Interval interval, float offset) {
            ThrowIfNotLeaf(leaf);

            Span span = Span.From(interval);
            Span fat = span.Expand(_expandConstant);
            float delta = offset * _moveConstant;

            if (delta < 0f) fat.Min += delta;
            else fat.Max += delta;

            Span old = _nodes[_proxies[leaf].Node].Span;
            if (old.Contains(span)) {
                // The leaf still holds the item but it might be way too big if the item stopped moving or shrank.
                Span huge = fat.Expand(4f * _expandConstant);
                if (huge.Contains(old)) {
                    return false;
                }
            }

            RemoveLeaf(leaf);
            InsertLeaf(leaf, fat);

            _version++;

            return true;
        }

        /// <summary>
        /// Rebuilds the whole tree from scratch. Leafs stay valid.
        /// This can produce a better tree after adding a lot of items at once, for example when loading a level.
        /// </summary>
        public void Rebuild() {
            if (_count <= 1) return;

            Node[] leaves = new Node[_count];
            int[] order = new int[_count];
            float[] centers = new float[_count];
            int leafCount = 0;

            int[] stack = RentStack();
            int sp = 0;
            stack[sp++] = Root;
            while (sp > 0) {
                ref Node node = ref _nodes[stack[--sp]];
                if (node.Height == 0) {
                    leaves[leafCount] = node;
                    order[leafCount] = leafCount;
                    centers[leafCount] = node.Span.Center;
                    leafCount++;
                } else {
                    if (sp + 2 > stack.Length) stack = GrowStack(stack);
                    stack[sp++] = node.Index;
                    stack[sp++] = node.Index + 1;
                }
            }
            ReturnStack(stack);

            BuildTree(leaves, order, centers, leafCount);

            _version++;
        }

        /// <summary>
        /// Returns the item's leaf.
        /// </summary>
        /// <param name="item">The item to lookup.</param>
        public int GetLeaf(T item) {
            if (!_leaves.TryGetValue(item, out int leaf)) {
                throw new ArgumentException("The item isn't in the tree.", nameof(item));
            }
            return leaf;
        }
        /// <summary>
        /// Returns the internal "expanded" interval. This is useful for when you want to generate all pairs of
        /// potential overlaps for a specific leaf. Just simply use `Query` on the the return value
        /// of this function.
        /// </summary>
        /// <param name="leaf">The leaf to lookup.</param>
        public Interval GetInterval(int leaf) {
            ThrowIfNotLeaf(leaf);
            return _nodes[_proxies[leaf].Node].Span.ToInterval();
        }
        /// <summary>
        /// Returns the `item` from `Add`.
        /// </summary>
        /// <param name="leaf">The leaf to lookup.</param>
        public T GetItem(int leaf) {
            ThrowIfNotLeaf(leaf);
            return _items[leaf];
        }

        /// <summary>
        /// Returns every item in the tree. Used implicitly by foreach loops.
        /// </summary>
        public Enumerator GetEnumerator() {
            return new Enumerator(this);
        }
        IEnumerator<T> IEnumerable<T>.GetEnumerator() {
            return new Enumerator(this);
        }
        IEnumerator IEnumerable.GetEnumerator() {
            return new Enumerator(this);
        }

        /// <summary>
        /// Finds all items overlapping the position `v`.
        /// </summary>
        /// <param name="v">The position to query.</param>
        public QueryEnumerator Query(float v) {
            return new QueryEnumerator(this, new Span(v, v));
        }
        /// <summary>
        /// Finds all items overlapping the `interval`.
        /// </summary>
        /// <param name="interval">The interval to query.</param>
        public QueryEnumerator Query(Interval interval) {
            return new QueryEnumerator(this, Span.From(interval));
        }

        /// <summary>
        /// Mostly used for debugging. It returns all the intervals in the tree including parent nodes.
        /// </summary>
        /// <param name="interval">The interval to query.</param>
        public IEnumerable<Interval> DebugNodes(Interval interval) {
            return DebugNodes(Span.From(interval));
        }
        /// <summary>
        /// Mostly used for debugging. It returns all the intervals in the tree including parent nodes.
        /// </summary>
        public IEnumerable<Interval> DebugAllNodes() {
            return DebugNodes(new Span(float.NegativeInfinity, float.PositiveInfinity));
        }
        private IEnumerable<Interval> DebugNodes(Span span) {
            if (_count == 0) yield break;

            int version = _version;
            Stack<int> stack = new();
            stack.Push(Root);
            while (stack.Count > 0) {
                Node node = _nodes[stack.Pop()];
                if (!node.Span.Overlaps(span)) continue;

                yield return node.Span.ToInterval();
                ThrowIfModified(version);

                if (node.Height > 0) {
                    stack.Push(node.Index + 1);
                    stack.Push(node.Index);
                }
            }
        }

        internal int Height => _count > 0 ? _nodes[Root].Height : 0;

        internal void Validate() {
            if (_parents[Root] != NullNode) throw new InvalidOperationException("Root has a parent.");
            if (_nodes[Root + 1].Height != EmptyHeight) throw new InvalidOperationException("The node beside the root isn't empty.");

            int freePairs = 0;
            for (int pair = _pairFreeList; pair != NullNode; pair = _parents[pair]) {
                if (_nodes[pair].Height != EmptyHeight || _nodes[pair + 1].Height != EmptyHeight) throw new InvalidOperationException("Free pair isn't empty.");
                freePairs++;
            }
            if (_nodeEnd != 2 * Math.Max(_count, 1) + 2 * freePairs) throw new InvalidOperationException("Leaked nodes.");

            int freeProxies = 0;
            for (int i = _proxyFreeList; i != NullNode; i = _proxies[i].Next) {
                if (_proxies[i].Node != NullNode) throw new InvalidOperationException("Free proxy points to a node.");
                freeProxies++;
            }
            if (freeProxies + _count != _proxies.Length) throw new InvalidOperationException("Leaked proxies.");

            int leafCount = 0;
            if (_count == 0) {
                if (_nodes[Root].Height != EmptyHeight) throw new InvalidOperationException("Empty tree has a root.");
            } else {
                ValidateSubtree(Root, ref leafCount);
            }
            if (leafCount != _count) throw new InvalidOperationException($"Expected {_count} leaves, found {leafCount}.");
            if (_leaves.Count != _count) throw new InvalidOperationException("The item lookup is out of sync.");
        }
        private int ValidateSubtree(int index, ref int leafCount) {
            ref Node node = ref _nodes[index];
            if (node.Height == 0) {
                if (_proxies[node.Index].Node != index) throw new InvalidOperationException("Proxy doesn't point to its leaf.");
                if (_leaves[_items[node.Index]] != node.Index) throw new InvalidOperationException("Item doesn't map to its leaf.");
                leafCount++;
                return 0;
            }
            if (node.Height < 0) throw new InvalidOperationException("Empty node in the tree.");

            int pair = node.Index;
            if ((pair & 1) != 0 || pair < 2 || pair >= _nodeEnd) throw new InvalidOperationException("Bad pair.");
            if (_parents[pair] != index || _parents[pair + 1] != index) throw new InvalidOperationException("Bad parent.");

            Span union = Span.Union(_nodes[pair].Span, _nodes[pair + 1].Span);
            if (!node.Span.Equals(union)) throw new InvalidOperationException("Node isn't the union of its children.");

            int height1 = ValidateSubtree(pair, ref leafCount);
            int height2 = ValidateSubtree(pair + 1, ref leafCount);
            if (node.Height != 1 + Math.Max(height1, height2)) throw new InvalidOperationException("Bad height.");
            return node.Height;
        }

        private void Initialize(int initialCapacity) {
            int capacity = Math.Max(initialCapacity, 16);

            // A tree of n items has 2n - 1 nodes plus the empty node beside the root.
            _nodes = new Node[2 * capacity];
            _parents = new int[2 * capacity];
            _nodes[Root] = Node.Empty;
            _nodes[Root + 1] = Node.Empty;
            _parents[Root] = NullNode;
            _parents[Root + 1] = NullNode;
            _nodeEnd = 2;
            _pairFreeList = NullNode;

            _proxies = new Proxy[capacity];
            _items = new T[capacity];
            LinkFreeProxies(0);

            _count = 0;
        }

        private void LinkFreeProxies(int start) {
            for (int i = start; i < _proxies.Length; i++) {
                _proxies[i].Node = NullNode;
                _proxies[i].Next = i + 1;
            }
            _proxies[^1].Next = NullNode;
            _proxyFreeList = start;
        }
        private int AllocateProxy() {
            if (_proxyFreeList == NullNode) {
                int oldCapacity = _proxies.Length;
                Array.Resize(ref _proxies, oldCapacity * 2);
                Array.Resize(ref _items, oldCapacity * 2);
                LinkFreeProxies(oldCapacity);
            }

            int leaf = _proxyFreeList;
            _proxyFreeList = _proxies[leaf].Next;
            _proxies[leaf].Next = NullNode;
            return leaf;
        }
        private void FreeProxy(int leaf) {
            _proxies[leaf].Node = NullNode;
            _proxies[leaf].Next = _proxyFreeList;
            _items[leaf] = default!;
            _proxyFreeList = leaf;
        }

        // Children are allocated in pairs so that siblings sit next to each other in memory.
        // Free pairs are chained through the parent of their first node.
        private int AllocatePair() {
            if (_pairFreeList != NullNode) {
                int free = _pairFreeList;
                _pairFreeList = _parents[free];
                return free;
            }

            if (_nodeEnd + 2 > _nodes.Length) {
                Array.Resize(ref _nodes, _nodes.Length * 2);
                Array.Resize(ref _parents, _parents.Length * 2);
            }

            int pair = _nodeEnd;
            _nodeEnd += 2;
            return pair;
        }
        private void FreePair(int pair) {
            _nodes[pair] = Node.Empty;
            _nodes[pair + 1] = Node.Empty;
            _parents[pair] = _pairFreeList;
            _parents[pair + 1] = NullNode;
            _pairFreeList = pair;
        }

        // A node moved to `index`, the nodes under it need to know.
        private void LinkChildren(int index) {
            ref Node node = ref _nodes[index];
            if (node.Height == 0) {
                _proxies[node.Index].Node = index;
            } else {
                _parents[node.Index] = index;
                _parents[node.Index + 1] = index;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Node MakeInternal(Node[] nodes, int pair) {
            ref Node child1 = ref nodes[pair];
            ref Node child2 = ref nodes[pair + 1];
            return new Node {
                Span = Span.Union(child1.Span, child2.Span),
                Index = pair,
                Height = 1 + Math.Max(child1.Height, child2.Height),
            };
        }

        // Greedy search for the sibling that adds the least length to the tree.
        // There are three choices for a new leaf D under a node A with children B and C:
        // 1: D becomes the sibling of A.
        // 2: D goes somewhere under B.
        // 3: D goes somewhere under C.
        // The descent picks the child with the lowest lower bound cost and stops once no child can beat the best cost.
        private int FindBestSibling(Span spanD) {
            Node[] nodes = _nodes;
            float centerD = spanD.Center;
            float lengthD = spanD.Length;

            int index = Root;
            if (nodes[index].Height == 0) return index;

            Span rootSpan = nodes[index].Span;
            float lengthBase = rootSpan.Length;
            float directCost = Span.Union(rootSpan, spanD).Length;
            float inheritedCost = 0f;

            int bestSibling = index;
            float bestCost = directCost;

            while (true) {
                int child1 = nodes[index].Index;
                int child2 = child1 + 1;

                // Cost of creating a new parent for this node and the new leaf.
                float cost = directCost + inheritedCost;
                if (cost < bestCost) {
                    bestSibling = index;
                    bestCost = cost;
                }

                // Inheritance cost seen by the children.
                inheritedCost += directCost - lengthBase;

                ref Node node1 = ref nodes[child1];
                ref Node node2 = ref nodes[child2];
                bool leaf1 = node1.Height == 0;
                bool leaf2 = node2.Height == 0;

                float lowerCost1 = float.MaxValue;
                float directCost1 = Span.Union(node1.Span, spanD).Length;
                float length1 = 0f;
                if (leaf1) {
                    float cost1 = directCost1 + inheritedCost;
                    if (cost1 < bestCost) {
                        bestSibling = child1;
                        bestCost = cost1;
                    }
                } else {
                    length1 = node1.Span.Length;
                    // Either child 1 is the sibling or one of its descendants is.
                    lowerCost1 = inheritedCost + directCost1 + MathF.Min(lengthD - length1, 0f);
                }

                float lowerCost2 = float.MaxValue;
                float directCost2 = Span.Union(node2.Span, spanD).Length;
                float length2 = 0f;
                if (leaf2) {
                    float cost2 = directCost2 + inheritedCost;
                    if (cost2 < bestCost) {
                        bestSibling = child2;
                        bestCost = cost2;
                    }
                } else {
                    length2 = node2.Span.Length;
                    lowerCost2 = inheritedCost + directCost2 + MathF.Min(lengthD - length2, 0f);
                }

                if (leaf1 && leaf2) break;

                // Can the cost possibly be decreased?
                if (bestCost <= lowerCost1 && bestCost <= lowerCost2) break;

                if (lowerCost1 == lowerCost2 && !leaf1) {
                    // No clear choice, this happens when both children fully contain D. Fall back to the distance.
                    lowerCost1 = MathF.Abs(node1.Span.Center - centerD);
                    lowerCost2 = MathF.Abs(node2.Span.Center - centerD);
                }

                if (lowerCost1 < lowerCost2 && !leaf1) {
                    index = child1;
                    lengthBase = length1;
                    directCost = directCost1;
                } else {
                    index = child2;
                    lengthBase = length2;
                    directCost = directCost2;
                }
            }

            return bestSibling;
        }

        private void InsertLeaf(int leaf, Span span) {
            Node leafNode = new() { Span = span, Index = leaf, Height = 0 };

            if (_nodes[Root].Height == EmptyHeight) {
                _nodes[Root] = leafNode;
                _proxies[leaf].Node = Root;
                return;
            }

            int sibling = FindBestSibling(span);

            // The sibling moves down into a new pair beside the leaf and its old spot becomes their parent.
            // Allocating can resize the arrays so it happens before reading them.
            int pair = AllocatePair();
            Node[] nodes = _nodes;
            int[] parents = _parents;

            nodes[pair] = nodes[sibling];
            nodes[pair + 1] = leafNode;
            parents[pair] = sibling;
            parents[pair + 1] = sibling;
            LinkChildren(pair);
            _proxies[leaf].Node = pair + 1;

            nodes[sibling] = MakeInternal(nodes, pair);

            Rotate(sibling);
            RefitAncestors(parents[sibling], rotate: true);
        }

        private void RemoveLeaf(int leaf) {
            int index = _proxies[leaf].Node;
            if (index == Root) {
                _nodes[Root] = Node.Empty;
                return;
            }

            // The sibling takes the parent's spot.
            int parent = _parents[index];
            _nodes[parent] = _nodes[index ^ 1];
            LinkChildren(parent);
            FreePair(index & ~1);

            RefitAncestors(_parents[parent], rotate: false);
        }

        // Rotating while removing costs a lot more than it improves the tree.
        private void RefitAncestors(int index, bool rotate) {
            Node[] nodes = _nodes;
            int[] parents = _parents;
            while (index != NullNode) {
                ref Node node = ref nodes[index];
                Node refit = MakeInternal(nodes, node.Index);

                // Nothing changes above this node.
                if (refit.Height == node.Height && refit.Span.Equals(node.Span)) break;

                node = refit;
                if (rotate) Rotate(index);

                index = parents[index];
            }
        }

        // Swaps a child of A with a grandchild under the other child when that lowers the length of the tree.
        //       A
        //    /     \
        //   B       C
        //  / \     / \
        // D   E   F   G
        private void Rotate(int iA) {
            Node[] nodes = _nodes;
            ref Node a = ref nodes[iA];
            if (a.Height < 2) return;

            int iB = a.Index;
            int iC = iB + 1;
            ref Node b = ref nodes[iB];
            ref Node c = ref nodes[iC];

            int bestDown = NullNode;
            int bestUp = NullNode;
            float bestDelta = 0f;

            if (c.Height > 0) {
                int iF = c.Index;
                int iG = iF + 1;
                float lengthC = c.Span.Length;

                // B <-> F then C (B G)
                float deltaBF = Span.Union(b.Span, nodes[iG].Span).Length - lengthC;
                if (deltaBF < bestDelta) {
                    bestDown = iB;
                    bestUp = iF;
                    bestDelta = deltaBF;
                }

                // B <-> G then C (F B)
                float deltaBG = Span.Union(b.Span, nodes[iF].Span).Length - lengthC;
                if (deltaBG < bestDelta) {
                    bestDown = iB;
                    bestUp = iG;
                    bestDelta = deltaBG;
                }
            }

            if (b.Height > 0) {
                int iD = b.Index;
                int iE = iD + 1;
                float lengthB = b.Span.Length;

                // C <-> D then B (C E)
                float deltaCD = Span.Union(c.Span, nodes[iE].Span).Length - lengthB;
                if (deltaCD < bestDelta) {
                    bestDown = iC;
                    bestUp = iD;
                    bestDelta = deltaCD;
                }

                // C <-> E then B (D C)
                float deltaCE = Span.Union(c.Span, nodes[iD].Span).Length - lengthB;
                if (deltaCE < bestDelta) {
                    bestDown = iC;
                    bestUp = iE;
                }
            }

            if (bestDown == NullNode) return;

            (nodes[bestDown], nodes[bestUp]) = (nodes[bestUp], nodes[bestDown]);
            LinkChildren(bestDown);
            LinkChildren(bestUp);

            // The node that went down now sits under its old sibling, which needs a refit.
            int sibling = bestDown ^ 1;
            nodes[sibling] = MakeInternal(nodes, nodes[sibling].Index);
            a.Height = 1 + Math.Max(nodes[iB].Height, nodes[iC].Height);
        }

        // Top down build that splits the leaves at the middle of their centers.
        // The nodes are written in depth first order.
        private void BuildTree(Node[] leaves, int[] order, float[] centers, int leafCount) {
            Node[] nodes = _nodes;
            _nodeEnd = 2;
            _pairFreeList = NullNode;
            nodes[Root + 1] = Node.Empty;

            BuildItem[] stack = new BuildItem[64];
            int top = 0;
            stack[0] = new BuildItem {
                NodeIndex = Root,
                Pair = BumpPair(Root),
                ChildCount = -1,
                StartIndex = 0,
                EndIndex = leafCount,
                SplitIndex = PartitionMid(order, centers, 0, leafCount),
            };

            while (true) {
                ref BuildItem item = ref stack[top];
                item.ChildCount++;

                if (item.ChildCount == 2) {
                    // Both children are written, the node can be finalized.
                    nodes[item.NodeIndex] = MakeInternal(nodes, item.Pair);
                    if (top == 0) break;
                    top--;
                    continue;
                }

                int slot = item.ChildCount;
                int startIndex = slot == 0 ? item.StartIndex : item.SplitIndex;
                int endIndex = slot == 0 ? item.SplitIndex : item.EndIndex;
                int nodeIndex = item.Pair + slot;

                if (endIndex - startIndex == 1) {
                    Node leaf = leaves[order[startIndex]];
                    nodes[nodeIndex] = leaf;
                    _proxies[leaf.Index].Node = nodeIndex;
                    continue;
                }

                if (top + 1 == stack.Length) Array.Resize(ref stack, stack.Length * 2);
                top++;
                stack[top] = new BuildItem {
                    NodeIndex = nodeIndex,
                    Pair = BumpPair(nodeIndex),
                    ChildCount = -1,
                    StartIndex = startIndex,
                    EndIndex = endIndex,
                    SplitIndex = PartitionMid(order, centers, startIndex, endIndex),
                };
            }
        }
        private int BumpPair(int parent) {
            int pair = _nodeEnd;
            _nodeEnd += 2;
            _parents[pair] = parent;
            _parents[pair + 1] = parent;
            return pair;
        }
        private static int PartitionMid(int[] order, float[] centers, int start, int end) {
            int count = end - start;
            if (count <= 2) return start + count / 2;

            float min = centers[start];
            float max = centers[start];
            for (int i = start + 1; i < end; i++) {
                min = MathF.Min(min, centers[i]);
                max = MathF.Max(max, centers[i]);
            }
            float pivot = (min + max) * 0.5f;

            // Hoare partition around the pivot.
            int i1 = start;
            int i2 = end;
            while (i1 < i2) {
                while (i1 < i2 && centers[i1] < pivot) i1++;
                while (i1 < i2 && centers[i2 - 1] >= pivot) i2--;

                if (i1 < i2) {
                    (order[i1], order[i2 - 1]) = (order[i2 - 1], order[i1]);
                    (centers[i1], centers[i2 - 1]) = (centers[i2 - 1], centers[i1]);
                    i1++;
                    i2--;
                }
            }

            if (i1 > start && i1 < end) return i1;
            return start + count / 2;
        }

        // The stacks are reused between searches to avoid allocations. Searches that run at the same time get their own.
        private int[] RentStack() {
            return Interlocked.Exchange(ref _stackCache, null) ?? new int[StackCapacity];
        }
        private void ReturnStack(int[] stack) {
            _stackCache = stack;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int[] GrowStack(int[] stack) {
            int[] bigger = new int[stack.Length * 2];
            stack.CopyTo(bigger, 0);
            return bigger;
        }

        private void ThrowIfNotLeaf(int leaf) {
            if ((uint)leaf >= (uint)_proxies.Length || _proxies[leaf].Node == NullNode) {
                throw new ArgumentException($"{leaf} isn't a leaf in the tree.", nameof(leaf));
            }
        }
        private void ThrowIfModified(int version) {
            if (version != _version) {
                throw new InvalidOperationException("Collection was modified after the enumerator was instantiated.");
            }
        }

        /// <summary>
        /// Enumerates every item in the tree.
        /// </summary>
        public struct Enumerator : IEnumerator<T> {
            internal Enumerator(IntervalTree<T> tree) {
                _tree = tree;
                _version = tree._version;
                _index = -1;
                _current = default;
            }

            /// <inheritdoc/>
            public readonly T Current => _current!;
            readonly object IEnumerator.Current => _current!;

            /// <inheritdoc/>
            public bool MoveNext() {
                _tree.ThrowIfModified(_version);

                Proxy[] proxies = _tree._proxies;
                while (++_index < proxies.Length) {
                    if (proxies[_index].Node != NullNode) {
                        _current = _tree._items[_index];
                        return true;
                    }
                }

                _index = proxies.Length;
                _current = default;
                return false;
            }

            /// <inheritdoc/>
            public void Reset() {
                _tree.ThrowIfModified(_version);
                _index = -1;
                _current = default;
            }

            /// <inheritdoc/>
            public readonly void Dispose() { }

            private readonly IntervalTree<T> _tree;
            private readonly int _version;
            private int _index;
            private T? _current;
        }

        /// <summary>
        /// Enumerates the items overlapping an interval. Use it in a foreach loop.
        /// </summary>
        public struct QueryEnumerator : IEnumerator<T>, IEnumerable<T> {
            internal QueryEnumerator(IntervalTree<T> tree, Span span) {
                _tree = tree;
                _span = span;
                _version = tree._version;
                _stack = null;
                _sp = -1;
                _current = default;
            }

            /// <inheritdoc/>
            public readonly T Current => _current!;
            readonly object IEnumerator.Current => _current!;

            /// <inheritdoc/>
            public bool MoveNext() {
                _tree.ThrowIfModified(_version);
                if (_sp < 0) Start();

                // Everything on the stack overlaps the span.
                ref Node nodes = ref MemoryMarshal.GetArrayDataReference(_tree._nodes);
                int[] stack = _stack!;
                int sp = _sp;
                while (sp > 0) {
                    int index = stack[--sp];
                    ref Node node = ref Unsafe.Add(ref nodes, index);

                    if (node.Height == 0) {
                        _sp = sp;
                        _current = _tree._items[node.Index];
                        return true;
                    }

                    if (sp + 2 > stack.Length) _stack = stack = GrowStack(stack);
                    int pair = node.Index;
                    ref Node child1 = ref Unsafe.Add(ref nodes, pair);
                    ref Node child2 = ref Unsafe.Add(ref child1, 1);
                    if (child1.Span.Overlaps(_span)) stack[sp++] = pair;
                    if (child2.Span.Overlaps(_span)) stack[sp++] = pair + 1;
                }

                _sp = 0;
                _current = default;
                return false;
            }
            private void Start() {
                _stack = _tree.RentStack();
                _sp = 0;
                if (_tree._nodes[Root].Span.Overlaps(_span)) _stack[_sp++] = Root;
            }

            /// <inheritdoc/>
            public void Reset() {
                _tree.ThrowIfModified(_version);
                Dispose();
                _sp = -1;
            }

            /// <inheritdoc/>
            public void Dispose() {
                if (_stack != null) {
                    _tree.ReturnStack(_stack);
                    _stack = null;
                }
                _current = default;
            }

            /// <summary>
            /// Returns this enumerator so that it can be used in a foreach loop.
            /// </summary>
            public readonly QueryEnumerator GetEnumerator() => this;
            readonly IEnumerator<T> IEnumerable<T>.GetEnumerator() => this;
            readonly IEnumerator IEnumerable.GetEnumerator() => this;

            private readonly IntervalTree<T> _tree;
            private readonly Span _span;
            private readonly int _version;
            private int[]? _stack;
            private int _sp;
            private T? _current;
        }

        internal struct Span(float min, float max) : IEquatable<Span> {
            public float Min = min;
            public float Max = max;

            public readonly float Length {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => Max - Min;
            }
            public readonly float Center => (Min + Max) * 0.5f;

            public static Span From(Interval interval) => new(interval.X, interval.X + interval.Length);
            public readonly Interval ToInterval() => new(Min, Max - Min);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Span Expand(float v) => new(Min - v, Max + v);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Span Union(in Span a, in Span b) => new(MathF.Min(a.Min, b.Min), MathF.Max(a.Max, b.Max));
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly bool Contains(in Span b) => Min <= b.Min && b.Max <= Max;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly bool Overlaps(in Span b) => Min <= b.Max && b.Min <= Max;

            public readonly bool Equals(Span b) => Min == b.Min && Max == b.Max;
            public override readonly bool Equals(object? obj) => obj is Span b && Equals(b);
            public override readonly int GetHashCode() => HashCode.Combine(Min, Max);
        }

        private struct Node {
            public Span Span;
            // The leaf for leafs, the first child of the pair for internal nodes.
            public int Index;
            // 0 for leafs, -1 for empty nodes.
            public int Height;

            public static readonly Node Empty = new() {
                Span = new Span(float.PositiveInfinity, float.NegativeInfinity),
                Index = NullNode,
                Height = EmptyHeight,
            };
        }

        private struct Proxy {
            // Where the leaf is in the nodes, -1 when the leaf is free.
            public int Node;
            public int Next;
        }

        private struct BuildItem {
            public int NodeIndex;
            public int Pair;
            public int ChildCount;
            public int StartIndex;
            public int SplitIndex;
            public int EndIndex;
        }

        private float _expandConstant;
        private float _moveConstant;

        private const int StackCapacity = 64;
        private const int NullNode = -1;
        private const int EmptyHeight = -1;
        private const int Root = 0;

        private Node[] _nodes = null!;
        private int[] _parents = null!;
        private int _nodeEnd;
        private int _pairFreeList;

        private Proxy[] _proxies = null!;
        private T[] _items = null!;
        private int _proxyFreeList;

        private int _count;
        private int _version = 0;
        private int[]? _stackCache;
        private readonly Dictionary<T, int> _leaves = [];
    }
}
