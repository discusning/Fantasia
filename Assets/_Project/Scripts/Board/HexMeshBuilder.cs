using System.Collections.Generic;
using UnityEngine;

namespace Fantasia.Board
{
    // Builds a flat-shaded hex prism as placeholder tile geometry (no art dependency).
    public static class HexMeshBuilder
    {
        public static Mesh BuildFlatTopHexPrism(float radius, float height)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            Vector3 Corner(int i, float y)
            {
                float angle = Mathf.Deg2Rad * (60f * i);
                return new Vector3(radius * Mathf.Cos(angle), y, radius * Mathf.Sin(angle));
            }

            void AddTri(Vector3 a, Vector3 b, Vector3 c)
            {
                int start = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
            }

            var topCenter = new Vector3(0f, height, 0f);
            var bottomCenter = Vector3.zero;

            for (int i = 0; i < 6; i++)
            {
                int next = (i + 1) % 6;
                Vector3 topA = Corner(i, height);
                Vector3 topB = Corner(next, height);
                Vector3 bottomA = Corner(i, 0f);
                Vector3 bottomB = Corner(next, 0f);

                AddTri(topCenter, topB, topA);
                AddTri(bottomCenter, bottomA, bottomB);
                AddTri(topA, bottomB, bottomA);
                AddTri(topA, topB, bottomB);
            }

            var mesh = new Mesh { name = "HexPrism" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
