using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Apos.Spatial.Tests {
    public class IntervalTreeTests {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void RandomOperationsMatchBruteForce(int seed) {
            Random random = new(seed);
            IntervalTree<Item> tree = new();
            List<Item> items = [];
            int nextId = 0;

            for (int step = 0; step < 4000; step++) {
                int op = random.Next(100);
                if (op < 40 || items.Count == 0) {
                    Item item = new(nextId++, RandomInterval(random));
                    tree.Add(item.Interval, item);
                    items.Add(item);
                } else if (op < 55) {
                    Item item = items[random.Next(items.Count)];
                    items.Remove(item);
                    Assert.True(tree.Remove(item));
                    Assert.False(tree.Contains(item));
                } else if (op < 85) {
                    Item item = items[random.Next(items.Count)];
                    float offset = random.NextSingle() * 6f - 3f;
                    item.Interval = new Interval(item.Interval.X + offset, item.Interval.Length);
                    if (random.Next(2) == 0) tree.Move(item, item.Interval, offset);
                    else tree.Update(item, item.Interval);
                } else if (op < 95) {
                    Item item = items[random.Next(items.Count)];
                    item.Interval = RandomInterval(random);
                    tree.Update(tree.GetLeaf(item), item.Interval);
                } else {
                    tree.Rebuild();
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
        public void UpdateKeepsTheFatInterval() {
            IntervalTree<Item> tree = new(expandConstant: 2f);
            Item item = new(0, new Interval(10, 5));
            tree.Add(item.Interval, item);
            tree.Add(new Interval(100, 5), new Item(1, default));
            Interval fat = tree.GetInterval(tree.GetLeaf(item));

            Assert.False(tree.Update(item, new Interval(11, 5)));
            Assert.False(tree.Update(item, new Interval(12, 5)));
            Assert.Equal(fat, tree.GetInterval(tree.GetLeaf(item)));
            Assert.True(tree.Update(item, new Interval(13, 5)));
        }

        [Fact]
        public void EmptyTree() {
            IntervalTree<Item> tree = new();
            Assert.Empty(tree);
            Assert.Empty(tree.Query(new Interval(0, 10)));
            Assert.Empty(tree.Query(0f));
            Assert.Empty(tree.DebugAllNodes());
            tree.Rebuild();
            tree.Validate();
            Assert.Null(tree.Bounds);
        }

        private static void Check(IntervalTree<Item> tree, List<Item> items, Random random) {
            tree.Validate();
            Assert.Equal(items.Count, tree.Count);
            Assert.Equal(items.OrderBy(e => e.Id), tree.OrderBy(e => e.Id));

            Dictionary<Item, Interval> fat = [];
            foreach (Item item in items) {
                int leaf = tree.GetLeaf(item);
                Assert.Same(item, tree.GetItem(leaf));
                fat[item] = tree.GetInterval(leaf);
                Assert.True(fat[item].X <= item.Interval.X + 0.001f && item.Interval.X + item.Interval.Length <= fat[item].X + fat[item].Length + 0.001f);
            }

            for (int i = 0; i < 20; i++) {
                Interval query = RandomInterval(random, 40f);
                HashSet<Item> result = [.. tree.Query(query)];
                foreach (Item item in items) {
                    if (Overlaps(fat[item], query, 0.001f)) Assert.Contains(item, result);
                    if (!Overlaps(fat[item], query, -0.001f)) Assert.DoesNotContain(item, result);
                }

                float point = random.NextSingle() * 400f - 200f;
                result = [.. tree.Query(point)];
                foreach (Item item in items) {
                    if (Overlaps(fat[item], new Interval(point, 0f), 0.001f)) Assert.Contains(item, result);
                    if (!Overlaps(fat[item], new Interval(point, 0f), -0.001f)) Assert.DoesNotContain(item, result);
                }
            }

            Assert.Equal(tree.NodeCount, tree.DebugAllNodes().Count());
        }

        private static Interval RandomInterval(Random random, float maxLength = 20f) {
            return new Interval(random.NextSingle() * 400f - 200f, random.NextSingle() * maxLength);
        }
        // Positive `shrink` only counts clear overlaps, negative also counts near misses.
        private static bool Overlaps(Interval a, Interval b, float shrink) {
            return a.X + shrink <= b.X + b.Length && b.X <= a.X + a.Length - shrink;
        }

        private class Item(int id, Interval interval) {
            public int Id { get; } = id;
            public Interval Interval { get; set; } = interval;
        }
    }
}
