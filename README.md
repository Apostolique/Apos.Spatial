# Apos.Spatial
Spatial partitioning library for MonoGame.

## Thanks!

Special thanks to [RandyGaul](https://github.com/RandyGaul) for providing me the AABBTree algorithm.

## Documentation

Coming soon!

## Build

[![NuGet](https://img.shields.io/nuget/v/Apos.Spatial.svg)](https://www.nuget.org/packages/Apos.Spatial/) [![NuGet](https://img.shields.io/nuget/dt/Apos.Spatial.svg)](https://www.nuget.org/packages/Apos.Spatial/)

## Features

* Fast spatial partition algorithm. Great for 2D culling.

## Usage samples

First of all, your entities must implement the `IEqualityComparer' interface. So if your entity class is `Thing` you will have:

`public class Thing : IEqualityComparer<Thing>`

Your entities should have an identifier, `int Id` (or any class that you can do HashCode) and another integer, `int Leaf`. It will explained later on. Then implement the methods in the interface. You will end up with something in the likes of: (note that I use a Guid for the Id, it can be an integer or other hashable class)

``` public Guid Id = Guid.NewGuid();
    public int Leaf;

    public bool Equals(Thing x, Thing y)
    {
        if (x == null && y == null) return true;
        if (x == null || y == null) return false;
        return x.Id.Equals(y.Id);
    }

    public int GetHashCode(Thing obj)
    {
        return Id.GetHashCode();
    }
```

We are redy to use our entity in our game loop. The most important thing we need to declare is our AABBTree collection in which we will store our entities. Like this:

`private readonly AABBTree<Thing> _things = new (1024, 0, 1024);`

The parameters are optional and are only necessary if you want to do optimizations. First parameter is initial capacity, and it is described in the source code as the amount of nodes the tree can hold before it needs to be resized. Second is the expand constant, which expands the items that are added so that they don't need to be updated as often. Defaults to 2f, but you can be fine with 0 if you don't want to optimize this. Last one is the move constant, which expands the items that are moved so that they don't need to be updated as much. Defaults to 4f.

Now we can add the entities to our aabb tree, like for instance an enemy:

`enemy.Leaf = _things.Add(enemy.HitBox, enemy);`

First parameter is the enemy hit box, second the enemy entity itself. Note that it returns a leaf, which is the identifier within the aabb tree. We need to save it when we want to refer to the entity inside the aabb tree.

We need to update the entities inside the aabb tree (unless they have not moved). We do it like so:

`_things.Update(Player.Leaf, Player.HitBox);`

In this case we are updating the player entity. The first parameter in the Update is the entity leaf, which we stored when inserting. Second is the hit box.

We are ready now to be able to calculate collisions. This is as simple as: (for our `Thing thing`)

```
        foreach (Thing otherThing in _things.Query(thing.HitBox)) // We query the aabb tree instead of iterating it
        {
            if (otherThing.Id == thing.Id) continue; // We don't want an entity to collide with itself
            // Collision logic takes place here.
        }
```

## Other projects you might like

* [Apos.Gui](https://github.com/Apostolique/Apos.Gui) - UI library for MonoGame.
* [Apos.Input](https://github.com/Apostolique/Apos.Input) - Polling input library for MonoGame.
* [Apos.Camera](https://github.com/Apostolique/Apos.Camera) - Camera library for MonoGame.
* [Apos.Editor](https://github.com/Apostolique/Apos.Editor) - Editor project built with MonoGame.
* [Apos.History](https://github.com/Apostolique/Apos.History) - A C# library that makes it easy to handle undo and redo.
* [Apos.Framework](https://github.com/Apostolique/Apos.Framework) - Game architecture for MonoGame.
* [AposGameStarter](https://github.com/Apostolique/AposGameStarter) - MonoGame project starter. Common files to help create a game faster.
