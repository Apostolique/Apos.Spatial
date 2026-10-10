using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Xunit;

namespace Apos.Spatial.Tests {
    public class AABBTreeTests {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void RandomOperationsMatchBruteForce(int seed) {
            Random random = new(seed);
            AABBTree<Item> tree = new();
            List<Item> items = [];
            int nextId = 0;

            for (int step = 0; step < 4000; step++) {
                int op = random.Next(100);
                if (op < 40 || items.Count == 0) {
                    Item item = RandomItem(random, nextId++);
                    if (random.Next(2) == 0) item.Leaf = tree.Add(item.XY, item.Size, item);
                    else tree.Add(item.XY, item.Size, item);
                    items.Add(item);
                } else if (op < 55) {
                    Item item = items[random.Next(items.Count)];
                    items.Remove(item);
                    Assert.True(tree.Remove(item));
                    Assert.False(tree.Contains(item));
                } else if (op < 85) {
                    Item item = items[random.Next(items.Count)];
                    Vector2 offset = new(random.NextSingle() * 6f - 3f, random.NextSingle() * 6f - 3f);
                    item.XY += offset;
                    if (random.Next(2) == 0) tree.Move(item, item.XY, item.Size, offset);
                    else tree.Update(item, item.XY, item.Size);
                } else if (op < 95) {
                    // Resize, sometimes a lot.
                    Item item = items[random.Next(items.Count)];
                    item.XY = RandomPoint(random);
                    item.Size = RandomSize(random);
                    tree.Update(tree.GetLeaf(item), item.XY, item.Size);
                } else if (op < 96) {
                    tree.Rebuild();
                } else {
                    // Teleport.
                    Item item = items[random.Next(items.Count)];
                    item.XY = RandomPoint(random);
                    tree.Update(item, item.XY, item.Size);
                }

                if (step % 50 == 0) Check(tree, items, random);
            }

            Check(tree, items, random);

            foreach (Item item in items.ToList()) {
                Assert.True(tree.Remove(item));
                items.Remove(item);
            }
            Check(tree, items, random);
            Assert.Equal(0, tree.NodeCount);
            Assert.Null(tree.Bounds);
        }

        [Fact]
        public void LeafsStayValidAfterRebuild() {
            Random random = new(5);
            AABBTree<Item> tree = new();
            List<Item> items = [];
            for (int i = 0; i < 1000; i++) {
                Item item = RandomItem(random, i);
                item.Leaf = tree.Add(item.XY, item.Size, item);
                items.Add(item);
            }

            tree.Rebuild();
            tree.Validate();

            foreach (Item item in items) {
                Assert.Same(item, tree.GetItem(item.Leaf));
                Assert.Equal(item.Leaf, tree.GetLeaf(item));
            }
            Check(tree, items, random);
        }

        [Fact]
        public void UpdateKeepsTheFatAABB() {
            AABBTree<Item> tree = new(expandConstant: 2f);
            Item item = new(0, new Vector2(10, 10), new Vector2(5, 5));
            tree.Add(item.XY, item.Size, item);
            tree.Add(new Vector2(100, 100), new Vector2(5, 5), new Item(1, default, default));
            var fat = tree.GetAABB(tree.GetLeaf(item));

            Assert.False(tree.Update(item, item.XY, item.Size));
            Assert.False(tree.Update(item, new Vector2(11, 11), item.Size));
            Assert.False(tree.Update(item, new Vector2(12, 12), item.Size));
            Assert.Equal(fat, tree.GetAABB(tree.GetLeaf(item)));
            Assert.True(tree.Update(item, new Vector2(13, 13), item.Size));
        }

        [Fact]
        public void UpdateShrinksAHugeAABB() {
            AABBTree<Item> tree = new(expandConstant: 2f);
            Item item = new(0, new Vector2(0, 0), new Vector2(100, 100));
            tree.Add(item.XY, item.Size, item);

            Assert.True(tree.Update(item, new Vector2(50, 50), new Vector2(1, 1)));
            Assert.True(tree.GetAABB(tree.GetLeaf(item)).Size.X < 10f);
        }

        [Fact]
        public void MoveAtConstantSpeedRarelyRestructures() {
            AABBTree<Item> tree = new(expandConstant: 2f, moveConstant: 4f);
            Item item = new(0, new Vector2(0, 0), new Vector2(10, 10));
            tree.Add(item.XY, item.Size, item);

            int restructures = 0;
            Vector2 velocity = new(1f, 0.5f);
            for (int i = 0; i < 100; i++) {
                item.XY += velocity;
                if (tree.Move(item, item.XY, item.Size, velocity)) restructures++;
                Assert.True(Contains(tree.GetAABB(tree.GetLeaf(item)), (item.XY, item.Size), 0f));
            }

            Assert.InRange(restructures, 1, 30);
        }

        [Fact]
        public void ItemsCanOnlyBeAddedOnce() {
            AABBTree<Item> tree = new();
            Item item = new(0, Vector2.Zero, Vector2.One);
            tree.Add(item.XY, item.Size, item);

            Assert.Throws<ArgumentException>(() => tree.Add(item.XY, item.Size, item));
            Assert.Throws<ArgumentException>(() => tree.Update(new Item(1, default, default), item.XY, item.Size));
            Assert.False(tree.Remove(new Item(1, default, default)));
            Assert.Equal(1, tree.Count);

            tree.Clear();
            Assert.False(tree.Contains(item));
            tree.Add(item.XY, item.Size, item);
            Assert.True(tree.Contains(item));
        }

        [Fact]
        public void RemovedLeafsThrow() {
            AABBTree<Item> tree = new();
            Item a = new(0, Vector2.Zero, Vector2.One);
            Item b = new(1, new Vector2(5, 5), Vector2.One);
            int leafA = tree.Add(a.XY, a.Size, a);
            tree.Add(b.XY, b.Size, b);
            tree.Remove(leafA);

            Assert.Throws<ArgumentException>(() => tree.Remove(leafA));
            Assert.Throws<ArgumentException>(() => tree.GetAABB(leafA));
            Assert.Equal(-1, tree.Remove(-1));
        }

        [Fact]
        public void ModifyingDuringEnumerationThrows() {
            AABBTree<Item> tree = new();
            for (int i = 0; i < 10; i++) tree.Add(new Vector2(i, i), Vector2.One, new Item(i, default, default));

            Assert.Throws<InvalidOperationException>(() => {
                foreach (Item item in tree.Query(Vector2.Zero, new Vector2(20, 20))) tree.Remove(item);
            });
            Assert.Throws<InvalidOperationException>(() => {
                foreach (Item item in tree) tree.Add(Vector2.Zero, Vector2.One, new Item(100 + item.Id, default, default));
            });
            Assert.Throws<InvalidOperationException>(() => {
                tree.Raycast(new Vector2(-5, -5), new Vector2(50, 50), (item, fraction) => {
                    tree.Remove(item);
                    return fraction;
                });
            });
        }

        [Fact]
        public void NestedQueriesWork() {
            Random random = new(6);
            AABBTree<Item> tree = new();
            List<Item> items = [];
            for (int i = 0; i < 500; i++) {
                Item item = RandomItem(random, i);
                tree.Add(item.XY, item.Size, item);
                items.Add(item);
            }

            int pairs = 0;
            foreach (Item a in tree.Query(new Vector2(-1000, -1000), new Vector2(2000, 2000))) {
                var (xy, size) = tree.GetAABB(tree.GetLeaf(a));
                foreach (Item b in tree.Query(xy, size)) {
                    pairs++;
                }
            }

            int expected = 0;
            foreach (Item a in items) {
                var fatA = tree.GetAABB(tree.GetLeaf(a));
                foreach (Item b in items) {
                    if (Overlaps(fatA, tree.GetAABB(tree.GetLeaf(b)))) expected++;
                }
            }

            Assert.Equal(expected, pairs);
        }

        [Fact]
        public void EmptyTree() {
            AABBTree<Item> tree = new();
            Assert.Empty(tree);
            Assert.Empty(tree.Query(Vector2.Zero, new Vector2(10, 10)));
            Assert.Empty(tree.Query(Vector2.Zero));
            Assert.Empty(tree.Raycast(Vector2.Zero, Vector2.One));
            Assert.Empty(tree.DebugAllNodes());
            tree.Raycast(Vector2.Zero, Vector2.One, (item, fraction) => throw new Exception());
            tree.Rebuild();
            tree.Validate();
            Assert.Null(tree.Bounds);
        }

        [Fact]
        public void SingleItem() {
            AABBTree<Item> tree = new(expandConstant: 0f);
            Item item = new(0, Vector2.Zero, new Vector2(10, 10));
            tree.Add(item.XY, item.Size, item);
            tree.Validate();

            Assert.Equal([item], tree.Query(new Vector2(5, 5)).ToList());
            Assert.Equal([item], tree.Raycast(new Vector2(-5, 5), new Vector2(5, 5)).ToList());
            Assert.Equal([item], tree.ToList());
            Assert.Equal((Vector2.Zero, new Vector2(10, 10)), tree.Bounds);
            Assert.Equal(1, tree.NodeCount);

            Assert.True(tree.Update(item, new Vector2(100, 100), new Vector2(10, 10)));
            tree.Validate();
            Assert.Empty(tree.Query(new Vector2(5, 5)));
            Assert.Equal([item], tree.Query(new Vector2(105, 105)).ToList());
        }

        [Fact]
        public void GrowsPastTheInitialCapacity() {
            Random random = new(8);
            AABBTree<Item> tree = new(initialCapacity: 1);
            List<Item> items = [];
            for (int i = 0; i < 300; i++) {
                Item item = RandomItem(random, i);
                tree.Add(item.XY, item.Size, item);
                items.Add(item);
            }
            Check(tree, items, random);
        }

        [Fact]
        public void RaycastFindsTheClosestHit() {
            Random random = new(7);
            AABBTree<Item> tree = new(expandConstant: 0f);
            List<Item> items = [];
            for (int i = 0; i < 2000; i++) {
                Item item = RandomItem(random, i);
                tree.Add(item.XY, item.Size, item);
                items.Add(item);
            }

            for (int i = 0; i < 200; i++) {
                Vector2 start = RandomPoint(random);
                Vector2 end = RandomPoint(random);

                Item? closest = null;
                float closestFraction = 2f;
                int calls = 0;
                tree.Raycast(start, end, (item, maxFraction) => {
                    calls++;
                    if (SegmentFraction(start, end, (item.XY, item.Size), 0f) is float t && t <= maxFraction) {
                        closest = item;
                        closestFraction = t;
                        return t;
                    }
                    return -1f;
                });

                float expected = items.Select(e => SegmentFraction(start, end, (e.XY, e.Size), 0f) ?? 2f).DefaultIfEmpty(2f).Min();
                if (expected > 1f) {
                    Assert.Null(closest);
                } else {
                    Assert.NotNull(closest);
                    Assert.Equal(expected, closestFraction, 0.0001f);
                }
                Assert.True(calls <= tree.Raycast(start, end).Count());
            }
        }

        [Fact]
        public void ZeroLengthRayActsLikeAPointQuery() {
            AABBTree<Item> tree = new(expandConstant: 0f);
            Item item = new(0, Vector2.Zero, new Vector2(10, 10));
            tree.Add(item.XY, item.Size, item);
            tree.Add(new Vector2(20, 20), new Vector2(10, 10), new Item(1, default, default));

            Assert.Equal([item], tree.Raycast(new Vector2(5, 5), new Vector2(5, 5)).ToList());
        }

        private static void Check(AABBTree<Item> tree, List<Item> items, Random random) {
            tree.Validate();
            Assert.Equal(items.Count, tree.Count);
            Assert.Equal(items.Count, tree.Count());
            Assert.Equal(items.OrderBy(e => e.Id), tree.OrderBy(e => e.Id));

            Dictionary<Item, (Vector2, Vector2)> fat = [];
            foreach (Item item in items) {
                Assert.True(tree.Contains(item));
                int leaf = tree.GetLeaf(item);
                Assert.Same(item, tree.GetItem(leaf));
                fat[item] = tree.GetAABB(leaf);
                Assert.True(Contains(fat[item], (item.XY, item.Size), 0.001f));
            }

            for (int i = 0; i < 20; i++) {
                (Vector2, Vector2) query = (RandomPoint(random), RandomSize(random, 40f));
                HashSet<Item> result = [.. tree.Query(query.Item1, query.Item2)];
                Assert.Equal(result.Count, tree.Query(query.Item1, query.Item2).Count());
                foreach (Item item in items) {
                    if (Overlaps(Shrink(fat[item], 0.001f), query)) Assert.Contains(item, result);
                    if (!Overlaps(Shrink(fat[item], -0.001f), query)) Assert.DoesNotContain(item, result);
                }

                Vector2 point = RandomPoint(random);
                result = [.. tree.Query(point)];
                foreach (Item item in items) {
                    if (Overlaps(Shrink(fat[item], 0.001f), (point, Vector2.Zero))) Assert.Contains(item, result);
                    if (!Overlaps(Shrink(fat[item], -0.001f), (point, Vector2.Zero))) Assert.DoesNotContain(item, result);
                }

                Vector2 start = RandomPoint(random);
                Vector2 end = RandomPoint(random);
                result = [.. tree.Raycast(start, end)];
                foreach (Item item in items) {
                    if (SegmentFraction(start, end, fat[item], 0.001f) is not null) Assert.Contains(item, result);
                    if (SegmentFraction(start, end, fat[item], -0.001f) is null) Assert.DoesNotContain(item, result);
                }
            }

            Assert.Equal(tree.NodeCount, tree.DebugAllNodes().Count());
        }

        private static Item RandomItem(Random random, int id) {
            return new Item(id, RandomPoint(random), RandomSize(random));
        }
        private static Vector2 RandomSize(Random random, float maxSize = 20f) {
            return new Vector2(random.NextSingle() * maxSize, random.NextSingle() * maxSize);
        }
        private static Vector2 RandomPoint(Random random) {
            return new Vector2(random.NextSingle() * 400f - 200f, random.NextSingle() * 400f - 200f);
        }

        private static (Vector2 XY, Vector2 Size) Shrink((Vector2 XY, Vector2 Size) r, float v) {
            return (r.XY + new Vector2(v), r.Size - new Vector2(2f * v));
        }
        private static bool Overlaps((Vector2 XY, Vector2 Size) a, (Vector2 XY, Vector2 Size) b) {
            Vector2 aMax = a.XY + a.Size;
            Vector2 bMax = b.XY + b.Size;
            return a.XY.X <= bMax.X && b.XY.X <= aMax.X && a.XY.Y <= bMax.Y && b.XY.Y <= aMax.Y;
        }
        private static bool Contains((Vector2 XY, Vector2 Size) outer, (Vector2 XY, Vector2 Size) inner, float tolerance) {
            Vector2 outerMax = outer.XY + outer.Size;
            Vector2 innerMax = inner.XY + inner.Size;
            return outer.XY.X <= inner.XY.X + tolerance && outer.XY.Y <= inner.XY.Y + tolerance && innerMax.X <= outerMax.X + tolerance && innerMax.Y <= outerMax.Y + tolerance;
        }

        // Slab test. Returns where the segment enters the box shrunk by `shrink`, or null when it misses.
        private static float? SegmentFraction(Vector2 start, Vector2 end, (Vector2 XY, Vector2 Size) r, float shrink) {
            r = Shrink(r, shrink);
            if (r.Size.X < 0f || r.Size.Y < 0f) return null;

            Vector2 d = end - start;
            float tMin = 0f;
            float tMax = 1f;
            float[] p = [start.X, start.Y];
            float[] dir = [d.X, d.Y];
            float[] min = [r.XY.X, r.XY.Y];
            float[] max = [r.XY.X + r.Size.X, r.XY.Y + r.Size.Y];
            for (int axis = 0; axis < 2; axis++) {
                if (MathF.Abs(dir[axis]) < 1e-9f) {
                    if (p[axis] < min[axis] || p[axis] > max[axis]) return null;
                } else {
                    float t1 = (min[axis] - p[axis]) / dir[axis];
                    float t2 = (max[axis] - p[axis]) / dir[axis];
                    if (t1 > t2) (t1, t2) = (t2, t1);
                    tMin = MathF.Max(tMin, t1);
                    tMax = MathF.Min(tMax, t2);
                    if (tMin > tMax) return null;
                }
            }
            return tMin;
        }

        private class Item(int id, Vector2 xy, Vector2 size) {
            public int Id { get; } = id;
            public Vector2 XY { get; set; } = xy;
            public Vector2 Size { get; set; } = size;
            public int Leaf { get; set; } = -1;
        }
    }
}
