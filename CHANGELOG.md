# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

- Nothing yet!

## [0.6.0] - 2026-10-10

### Added

- Ray casts on `AABBTree`. `Raycast(start, end)` returns every item whose AABB the segment crosses.
- `Raycast(start, end, callback)`, where the callback can clip the ray to find the closest hit.
- `Rebuild()` on both trees, which rebuilds the whole tree from scratch. Leaves stay valid.
- `Remove`, `Update`, and `Move` take the item itself, so items don't need to keep their leaf.
- `Contains(item)` and `GetLeaf(item)` on both trees.

### Changed

- `AABBTree` takes a position and a size as two `Vector2` where it took a `RectangleF`.
- `GetAABB`, `Bounds`, `DebugNodes`, and `DebugAllNodes` return `(Vector2 XY, Vector2 Size)` tuples where they returned a `RectangleF`.
- An item can only be added once to a tree and can't be null. Adding it again throws.
- `initialCapacity` counts items instead of nodes.
- `Query` and the tree's enumerator return structs instead of `IEnumerable<T>` and `IEnumerator<T>`.
- `Update`, `Move`, `Remove`, `GetAABB`, `GetInterval`, and `GetItem` throw on a leaf that isn't in the tree.

### Removed

- The dependency on MonoGame.Extended and KNI.Extended.

### Fixed

- `Move` restructured the tree on every call while an item kept moving. It now only does when the item leaves its expanded AABB.
- `Update` shrank an item's expanded AABB down to its exact size, so the next update almost always restructured the tree.

### Optimized

- Adding, removing, updating, querying, and iterating are faster on both trees.
- Queries and iteration no longer allocate.

## [0.5.0] - 2026-01-07

### Added

- `Apos.Spatial.KNI`, a package for KNI.

### Changed

- Targets .NET 8 instead of .NET Standard 2.0.
- Depends on MonoGame.Extended 5.3.1 instead of 3.8.0.

## [0.4.2] - 2024-02-11

### Added

- `Bounds` on both trees, the bounds of every item in the tree.

## [0.4.1] - 2023-12-28

### Fixed

- Adding an item to a big tree could throw an `IndexOutOfRangeException`.

## [0.4.0] - 2023-12-04

### Changed

- `Count` is the number of items. The number of nodes moved to `NodeCount`.

## [0.3.0] - 2023-04-30

### Added

- `IntervalTree`, the same tree in 1D, along with an `Interval` type.

## [0.2.4] - 2023-02-14

### Added

- `DebugNodes` and `DebugAllNodes`, which return the AABBs of the nodes in the tree, parents included.

## [0.2.3] - 2023-01-31

### Added

- `expandConstant` and `moveConstant` on the constructor, along with the `ExpandConstant` and `MoveConstant` properties.

### Fixed

- AABBs only expanded on their top and left sides. They expand evenly on every side now.

## [0.2.2] - 2022-01-25

### Added

- `Count`, the number of nodes in the tree.

## [0.2.1] - 2021-09-21

### Changed

- Items don't need to be classes anymore.

### Fixed

- `Update` and `Move` expanded AABBs twice when they restructured the tree.

## [0.2.0] - 2021-04-19

### Added

- XML documentation.

### Changed

- `Remove` returns -1, and passing it -1 does nothing.
- `Add` takes a non-nullable item.
- Changing the tree while iterating it or a query throws an `InvalidOperationException`.

### Removed

- The public `AABBTreeT` and `PriorityQueue` types.

### Fixed

- `Move` predicted motion in the wrong direction for items moving right or down.

## [0.1.0] - 2021-04-17

### Added

- `AABBTree`, with `Add`, `Remove`, `Update`, `Move`, and point and rectangle queries.

[Unreleased]: https://github.com/Apostolique/Apos.Spatial/compare/v0.6.0...HEAD
[0.6.0]: https://github.com/Apostolique/Apos.Spatial/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/Apostolique/Apos.Spatial/compare/v0.4.2...v0.5.0
[0.4.2]: https://github.com/Apostolique/Apos.Spatial/compare/v0.4.1...v0.4.2
[0.4.1]: https://github.com/Apostolique/Apos.Spatial/compare/v0.4.0...v0.4.1
[0.4.0]: https://github.com/Apostolique/Apos.Spatial/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/Apostolique/Apos.Spatial/compare/v0.2.4...v0.3.0
[0.2.4]: https://github.com/Apostolique/Apos.Spatial/compare/v0.2.3...v0.2.4
[0.2.3]: https://github.com/Apostolique/Apos.Spatial/compare/v0.2.2...v0.2.3
[0.2.2]: https://github.com/Apostolique/Apos.Spatial/compare/v0.2.1...v0.2.2
[0.2.1]: https://github.com/Apostolique/Apos.Spatial/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/Apostolique/Apos.Spatial/compare/d0dc8c99a81e680b51db955e9e54fed8b71ab05d...v0.2.0
[0.1.0]: https://github.com/Apostolique/Apos.Spatial/tree/d0dc8c99a81e680b51db955e9e54fed8b71ab05d
