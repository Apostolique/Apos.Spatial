# Apos.Spatial
Spatial partitioning library for MonoGame and KNI.

[![Discord](https://img.shields.io/discord/355231098122272778.svg)](https://discord.gg/MonoGame) [![NuGet](https://img.shields.io/nuget/v/Apos.Spatial.svg)](https://www.nuget.org/packages/Apos.Spatial/) [![NuGet](https://img.shields.io/nuget/dt/Apos.Spatial.svg)](https://www.nuget.org/packages/Apos.Spatial/)

## Description

This library finds the items in an area of your game world without checking every item. It's useful for culling what's off screen, picking what's under the mouse, finding what a bullet hits, or finding which objects are close enough to collide.

## Thanks!

Special thanks to [RandyGaul](https://github.com/RandyGaul) for providing me the AABBTree algorithm.

Thanks to [Erin Catto](https://github.com/erincatto) for [Box2D](https://github.com/erincatto/box2d).

## Features

* `AABBTree` for 2D and `IntervalTree` for 1D
* Point and rectangle queries
* Ray casts that return every hit, or that can stop at the closest one
* Items that move only restructure the tree once they leave their expanded AABB
* Queries and iteration don't allocate
* `Rebuild` to optimize the tree after adding a lot of items at once
* Debug queries that return every node of the tree so you can draw it
* Works with MonoGame and [KNI](https://github.com/kniengine/kni)

## Documentation

* [Getting started](https://apostolique.github.io/Apos.Spatial/getting-started/)
* [Moving items](https://apostolique.github.io/Apos.Spatial/moving-items/)
* [Queries](https://apostolique.github.io/Apos.Spatial/queries/)
* [Ray casts](https://apostolique.github.io/Apos.Spatial/ray-casts/)
* [Interval tree](https://apostolique.github.io/Apos.Spatial/interval-tree/)

## Getting started

Install with:

```
dotnet add package Apos.Spatial
```

Create a tree, add items with the top left corner and size of their AABB, then query it:

```csharp
using Apos.Spatial;

AABBTree<Entity> tree = new AABBTree<Entity>();

tree.Add(e.XY, e.Size, e);

foreach (Entity e in tree.Query(cameraXY, cameraSize)) {
    // Draw e.
}

e.XY += velocity * dt;
tree.Update(e, e.XY, e.Size);

tree.Remove(e);
```

Read the [Getting started](https://apostolique.github.io/Apos.Spatial/getting-started/) guide for the full walkthrough.

## Other projects you might like

* [Apos.Gui](https://github.com/Apostolique/Apos.Gui) - UI library for MonoGame.
* [Apos.Input](https://github.com/Apostolique/Apos.Input) - Polling input library for MonoGame.
* [Apos.Shapes](https://github.com/Apostolique/Apos.Shapes) - Shape rendering library for MonoGame.
* [Apos.Camera](https://github.com/Apostolique/Apos.Camera) - Camera library for MonoGame.
* [Apos.Editor](https://github.com/Apostolique/Apos.Editor) - Editor project built with MonoGame.
* [Apos.History](https://github.com/Apostolique/Apos.History) - A C# library that makes it easy to handle undo and redo.
* [Apos.Framework](https://github.com/Apostolique/Apos.Framework) - Game architecture for MonoGame.
* [AposGameStarter](https://github.com/Apostolique/AposGameStarter) - MonoGame project starter. Common files to help create a game faster.
