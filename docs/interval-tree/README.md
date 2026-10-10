# Interval tree
This guide shows the `IntervalTree`, the same tree as the `AABBTree` in one dimension. It assumes you've read the [Getting started](../getting-started/README.md) guide.

Let's say you're making a music sequencer. Each note starts at some time and lasts for some time. To draw the notes in view, or to find the notes playing right now, you want every note that overlaps a range of time. That's what an interval tree is for.

## Intervals

An `Interval` is a start `X` and a `Length`:

```csharp
Interval interval = new Interval(2f, 0.5f);
```

That interval goes from `2f` to `2.5f`.

## Setup

Create a tree and add items with their interval:

```csharp
IntervalTree<Note> _tree = new IntervalTree<Note>();

_tree.Add(new Interval(note.Start, note.Duration), note);
```

Find every item that overlaps an interval:

```csharp
foreach (Note n in _tree.Query(new Interval(viewStart, viewLength))) {
    // Draw n.
}
```

Find every item at a position:

```csharp
foreach (Note n in _tree.Query(time)) {
    // n is playing.
}
```

## Everything else

The rest works like the `AABBTree` with an `Interval` where it takes a position and a size:

* `Update(item, interval)` and `Move(item, interval, offset)`, where `offset` is a `float`. [Moving items](../moving-items/README.md) explains how they work.
* `Remove`, `Contains`, `GetLeaf`, `GetItem`, and `Rebuild`.
* `GetInterval(leaf)` returns the expanded interval the tree is using for a leaf.
* `Bounds`, `DebugNodes`, and `DebugAllNodes` return intervals.

There's no ray cast.
