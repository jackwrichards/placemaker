using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    /// <summary>
    /// (P2-4) Walks on hills never sawtooth. The user: "smoothing the paths out, not having them double back on each other and snap".
    /// The game runner's hill matrix showed botanical gardens, woodland and adventure playgrounds on a 10 % slope with paths
    /// kinking every 6-10 m (a bend on every short roadmap link). Measured as how much the walks turn per 100 m.
    /// </summary>
    public class ParkPathKinkTests
    {
        private readonly ITestOutputHelper _out;
        public ParkPathKinkTests(ITestOutputHelper o) { _out = o; }

        private static float Mountains(float2 p)
        {
            float k = 2f * math.PI / 320f; float u = p.x - 110f, v = p.y - 100f;
            return 30f * (0.55f * math.sin((u * 0.8f + v * 0.6f) * k) + 0.3f * math.sin((-u * 0.5f + v * 0.9f) * k * 2f + 1.3f)
                + 0.15f * math.sin((u * 0.3f - v) * k * 4f + 0.7f));
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (var (t, s) in new[] { ("park", "garden"), ("park", "woodland"), ("park", "neglected"), ("botanicalGarden", "flowerShow"), ("botanicalGarden", "shrubbery"),
                         ("adventurePlayground", "adventure"), ("adventurePlayground", "toddler"), ("memorialPark", "sculpture") })
                foreach (var land in new[] { "slope", "mountains" }) yield return new object[] { t, s, land };
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void WalksOnHillsDoNotSawtooth(string id, string preset, string land)
        {
            Func<float2, float> h = land == "slope" ? p => 0.10f * p.y : Mountains;
            var gen = GeneratorRegistry.Get(id);
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var site = DynamicDecalsTests.Streets(220, 200, h);
            var ring = DynamicDecalsTests.Block(220, 200).Select(p => new float3(p.x, h(p.xz), p.z)).ToArray();
            var recipe = editor.SingleLayerRecipe(gen, 3);
            editor.ApplyPreset(recipe, "0", preset);
            TestKit.Entered(recipe, ring, site);
            var plan = Composer.Compose(recipe, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            var nets = plan.Layers.SelectMany(l => l.Nets).Where(n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide).ToList();
            // joints: net ends meeting at one point (rounded to 0.1 m)
            var ends = new Dictionary<(int, int), List<(float2 dirOut, float len)>>();
            foreach (var n in nets)
            {
                float len = 0f;
                for (int k = 0; k < 8; k++) len += math.distance(Composer.Bezier(n.A, n.B, n.C, n.D, k / 8f).xz, Composer.Bezier(n.A, n.B, n.C, n.D, (k + 1) / 8f).xz);
                var a = n.A.xz; var d = n.D.xz;
                var outA = math.normalizesafe(Composer.Bezier(n.A, n.B, n.C, n.D, 0.05f).xz - a);
                var outD = math.normalizesafe(Composer.Bezier(n.A, n.B, n.C, n.D, 0.95f).xz - d);
                Add((a, outA, len)); Add((d, outD, len));
                void Add((float2 p, float2 dir, float len) e)
                {
                    var key = ((int)math.round(e.p.x * 10f), (int)math.round(e.p.y * 10f));
                    if (!ends.TryGetValue(key, out var l)) ends[key] = l = new List<(float2, float)>();
                    l.Add((e.dir, e.len));
                }
            }
            // turning: inside each piece (every 2 m) and across joints where a walk runs on (two pieces meet, nothing else)
            double turn = 0, total = 0;
            foreach (var n in nets)
            {
                int m = math.max(2, (int)(math.distance(n.A.xz, n.D.xz) / 2f));
                float2 prev = default; bool has = false;
                for (int k = 0; k < m; k++)
                {
                    var a = Composer.Bezier(n.A, n.B, n.C, n.D, k / (float)m).xz; var b = Composer.Bezier(n.A, n.B, n.C, n.D, (k + 1) / (float)m).xz;
                    total += math.distance(a, b);
                    var d = math.normalizesafe(b - a);
                    if (has) turn += math.degrees(math.acos(math.clamp(math.dot(prev, d), -1f, 1f)));
                    prev = d; has = true;
                }
            }
            foreach (var j in ends.Values)
                if (j.Count == 2) turn += 180.0 - math.degrees(math.acos(math.clamp(math.dot(j[0].dirOut, j[1].dirOut), -1f, 1f)));
            var dump = Environment.GetEnvironmentVariable("PM_DUMP_PATHS");
            if (!string.IsNullOrEmpty(dump) && (id + "/" + preset + "/" + land).Contains(dump))
            {
                var sb = new System.Text.StringBuilder();
                foreach (var n in nets) { for (int k = 0; k <= 8; k++) { var q = Composer.Bezier(n.A, n.B, n.C, n.D, k / 8f).xz; sb.Append(q.x.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append(",").Append(q.y.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append(" "); } sb.AppendLine(); }
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(TestKit.RepoRoot(), ".forensics", "paths"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(TestKit.RepoRoot(), ".forensics", "paths", id + "-" + preset + "-" + land + ".txt"), sb.ToString());
            }
            double per100 = total > 1 ? turn / total * 100.0 : 0;
            _out.WriteLine($"{id}/{preset} on {land}: {nets.Count} path pieces, {total:F0} m, turning {per100:F0}° per 100 m");
            // before P2-4: botanical gardens 246-271° per 100 m (a kink every 10 m), garden park 160-195; after: 46-133
            Assert.True(per100 <= 180, $"walks turn {per100:F0}° per 100 m (sawtooth)");
        }
        [Theory]
        [InlineData("urban")]
        [InlineData("picnic")]
        public void DrawnPathStylesFollowTheSlopeOnSteepLand(string preset)
        {
            // (P2-4) the urban spokes made 408 steep path pieces on the runner's mountains, the picnic circuit 322
            foreach (var land in new[] { "slope", "mountains" })
            {
                Func<float2, float> h = land == "slope" ? p => 0.10f * p.y : Mountains;
                var gen = GeneratorRegistry.Get("park");
                var editor = new DesignEditor(GeneratorRegistry.Get);
                var site = DynamicDecalsTests.Streets(220, 200, h);
                var ring = DynamicDecalsTests.Block(220, 200).Select(p => new float3(p.x, h(p.xz), p.z)).ToArray();
                var recipe = editor.SingleLayerRecipe(gen, 3);
                editor.ApplyPreset(recipe, "0", preset);
                TestKit.Entered(recipe, ring, site);
                var plan = Composer.Compose(recipe, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
                var f = plan.Layers[0].Facts;
                bool switched = f.ContainsKey("pathStyleSteep");
                _out.WriteLine($"{preset} on {land}: switched {switched}, steep path pieces {(f.TryGetValue("harmonySteepEdges", out var st) ? st : -1)}");
                Assert.Equal(land == "mountains", switched);
                if (switched) Assert.Contains(plan.Layers[0].Issues, i => i.Key == "Placemaker.Park.Issue.PathStyleSteep");
            }
        }
    }
}
