// Skate bowl pad: a levelled concrete pad (34 × 26 or 26 × 20 m) with a bowl dug into it — round, or a
// two-lobed kidney (the classic backyard-pool shape) — as two nested terraform steps (shoulder at
// ~45 % of the depth, deep end at full depth), coping painted round the rim, street obstacles
// (ramps, rails, boxes) on the flat deck facing the bowl, benches and a bin at the edge people arrive
// from, floodlights at the corners, optional cage. "Street plaza" leaves the bowl out and lays the
// obstacles in two staggered rows round a central funbox.

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns.Fields
{
    [AreaGenerator(186)]
    public sealed class SkateBowlAddOn : FieldAddOn
    {
        protected override string Area => "Field.Skate";
        public override string Id => "skateBowl";

        protected override FieldFormat[] Formats(LayerContext ctx) => FieldSpecs.Skate;
        protected override float Apron(OrientedRect place) => 2.5f;

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("style", "kidney", ControlTier.Essential, "skate", "bowl", "kidney", "street").Tip();
            c.Choice("format", "auto", ControlTier.Essential, "skate", WithAuto(FieldSpecs.Skate)).Tip();
            c.Slider("depth", 2.0, 1.0, 3.5, 0.5, "m", ControlTier.Essential, "skate").Tip();
            c.Slider("obstacles", 4, 0, 10, 1, "count", ControlTier.More, "skate").Tip();
            c.AssetMulti("obstacleAssets", AssetCategories.SportsSkate, ControlTier.More, "skate");
            c.Asset("padAsset", AssetCategories.SurfacePaving, ControlTier.More, "ground");
            LineControls(c);
            c.Toggle("benches", true, ControlTier.More, "spectators");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "spectators").When("benches=true");
            LightControls(c, "lighting", false, "true");
            c.Toggle("fence", false, ControlTier.More, "boundary").Tip();
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "boundary").When("fence=true");
            GroundControls(c);
            PlacementControls(c);
            c.RandomRange("obstacles", 2, 6);
            c.RandomRange("depth", 1.5, 3.0);
        }

        protected override void DefinePresets()
        {
            Preset("pool", ("style", "kidney"), ("depth", 2.5), ("obstacles", 3.0), ("fence", false));
            Preset("bowl", ("style", "bowl"), ("depth", 2.0), ("obstacles", 4.0), ("floodlights", true));
            Preset("street", ("style", "street"), ("obstacles", 8.0), ("floodlights", true));
            Preset("caged", ("style", "bowl"), ("format", "small"), ("obstacles", 2.0), ("fence", true));
        }

        protected override void Lay(FieldJob j)
        {
            var ctx = j.Ctx;
            var place = j.Place;
            var f = Frame.Of(place);
            float hl = place.HalfLength - 1f, hw = place.HalfWidth - 1f;
            FieldKit.Surface(ctx, f.Box(-hl, -hw, hl, hw), SurfaceRole.Paving, ctx.S("padAsset"), AssetCategories.SurfacePaving, AssetCategories.SurfaceAsphalt);
            string style = ctx.S("style");
            float end = j.End;
            // The bowl sits towards the far end; the flat deck with the obstacles is by the way in.
            float radius = math.min(hw - 2.5f, hl * 0.42f);
            var centre = new float2(-end * (hl - radius - 2.5f), 0f);
            List<float2> rim = null;
            if (style != "street" && radius > 3f)
            {
                rim = style == "kidney" ? Kidney(centre, radius, 40) : Round(centre, radius, 32);
                float depth = math.clamp(ctx.F("depth"), 0.5f, 4f);
                float top = j.Ground.Levelled ? j.Ground.PadHeight : ctx.H(f.P(centre));
                var deep = new List<float2>();
                foreach (var p in rim) deep.Add(centre + (p - centre) * 0.58f);
                ctx.Plan.Terraform.Add(new PlannedTerraform { Ring = Poly.To3D(Poly.EnsureCcw(World(f, rim)), ctx.H), Kind = TerraformKind.Lower, TargetHeight = top - depth * 0.45f, Permanent = true });
                ctx.Plan.Terraform.Add(new PlannedTerraform { Ring = Poly.To3D(Poly.EnsureCcw(World(f, deep)), ctx.H), Kind = TerraformKind.Lower, TargetHeight = top - depth, Permanent = true });
                bool warned = false;
                foreach (var i in ctx.Plan.Issues) if (i.Key == TerrainShaper.PermanentKey) { warned = true; break; }
                if (!warned) ctx.Issue(IssueSeverity.Warning, TerrainShaper.PermanentKey);
                j.Lines.Path(World(f, rim), true);   // coping
                foreach (var p in rim) j.Occ.Take(f.P(p), 1.2f, 2);
                j.Occ.Take(f.P(centre), radius, 2);
                ctx.Plan.Facts["bowlDepth"] = depth;
                ctx.Plan.Facts["intentionalPit"] = 1;   // (rf-sports) dug on purpose: the pit guard raised the bowl back to flat ground
                ctx.Plan.Facts["bowlRadius"] = radius;
            }

            // Obstacles: on the deck, facing the bowl (or the funbox in a street plaza).
            int wanted = ctx.I("obstacles") + (style == "street" ? 2 : 0);
            var ids = ctx.L("obstacleAssets");
            int placed = 0;
            if (wanted > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.SportsSkate, ids, Keys.Category(AssetCategories.SportsSkate)))
            {
                var spots = new List<(float2 p, float2 face)>();
                if (style == "street" || rim == null)
                {
                    spots.Add((float2.zero, new float2(1f, 0f)));
                    for (int row = -1; row <= 1; row += 2)
                        for (int k = 0; k < 5; k++)
                        {
                            float u = -hl + 4f + k * (2f * hl - 8f) / 4f + (row > 0 ? 2.5f : 0f);
                            if (math.abs(u) > hl - 3f) continue;
                            spots.Add((new float2(u, row * (hw - 3f)), new float2(u, 0f)));
                        }
                }
                else
                {
                    float deckU = end * (hl - 3f);
                    spots.Add((new float2(deckU, 0f), centre));
                    for (int s = -1; s <= 1; s += 2)
                    {
                        spots.Add((new float2(deckU, s * (hw - 3f)), centre));
                        spots.Add((new float2(end * (hl * 0.25f), s * (hw - 2.6f)), new float2(end * (hl * 0.25f), 0f)));
                        spots.Add((new float2(centre.x, s * (hw - 1.8f)), centre));
                        spots.Add((new float2(-end * (hl - 1.8f), s * (hw * 0.5f)), centre));
                    }
                    spots.Add((new float2(end * (hl * 0.25f), 0f), centre));
                }
                foreach (var (p, face) in spots)
                {
                    if (placed >= wanted) break;
                    uint v = j.V(0x5CA7u + (uint)placed * 31u);
                    var wp = f.P(p);
                    // (rf-sports) Automatic skate props are one uniform prop per build (a deck of identical boxes): mix the game's set.
                    var asset = ids == null || ids.Length == 0 ? AssetMix.One("", MixedSet[(int)(v % (uint)MixedSet.Length)]) : AssetMix.Pick(ids, v);
                    if (LeisureKit.Put(ctx, j.Occ, wp, LeisureKit.Yaw(wp, f.P(face)), ObjectRole.Furniture, asset, AssetCategories.SportsSkate, 1.8f, v, 0.3f, 0.4f))
                    { placed++; j.Equipment++; }
                }
            }

            // Benches and a bin on the side people arrive from, lights at the corners.
            float side = j.Side;
            if (ctx.B("benches"))
                FieldKit.Benches(j, new[] { f.P(-hl * 0.45f, side * (hw - 0.9f)), f.P(hl * 0.45f, side * (hw - 0.9f)) }, f.P(0f, 0f));
            LeisureKit.Put(ctx, j.Occ, f.P(0f, side * (hw - 0.7f)), f.Yaw, ObjectRole.Bin, new AssetRef(""), null, 0.4f, j.V(0xB1Bu));
            if (ctx.B("floodlights"))
                FieldKit.Floodlights(j, new[] { f.P(-hl + 0.6f, -hw + 0.6f), f.P(hl - 0.6f, -hw + 0.6f), f.P(hl - 0.6f, hw - 0.6f), f.P(-hl + 0.6f, hw - 0.6f) }, f.O, false);
            if (ctx.B("fence"))
                RectFence(ctx, new OrientedRect { Center = place.Center, Axis = place.Axis, HalfLength = place.HalfLength - 0.4f, HalfWidth = place.HalfWidth - 0.4f },
                    j.Toward, 2.5f, AssetMix.One(ctx.S("fenceAsset")));
            ctx.Stat("Placemaker.Stat.SkateFeatures", placed + (rim != null ? 1 : 0), Keys.UnitCount);
        }

        private static readonly string[] MixedSet = { "SkateParkRamp01", "SkateParkBox01", "SkateParkRail01", "SkateParkRamp02", "SkateParkBox02", "SkateParkRail02", "SkateParkRamp03" };

        private static float2[] World(Frame f, List<float2> l)
        {
            var r = new float2[l.Count];
            for (int i = 0; i < l.Count; i++) r[i] = f.P(l[i]);
            return r;
        }

        private static List<float2> Round(float2 c, float r, int n)
        {
            var l = new List<float2>(n);
            for (int i = 0; i < n; i++) { float a = i * 2f * math.PI / n; l.Add(c + new float2(math.cos(a), math.sin(a)) * r); }
            return l;
        }

        /// <summary>Two overlapping lobes (a big one and a smaller, offset one) traced from the centre: star-shaped, so the ring is simple.</summary>
        private static List<float2> Kidney(float2 c, float r, int n)
        {
            var l = new List<float2>(n);
            var c1 = new float2(-0.38f * r, -0.08f * r); float r1 = 0.66f * r;
            var c2 = new float2(0.42f * r, 0.12f * r); float r2 = 0.54f * r;
            for (int i = 0; i < n; i++)
            {
                float a = i * 2f * math.PI / n;
                var d = new float2(math.cos(a), math.sin(a));
                float t = math.max(Reach(c1, r1, d), Reach(c2, r2, d));
                l.Add(c + d * t);
            }
            return l;
        }

        /// <summary>Distance from the origin along d to the far side of a circle containing the origin.</summary>
        private static float Reach(float2 cc, float rr, float2 d)
        {
            float b = math.dot(cc, d);
            float disc = b * b - (math.dot(cc, cc) - rr * rr);
            return disc <= 0f ? 0f : b + math.sqrt(disc);
        }
    }
}
