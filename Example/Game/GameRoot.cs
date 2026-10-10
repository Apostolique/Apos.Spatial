using System;
using System.Collections.Generic;
using System.Linq;
using Apos.Input;
using Apos.Shapes;
using Apos.Spatial;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace GameProject {
    /// <summary>
    /// Controls:
    /// Hover to query a point. Left drag to query a rectangle, Delete removes what's inside.
    /// Right click adds an entity at the mouse, Enter adds 10 random ones.
    /// Hold Q or E to place the start or end of the ray.
    /// Arrow keys push the entities under the mouse. Space pauses the movement.
    /// R rebuilds the tree. Tab shows or hides the tree nodes.
    /// </summary>
    public class GameRoot : Game {
        public GameRoot() {
            _graphics = new GraphicsDeviceManager(this);
#if KNI
            _graphics.GraphicsProfile = GraphicsProfile.FL10_0;
#else
            _graphics.GraphicsProfile = GraphicsProfile.HiDef;
#endif
            IsMouseVisible = true;
            Content.RootDirectory = "Content";
        }

        protected override void Initialize() {
            Window.AllowUserResizing = true;

            base.Initialize();
        }

        protected override void LoadContent() {
            _sb = new ShapeBatch(GraphicsDevice);

            _random = new Random();

            InputHelper.Setup(this);

            // The tree works in pixels here so the AABBs get a bigger margin than the default.
            _aabbTree = new AABBTree<Entity>(expandConstant: 8f);
            _closestHit = ClosestHit;

            Rectangle bounds = GraphicsDevice.Viewport.Bounds;
            _rayStart = new Vector2(50, bounds.Height / 2f);
            _rayEnd = new Vector2(bounds.Width - 50, bounds.Height / 3f);

            for (int i = 0; i < 40; i++) {
                CreateNew(RandomPosition());
            }
        }

        protected override void Update(GameTime gameTime) {
            InputHelper.UpdateSetup();

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            Vector2 mouse = InputHelper.NewMouse.Position.ToVector2();

            if (_quit.Pressed())
                Exit();

            if (_select.Pressed()) {
                _selectStart = mouse;
            }
            if (_select.Held()) {
                _selectEnd = mouse;
            }
            if (_delete.Pressed() && _select.Held()) {
                var (xy, size) = CreateRect(_selectStart, _selectEnd);
                // Copy the results first since the tree can't change during a query.
                foreach (var e in _aabbTree.Query(xy, size).ToList()) {
                    _aabbTree.Remove(e);
                    _entities.Remove(e);
                }
            }

            if (_add.Pressed()) {
                CreateNew(mouse);
            }
            if (_addMany.Pressed()) {
                for (int i = 0; i < 10; i++) {
                    CreateNew(RandomPosition());
                }
            }

            if (_placeStart.Held()) _rayStart = mouse;
            if (_placeEnd.Held()) _rayEnd = mouse;

            if (_pause.Pressed()) _paused = !_paused;
            if (_rebuild.Pressed()) _aabbTree.Rebuild();
            if (_showNodes.Pressed()) _drawNodes = !_drawNodes;

            _reinserted = 0;

            Vector2 push = Vector2.Zero;
            if (_left.Held()) push.X -= 1f;
            if (_right.Held()) push.X += 1f;
            if (_up.Held()) push.Y -= 1f;
            if (_down.Held()) push.Y += 1f;
            if (push != Vector2.Zero) {
                foreach (var e in _aabbTree.Query(mouse).ToList()) {
                    Vector2 offset = push * 300f * dt;
                    e.XY += offset;
                    if (_aabbTree.Move(e, e.XY, e.Size, offset)) _reinserted++;
                }
            }

            if (!_paused) {
                Rectangle bounds = GraphicsDevice.Viewport.Bounds;
                foreach (var e in _entities) {
                    Vector2 offset = e.Velocity * dt;
                    e.XY += offset;
                    Bounce(e, bounds);
                    // Move only touches the tree when the entity leaves its fat AABB.
                    if (_aabbTree.Move(e, e.XY, e.Size, offset)) _reinserted++;
                }
            }

            InputHelper.UpdateCleanup();
            base.Update(gameTime);

            Window.Title = $"Apos.Spatial | Items: {_aabbTree.Count} | Nodes: {_aabbTree.NodeCount} | Moved in tree this frame: {_reinserted}";
        }

        protected override void Draw(GameTime gameTime) {
            GraphicsDevice.Clear(Color.Black);

            Vector2 mouse = InputHelper.NewMouse.Position.ToVector2();

            _sb.Begin();
            if (_drawNodes) {
                foreach (var (xy, size) in _aabbTree.DebugAllNodes()) {
                    _sb.BorderRectangle(xy, size, Color.White * 0.2f, 1f);
                }
            }

            foreach (var e in _aabbTree) {
                _sb.FillRectangle(e.XY, e.Size, Color.Red * 0.8f);
            }

            if (_select.Held()) {
                var (xy, size) = CreateRect(_selectStart, _selectEnd);
                foreach (var e in _aabbTree.Query(xy, size)) {
                    _sb.FillRectangle(e.XY, e.Size, Color.Blue * 0.5f);
                }
                _sb.BorderRectangle(xy, size, Color.White, 1f);
            } else {
                foreach (var e in _aabbTree.Query(mouse)) {
                    _sb.FillRectangle(e.XY, e.Size, Color.White * 0.3f);
                }
            }

            // The tree only knows about the fat AABBs so the ray can hit an AABB and still miss the entity.
            foreach (var e in _aabbTree.Raycast(_rayStart, _rayEnd)) {
                var (xy, size) = _aabbTree.GetAABB(_aabbTree.GetLeaf(e));
                _sb.BorderRectangle(xy, size, Color.Yellow * 0.4f, 1f);
                if (RayVsRect(_rayStart, _rayEnd, e.XY, e.Size) is not null) {
                    _sb.BorderRectangle(e.XY, e.Size, Color.Yellow, 2f);
                }
            }

            _closest = 1f;
            _aabbTree.Raycast(_rayStart, _rayEnd, _closestHit);
            Vector2 hit = Vector2.Lerp(_rayStart, _rayEnd, _closest);

            _sb.FillLine(_rayStart, hit, 1f, Color.White);
            _sb.FillLine(hit, _rayEnd, 0.5f, Color.White * 0.4f);
            if (_closest < 1f) {
                _sb.DrawCircle(hit, 5f, Color.Yellow, Color.White, 1f);
            }
            _sb.DrawCircle(_rayStart, 6f, Color.Green, Color.White, 1f);
            _sb.DrawCircle(_rayEnd, 6f, Color.Blue, Color.White, 1f);

            _sb.End();

            base.Draw(gameTime);
        }

        /// <summary>
        /// Returning the fraction clips the ray so the tree skips anything farther than this hit.
        /// </summary>
        private float ClosestHit(Entity e, float maxFraction) {
            if (RayVsRect(_rayStart, _rayEnd, e.XY, e.Size) is float fraction && fraction < maxFraction) {
                _closest = fraction;
                return fraction;
            }
            return -1f;
        }

        private static void Bounce(Entity e, Rectangle bounds) {
            Vector2 xy = e.XY;
            Vector2 velocity = e.Velocity;
            if (xy.X < bounds.Left && velocity.X < 0f || xy.X + e.Size.X > bounds.Right && velocity.X > 0f) velocity.X = -velocity.X;
            if (xy.Y < bounds.Top && velocity.Y < 0f || xy.Y + e.Size.Y > bounds.Bottom && velocity.Y > 0f) velocity.Y = -velocity.Y;
            e.Velocity = velocity;
        }

        private static (Vector2 XY, Vector2 Size) CreateRect(Vector2 v1, Vector2 v2) {
            Vector2 min = Vector2.Min(v1, v2);
            Vector2 max = Vector2.Max(v1, v2);
            return (min, max - min);
        }

        private static float? RayVsRect(Vector2 start, Vector2 end, Vector2 xy, Vector2 size) {
            Vector2 d = end - start;
            float tMin = 0f;
            float tMax = 1f;
            if (!Slab(start.X, d.X, xy.X, xy.X + size.X, ref tMin, ref tMax)) return null;
            if (!Slab(start.Y, d.Y, xy.Y, xy.Y + size.Y, ref tMin, ref tMax)) return null;
            return tMin;
        }
        private static bool Slab(float p, float d, float min, float max, ref float tMin, ref float tMax) {
            if (d == 0f) return min <= p && p <= max;
            float t1 = (min - p) / d;
            float t2 = (max - p) / d;
            tMin = MathF.Max(tMin, MathF.Min(t1, t2));
            tMax = MathF.Min(tMax, MathF.Max(t1, t2));
            return tMin <= tMax;
        }

        private void CreateNew(Vector2 center) {
            var size = new Vector2(RandomRange(20, 80), RandomRange(20, 80));
            float angle = RandomRange(0, MathF.Tau);
            var velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * RandomRange(20, 80);
            Entity e = new Entity(_index++, center - size / 2f, size, velocity);
            _aabbTree.Add(e.XY, e.Size, e);
            _entities.Add(e);
        }
        private Vector2 RandomPosition() {
            Rectangle bounds = GraphicsDevice.Viewport.Bounds;
            return new Vector2(RandomRange(50, bounds.Width - 50), RandomRange(50, bounds.Height - 50));
        }
        private float RandomRange(float min, float max) {
            return min + _random.NextSingle() * (max - min);
        }

        GraphicsDeviceManager _graphics;
        ShapeBatch _sb;

        AABBTree<Entity> _aabbTree;
        List<Entity> _entities = new List<Entity>();
        AABBTree<Entity>.RaycastCallback _closestHit;

        ICondition _quit =
            new AnyCondition(
                new KeyboardCondition(Keys.Escape),
                new GamePadCondition(GamePadButton.Back, 0)
            );
        ICondition _select = new MouseCondition(MouseButton.LeftButton);
        ICondition _add = new MouseCondition(MouseButton.RightButton);
        ICondition _addMany = new KeyboardCondition(Keys.Enter);
        ICondition _delete = new KeyboardCondition(Keys.Delete);

        ICondition _placeStart = new KeyboardCondition(Keys.Q);
        ICondition _placeEnd = new KeyboardCondition(Keys.E);

        ICondition _pause = new KeyboardCondition(Keys.Space);
        ICondition _rebuild = new KeyboardCondition(Keys.R);
        ICondition _showNodes = new KeyboardCondition(Keys.Tab);

        ICondition _left = new KeyboardCondition(Keys.Left);
        ICondition _right = new KeyboardCondition(Keys.Right);
        ICondition _up = new KeyboardCondition(Keys.Up);
        ICondition _down = new KeyboardCondition(Keys.Down);

        Random _random;
        uint _index = 1;

        Vector2 _selectStart;
        Vector2 _selectEnd;

        Vector2 _rayStart;
        Vector2 _rayEnd;
        float _closest;

        bool _paused = false;
        bool _drawNodes = true;
        int _reinserted = 0;
    }
}
