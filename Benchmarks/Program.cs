using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using MonoGame.Extended;

namespace Apos.Spatial.Benchmarks {
    public static class Program {
        public static void Main(string[] args) {
            int[] counts = args.Length > 0 ? args.Select(int.Parse).ToArray() : [1_000, 10_000, 100_000];

            // Warm up so that the JIT is done optimizing before anything is measured.
            TextWriter output = Console.Out;
            Console.SetOut(TextWriter.Null);
            Run(2_000);
            Console.SetOut(output);

            foreach (int count in counts) {
                Run(count);
            }
        }

        private static void Run(int count) {
            Console.WriteLine($"## {count} items");
            Console.WriteLine();
            Console.WriteLine("| Test | Old | New | Speedup |");
            Console.WriteLine("| --- | ---: | ---: | ---: |");

            Scenario scenario = new(count, seed: 1);
            Compare("Add all", scenario, (s, t) => s.AddAll(t));
            Compare("Query 10k rects", scenario, (s, t) => s.QueryRects(t), prepare: (s, t) => s.AddAll(t));
            Compare("Query 100k points", scenario, (s, t) => s.QueryPoints(t), prepare: (s, t) => s.AddAll(t));
            Compare("Move all x60 frames", scenario, (s, t) => s.MoveAll(t, 60, useOffset: true), prepare: (s, t) => s.AddAll(t));
            Compare("Update all x60 frames", scenario, (s, t) => s.MoveAll(t, 60, useOffset: false), prepare: (s, t) => s.AddAll(t));
            Compare("Update all x60 frames, item API", scenario, (s, t) => s.MoveAllItemApi(t, 60), prepare: (s, t) => s.AddAll(t), withOld: false);
            Compare("Enumerate all x100", scenario, (s, t) => s.EnumerateAll(t, 100), prepare: (s, t) => s.AddAll(t));
            Compare("Remove all", scenario, (s, t) => s.RemoveAll(t), prepare: (s, t) => s.AddAll(t));
            Compare("Add sorted along a line", scenario, (s, t) => s.AddSorted(t));
            Compare("Raycast 10k, enumerate hits", scenario, (s, t) => s.Raycasts(t), prepare: (s, t) => s.AddAll(t), withOld: false);
            Compare("Raycast 10k, closest hit", scenario, (s, t) => s.RaycastsClosest(t), prepare: (s, t) => s.AddAll(t), withOld: false);
            Compare("Rebuild", scenario, (s, t) => { t.Rebuild(); return 0; }, prepare: (s, t) => s.AddAll(t), withOld: false);
            Compare("Query 10k rects after Rebuild", scenario, (s, t) => s.QueryRects(t), prepare: (s, t) => { s.AddAll(t); t.Rebuild(); }, withOld: false);

            Console.WriteLine();
            Quality(scenario);
            Allocations(scenario);
            Console.WriteLine();
        }

        private static void Compare(string name, Scenario scenario, Func<Scenario, ITree, long> run, Action<Scenario, ITree>? prepare = null, bool withOld = true) {
            (double oldTime, long oldResult) = withOld ? Measure(scenario, () => new OldTree(), run, prepare) : (double.NaN, 0L);
            (double newTime, long newResult) = Measure(scenario, () => new NewTree(), run, prepare);

            string check = "";
            if (withOld && oldResult != newResult) check += $" (old result {oldResult} vs {newResult})";

            string oldColumn = withOld ? $"{oldTime:0.00} ms" : "";
            string oldRatio = withOld ? $"{oldTime / newTime:0.0}x" : "";
            Console.WriteLine($"| {name} | {oldColumn} | {newTime:0.00} ms | {oldRatio}{check} |");
        }

        private static (double, long) Measure(Scenario scenario, Func<ITree> create, Func<Scenario, ITree, long> run, Action<Scenario, ITree>? prepare) {
            double best = double.MaxValue;
            long result = 0;
            for (int i = 0; i < 7; i++) {
                scenario.Reset();
                ITree tree = create();
                prepare?.Invoke(scenario, tree);

                GC.Collect();
                GC.WaitForPendingFinalizers();

                long start = Stopwatch.GetTimestamp();
                result = run(scenario, tree);
                double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (i > 0) best = Math.Min(best, elapsed);
            }
            return (best, result);
        }

        private static void Quality(Scenario scenario) {
            foreach (ITree tree in new ITree[] { new OldTree(), new NewTree() }) {
                scenario.Reset();
                scenario.AddAll(tree);
                string added = $"height {tree.Height}, area ratio {tree.AreaRatio:0.0}";
                scenario.MoveAll(tree, 300, useOffset: true);
                Console.WriteLine($"{tree.GetType().Name}: after adding {added}. After 300 frames of movement height {tree.Height}, area ratio {tree.AreaRatio:0.0}.");
            }
        }

        private static void Allocations(Scenario scenario) {
            scenario.Reset();
            OldTree oldTree = new();
            scenario.AddAll(oldTree);
            scenario.Reset();
            NewTree newTree = new();
            scenario.AddAll(newTree);

            scenario.QueryRects(oldTree);
            scenario.QueryRects(newTree);
            scenario.Raycasts(newTree);

            long before = GC.GetAllocatedBytesForCurrentThread();
            scenario.QueryRects(oldTree);
            long oldBytes = GC.GetAllocatedBytesForCurrentThread() - before;

            before = GC.GetAllocatedBytesForCurrentThread();
            scenario.QueryRects(newTree);
            long newBytes = GC.GetAllocatedBytesForCurrentThread() - before;

            before = GC.GetAllocatedBytesForCurrentThread();
            scenario.Raycasts(newTree);
            long rayBytes = GC.GetAllocatedBytesForCurrentThread() - before;

            Console.WriteLine($"Allocations for 10k rect queries: old {oldBytes / 1024} KB, new {newBytes / 1024} KB. 10k raycasts: {rayBytes / 1024} KB.");
        }
    }

    public class Item(int id) {
        public int Id = id;
        public Vector2 XY;
        public Vector2 Size;
        public Vector2 Velocity;
        public int Leaf = -1;
    }

    public class Scenario {
        public Scenario(int count, int seed) {
            Random random = new(seed);
            WorldSize = MathF.Sqrt(count) * 30f;

            _initialXY = new Vector2[count];
            _initialSize = new Vector2[count];
            _velocities = new Vector2[count];
            Items = new Item[count];
            for (int i = 0; i < count; i++) {
                _initialXY[i] = new Vector2(random.NextSingle() * WorldSize, random.NextSingle() * WorldSize);
                _initialSize[i] = new Vector2(1f + random.NextSingle() * 19f, 1f + random.NextSingle() * 19f);
                _velocities[i] = new Vector2(random.NextSingle() * 4f - 2f, random.NextSingle() * 4f - 2f);
                Items[i] = new Item(i);
            }

            _queries = new Vector2[10_000];
            for (int i = 0; i < _queries.Length; i++) {
                _queries[i] = new Vector2(random.NextSingle() * WorldSize, random.NextSingle() * WorldSize);
            }
            _points = new Vector2[100_000];
            for (int i = 0; i < _points.Length; i++) {
                _points[i] = new Vector2(random.NextSingle() * WorldSize, random.NextSingle() * WorldSize);
            }
            _rays = new (Vector2, Vector2)[10_000];
            for (int i = 0; i < _rays.Length; i++) {
                Vector2 start = new(random.NextSingle() * WorldSize, random.NextSingle() * WorldSize);
                float angle = random.NextSingle() * MathF.Tau;
                _rays[i] = (start, start + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 300f);
            }
        }

        public float WorldSize { get; }
        public Item[] Items { get; }

        public void Reset() {
            for (int i = 0; i < Items.Length; i++) {
                Items[i].XY = _initialXY[i];
                Items[i].Size = _initialSize[i];
                Items[i].Velocity = _velocities[i];
                Items[i].Leaf = -1;
            }
        }

        public long AddAll(ITree tree) {
            foreach (Item item in Items) item.Leaf = tree.Add(item.XY, item.Size, item);
            return tree.Count;
        }
        public long AddSorted(ITree tree) {
            for (int i = 0; i < Items.Length; i++) {
                Items[i].XY = new Vector2(i * 2f, 0f);
                Items[i].Size = Vector2.One;
                Items[i].Leaf = tree.Add(Items[i].XY, Items[i].Size, Items[i]);
            }
            return tree.Count;
        }
        public long RemoveAll(ITree tree) {
            foreach (Item item in Items) tree.Remove(item.Leaf);
            return tree.Count;
        }
        public long QueryRects(ITree tree) {
            long total = 0;
            Vector2 size = new(100f, 100f);
            foreach (Vector2 xy in _queries) total += tree.Query(xy, size);
            return total;
        }
        public long QueryPoints(ITree tree) {
            long total = 0;
            foreach (Vector2 point in _points) total += tree.Query(point);
            return total;
        }
        public long EnumerateAll(ITree tree, int times) {
            long total = 0;
            for (int i = 0; i < times; i++) total += tree.CountAll();
            return total;
        }
        public long MoveAll(ITree tree, int frames, bool useOffset) {
            long restructures = 0;
            for (int frame = 0; frame < frames; frame++) {
                foreach (Item item in Items) {
                    Step(item);
                    bool restructured = useOffset ? tree.Move(item.Leaf, item.XY, item.Size, item.Velocity) : tree.Update(item.Leaf, item.XY, item.Size);
                    if (restructured) restructures++;
                }
            }
            return restructures;
        }
        public long MoveAllItemApi(ITree tree, int frames) {
            long restructures = 0;
            for (int frame = 0; frame < frames; frame++) {
                foreach (Item item in Items) {
                    Step(item);
                    if (tree.UpdateItem(item, item.XY, item.Size)) restructures++;
                }
            }
            return restructures;
        }
        public long Raycasts(ITree tree) {
            long total = 0;
            foreach ((Vector2 start, Vector2 end) in _rays) total += tree.Raycast(start, end);
            return total;
        }
        public long RaycastsClosest(ITree tree) {
            long total = 0;
            foreach ((Vector2 start, Vector2 end) in _rays) total += tree.RaycastClosest(start, end);
            return total;
        }

        private void Step(Item item) {
            Vector2 xy = item.XY + item.Velocity;
            if (xy.X < 0f || xy.X > WorldSize) item.Velocity.X = -item.Velocity.X;
            if (xy.Y < 0f || xy.Y > WorldSize) item.Velocity.Y = -item.Velocity.Y;
            item.XY = xy;
        }

        public static float? RayVsBox(Vector2 start, Vector2 end, Vector2 xy, Vector2 size) {
            Vector2 d = end - start;
            float tMin = 0f;
            float tMax = 1f;
            if (!Slab(start.X, d.X, xy.X, xy.X + size.X, ref tMin, ref tMax)) return null;
            if (!Slab(start.Y, d.Y, xy.Y, xy.Y + size.Y, ref tMin, ref tMax)) return null;
            return tMin;
        }
        private static bool Slab(float p, float d, float min, float max, ref float tMin, ref float tMax) {
            if (MathF.Abs(d) < 1e-9f) return min <= p && p <= max;
            float t1 = (min - p) / d;
            float t2 = (max - p) / d;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = MathF.Max(tMin, t1);
            tMax = MathF.Min(tMax, t2);
            return tMin <= tMax;
        }

        private readonly Vector2[] _initialXY;
        private readonly Vector2[] _initialSize;
        private readonly Vector2[] _velocities;
        private readonly Vector2[] _queries;
        private readonly Vector2[] _points;
        private readonly (Vector2, Vector2)[] _rays;
    }

    public interface ITree {
        int Count { get; }
        int Height { get; }
        float AreaRatio { get; }
        int Add(Vector2 xy, Vector2 size, Item item);
        void Remove(int leaf);
        bool Move(int leaf, Vector2 xy, Vector2 size, Vector2 offset);
        bool Update(int leaf, Vector2 xy, Vector2 size);
        bool UpdateItem(Item item, Vector2 xy, Vector2 size) => throw new NotSupportedException();
        int Query(Vector2 xy, Vector2 size);
        int Query(Vector2 point);
        int CountAll();
        int Raycast(Vector2 start, Vector2 end) => throw new NotSupportedException();
        int RaycastClosest(Vector2 start, Vector2 end) => throw new NotSupportedException();
        void Rebuild() => throw new NotSupportedException();
    }

    public class OldTree : ITree {
        public OldAABBTree<Item> Tree = new();

        public int Count => Tree.Count;
        public int Height => Tree.Height;
        public float AreaRatio {
            get {
                List<RectangleF> nodes = Tree.DebugAllNodes().ToList();
                if (nodes.Count == 0) return 0f;
                float root = Perimeter(Tree.Bounds!.Value);
                float leafArea = 0f;
                foreach (Item item in Tree) leafArea += Perimeter(Tree.GetAABB(item.Leaf));
                return (nodes.Sum(Perimeter) - leafArea - root) / root;
            }
        }
        public int Add(Vector2 xy, Vector2 size, Item item) => Tree.Add(new RectangleF(xy, size), item);
        public void Remove(int leaf) => Tree.Remove(leaf);
        public bool Move(int leaf, Vector2 xy, Vector2 size, Vector2 offset) => Tree.Move(leaf, new RectangleF(xy, size), offset);
        public bool Update(int leaf, Vector2 xy, Vector2 size) => Tree.Update(leaf, new RectangleF(xy, size));
        public int Query(Vector2 xy, Vector2 size) {
            int count = 0;
            foreach (Item item in Tree.Query(new RectangleF(xy, size))) count++;
            return count;
        }
        public int Query(Vector2 point) {
            int count = 0;
            foreach (Item item in Tree.Query(point)) count++;
            return count;
        }
        public int CountAll() {
            int count = 0;
            foreach (Item item in Tree) count++;
            return count;
        }

        private static float Perimeter(RectangleF r) => r.Width + r.Height;
    }

    public class NewTree : ITree {
        public AABBTree<Item> Tree = new();

        public int Count => Tree.Count;
        public int Height => Tree.Height;
        public float AreaRatio => Tree.AreaRatio;
        public int Add(Vector2 xy, Vector2 size, Item item) => Tree.Add(xy, size, item);
        public void Remove(int leaf) => Tree.Remove(leaf);
        public bool Move(int leaf, Vector2 xy, Vector2 size, Vector2 offset) => Tree.Move(leaf, xy, size, offset);
        public bool Update(int leaf, Vector2 xy, Vector2 size) => Tree.Update(leaf, xy, size);
        public bool UpdateItem(Item item, Vector2 xy, Vector2 size) => Tree.Update(item, xy, size);
        public int Query(Vector2 xy, Vector2 size) {
            int count = 0;
            foreach (Item item in Tree.Query(xy, size)) count++;
            return count;
        }
        public int Query(Vector2 point) {
            int count = 0;
            foreach (Item item in Tree.Query(point)) count++;
            return count;
        }
        public int CountAll() {
            int count = 0;
            foreach (Item item in Tree) count++;
            return count;
        }
        public int Raycast(Vector2 start, Vector2 end) {
            int count = 0;
            foreach (Item item in Tree.Raycast(start, end)) count++;
            return count;
        }
        public int RaycastClosest(Vector2 start, Vector2 end) {
            Item? closest = null;
            Tree.Raycast(start, end, (item, maxFraction) => {
                if (Scenario.RayVsBox(start, end, item.XY, item.Size) is float fraction && fraction < maxFraction) {
                    closest = item;
                    return fraction;
                }
                return -1f;
            });
            return closest?.Id ?? 0;
        }
        public void Rebuild() => Tree.Rebuild();
    }
}
