// Placemaker road lab: a plan as plain text (nets, blocks, caps and the ground after the build), so another build of
// the planner (master, for the "before" pictures) can hand its plans to this lab and have them rated with today's rating.
//
//   net <role> <A> <B> <C> <D> <startAttach> <endAttach> <startT> <endT> <category|->
//   region <tag,tag,...> <x y z>...
//   object <role> <x y z>
//   height <x0> <z0> <step> <nx> <nz> <values...>     (the ground after the build, sampled; absent = the site's ground)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Placemaker.Core;
using Unity.Mathematics;

namespace Placemaker.RoadLab
{
    public static class PlanText
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        public static void Write(string path, AreaPlan plan, float3[] ring)
        {
            var sb = new StringBuilder();
            string P(float3 p) => string.Format(C, "{0:0.###} {1:0.###} {2:0.###}", p.x, p.y, p.z);
            foreach (var n in plan.Nets)
                sb.AppendLine(string.Format(C, "net {0} {1} {2} {3} {4} {5} {6} {7:0.#####} {8:0.#####} {9}", n.Role, P(n.A), P(n.B), P(n.C), P(n.D),
                    n.StartAttach, n.EndAttach, n.StartAttachT, n.EndAttachT, string.IsNullOrEmpty(n.Category) ? "-" : n.Category));
            foreach (var r in plan.Regions)
                if (r?.Ring != null && r.Ring.Length >= 3)
                    sb.AppendLine("region " + (r.Tags.Length > 0 ? string.Join(",", r.Tags) : "-") + " " + string.Join(" ", r.Ring.Select(P)));
            foreach (var o in plan.Objects)
                if (o != null) sb.AppendLine("object " + o.Role + " " + P(o.Position));
            if (plan.HeightAfter != null && ring != null && ring.Length >= 3)
            {
                float2 mn = new float2(ring.Min(p => p.x), ring.Min(p => p.z)) - 60f, mx = new float2(ring.Max(p => p.x), ring.Max(p => p.z)) + 60f;
                const float step = 4f;
                int nx = (int)math.ceil((mx.x - mn.x) / step) + 1, nz = (int)math.ceil((mx.y - mn.y) / step) + 1;
                sb.Append(string.Format(C, "height {0:0.###} {1:0.###} {2} {3} {4}", mn.x, mn.y, step, nx, nz));
                for (int j = 0; j < nz; j++)
                    for (int i = 0; i < nx; i++)
                        sb.Append(string.Format(C, " {0:0.##}", plan.HeightAfter(new float2(mn.x + i * step, mn.y + j * step))));
                sb.AppendLine();
            }
            File.WriteAllText(path, sb.ToString());
        }

        public static AreaPlan Read(string path)
        {
            var plan = new AreaPlan();
            foreach (var line in File.ReadAllLines(path))
            {
                var t = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (t.Length == 0) continue;
                float F(int i) => float.Parse(t[i], C);
                float3 V(int i) => new float3(F(i), F(i + 1), F(i + 2));
                switch (t[0])
                {
                    case "net":
                        plan.Nets.Add(new PlannedNet
                        {
                            Role = (NetRole)Enum.Parse(typeof(NetRole), t[1]), A = V(2), B = V(5), C = V(8), D = V(11),
                            StartAttach = long.Parse(t[14], C), EndAttach = long.Parse(t[15], C), StartAttachT = F(16), EndAttachT = F(17),
                            Category = t[18] == "-" ? null : t[18],
                        });
                        break;
                    case "region":
                    {
                        var pts = new List<float3>();
                        for (int i = 2; i + 2 < t.Length; i += 3) pts.Add(V(i));
                        plan.Regions.Add(new PlannedRegion { Ring = pts.ToArray(), Tags = t[1] == "-" ? Array.Empty<string>() : t[1].Split(',') });
                        break;
                    }
                    case "object":
                        if (Enum.TryParse(t[1], out ObjectRole role)) plan.Objects.Add(new PlannedObject { Role = role, Position = V(2) });
                        break;
                    case "height":
                    {
                        float x0 = F(1), z0 = F(2), step = F(3);
                        int nx = int.Parse(t[4], C), nz = int.Parse(t[5], C);
                        var h = new float[nx * nz];
                        for (int k = 0; k < h.Length; k++) h[k] = F(6 + k);
                        plan.HeightAfter = p =>
                        {
                            float fx = math.clamp((p.x - x0) / step, 0f, nx - 1.001f), fz = math.clamp((p.y - z0) / step, 0f, nz - 1.001f);
                            int i = (int)fx, j = (int)fz;
                            float u = fx - i, v = fz - j;
                            float a = h[j * nx + i], b = h[j * nx + i + 1], c = h[(j + 1) * nx + i], d = h[(j + 1) * nx + i + 1];
                            return math.lerp(math.lerp(a, b, u), math.lerp(c, d, u), v);
                        };
                        break;
                    }
                }
            }
            return plan;
        }
    }
}
