// AI builder (docs/AI-BUILDER.md): the rough examples sent to the AI (Recipes/ai/examples.md), made from the premade types
// the user graded well, planned on one standard site (140 x 110 m, a street along the south side) and written down in
// the AI's own terms: local metres, its tools' words, counts and key positions. Runs only when asked:
//   PM_EXPORT_AI_EXAMPLES=1 dotnet test Tests/Placemaker.Tests --filter AiExamplesExport

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class AiExamplesExport
    {
        private readonly ITestOutputHelper _out;
        public AiExamplesExport(ITestOutputHelper output) { _out = output; }

        // (type, preset or null = its first, what the user said about it)
        private static readonly (string id, string preset, string grade)[] Types =
        {
            ("park", "natural", "B+ (the user: parks)"),
            ("wetland", null, "A+"),
            ("lookoutHill", null, "A"),
            ("memorialPark", null, "A-"),
            ("adventurePlayground", null, "A-"),
            ("farmstead", null, "good"),
            ("campground", null, "good"),
            ("botanicalGarden", null, "liked"),
            ("sportsPark", null, "D overall; the layout rules are sound"),
            ("parking", null, "D-: the layout rules are sound, the look was not"),
        };

        private static string F(float v) => Math.Round(v, 0).ToString(CultureInfo.InvariantCulture);
        private static string P(float2 p) => "(" + F(p.x) + "," + F(p.y) + ")";

        [Fact]
        public void Export()
        {
            if (Environment.GetEnvironmentVariable("PM_EXPORT_AI_EXAMPLES") != "1") return;
            var ring = TestKit.Rect(140, 110);
            var r2 = Poly.To2D(ring);
            var site = TestKit.RoadAlongBottom(140);
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var sb = new StringBuilder();
            sb.AppendLine("# Rough examples (a head start, not a template)");
            sb.AppendLine();
            sb.AppendLine("What this game's own premade types build on one standard site: 140 x 110 m, flat, a street along the south side (local metres, x east, z north, origin the south-west corner). They show scale, counts and structure that read well in this game. They are rough and they are the floor, not the ceiling: take the player's request and run with it, go further and be far more creative and complete than these. A farm should be a fully fleshed-out farm.");
            foreach (var (id, preset, grade) in Types)
            {
                var gen = GeneratorRegistry.Get(id);
                if (gen == null) { _out.WriteLine("no generator " + id); continue; }
                var recipe = editor.SingleLayerRecipe(gen, 11);
                string pid = preset ?? gen.Presets.FirstOrDefault()?.Id;
                if (pid != null) editor.ApplyPreset(recipe, "0", pid);
                TestKit.Entered(recipe, ring, site);
                var plan = Composer.Compose(recipe, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
                sb.AppendLine();
                sb.AppendLine($"## {id}{(pid != null ? " (" + pid + ")" : "")}: graded {grade}");
                Describe(sb, plan, r2);
            }
            var root = Directory.GetCurrentDirectory();
            while (root != null && !File.Exists(Path.Combine(root, "Placemaker.csproj"))) root = Path.GetDirectoryName(root);
            Assert.NotNull(root);
            var path = Path.Combine(root, "Recipes", "ai", "examples.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString().Replace("\r\n", "\n"));
            _out.WriteLine($"{path}: {sb.Length} chars");
        }

        private static float Length(PlannedNet n) => math.distance(n.A.xz, n.B.xz) + math.distance(n.B.xz, n.C.xz) + math.distance(n.C.xz, n.D.xz);

        private static void Describe(StringBuilder sb, ComposedPlan plan, float2[] site)
        {
            var objects = plan.Layers.SelectMany(l => l.Objects).ToList();
            var nets = plan.Layers.SelectMany(l => l.Nets).ToList();
            var surfaces = plan.Layers.SelectMany(l => l.Surfaces).ToList();
            var paints = plan.Layers.SelectMany(l => l.Paint).ToList();
            var terraform = plan.Layers.SelectMany(l => l.Terraform).ToList();
            float area = math.abs(Poly.SignedArea(site));

            // ground
            var ground = new List<string>();
            if (terraform.Count > 0)
            {
                var water = terraform.Where(t => math.isfinite(t.WaterLevel)).ToList();
                float hmin = terraform.Min(t => t.Sloped ? t.Ring.Min(q => q.y) : t.TargetHeight), hmax = terraform.Max(t => t.Sloped ? t.Ring.Max(q => q.y) : t.TargetHeight);
                ground.Add($"terrain shaped in {terraform.Count} patches from {F(hmin)} to {F(hmax)} m (site at 0)");
                if (water.Count > 0) ground.Add($"{water.Count} water bodies, {F(water.Sum(t => math.abs(Poly.SignedArea(Poly.To2D(t.Ring)))))} m² of basin");
            }
            foreach (var g in paints.GroupBy(p => p.Layer))
            {
                // the share of the site it paints (a paint ring is only the support of its strength field: measure inside the site)
                int inside = 0, total = 0;
                for (float x = 1f; x < 140f; x += 2f)
                    for (float z = 1f; z < 110f; z += 2f)
                    {
                        var q = new float2(x, z);
                        total++;
                        if (g.Any(p => p.Shape != null ? p.Shape.At(q) > 0.3f : Poly.Contains(Poly.To2D(p.Ring), q))) inside++;
                    }
                ground.Add($"paint layer {g.Key} ({(g.Key == 0 ? "erase" : Core.Ai.SiteBrief.DefaultPaint(g.Key))}) on about {F(100f * inside / math.max(1, total))} % of the site in {g.Count()} patches");
            }
            foreach (var g in surfaces.GroupBy(s => s.Role))
                ground.Add($"{g.Key.ToString().ToLowerInvariant()} surface {F(g.Sum(s => math.abs(Poly.SignedArea(Poly.To2D(s.Ring)))))} m² in {g.Count()} pieces{(string.IsNullOrEmpty(g.First().Asset.Id) ? "" : " (" + g.First().Asset.Id + ")")}");
            sb.AppendLine("- Ground: " + (ground.Count > 0 ? string.Join("; ", ground) : "untouched") + $" (site {F(area)} m²).");

            // lines
            foreach (var g in nets.GroupBy(n => n.Role))
            {
                float len = g.Sum(Length);
                var longest = g.OrderByDescending(Length).First();
                string asset = string.IsNullOrEmpty(longest.Asset.Id) ? (longest.Category ?? "") : longest.Asset.Id;
                int joined = g.Count(n => n.StartAttach >= 0 || n.EndAttach >= 0);
                sb.AppendLine($"- {g.Key} lines: {F(len)} m in {g.Count()} segments{(asset.Length > 0 ? ", e.g. " + asset : "")}{(joined > 0 ? $", {joined} joined to the street" : "")}; longest runs {P(longest.A.xz)} -> {P(longest.D.xz)}{(longest.FollowTerrain ? "" : " (raised, level)")}.");
            }

            // objects: counts by kind, positions of the rare ones (the features)
            var byKind = objects.GroupBy(o => !string.IsNullOrEmpty(o.Category) ? o.Category : o.Role.ToString().ToLowerInvariant()).OrderByDescending(g => g.Count()).ToList();
            var counts = byKind.Select(g => $"{g.Key} {g.Count()}");
            sb.AppendLine($"- Objects ({objects.Count}): {string.Join(", ", counts)}.");
            var trees = objects.Where(o => o.Role == ObjectRole.Tree).ToList();
            if (trees.Count > 0) sb.AppendLine($"- Trees: {trees.Count} ({F(trees.Count / (area / 10000f))}/ha), nearest-neighbour spacing about {F(MedianNearest(trees))} m.");
            var features = byKind.Where(g => g.Count() <= 6 && g.Key != "tree" && g.Key != "bush" && g.Key != "decal").Take(8)
                .Select(g => $"{g.Key} at {string.Join(" ", g.Select(o => P(o.Position.xz)))}").ToList();
            if (features.Count > 0) sb.AppendLine("- Features: " + string.Join("; ", features) + ".");
            var facts = plan.Layers.SelectMany(l => l.Facts).GroupBy(kv => kv.Key).Select(g => g.First()).Take(10).Select(kv => $"{kv.Key} {Math.Round(kv.Value, 1).ToString(CultureInfo.InvariantCulture)}").ToList();
            if (facts.Count > 0) sb.AppendLine("- Facts: " + string.Join(", ", facts) + ".");
        }

        private static float MedianNearest(List<PlannedObject> objs)
        {
            var d = new List<float>();
            for (int i = 0; i < objs.Count; i++)
            {
                float best = float.MaxValue;
                for (int j = 0; j < objs.Count; j++) if (i != j) best = math.min(best, math.distance(objs[i].Position.xz, objs[j].Position.xz));
                if (best < float.MaxValue) d.Add(best);
            }
            d.Sort();
            return d.Count > 0 ? d[d.Count / 2] : 0f;
        }
    }
}
