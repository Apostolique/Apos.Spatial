using System;
using System.Runtime.Serialization;

namespace Apos.Spatial {
    public struct Interval : IEquatable<Interval> {
        public Interval(float x, float length) {
            X = x;
            Length = length;
        }

        [DataMember]
        public float X;

        [DataMember]
        public float Length;

        public bool Contains(int x) {
            return X <= x && x < (X + Length);
        }
        public bool Contains(float x) {
            return X <= x && x < (X + Length);
        }
        public bool Contains(Interval r) {
            return X <= r.X && (r.X + r.Length) <= (X + Length);
        }

        public bool Intersects(Interval r) {
            return r.X < X + Length && X < r.X + r.Length;
        }

        public override bool Equals(object obj) {
            return (obj is Interval r) && this == r;
        }
        public bool Equals(Interval r) {
            return this == r;
        }

        public override int GetHashCode() {
            unchecked {
                var hash = 17;
                hash = hash * 23 + X.GetHashCode();
                hash = hash * 23 + Length.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(Interval a, Interval b) {
            return a.X == b.X && a.Length == b.Length;
        }

        public static bool operator !=(Interval a, Interval b) {
            return !(a == b);
        }

        public static Interval Union(Interval a, Interval b) {
            float x = Math.Min(a.X, b.X);
            float length = Math.Max(a.X + a.Length, b.X + b.Length) - x;
            return new Interval(x, length);
        }
    }
}
