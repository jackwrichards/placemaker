// Decals feature: scatters flat decal props with a theme, density and clustering. Each theme knows
// where it belongs (playtest wish "decals"):
//   leaves   lawns and paths              dirt     lawns only (paved areas: near the edge)
//   cracks   paving only                  puddles  anywhere free
//   paving   paving only, on a lattice    markings paving only, aligned to aisles/paths
//   graffiti paving near the outline
// "Paving" = reserved path/aisle corridors, or the whole region when it is paved (ground = paved,
// or the region is tagged paved/lot). Decals take no space (s21): they may lie under objects and on paths,
// never reserve space, and there is never "no room" — a theme whose ground is missing here (cracks in a
// park without paving) falls back to any dry ground. Water is the only place they never go.
// Context-aware dressing (leaves under trees, oil in bays…) is the composer's pass: Core/Decals/Dressing.

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns
{
    [AreaGenerator(150)]
    public sealed class DecalsAddOn : AddOnLayer
    {
        public const string LayerId = "decals";
        public static readonly string[] Themes = { "leaves", "dirt", "cracks", "puddles", "paving", "markings", "graffiti" };

        protected override string Area => "AddOn.Decals";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/PropsDecals.svg";

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("theme", "leaves", ControlTier.Essential, "decals", Themes);
            // Playtest 4b: subtle by default (decals are on in most types now) — a sprinkle, not a carpet.
            c.Slider("density", 0.6, 0.2, 10, 0.2, "per100m2", ControlTier.Essential, "decals").Tip();
            c.Slider("clustering", 70, 0, 100, 5, "%", ControlTier.Essential, "decals").Tip();
            c.Choice("ground", "auto", ControlTier.More, "decals", "auto", "paved", "lawn").Tip();
            c.AssetMulti("assets", AssetCategories.Decal, ControlTier.More, "decals");
            c.RandomRange("density", 0.6, 3);
            c.Fixed("ground");
        }

        protected override void DefinePresets()
        {
            Preset("autumn", ("theme", "leaves"), ("density", 4.0), ("clustering", 70.0));
            Preset("worn", ("theme", "cracks"), ("density", 1.5), ("clustering", 60.0));
            Preset("rainy", ("theme", "puddles"), ("density", 1.0), ("clustering", 40.0));
            Preset("patterned", ("theme", "paving"), ("density", 1.0), ("clustering", 0.0));
        }

        public static string CategoryFor(string theme)
        {
            switch (theme)
            {
                case "dirt": return AssetCategories.DecalDirt;
                case "cracks": return AssetCategories.DecalCracks;
                case "puddles": return AssetCategories.DecalPuddles;
                case "paving": return AssetCategories.DecalPaving;
                case "markings": return AssetCategories.DecalMarkings;
                case "graffiti": return AssetCategories.DecalGraffiti;
                default: return AssetCategories.DecalLeaves;
            }
        }

        protected override void PlanCore(LayerContext ctx)
        {
            string theme = ctx.S("theme");
            // (dynamic decals, 2026-10-29: "disable our other decal system") Every theme but the base game's markings needs
            // decal packs, and pack decals are dropped from every plan (PackDecals): nothing is scattered for them any more.
            // Grime is code-made now (Core/Decals/Dynamic). The code below stays for the markings theme.
            if (theme != "markings") return;
            string category = CategoryFor(theme);
            var ids = ctx.L("assets");
            if (!AssetAvailability.Require(ctx.Plan, category, ids, Keys.Category(category))) return;
            var region = ctx.LayerRegion;
            var occ = Occupancy(ctx);
            bool pavedRegion = ctx.S("ground") == "paved" || (ctx.S("ground") == "auto" && HasTag(ctx.Input, RegionTags.Paved, RegionTags.Lot));
            bool lawnOnly = ctx.S("ground") == "lawn";
            // Density counts decals of ~2 m. Leaf and dirt decals are 6 m squares (12 m in some packs):
            // the count shrinks with the typical footprint and no two decals stack closer than most of
            // their size (2026-10-13 playtest: "decals too dense", they read as a carpet of squares).
            float size = TypicalSize(theme);
            float scale = math.min(1f, 5f / (size * size));
            int wanted = math.min(3000, (int)math.round(region.Area / 100f * ctx.F("density") * scale));
            float spacing = theme == "paving" || theme == "markings" ? 0f : size * 0.8f;
            var placedAt = new List<float2>();
            float clustering = ctx.F("clustering") / 100f;
            region.PrincipalAxes(out var centre, out var axis, out _, out _);
            float axisYaw = math.atan2(axis.x, axis.y);
            uint clump = Hashing.Mix(ctx.Seed, 0xDEC0u);
            int placed = 0;
            float cell = theme == "paving" ? 3f : 1.2f;
            // s23: the objects leaves and dirt belong to (trees/shrubs; benches, lamps, bins), indexed once.
            var hosts = new List<ReservedSpace.Disc>();
            var hostGrid = new Decals.DressingWorld.Grid(8f);
            foreach (var d in ctx.Input.Reserved.Discs)
            {
                bool host = theme == "leaves" ? d.Tag == ObjectRole.Tree.ToString() || d.Tag == ObjectRole.Bush.ToString()
                    : d.Tag == ObjectRole.Bench.ToString() || d.Tag == ObjectRole.Lamp.ToString() || d.Tag == ObjectRole.Bin.ToString();
                if (!host) continue;
                hostGrid.Add(d.Center, d.Radius, hosts.Count);
                hosts.Add(d);
            }
            var candidates = Scatter.Candidates(region, cell, Hashing.Mix(ctx.Seed, 0xDEC1u), math.min(80000, wanted * 12 + 100), ctx.Cancel);
            for (int pass = 0; pass < 2 && placed < wanted; pass++)
            foreach (var cand in candidates)
            {
                if (placed >= wanted) break;
                // Pass 1 (only when pass 0 found no ground for the theme): anywhere dry, no gathering rule.
                bool strict = pass == 0;
                if (!strict && (placed > 0 || theme == "markings" || theme == "paving")) break;
                var p = cand;
                float yaw;
                uint v = Hashing.Mix(ctx.Seed, (uint)placed * 2654435761u + 17u);
                if (theme == "paving")
                {
                    // Snap to a 3 m lattice in the principal frame: patterns read as laid, not thrown.
                    var d = p - centre; var side = new float2(-axis.y, axis.x);
                    float u = math.round(math.dot(d, axis) / 3f) * 3f, w = math.round(math.dot(d, side) / 3f) * 3f;
                    p = centre + axis * u + side * w;
                    if (!region.Contains(p)) continue;
                    yaw = axisYaw + (v % 4u) * math.PI * 0.5f;
                }
                else yaw = Hashing.Unit(v) * 6.283f;
                if (clustering > 0f)
                {
                    float chance = Scatter.Clustering(p, 12f, clump, 1f - clustering);
                    if (Hashing.Unit(Hashing.Mix(clump, (int)(p.x * 11f), (int)(p.y * 5f))) > chance) continue;
                }
                // Places (another feature's ground, zoning cutouts) are not ours; objects never block a decal.
                if (!region.ContainsDisc(p, 0.3f) || OnWater(ctx, p) || occ.HitsPolygon(p, 0.3f)) continue;
                if (theme == "leaves" || theme == "dirt")
                {
                    // s23 (user: "poop all over the floor"): leaves lie under crowns, dirt where feet stop (benches, lamps,
                    // bins); never on open lawn far from anything, never on the path itself.
                    if (occ.Corridors.Clearance(p, 1f) < 0f) continue;
                    float reach = theme == "leaves" ? 3.5f : 2f;
                    float near = float.MaxValue;
                    foreach (int hi in hostGrid.Near(p, reach + 3f))
                        near = math.min(near, math.distance(hosts[hi].Center, p) - hosts[hi].Radius);
                    if (near > reach || Hashing.Unit(Hashing.Mix(v, 0x6A7u)) > 1f - math.max(0f, near) / (reach + 0.5f)) continue;
                }
                // On a path/aisle: well inside it (the reserved corridors approximate curves by chords).
                bool onCorridor = occ.Corridors.Clearance(p, 1f) < -0.6f;
                bool paved = pavedRegion || onCorridor;
                float edge = region.BoundaryDistance(p, 6f);
                bool ok;
                switch (theme)
                {
                    case "leaves": case "puddles": ok = !lawnOnly || !onCorridor; break;
                    case "dirt": ok = pavedRegion ? edge < 3f : !onCorridor; break;
                    case "cracks": case "paving": ok = paved && !lawnOnly; break;
                    case "markings":
                        ok = paved && !lawnOnly;
                        int idx = ok ? occ.Corridors.Nearest(p, 3f, out _) : -1;
                        if (idx >= 0)
                        {
                            // Align with the aisle/path under it.
                            var s = occ.Corridors.Segments[idx];
                            var t = math.normalizesafe(s.B - s.A, axis);
                            yaw = math.atan2(t.x, t.y);
                        }
                        else yaw = axisYaw;
                        break;
                    case "graffiti": ok = paved && !lawnOnly && edge < 4f; break;
                    default: ok = true; break;
                }
                if (!ok && strict) continue;
                if (spacing > 0f)
                {
                    bool crowded = false;
                    foreach (var q in placedAt) if (math.distancesq(q, p) < spacing * spacing) { crowded = true; break; }
                    if (crowded) continue;
                    placedAt.Add(p);
                }
                ctx.Object(p, yaw, ObjectRole.Decal, AssetMix.Pick(ids, v), v, -1, category);
                placed++;
            }
            ctx.Stat("Placemaker.Stat.Decals", placed, Keys.UnitCount);
        }

        /// <summary>Typical edge length (m) of a theme's decals in the real catalog (vanilla + common packs).</summary>
        public static float TypicalSize(string theme)
        {
            switch (theme)
            {
                case "leaves": case "dirt": return 6f;
                case "paving": return 4f;
                case "puddles": return 3f;
                case "cracks": return 2.5f;
                case "graffiti": return 2f;
                default: return 1.5f;
            }
        }

        private static bool OnWater(LayerContext ctx, float2 p)
        {
            try { return ctx.Site?.WaterDepthAt != null && ctx.Site.WaterDepthAt(p) > 0.05f; }
            catch (System.Exception) { return false; }
        }

        private static bool HasTag(AreaInput input, params string[] tags)
        {
            if (input.RegionTags == null) return false;
            foreach (var t in input.RegionTags) foreach (var w in tags) if (t == w) return true;
            return false;
        }
    }
}
