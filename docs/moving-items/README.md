# Moving items
This guide explains how the tree keeps up with items that move. If you don't have a tree set up yet, read the [Getting started](../getting-started/README.md) guide first.

## Update

Call `Update` after you change an item's position or size:

```csharp
e.XY += velocity * dt;
_tree.Update(e, e.XY, e.Size);
```

## Expanded AABBs

The tree doesn't store the exact AABB you give it. It expands it by `ExpandConstant` on every side. As long as the item stays inside that expanded AABB, `Update` doesn't touch the tree. Once the item leaves it, the item gets removed and inserted again with a new expanded AABB. `Update` returns `true` when that happens.

The trick is that most frames, most items don't go far enough to leave their expanded AABB, so updating them costs almost nothing.

Queries use the expanded AABBs though, so they can return items that are a bit outside the area you asked for. That's fine for a broad phase search, see [Queries](../queries/README.md#broad-phase-and-narrow-phase). A bigger margin means fewer reinserts while queries return more extra items. The default is `2f`, in your own world units. If your world units are pixels, that's a small margin and you can raise it:

```csharp
AABBTree<Entity> _tree = new AABBTree<Entity>(expandConstant: 8f);
```

If an item shrinks or stops moving, its expanded AABB can end up way bigger than it needs to be. When that happens, `Update` reinserts the item with a tighter one.

`GetAABB` returns the expanded AABB that the tree is using for a leaf:

```csharp
var (xy, size) = _tree.GetAABB(_tree.GetLeaf(e));
```

## Move

If you know how far an item moves each frame, you can use `Move` and pass that offset:

```csharp
Vector2 offset = velocity * dt;
e.XY += offset;
_tree.Move(e, e.XY, e.Size, offset);
```

`Move` stretches the expanded AABB toward where the item is going, by `offset * MoveConstant`. The default `MoveConstant` is `4f`. An item that keeps going in the same direction stays inside its expanded AABB for more frames, so it gets reinserted less often.

## Leaves

`Add` returns an `int`, the item's leaf. Every method that takes an item also has a version that takes its leaf. The item versions look up the leaf in a dictionary, the leaf versions skip that lookup. You can ignore leaves entirely, or keep them if you want to skip the lookup:

```csharp
e.Leaf = _tree.Add(e.XY, e.Size, e);
_tree.Update(e.Leaf, e.XY, e.Size);
e.Leaf = _tree.Remove(e.Leaf);
```

`Remove` returns `-1`, so it clears the field in the same line. Passing `-1` to `Remove` does nothing.

A leaf stays the same for as long as the item is in the tree, through `Update`, `Move`, and `Rebuild`. Once the item is removed, the next item you add can get the same leaf, so don't keep using a leaf after removing its item. Methods that take a leaf throw if the leaf isn't in the tree.

You can go from one to the other with `GetLeaf(item)` and `GetItem(leaf)`. `Contains(item)` checks if an item is in the tree.

## Rebuild

Adding items one by one builds a good tree, but not the best one. After you add a lot of items at once, for example when you load a level, you can call `Rebuild`:

```csharp
_tree.Rebuild();
```

It builds the whole tree again from scratch. Queries get faster, and leaves stay valid. It goes through every item though, so it's for once in a while, not every frame.

## Follow up

[Queries](../queries/README.md), a guide that goes over every way to search the tree.
