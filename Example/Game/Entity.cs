using Microsoft.Xna.Framework;

namespace GameProject {
    public class Entity {
        public Entity(uint id, Vector2 xy, Vector2 size, Vector2 velocity) {
            _id = id;
            XY = xy;
            Size = size;
            Velocity = velocity;
        }

        public uint Id => _id;
        public Vector2 XY { get; set; }
        public Vector2 Size { get; set; }
        public Vector2 Velocity { get; set; }

        readonly uint _id;
    }
}
