// Game runner lab (exploration, not a Placemaker feature): procedural meshes for the "custom assets on the fly" spike.
// A footprint (any simple outline in metres, object space XZ, origin on the ground) is extruded into walls and a roof:
// the shape a building made to fit a gap of land would have. Winding is set from each face's own normal, so the
// outline may be either orientation.

using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Placemaker.Core.Geometry;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    internal static class SuiteLabMesh
    {
        /// <summary>The outline of a named shape (centred on the origin, front toward -Z), or the given points.</summary>
        public static float2[] Footprint(string shape, float2 size, JArray points)
        {
            float w = size.x, d = size.y;
            switch ((shape ?? "box").ToLowerInvariant())
            {
                case "polygon":
                {
                    var r = new List<float2>();
                    if (points != null) foreach (var p in points) if (p is JArray a && a.Count >= 2) r.Add(new float2((float)a[0], (float)a[1]));
                    if (r.Count < 3) goto default;
                    var c = Poly.Centroid(r);
                    for (int i = 0; i < r.Count; i++) r[i] -= c;
                    return r.ToArray();
                }
                case "wedge":
                    // the land between two zone blocks on the outside of a curve: narrow at the street, wide at the back
                    return new[] { new float2(-0.2f * w, -d / 2), new float2(0.2f * w, -d / 2), new float2(0.5f * w, d / 2), new float2(-0.5f * w, d / 2) };
                case "tower":
                {
                    var r = new float2[8];
                    for (int i = 0; i < 8; i++) { float a = (i + 0.5f) * math.PI / 4f; r[i] = new float2(math.cos(a) * w / 2, math.sin(a) * d / 2); }
                    return r;
                }
                case "arch":
                {
                    // an L: two wings around a courtyard corner
                    float t = 0.4f;
                    return new[] { new float2(-w / 2, -d / 2), new float2(w / 2, -d / 2), new float2(w / 2, -d / 2 + t * d), new float2(-w / 2 + t * w, -d / 2 + t * d), new float2(-w / 2 + t * w, d / 2), new float2(-w / 2, d / 2) };
                }
                default:
                    return new[] { new float2(-w / 2, -d / 2), new float2(w / 2, -d / 2), new float2(w / 2, d / 2), new float2(-w / 2, d / 2) };
            }
        }

        /// <summary>
        /// Walls and a flat roof; null when the roof cannot be triangulated. The texture repeats every
        /// <paramref name="uvMetres"/> m, or (0) spans each face once, 0..1: virtual-textured materials only stream that range.
        /// </summary>
        public static Mesh Extrude(float2[] ring, float height, float uvMetres, string name)
        {
            if (ring == null || ring.Length < 3) return null;
            var ccw = Poly.IsCcw(ring) ? ring : Reverse(ring);
            bool perFace = uvMetres <= 0f;
            float2 lo = ccw[0], hi = ccw[0];
            foreach (var p in ccw) { lo = math.min(lo, p); hi = math.max(hi, p); }
            float2 span = math.max(hi - lo, new float2(0.01f));
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<Vector4>();
            var uv = new List<Vector2>();
            var tri = new List<int>();

            void Face(int a, int b, int c, float3 normal)
            {
                float3 pa = v[a], pb = v[b], pc = v[c];
                if (math.dot(math.cross(pb - pa, pc - pa), normal) < 0f) { int s = b; b = c; c = s; }
                tri.Add(a); tri.Add(b); tri.Add(c);
            }

            // walls: one quad per edge, outward normals, u along the perimeter, v up
            float run = 0f;
            for (int i = 0; i < ccw.Length; i++)
            {
                float2 a = ccw[i], b = ccw[(i + 1) % ccw.Length];
                float len = math.distance(a, b);
                if (len < 0.01f) continue;
                float2 dir = (b - a) / len;
                var outward = new float3(dir.y, 0f, -dir.x);   // CCW ring: the inside is on the left of each edge
                int k = v.Count;
                v.Add(new Vector3(a.x, 0f, a.y)); v.Add(new Vector3(b.x, 0f, b.y)); v.Add(new Vector3(b.x, height, b.y)); v.Add(new Vector3(a.x, height, a.y));
                for (int j = 0; j < 4; j++) { n.Add(outward); t.Add(new Vector4(dir.x, 0f, dir.y, 1f)); }
                if (perFace)
                {
                    uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(1f, 0f)); uv.Add(new Vector2(1f, 1f)); uv.Add(new Vector2(0f, 1f));
                }
                else
                {
                    uv.Add(new Vector2(run / uvMetres, 0f)); uv.Add(new Vector2((run + len) / uvMetres, 0f));
                    uv.Add(new Vector2((run + len) / uvMetres, height / uvMetres)); uv.Add(new Vector2(run / uvMetres, height / uvMetres));
                }
                Face(k, k + 1, k + 2, outward);
                Face(k, k + 2, k + 3, outward);
                run += len;
            }

            // roof: the outline ear-clipped at the top
            if (!EarClip.Triangulate(ccw, out var roof)) return null;
            int r0 = v.Count;
            foreach (var p in ccw)
            {
                v.Add(new Vector3(p.x, height, p.y));
                n.Add(Vector3.up);
                t.Add(new Vector4(1f, 0f, 0f, 1f));
                uv.Add(perFace ? new Vector2((p.x - lo.x) / span.x, (p.y - lo.y) / span.y) : new Vector2(p.x / uvMetres, p.y / uvMetres));
            }
            foreach (var f in roof) Face(r0 + f.x, r0 + f.y, r0 + f.z, new float3(0f, 1f, 0f));

            var mesh = new Mesh { name = name };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetTangents(t);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float2[] Reverse(float2[] r)
        {
            var o = new float2[r.Length];
            for (int i = 0; i < r.Length; i++) o[i] = r[r.Length - 1 - i];
            return o;
        }
    }
}
