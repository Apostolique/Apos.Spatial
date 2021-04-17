using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using MonoGame.Extended;

namespace GameProject {
    public class Entity : IEqualityComparer<Entity> {
        public Entity(uint id, RectangleF r) {
            _id = id;
            _rect = r;
        }

        public uint Id => _id;
        public RectangleF Rect {
            get => _rect;
            set {
                _rect = value;
            }
        }
        public int Leaf {
            get;
            set;
        }

        public bool Equals([AllowNull] Entity x, [AllowNull] Entity y) {
            return x.Id == y.Id;
        }

        public int GetHashCode([DisallowNull] Entity obj) {
            return obj.Id.GetHashCode();
        }

        uint _id;
        RectangleF _rect;
    }
}
