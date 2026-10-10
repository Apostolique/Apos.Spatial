# Getting started
This guide will show you how to get started with the Apos.Spatial library.

Before you start, make sure that you have a valid MonoGame project. You can create a new project by following this [other guide](https://learn-monogame.github.io/how-to/get-started/).

## Install

Install using the following dotnet command:

```
dotnet add package Apos.Spatial
```

If you're using [KNI](https://github.com/kniengine/kni), install `Apos.Spatial.KNI` instead. You can find other ways to install it on the [NuGet page](https://www.nuget.org/packages/Apos.Spatial/).

## What it does

Let's say your game has 10,000 entities and you only want to draw the ones on screen. You could check every entity each frame, but that gets slow. An AABB tree puts nearby entities in boxes, then puts those boxes in bigger boxes, all the way up to one box that holds the whole world. A query only goes inside the boxes that overlap what you're looking for, so most of the world gets skipped right away.

AABB stands for axis aligned bounding box. It's a rectangle that isn't rotated. Every item in the tree gets one. The tree only knows about those boxes. If your entity is a circle, a query returns the entities whose box touches the area. That's called the broad phase. Testing the circle yourself afterward is the narrow phase, for when you need to be exact.

## Setup

Import the library with:

```csharp
using Apos.Spatial;
```

Create a tree. The type parameter is the type of the items you'll put in it:

```csharp
AABBTree<Entity> _tree = new AABBTree<Entity>();
```

Add items with the top left corner and the size of their AABB:

```csharp
Entity e = new Entity(new Vector2(100, 100), new Vector2(50, 30));
_tree.Add(e.XY, e.Size, e);
```

An item can only be added once to the same tree. The tree finds items with their `Equals` and `GetHashCode`, so two items that are equal count as the same item.

## Queries

Find every item in a rectangle, for example the camera's view:

```csharp
foreach (Entity e in _tree.Query(cameraXY, cameraSize)) {
    // Draw e.
}
```

Find every item under a point, for example the mouse:

```csharp
foreach (Entity e in _tree.Query(mouse)) {
    // Highlight e.
}
```

You can also go through every item in the tree:

```csharp
foreach (Entity e in _tree) {
    // Do something with e.
}
```

## Moving and removing

When an item moves or changes size, tell the tree about its new AABB:

```csharp
e.XY += velocity * dt;
_tree.Update(e, e.XY, e.Size);
```

Remove an item with:

```csharp
_tree.Remove(e);
```

## Follow up

[Moving items](../moving-items/README.md), a guide that explains how the tree keeps up with items that move.
