// Tennis courts: one to four doubles courts (23.77 × 10.97 m) side by side in one fenced block, 6.4 m
// behind each baseline, 3.66 m at the outer sides and 4.27 m between neighbouring courts (ITF club
// minimums). Hard (asphalt), clay or grass surface; full markings (baselines, doubles and singles
// sidelines, service lines, centre service line, centre marks); a net per court; a chain-link cage with
// one gate towards the path; a bench per court at the net; optional floodlight poles along the sides.

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns.Fields
{
    [AreaGenerator(183)]
    public sealed class TennisAddOn : FieldAddOn
    {
        protected override string Area => "Field.Tennis";
        public override string Id => "tennisCourts";

        public const float CourtL = 23.77f, CourtW = 10.97f, Back = 6.4f, Outer = 3.66f, Between = 4.27f;

        protected override FieldFormat[] Formats(LayerContext ctx) => FieldSpecs.Tennis;

        /// <summary>The courts slider picks the rung (4 → "four" … 1 → "one").</summary>
        protected override int Wanted(LayerContext ctx, FieldFormat[] formats) => math.clamp(4 - ctx.I("courts"), 0, formats.Length - 1);
        protected override bool Auto(LayerContext ctx) => true;   // "fewer courts" is reported instead
        protected override float Apron(OrientedRect place) => 1.0f;

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("courts", 2, 1, 4, 1, "count", ControlTier.Essential, "field").Tip();
            c.Choice("surface", "hard", ControlTier.Essential, "field", "hard", "clay", "grass").Tip();
            c.Asset("surfaceAsset", AssetCategories.SurfaceAny, ControlTier.More, "field");
            c.Asset("netAsset", AssetCategories.SportsTennis, ControlTier.More, "field");
            LineControls(c);
            c.Toggle("fence", true, ControlTier.More, "boundary").Tip();
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "boundary").When("fence=true");
            c.Toggle("benches", true, ControlTier.More, "spectators");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "spectators").When("benches=true");
            LightControls(c, "lighting", false, "false");
            GroundControls(c);
            PlacementControls(c);
            c.RandomRange("courts", 1, 3);
            c.Chance("floodlights", 0.3f);
        }

        protected override void DefinePresets()
        {
            Preset("club", ("courts", 4.0), ("surface", "clay"), ("fence", true), ("floodlights", true));
            Preset("park", ("courts", 2.0), ("surface", "hard"), ("fence", true), ("floodlights", false));
            Preset("lawn", ("courts", 2.0), ("surface", "grass"), ("fence", false), ("floodlights", false));
            Preset("single", ("courts", 1.0), ("surface", "hard"), ("fence", true), ("floodlights", false));
        }

        private static string[] Chain(string surface)
        {
            switch (surface)
            {
                case "clay": return new[] { AssetCategories.SurfaceDirt, AssetCategories.SurfaceNatural, AssetCategories.SurfaceAsphalt };
                case "grass": return new[] { AssetCategories.SurfaceGrass };
                default: return new[] { AssetCategories.SurfaceAsphalt, AssetCategories.SurfacePaving };
            }
        }

        protected override void Lay(FieldJob j)
        {
            var ctx = j.Ctx;
            var place = j.Place;
            int n = j.Format == "four" ? 4 : j.Format == "three" ? 3 : j.Format == "two" ? 2 : 1;
            n = math.clamp(n, 1, 4);
            if (n < ctx.I("courts") && !j.HostSized) ctx.Issue(IssueSeverity.Info, "Placemaker.Field.Tennis.Issue.Fewer", n, ctx.I("courts"));
            float blockL = CourtL + 2f * Back, blockW = n * CourtW + (n - 1) * Between + 2f * Outer;
            // Courts run along the place's axis when the block is longer than wide (1–2 courts), across it otherwise.
            var axis = blockL >= blockW ? place.Axis : place.Side;
            var f = new Frame(place.Center, axis);
            float hl = math.min(blockL * 0.5f, (blockL >= blockW ? place.HalfLength : place.HalfWidth) - 0.4f);
            float hw = math.min(blockW * 0.5f, (blockL >= blockW ? place.HalfWidth : place.HalfLength) - 0.4f);
            var block = f.Rect(0f, 0f, hl, hw);

            string surface = HostSurface(ctx.Input, ctx.S("surface"));
            FieldKit.Surface(ctx, block.Ring, surface == "grass" ? SurfaceRole.Grass : SurfaceRole.Asphalt, ctx.S("surfaceAsset"), Chain(surface));

            string netCat = FieldKit.Cat(ctx, ctx.S("netAsset"), AssetCategories.SportsTennis);
            var l = j.Lines;
            float pitch = CourtW + Between;
            // (rf-sports) No line pieces installed: each court gets the game's own court decal (lines only by pieces otherwise).
            bool stamp = FieldKit.StampMarkings(ctx);
            if (stamp) l = new LinePainter();
            float a = CourtL * 0.5f, b = CourtW * 0.5f, sb = 4.115f, sl = 6.40f;
            int benches = 0;
            for (int i = 0; i < n; i++)
            {
                float vc = (i - (n - 1) * 0.5f) * pitch;
                l.Box(f, -a, vc - b, a, vc + b);
                l.Seg(f, -a, vc - sb, a, vc - sb); l.Seg(f, -a, vc + sb, a, vc + sb);
                l.Seg(f, -sl, vc - sb, -sl, vc + sb); l.Seg(f, sl, vc - sb, sl, vc + sb);
                l.Seg(f, -sl, vc, sl, vc);
                l.Spot(f, -a + 0.15f, vc); l.Spot(f, a - 0.15f, vc);
                // (rf-sports) The game's tennis net by default: the automatic tennis pool starts with a bench, so
                // every "net" came out as a red bench in the middle of the court.
                FieldKit.Equip(j, f.P(0f, vc), f.Yaw + math.PI * 0.5f, netCat, ctx.S("netAsset"), 0.5f, 0x7E00u + (uint)i, "TennisNet01");
                if (stamp) FieldKit.Stamp(ctx, f, new float2(0f, vc), j.V(0x7EC0u + (uint)i), AssetCategories.DecalMarkings, "TennisCourtDecal02", "TennisCourtDecal01");
                if (ctx.B("benches"))
                {
                    // A bench by the net post on the +v side (in the gap between courts, or the outer run-off),
                    // the umpire's chair at the other post.
                    benches += FieldKit.Benches(j, new[] { f.P(0f, vc + b + 1.7f) }, f.P(0f, vc), "TennisBench01", "TennisBench02");
                    if (netCat != null)
                        LeisureKit.Put(ctx, j.Occ, f.P(1.6f, vc - b - 1.1f), f.Yaw - math.PI * 0.5f, ObjectRole.Furniture, AssetMix.One("", "TennisUmpireChair01"), netCat, 0.5f, j.V(0x7E40u + (uint)i), 0.1f, 0.1f);
                }
            }
            // (2026-10-29) A show court in its stands (a host seats it) has no cage.
            if (ctx.B("fence") && !j.HostStands)
                RectFence(ctx, block, j.Toward, 1.5f, AssetMix.One(ctx.S("fenceAsset"), "FenceIndustrialHigh01", "FenceIndustrialHigh02"));   // (rf-sports) a chain-link cage, not a garden picket
            if (ctx.B("floodlights") && !j.HostLights)
            {
                // Poles inside the fence behind each baseline, one per court end, shining down the court.
                var pts = new List<float2>();
                for (int i = 0; i < n; i++)
                {
                    float vc = (i - (n - 1) * 0.5f) * pitch + b * 0.6f;
                    pts.Add(f.P(-hl + 0.7f, vc)); pts.Add(f.P(hl - 0.7f, vc));
                }
                FieldKit.Floodlights(j, pts, f.O, false);
            }
            ctx.Stat("Placemaker.Stat.Courts", n, Keys.UnitCount);
            ctx.Plan.Facts["courts"] = n;
        }
    }
}
