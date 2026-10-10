# Queries
This guide goes over every way to search the tree. If you don't have a tree set up yet, read the [Getting started](../getting-started/README.md) guide first.

## Rectangle

`Query(xy, size)` returns every item whose AABB overlaps a rectangle. `xy` is the top left corner:

```csharp
foreach (Entity e in _tree.Query(new Vector2(0, 0), new Vector2(800, 480))) {
    // e is in the rectangle.
}
```

## Point

`Query(point)` returns every item whose AABB contains a point:

```csharp
foreach (Entity e in _tree.Query(mouse)) {
    // e is under the mouse.
}
```

Items that only touch the edge of the rectangle or the point count as overlapping.

## Broad phase and narrow phase

A query is a broad phase search. It's fast because it's allowed to return too much: every item whose AABB touches the area, even when the item itself doesn't. Checking those results against your actual shapes is the narrow phase, and that part is up to you.

There are two reasons a query returns more than expected. A shape doesn't fill its AABB, for example a circle leaves the corners of its box empty. On top of that, the tree expands every AABB so that items don't need to be reinserted every time they move. [Moving items](../moving-items/README.md) explains how that works. So a point query can return a circle while the mouse is a bit outside of it.

When you need to be exact, test each result yourself:

```csharp
foreach (Entity e in _tree.Query(mouse)) {
    if (e.Contains(mouse)) {
        // The mouse is really on e.
    }
}
```

Here `e.Contains` stands for your own test against the item's shape. For a lot of uses like culling, the broad phase is enough on its own. Drawing a few extra items that are just off screen doesn't matter.

## Every item

The tree works in a `foreach`:

```csharp
foreach (Entity e in _tree) {
    // Do something with e.
}
```

`Count` is the number of items in the tree.

## Allocations

A query doesn't allocate when you use it in a `foreach`. You can also use LINQ on it, which allocates like it usually does.

You can run a query inside another one. For example to find the neighbors of every item:

```csharp
foreach (Entity a in _tree) {
    var (xy, size) = _tree.GetAABB(_tree.GetLeaf(a));
    foreach (Entity b in _tree.Query(xy, size)) {
        if (a != b) {
            // a and b might overlap.
        }
    }
}
```

## Changing the tree during a query

A query searches the tree as you go through its results. If you add, remove, or restructure the tree in the middle of it, the next result throws an `InvalidOperationException`. You can copy the results first:

```csharp
foreach (Entity e in _tree.Query(xy, size).ToList()) {
    _tree.Remove(e);
}
```

## Bounds

`Bounds` is the AABB around every item in the tree. It's `null` when the tree is empty:

```csharp
if (_tree.Bounds is var (xy, size)) {
    // Fit the camera to the world.
}
```

## Debugging

`DebugAllNodes` returns the AABB of every node in the tree, the boxes that group items included. `DebugNodes(xy, size)` only returns the ones that overlap a rectangle. You can draw them with [Apos.Shapes](https://github.com/Apostolique/Apos.Shapes) to see how the tree splits your world:

```csharp
foreach (var (xy, size) in _tree.DebugAllNodes()) {
    _sb.BorderRectangle(xy, size, Color.White * 0.3f, 1f);
}
```

These allocate, so they're meant for debugging. `NodeCount` is the number of nodes in the tree.

## Follow up

[Ray casts](../ray-casts/README.md), a guide that shows how to find the items along a line.
