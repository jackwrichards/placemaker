// s18: size-aware plaza composition and ground palettes.
// Playtest: a 0.15 ha triangle came out as overlapping rings and bands, slivers, an orange/ochre look and a
// fountain half over its pad's edge. These tests pin the rules: tiers by size, no overlapping ground pieces,
// no slivers or holes, the fountain on the focal pad, nothing straddling the outline, palettes coordinated
// and calm by default, old saves stable.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Plaza;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class PlazaSizeTests
    {
        // ───────────── sites ─────────────

        private static float3[] Shape(string shape, float area)
        {
            float2[] b;
            switch (shape)
            {
                case "triangle": b = new[] { new float2(0, 0), new float2(100, 0), new float2(28, 72) }; break;
                case "slimTriangle": b = new[] { new float2(0, 0), new float2(100, 0), new float2(80, 30) }; break;
                case "sliver": b = new[] { new float2(0, 0), new float2(120, 0), new float2(104, 26), new float2(14, 26) }; break;
                case "L": b = new[] { new float2(0, 0), new float2(60, 0), new float2(60, 26), new float2(26, 26), new float2(26, 60), new float2(0, 60) }; break;
                default: b = new[] { new float2(0, 0), new float2(50, 0), new float2(50, 50), new float2(0, 50) }; break;
            }
            float s = math.sqrt(area / Poly.Area(b));
            return b.Select(p => new float3(p.x * s, 0, p.y * s)).ToArray();
        }

        private static readonly string[] Shapes = { "triangle", "slimTriangle", "sliver", "square", "L" };
        private static readonly float[] Areas = { 250f, 700f, 1500f, 5000f, 15000f };

        /// <summary>Plaza as the player gets it: defaults + default features (fountain…), a preset, suggested entrances.</summary>
        private static ComposedPlan Compose(float3[] ring, string preset = null, Action<SettingsBag> edit = null)
        {
            var gen = GeneratorRegistry.Get("plaza");
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var r = Recipe.Single(gen, 1);
            editor.ApplyDefaults(r);
            uint n = 0;
            editor.AddDefaultFeatures(r, () => ++n);
            if (preset != null) editor.ApplyPreset(r, RecipePaths.Root, preset);
            edit?.Invoke(r.Root.Settings);
            uint k = 0;
            foreach (var kv in RecipePaths.Walk(r)) kv.Value.Seed = 1000u + (++k);
            TestKit.Entered(r, ring, null);
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, new SiteContext(), CancellationToken.None);
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (var s in Shapes)
                foreach (var a in Areas)
                    yield return new object[] { s, a, "" };
            foreach (var p in TestKit.Gen("plaza").Presets)
                foreach (var s in new[] { "triangle", "sliver" })
                    foreach (var a in new[] { 300f, 1500f })
                        yield return new object[] { s, a, p.Id };
        }

        // ───────────── tiers ─────────────

        [Theory]
        [InlineData(300f, 9f, PlazaTier.Micro)]
        [InlineData(1500f, 5f, PlazaTier.Micro)]
        [InlineData(1500f, 15f, PlazaTier.Small)]
        [InlineData(4000f, 9f, PlazaTier.Small)]   // a long street strip stays compact
        [InlineData(5000f, 30f, PlazaTier.Medium)]
        [InlineData(20000f, 60f, PlazaTier.Large)]
        public void TiersFollowSizeAndDepth(float area, float inscribed, PlazaTier expected) =>
            Assert.Equal(expected, PlazaTiers.Of(area, inscribed));

        // ───────────── ground geometry ─────────────

        [Theory]
        [MemberData(nameof(Cases))]
        public void GroundIsCleanAtEverySizeAndShape(string shape, float area, string preset)
        {
            var ring = Shape(shape, area);
            var r2 = Poly.To2D(ring);
            string label = $"{shape} {area} m² {(preset == "" ? "default" : preset)}";
            var plan = Compose(ring, preset == "" ? null : preset);
            Assert.DoesNotContain(plan.AllIssues(), i => i.Severity == IssueSeverity.Error);
            var plaza = plan.Layers.First(l => l.GeneratorId == "plaza");
            var pieces = plaza.Surfaces.Select(s => Poly.To2D(s.Ring)).ToList();
            string What(int i) => plaza.Surfaces[i].Role + " " + plaza.Surfaces[i].Asset.Id;
            Assert.NotEmpty(pieces);

            // No slivers: every laid piece is a real piece of paving.
            for (int i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                Assert.True(Poly.Area(p) >= 1.2f, $"{label}: sliver of {Poly.Area(p):0.00} m² ({What(i)})");
                Assert.True(Poly.MeanWidth(p) >= 0.3f, $"{label}: needle piece {Poly.MeanWidth(p):0.00} m wide ({What(i)})");
            }

            // No overlaps: the ground is a tiling (overlapping paved surfaces z-fight in CS2).
            var bounds = pieces.Select(p => { Poly.Bounds(p, out var mn, out var mx); return (mn, mx); }).ToList();
            for (int i = 0; i < pieces.Count; i++)
                for (int j = i + 1; j < pieces.Count; j++)
                {
                    var (a0, a1) = bounds[i]; var (b0, b1) = bounds[j];
                    if (a1.x < b0.x || b1.x < a0.x || a1.y < b0.y || b1.y < a0.y) continue;
                    float overlap = Poly.Intersection(pieces[i], pieces[j]).Sum(x => x.Area);
                    Assert.True(overlap <= 0.5f, $"{label}: ground pieces {i} and {j} overlap by {overlap:0.0} m²");
                }

            // No holes: dropped slivers go to the field, so the paving covers the square.
            float covered = pieces.Sum(p => Poly.Area(p));
            float whole = Poly.Area(r2);
            Assert.True(covered >= whole * 0.93f, $"{label}: paving covers {covered / whole:P0} of the square");

            // Nothing straddles the outline; the focal piece stands well inside.
            foreach (var l in plan.Layers)
                foreach (var o in l.Objects)
                {
                    if (o.Role == ObjectRole.AccessPoint || o.Role == ObjectRole.Decal || o.Role == ObjectRole.Marker) continue;
                    float d = Poly.Contains(r2, o.Position.xz) ? Poly.DistanceToBoundary(r2, o.Position.xz) : -1f;
                    float need = o.Role == ObjectRole.Centerpiece ? 1.8f : o.Role == ObjectRole.Lamp || o.Role == ObjectRole.Furniture ? 0.1f : 0.3f;
                    Assert.True(d >= need, $"{label}: {l.GeneratorId} {o.Role} {d:0.00} m from the outline");
                }

            // The fountain stands on the square's focal pad: centre of the "core" region.
            var fountain = plan.Layers.Where(l => l.GeneratorId == "fountain").SelectMany(l => l.Objects).FirstOrDefault(o => o.Role == ObjectRole.Centerpiece);
            var core = plaza.Regions.FirstOrDefault(g => g.Key == "core");
            if (fountain != null && core != null)
            {
                var c = Poly.Centroid(Poly.To2D(core.Ring));
                Assert.True(math.distance(c, fountain.Position.xz) < 0.75f, $"{label}: fountain at {fountain.Position.xz} {math.distance(c, fountain.Position.xz):0.0} m off the focal pad {c}; issues " + string.Join(",", plan.AllIssues().Select(x => x.Key)));
            }
        }

        [Theory]
        [InlineData("triangle")]
        [InlineData("slimTriangle")]
        [InlineData("square")]
        public void SmallSquaresHaveOneClearIdea(string shape)
        {
            foreach (var area in new[] { 300f, 1500f })
            {
                var plan = Compose(Shape(shape, area));
                var plaza = plan.Layers.First(l => l.GeneratorId == "plaza");
                Assert.True(PlazaTiers.IsCompact((PlazaTier)(int)plaza.Facts["plazaTier"]), $"{shape} {area}: not compact");
                // Field, one border band, at most one focal pad — three stones, no rings, axes or lawns.
                int stones = plaza.Surfaces.Select(s => s.Asset.Id).Distinct().Count();
                Assert.InRange(stones, 1, 3);
                Assert.DoesNotContain(plaza.Surfaces, s => s.Role == SurfaceRole.Grass);
                Assert.DoesNotContain(plaza.Nets, n => n.Role != NetRole.Fence);
                // One focal piece, lamps on the edge rhythm.
                int focal = plan.Layers.SelectMany(l => l.Objects).Count(o => o.Role == ObjectRole.Centerpiece);
                Assert.True(focal <= 1, $"{shape} {area}: {focal} centrepieces");
                Assert.True(TestKit.Count(plaza, ObjectRole.Lamp) >= 3, $"{shape} {area}: lamps");
                // Street shops do not eat a small square.
                Assert.DoesNotContain(plaza.Regions, g => g.Tags.Contains(StreetFrontage.ShopsTag));
            }
        }

        [Fact]
        public void TheFountainFitsTheSquare()
        {
            // 0.15 ha triangle (the playtest square): a fountain that fits, on the focal point, never the 9 m tower half off it.
            var ring = Shape("slimTriangle", 1500f);
            var region = LayerRegion.From(Poly.To2D(ring));
            var plan = Compose(ring);
            var fountain = plan.Layers.Where(l => l.GeneratorId == "fountain").SelectMany(l => l.Objects).Single(o => o.Role == ObjectRole.Centerpiece);
            var focal = LeisureKit.FocalPoint(region, out float inscribed);
            Assert.True(math.distance(focal, fountain.Position.xz) < 0.5f);
            Assert.True(plan.Layers.First(l => l.GeneratorId == "fountain").Facts["fountainRadius"] + 2f <= inscribed);
        }

        [Fact]
        public void FocalPointIsTheIncentreOfATriangle()
        {
            var t = new[] { new float2(0, 0), new float2(100, 0), new float2(80, 30) };
            var region = LayerRegion.From(t);
            var p = LeisureKit.FocalPoint(region, out float r);
            // Incentre: weighted by the opposite side lengths.
            float a = math.distance(t[1], t[2]), b = math.distance(t[0], t[2]), c = math.distance(t[0], t[1]);
            var incentre = (a * t[0] + b * t[1] + c * t[2]) / (a + b + c);
            // Near-deepest (≥ 92 % of the inscribed radius) and close to the incentre, leaning to the centroid.
            Assert.True(region.BoundaryDistance(p) >= r * 0.9f, $"{p}: {region.BoundaryDistance(p):0.0} of {r:0.0}");
            Assert.True(math.distance(p, incentre) < r * 0.4f, $"{p} vs {incentre}");
            // A long rectangle: the middle of its medial line, not one end of it.
            var strip = LayerRegion.From(new[] { new float2(0, 0), new float2(100, 0), new float2(100, 12), new float2(0, 12) });
            var q = LeisureKit.FocalPoint(strip, out _);
            Assert.True(math.distance(q, new float2(50, 6)) < 1f, q.ToString());
        }

        // ───────────── palettes ─────────────

        [Fact]
        public void EveryPaletteIsComplete()
        {
            var lang = TestKit.Lang();
            foreach (var p in GroundPalettes.All)
            {
                foreach (var a in new[] { p.Field, p.Band, p.Focal, p.Path, p.Inlay })
                {
                    Assert.False(string.IsNullOrEmpty(a.Id), p.Id);
                    Assert.NotNull(GroundSurfaces.Tone(a.Id));
                    // Every role ends in a vanilla surface of the same character.
                    var last = a.Fallbacks.Length > 0 ? a.Fallbacks[a.Fallbacks.Length - 1] : a.Id;
                    Assert.True(last.EndsWith("Surface 01") || last.EndsWith("Surface 02") || last.EndsWith("Surface 03"), p.Id + ": " + last);
                }
                Assert.True(lang.ContainsKey(GroundPalettes.LabelKey(p.Id)), p.Id);
                Assert.True(lang.ContainsKey(GroundPalettes.DetailKey(p.Id)), p.Id);
                Assert.Equal(5, GroundPalettes.Swatch(p).Count);
            }
            Assert.Equal(GroundPalettes.All.Length, GroundPalettes.All.Select(p => p.Id).Distinct().Count());
            // The schema offers them all, with their one-line description.
            var control = TestKit.Gen("plaza").Controls.Single(c => c.Id == GroundPalettes.Control);
            Assert.Equal(GroundPalettes.Values(), control.Options.Select(o => o.Value).ToArray());
            Assert.All(control.Options, o => Assert.True(lang.ContainsKey(o.DetailKey), o.DetailKey));
            // Every palette card carries its five-role swatch (s16 option cards).
            Assert.All(control.Options.Where(o => GroundPalettes.Find(o.Value) != null), o => Assert.Equal(5, o.Swatch.Count));
            // The other paved types offer the same chooser.
            foreach (var type in new[] { "marketSquare", "transitPlaza", "amphitheatre", "fairground" })
            {
                var g = TestKit.Gen(type);
                Assert.Contains(g.Controls, c => c.Id == GroundPalettes.Control);
                Assert.IsAssignableFrom<ILinkedControls>(g);
                foreach (var c in g.Controls) Assert.True(lang.ContainsKey(c.LabelKey), type + ": " + c.LabelKey);
            }
            Assert.Equal(GroundPalettes.Automatic, control.Default);
        }

        private static string FieldOf(ComposedPlan plan)
        {
            var plaza = plan.Layers.First(l => l.GeneratorId == "plaza");
            // The field is the stone covering most of the square.
            return plaza.Surfaces.GroupBy(s => s.Asset.Id).OrderByDescending(g => g.Sum(s => Poly.Area(Poly.To2D(s.Ring)))).First().Key;
        }

        [Fact]
        public void DefaultIsCalmAndPalettesChangeTheStones()
        {
            var ring = Shape("square", 1500f);
            Assert.Equal(GroundPalettes.Get(GroundPalettes.Classic).Field.Id, FieldOf(Compose(ring)));
            Assert.NotEqual(GroundSurfaces.SlabBeige, FieldOf(Compose(ring)));
            Assert.Equal(GroundSurfaces.SlabBeige, FieldOf(Compose(ring, null, s => s.Values["palette"] = GroundPalettes.Terracotta)));
            Assert.Equal(GroundSurfaces.LargeSlabs, FieldOf(Compose(ring, null, s => s.Values["palette"] = GroundPalettes.Modern)));
            // No shipped preset uses the orange/ochre look; each declares its palette.
            foreach (var p in TestKit.Gen("plaza").Presets)
            {
                if (p.Id == "grungy") continue;   // (the plazas makeover) its own dark concrete on Automatic (PlazaDesigns.GrungyPalette)
                var id = p.Values.S(GroundPalettes.Control, "");
                Assert.NotNull(GroundPalettes.Find(id));
                Assert.NotEqual(GroundPalettes.Terracotta, id);
            }
        }

        [Fact]
        public void OldSavesKeepTheirLook()
        {
            // A pre-s18 save: paveStyle "warm", no palette value → the warm look (now "terracotta"), shown as Automatic.
            var ring = Shape("square", 1500f);
            var old = Compose(ring, null, s => { s.Values.Remove("palette"); s.Values["paveStyle"] = "warm"; });
            Assert.Equal(GroundSurfaces.SlabBeige, FieldOf(old));
            // Picking a palette wins over the old style.
            var picked = Compose(ring, null, s => { s.Values["paveStyle"] = "warm"; s.Values["palette"] = GroundPalettes.Classic; });
            Assert.Equal(GroundSurfaces.GreyStone, FieldOf(picked));
            // Control ids stay: the old style is still a (hidden) control, the field keeps "surface".
            var ids = TestKit.Gen("plaza").Controls.Select(c => c.Id).ToList();
            Assert.Contains("paveStyle", ids);
            Assert.Contains("surface", ids);
        }

        [Fact]
        public void RoleOverridesMakeItCustom()
        {
            var s = new SettingsBag();
            Assert.Equal(GroundPalettes.Automatic, GroundPalettes.Displayed(s, "surface"));
            s.Values["palette"] = GroundPalettes.Cobble;
            Assert.Equal(GroundPalettes.Cobble, GroundPalettes.Displayed(s, "surface"));
            s.Values[GroundPalettes.BorderControl] = "My Border";
            Assert.True(GroundPalettes.IsCustom(s, "surface"));
            Assert.Equal(GroundPalettes.Custom, GroundPalettes.Displayed(s, "surface"));
            var p = GroundPalettes.Resolve(s, GroundPalettes.Classic, "surface");
            Assert.Equal("My Border", p.Band.Id);
            Assert.Contains(GroundSurfaces.DarkFlags, p.Band.Fallbacks);   // the palette's own stone stays the fallback
            Assert.Equal(GroundPalettes.Get(GroundPalettes.Cobble).Field.Id, p.Field.Id);
            GroundPalettes.ClearOverrides(s, "surface");
            Assert.False(GroundPalettes.IsCustom(s, "surface"));

            // Linked like a road mix (ILinkedControls): a role pick reads Custom and keeps the palette it started from;
            // picking a palette clears the picks.
            var plaza = (ILinkedControls)TestKit.Gen("plaza");
            var b = TestKit.Gen("plaza").Defaults();
            b.Values["palette"] = GroundPalettes.Garden;
            plaza.OnValueSet(b, "palette");
            b.Values[GroundPalettes.FocalControl] = "My Pad";
            plaza.OnValueSet(b, GroundPalettes.FocalControl);
            plaza.Derive(b);
            Assert.Equal(GroundPalettes.Custom, b.S("palette", ""));
            var mixed = GroundPalettes.Resolve(b, GroundPalettes.Classic, "surface");
            Assert.Equal("My Pad", mixed.Focal.Id);
            Assert.Equal(GroundPalettes.Get(GroundPalettes.Garden).Field.Id, mixed.Field.Id);
            b.Values["palette"] = GroundPalettes.Modern;
            var cleared = plaza.OnValueSet(b, "palette");
            Assert.Contains(GroundPalettes.FocalControl, cleared);
            plaza.Derive(b);
            Assert.Equal(GroundPalettes.Modern, b.S("palette", ""));
            Assert.Equal("", b.S(GroundPalettes.FocalControl, "x"));
            // Clearing the last pick by hand returns to the palette it started from.
            b.Values["surface"] = "Mine"; plaza.Derive(b);
            Assert.Equal(GroundPalettes.Custom, b.S("palette", ""));
            b.Values["surface"] = ""; plaza.Derive(b);
            Assert.Equal(GroundPalettes.Modern, b.S("palette", ""));

            // The override reaches the ground.
            var plan = Compose(Shape("square", 1500f), null, b => b.Values["surface"] = "My Field");
            Assert.Equal("My Field", FieldOf(plan));
        }
    }
}
