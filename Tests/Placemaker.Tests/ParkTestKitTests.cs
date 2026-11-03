// Shared fixtures for the park / plaza / split / add-on tests: shapes, inputs, invariant checks,
// the localisation table and the shipped-recipe JSON reader (the format documented in
// docs/proposals/recipe-json.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public static class TestKit
    {
        public static float3[] Rect(float w, float h, float x0 = 0, float z0 = 0) => new[]
        {
            new float3(x0, 0, z0), new float3(x0 + w, 0, z0), new float3(x0 + w, 0, z0 + h), new float3(x0, 0, z0 + h),
        };

        /// <summary>L-shaped, concave test area.</summary>
        public static float3[] LShape(float size) => new[]
        {
            new float3(0, 0, 0), new float3(size, 0, 0), new float3(size, 0, size * 0.45f),
            new float3(size * 0.45f, 0, size * 0.45f), new float3(size * 0.45f, 0, size), new float3(0, 0, size),
        };

        /// <summary>Irregular polygon with many vertices, like a painted area (CCW).</summary>
        public static float3[] Blob(float radius, int vertices, uint seed)
        {
            var rng = new Unity.Mathematics.Random(seed);
            var r = new float3[vertices];
            for (int i = 0; i < vertices; i++)
            {
                float a = i * 2f * math.PI / vertices;
                float rr = radius * (0.8f + 0.2f * math.sin(3 * a) + rng.NextFloat(-0.04f, 0.04f));
                r[i] = new float3(math.cos(a) * rr, 0, math.sin(a) * rr);
            }
            return r;
        }

        public static AreaInput Input(float3[] ring, SettingsBag settings = null, uint seed = 1, List<EdgeMarker> markers = null,
            SiteContext site = null)
        {
            return new AreaInput
            {
                Shape = new AreaShape { Ring = ring },
                Site = site ?? new SiteContext(),
                Settings = settings ?? new SettingsBag(),
                Seed = seed,
                Markers = markers ?? new List<EdgeMarker>(),
                RegionTags = new[] { RegionTags.Root },
            };
        }

        /// <summary>A straight road along the bottom edge of a rect, as SiteCapture would report it.</summary>
        /// <summary>
        /// The path meets a street of the site: an end attached to it (a crosswalk entrance), or a free end within the game's
        /// LocalConnect reach of its edge (4 m plus the path's half width: StreetJoins stops footpaths there).
        /// </summary>
        public static bool MeetsStreet(PlannedNet n, SiteContext site)
        {
            if (n.StartAttach >= 0 || n.EndAttach >= 0) return true;
            foreach (var r in site.NearbyNets)
            {
                if (r.Category != NetCategory.Road) continue;
                float reach = r.Width * 0.5f + Composer.DefaultHalfWidth(n.Role) + 4f;
                if (Placemaker.Core.Layers.Common.StreetJoins.RoadDistance(r, n.A.xz) <= reach
                    || Placemaker.Core.Layers.Common.StreetJoins.RoadDistance(r, n.D.xz) <= reach) return true;
            }
            return false;
        }

        public static SiteContext RoadAlongBottom(float w, long id = 77)
        {
            var site = new SiteContext();
            site.NearbyNets.Add(new NearbyNet
            {
                ExternalId = id, A = new float3(-20, 0, -6), B = new float3(w / 3, 0, -6), C = new float3(2 * w / 3, 0, -6),
                D = new float3(w + 20, 0, -6), Width = 12, Category = NetCategory.Road, HasSidewalk = true,
            });
            return site;
        }

        public static IAreaGenerator Gen(string id)
        {
            var g = GeneratorRegistry.Get(id);
            Assert.NotNull(g);
            return g;
        }

        public static AreaPlan Plan(string id, AreaInput input) => Gen(id).Plan(input, CancellationToken.None);

        /// <summary>
        /// (s9) The player accepted the layer's suggested entrances: they become placed markers. Without any
        /// placed entrance a park-family layer keeps its paths inside (no connectors to the outline or a road).
        /// </summary>
        public static AreaInput Entered(string id, AreaInput input)
        {
            if (input.Markers.Count == 0) input.Markers.AddRange(Gen(id).SuggestMarkers(input) ?? new List<EdgeMarker>());
            return input;
        }

        /// <summary>(s9) Suggested entrances placed on a recipe's root step (as the gallery showcase does).</summary>
        public static Recipe Entered(Recipe r, float3[] ring, SiteContext site)
        {
            var step = r.Root;
            if (step.Markers.Count > 0) return r;
            var input = new AreaInput { Shape = new AreaShape { Ring = ring, Source = AreaSource.Drawn }, Site = site ?? new SiteContext(), Settings = step.Settings, Markers = step.Markers };
            step.Markers.AddRange(Gen(step.Layer).SuggestMarkers(input) ?? new List<EdgeMarker>());
            return r;
        }

        // ───────────── invariants ─────────────

        public static void AssertObjectsInside(AreaPlan plan, float3[] ring, float tolerance = 0.01f)
        {
            var r = Poly.To2D(ring);
            foreach (var o in plan.Objects)
                Assert.True(Poly.Contains(r, o.Position.xz) || Poly.DistanceToBoundary(r, o.Position.xz) < tolerance,
                    $"{o.Role} at {o.Position} lies outside the region");
        }

        /// <summary>No object footprint overlaps a planned path of the same plan.</summary>
        public static void AssertObjectsClearOfNets(AreaPlan plan, float slack = 0.05f)
        {
            foreach (var o in plan.Objects)
            {
                if (o.Role == ObjectRole.AccessPoint || o.Role == ObjectRole.AnimalSpawner || o.Role == ObjectRole.Decal) continue;   // s21: decals may lie on paths
                float r = FootprintFor(o.Role);
                foreach (var n in plan.Nets)
                {
                    if (n.Role == NetRole.Fence) continue;
                    float hw = Composer.DefaultHalfWidth(n.Role);
                    float d = DistanceToCurve(o.Position.xz, n);
                    Assert.True(d >= hw + r - slack, $"{o.Role} at {o.Position.xz} is {d:0.00} m from a {n.Role} (needs {hw + r:0.00})");
                }
            }
        }

        public static void AssertClearOfReserved(AreaPlan plan, ReservedSpace reserved, float slack = 0.05f)
        {
            foreach (var o in plan.Objects)
            {
                if (o.Role == ObjectRole.AccessPoint) continue;
                float r = FootprintFor(o.Role) - slack;
                foreach (var d in reserved.Discs)
                    Assert.True(math.distance(d.Center, o.Position.xz) >= d.Radius + r, $"{o.Role} at {o.Position.xz} overlaps reserved disc {d.Tag} at {d.Center}");
                foreach (var c in reserved.Corridors)
                    Assert.True(math.sqrt(SegmentIndex.DistSq(o.Position.xz, c.A, c.B)) >= c.HalfWidth + r,
                        $"{o.Role} at {o.Position.xz} overlaps reserved corridor {c.Tag}");
            }
        }

        /// <summary>Smallest footprint each role keeps from nets (the layers' own collision radii).</summary>
        public static float FootprintFor(ObjectRole role)
        {
            switch (role)
            {
                case ObjectRole.Tree: return 2.4f;
                case ObjectRole.Bush: return 1.1f;
                case ObjectRole.Bench: return 1.3f;
                case ObjectRole.Lamp: return 0.5f;
                case ObjectRole.Bin: return 0.5f;
                default: return 0.5f;
            }
        }

        public static float DistanceToCurve(float2 p, PlannedNet n)
        {
            float best = float.MaxValue;
            float3 prev = n.A;
            for (int i = 1; i <= 32; i++)
            {
                var q = Composer.Bezier(n.A, n.B, n.C, n.D, i / 32f);
                best = math.min(best, math.sqrt(SegmentIndex.DistSq(p, prev.xz, q.xz)));
                prev = q;
            }
            return best;
        }

        public static void AssertSurfacesAccepted(AreaPlan plan)
        {
            foreach (var s in plan.Surfaces) Assert.True(SurfaceAcceptance.IsAccepted(Poly.To2D(s.Ring)), $"{s.Role} surface would be dropped by CS2");
            foreach (var a in plan.Areas) Assert.True(SurfaceAcceptance.IsAccepted(Poly.To2D(a.Ring)), $"{a.Role} area would be dropped by CS2");
        }

        public static string Fingerprint(AreaPlan plan)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var o in plan.Objects) sb.Append($"O{(int)o.Role}:{o.Position.x:R},{o.Position.z:R},{o.Yaw:R},{o.Asset.Id},{o.Variation};");
            foreach (var n in plan.Nets) sb.Append($"N{(int)n.Role}:{n.A.x:R},{n.A.z:R},{n.B.x:R},{n.D.x:R},{n.D.z:R};");
            foreach (var s in plan.Surfaces) { sb.Append("S"); foreach (var p in s.Ring) sb.Append($"{p.x:R},{p.z:R} "); }
            foreach (var r in plan.Regions) { sb.Append("R" + r.Key + string.Join("+", r.Tags)); foreach (var p in r.Ring) sb.Append($"{p.x:R},{p.z:R} "); }
            foreach (var s in plan.Stats) sb.Append($"T{s.Key}={s.Value:R};");
            return sb.ToString();
        }

        public static int Count(AreaPlan plan, ObjectRole role) => plan.Objects.Count(o => o.Role == role);

        public static double Stat(AreaPlan plan, string key) => plan.Stats.Where(s => s.Key == key).Sum(s => s.Value);

        // ───────────── repository files ─────────────

        public static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Placemaker.csproj"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return dir.FullName;
        }

        private static Dictionary<string, string> _lang;

        /// <summary>All en-US strings, merged from Lang/en-US*.json as the game loader does.</summary>
        public static Dictionary<string, string> Lang()
        {
            if (_lang != null) return _lang;
            var all = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot(), "Lang"), "en-US*.json"))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                foreach (var p in doc.RootElement.EnumerateObject()) all[p.Name] = p.Value.GetString();
            }
            return _lang = all;
        }

        /// <summary>Shipped recipe (Recipes/), else an engine-only mix kept as a test fixture (Fixtures/Recipes/).</summary>
        public static Recipe LoadRecipe(string fileName)
        {
            var shipped = Path.Combine(RepoRoot(), "Recipes", fileName);
            var path = File.Exists(shipped) ? shipped : Path.Combine(RepoRoot(), "Tests", "Placemaker.Tests", "Fixtures", "Recipes", fileName);
            return ParseRecipe(File.ReadAllText(path));
        }

        /// <summary>Reference reader for docs/proposals/recipe-json.md.</summary>
        public static Recipe ParseRecipe(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var r = new Recipe
            {
                Id = root.GetProperty("id").GetString(),
                NameKey = root.TryGetProperty("nameKey", out var nk) ? nk.GetString() : "",
                Name = root.TryGetProperty("name", out var nm) ? nm.GetString() : "",
                Icon = root.TryGetProperty("icon", out var ic) ? ic.GetString() : "",
                FormatVersion = root.TryGetProperty("formatVersion", out var fv) ? fv.GetInt32() : 1,
            };
            r.Root = ParseStep(root.GetProperty("root"));
            return r;
        }

        private static RecipeStep ParseStep(JsonElement e)
        {
            var layer = e.GetProperty("layer").GetString();
            var gen = GeneratorRegistry.Get(layer);
            var step = new RecipeStep
            {
                Layer = layer,
                Settings = gen != null ? gen.Defaults() : new SettingsBag(),
                Target = e.TryGetProperty("target", out var t) ? t.GetString() : "",
                Enabled = !e.TryGetProperty("enabled", out var en) || en.GetBoolean(),
                Seed = e.TryGetProperty("seed", out var sd) ? sd.GetUInt32() : 0u,
            };
            if (e.TryGetProperty("settings", out var settings))
                foreach (var p in settings.EnumerateObject()) step.Settings.Values[p.Name] = Value(p.Value);
            if (e.TryGetProperty("locked", out var locked))
                foreach (var l in locked.EnumerateArray()) step.Locked.Add(l.GetString());
            if (e.TryGetProperty("children", out var children))
                foreach (var c in children.EnumerateArray()) step.Children.Add(ParseStep(c));
            if (e.TryGetProperty("regionOverrides", out var ov))
                foreach (var p in ov.EnumerateObject()) step.RegionOverrides[p.Name] = ParseStep(p.Value);
            return step;
        }

        private static object Value(JsonElement v)
        {
            switch (v.ValueKind)
            {
                case JsonValueKind.Number: return v.GetDouble();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.String: return v.GetString();
                case JsonValueKind.Array: return v.EnumerateArray().Select(x => x.GetString()).ToArray();
                default: throw new InvalidDataException("Unsupported setting value " + v);
            }
        }
    }
}
