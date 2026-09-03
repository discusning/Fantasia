using System;
using UnityEngine;

namespace Fantasia.Board
{
    [Serializable]
    public struct HexCoord : IEquatable<HexCoord>
    {
        public int Q;
        public int R;

        public HexCoord(int q, int r)
        {
            Q = q;
            R = r;
        }

        public int S => -Q - R;

        public static readonly HexCoord[] Directions =
        {
            new HexCoord(1, 0),
            new HexCoord(1, -1),
            new HexCoord(0, -1),
            new HexCoord(-1, 0),
            new HexCoord(-1, 1),
            new HexCoord(0, 1),
        };

        public HexCoord Neighbor(int direction) => this + Directions[direction];

        public int DistanceTo(HexCoord other)
        {
            int dq = Mathf.Abs(Q - other.Q);
            int dr = Mathf.Abs(R - other.R);
            int ds = Mathf.Abs(S - other.S);
            return Mathf.Max(dq, Mathf.Max(dr, ds));
        }

        // Flat-top axial layout (Red Blob Games convention). `size` is the hex circumradius.
        public Vector3 ToWorldPosition(float size)
        {
            float x = size * 1.5f * Q;
            float z = size * Mathf.Sqrt(3f) * (R + Q / 2f);
            return new Vector3(x, 0f, z);
        }

        public static HexCoord operator +(HexCoord a, HexCoord b) => new HexCoord(a.Q + b.Q, a.R + b.R);

        public bool Equals(HexCoord other) => Q == other.Q && R == other.R;
        public override bool Equals(object obj) => obj is HexCoord other && Equals(other);
        public override int GetHashCode() => (Q, R).GetHashCode();
        public override string ToString() => $"({Q}, {R})";
    }
}
