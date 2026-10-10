# Ray casts
This guide shows how to find the items along a line. If you don't have a tree set up yet, read the [Getting started](../getting-started/README.md) guide first.

A ray cast goes from a `start` position to an `end` position. Positions along the ray are fractions: `0f` is `start` and `1f` is `end`.

## Every hit

`Raycast(start, end)` returns every item whose AABB the ray crosses:

```csharp
foreach (Entity e in _tree.Raycast(start, end)) {
    // The ray crosses e's AABB.
}
```

Items closer to `start` tend to come first, but the order isn't sorted.

Like queries, ray casts are a broad phase search. They test the expanded AABBs, so the ray can cross an item's AABB and still miss the item itself. Testing each result against your own shape is the narrow phase. The [Queries](../queries/README.md#broad-phase-and-narrow-phase) guide goes over it.

## Closest hit

`Raycast(start, end, callback)` calls the callback for each item whose AABB the ray crosses. The callback gets the item and `maxFraction`, how far along the ray the search currently goes. What it returns decides what happens next:

* `-1f` ignores the item and keeps going.
* `maxFraction` keeps going without changing anything.
* A fraction between `0f` and `maxFraction` clips the ray there. Items past that point get skipped.
* `0f` stops the ray cast.

To find the closest hit, return where the ray hit each item. The ray gets shorter with each hit, so the tree skips everything that's farther away:

```csharp
float closest = 1f;
Entity? hit = null;

_tree.Raycast(start, end, (e, maxFraction) => {
    if (e.Raycast(start, end) is float fraction && fraction < maxFraction) {
        closest = fraction;
        hit = e;
        return fraction;
    }
    return -1f;
});

Vector2 hitPosition = Vector2.Lerp(start, end, closest);
```

Here `e.Raycast` stands for your own test against the item's shape. It returns the fraction where the ray hits it, or `null` when it misses. The `fraction < maxFraction` check matters since the ray can cross an AABB before the clip point while the shape inside is hit after it. The example project has a test against rectangles called `RayVsRect` in [GameRoot.cs](https://github.com/Apostolique/Apos.Spatial/blob/main/Example/Game/GameRoot.cs).

## Any hit

If you only need to know whether something is in the way, for example for line of sight, return `0f` on the first hit to stop right away:

```csharp
bool blocked = false;

_tree.Raycast(eye, target, (e, maxFraction) => {
    if (e.BlocksSight && e.Raycast(eye, target) is not null) {
        blocked = true;
        return 0f;
    }
    return -1f;
});
```

Changing the tree from inside the callback throws an `InvalidOperationException`.

## Follow up

[Interval tree](../interval-tree/README.md), the same tree in one dimension.
