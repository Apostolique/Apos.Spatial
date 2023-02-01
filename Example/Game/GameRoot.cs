using System;
using System.Linq;
using Apos.Input;
using Apos.Spatial;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;

namespace GameProject {
    public class GameRoot : Game {
        public GameRoot() {
            _graphics = new GraphicsDeviceManager(this);
            IsMouseVisible = true;
            Content.RootDirectory = "Content";
        }

        protected override void Initialize() {
            Window.AllowUserResizing = true;

            base.Initialize();
        }

        protected override void LoadContent() {
            _s = new SpriteBatch(GraphicsDevice);

            _random = new Random();

            InputHelper.Setup(this);

            _aabbTree = new AABBTree<Entity>();

            for (int i = 0; i < 20; i++) {
                CreateNew();
            }
        }

        protected override void Update(GameTime gameTime) {
            InputHelper.UpdateSetup();

            if (_quit.Pressed())
                Exit();
            if (_mouseLeft.Held() && !_held) {
                _start = InputHelper.NewMouse.Position.ToVector2();
                _end = _start;
            }
            if (_mouseLeft.HeldOnly()) {
                _end = InputHelper.NewMouse.Position.ToVector2();
            }
            _held = _mouseLeft.Held();

            if (_delete.Pressed()) {
                var rect = CreateRect(_start, _end);
                foreach (var e in _aabbTree.Query(rect).ToList()) {
                    _aabbTree.Remove(e.Leaf);
                }
            }

            if (_new.Pressed()) {
                CreateNew();
            }

            InputHelper.UpdateCleanup();
            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime) {
            GraphicsDevice.Clear(Color.Black);

            _s.Begin();
            foreach (var e in _aabbTree) {
                _s.FillRectangle(e.Rect, Color.Red * 0.8f);
            }

            if (_held) {
                var rect = CreateRect(_start, _end);
                foreach (var e in _aabbTree.Query(rect)) {
                    _s.FillRectangle(e.Rect, Color.Blue * 0.3f);
                }
                _s.DrawRectangle(rect, Color.White, 1f);
            } else {
                var xy = InputHelper.NewMouse.Position.ToVector2();
                foreach (var e in _aabbTree.Query(xy)) {
                    _s.FillRectangle(e.Rect, Color.White * 0.3f);
                }
            }
            _s.End();

            base.Draw(gameTime);
        }

        private static RectangleF CreateRect(Vector2 v1, Vector2 v2) {
            float left = MathF.Min(v1.X, v2.X);
            float right = MathF.Max(v1.X, v2.X);
            float top = MathF.Min(v1.Y, v2.Y);
            float bottom = MathF.Max(v1.Y, v2.Y);

            return new RectangleF(left, top, right - left, bottom - top);
        }

        private void CreateNew() {
            float minX = 10;
            float maxX = 500;
            float minY = 10;
            float maxY = 500;
            var r = new RectangleF(new Vector2(_random.NextSingle(minX, maxX), _random.NextSingle(minY, maxY)), new Vector2(_random.NextSingle(50, 100), _random.NextSingle(50, 100)));
            Entity b = new Entity(_index++, r);
            b.Leaf = _aabbTree.Add(b.Rect, b);
        }

        GraphicsDeviceManager _graphics;
        SpriteBatch _s;

        AABBTree<Entity> _aabbTree;

        ICondition _quit =
            new AnyCondition(
                new KeyboardCondition(Keys.Escape),
                new GamePadCondition(GamePadButton.Back, 0)
            );
        ICondition _mouseLeft = new MouseCondition(MouseButton.LeftButton);
        ICondition _new = new AnyCondition(new KeyboardCondition(Keys.Enter));
        ICondition _delete = new KeyboardCondition(Keys.Delete);

        Random _random;
        uint _index = 1;

        Vector2 _start;
        Vector2 _end;
        bool _held = false;
    }
}
