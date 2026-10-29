// Full basketball court (FIBA 28 × 15 m, or a compact 26 × 14 m school court): asphalt or paved
// surface with a painted-key option, full markings (boundary, halfway, centre circle 1.8 m, restricted
// areas 4.9 × 5.8 m with free-throw circles, 6.75 m three-point arcs with their 0.9 m straight runs,
// no-charge semicircles), two hoops facing each other, bleachers on the arrival side, team benches
// opposite, floodlights and an optional cage with a gate towards the path.

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns.Fields
{
    [AreaGenerator(184)]
    public sealed class BasketballCourtAddOn : FieldAddOn
    {
        protected override string Area => "Field.Basketball";
        public override string Id => "basketballCourt";

        protected override FieldFormat[] Formats(LayerContext ctx) => FieldSpecs.Basketball;
        protected override float Apron(OrientedRect place) => 2.5f;

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("format", "auto", ControlTier.Essential, "field", WithAuto(FieldSpecs.Basketball)).Tip();
            c.Choice("surface", "asphalt", ControlTier.Essential, "field", "asphalt", "paving").Tip();
            c.Asset("surfaceAsset", AssetCategories.SurfaceAny, ControlTier.More, "field");
            c.Toggle("paintedKeys", false, ControlTier.More, "field").Tip();
            c.Asset("hoopAsset", AssetCategories.SportsBasketball, ControlTier.More, "field");
            LineControls(c);
            c.Toggle("fence", false, ControlTier.More, "boundary").Tip();
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "boundary").When("fence=true");
            c.Toggle("stands", true, ControlTier.More, "spectators").Tip();
            c.Asset("standAsset", AssetCategories.SportsStand, ControlTier.More, "spectators").When("stands=true");
            c.Toggle("benches", true, ControlTier.More, "spectators");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "spectators");
            LightControls(c, "lighting", false, "true");
            GroundControls(c);
            PlacementControls(c);
            c.Chance("fence", 0.4f);
        }

        protected override void DefinePresets()
        {
            Preset("streetCourt", ("surface", "asphalt"), ("fence", true), ("stands", false), ("floodlights", true));
            Preset("community", ("surface", "asphalt"), ("fence", false), ("stands", true), ("floodlights", true));
            Preset("schoolyard", ("format", "compact"), ("surface", "paving"), ("fence", false), ("stands", false), ("floodlights", false));
        }

        protected override void Lay(FieldJob j)
        {
            var ctx = j.Ctx;
            var place = j.Place;
            var f = Frame.Of(place);
            bool compact = j.Format == "compact";
            float a = (compact ? 26f : 28f) * 0.5f, b = (compact ? 14f : 15f) * 0.5f;
            float k = compact ? 0.93f : 1f;
            string[] chain = ctx.S("surface") == "paving"
                ? new[] { AssetCategories.SurfacePaving, AssetCategories.SurfaceAsphalt }
                : new[] { AssetCategories.SurfaceAsphalt, AssetCategories.SurfacePaving };
            float runU = math.min(2f, place.HalfLength - a - 0.6f), runV = math.min(2f, place.HalfWidth - b - 0.6f);
            FieldKit.Surface(ctx, f.Box(-a - runU, -b - runV, a + runU, b + runV), SurfaceRole.Asphalt, ctx.S("surfaceAsset"), chain);

            // A full court gets the game's own court decal (28 × 15 m) when installed: one clean stamp
            // instead of hundreds of line pieces.
            // (rf-sports) The court decals are the game's own (named explicitly): the marking decals being usable is enough. A compact
            // court (26 x 14 m) takes it too: the decal (28 x 15 m) still lies inside the court's 2 m run-off surface.
            bool stamp = ctx.B("lines") && (AssetAvailability.Has(AssetCategories.DecalCourtBasketball) || AssetAvailability.Has(AssetCategories.DecalMarkings));
            if (stamp)
            {
                // The decal's long side is its local X: turn it a quarter so it runs along the court. (rf-sports) Named
                // explicitly: the category has no automatic pool, so the empty ref was dropped and no court had lines.
                FieldKit.Stamp(ctx, f, float2.zero, j.V(0xC0u), AssetCategories.DecalCourtBasketball, "BasketballCourtDecal01", "BasketballCourtDecal03");
                ctx.Plan.Facts["courtDecal"] = 1;
            }
            var l = stamp ? new LinePainter() : j.Lines;
            l.Box(f, -a, -b, a, b);
            l.Seg(f, 0f, -b, 0f, b);
            l.Circle(f, float2.zero, 1.8f);
            float keyW = 2.45f * k, keyD = 5.8f * k, basket = 1.575f, three = 6.75f * k, side3 = b - 0.9f;
            for (int e = -1; e <= 1; e += 2)
            {
                // Restricted area and free-throw circle.
                l.Box(f, e * a, -keyW, e * (a - keyD), keyW);
                l.Circle(f, new float2(e * (a - keyD), 0f), 1.8f * k);
                // Three-point line: straight runs from the end line, then the arc round the basket.
                float bu = e * (a - basket);
                float phi = math.asin(math.saturate(side3 / three));
                float run = a - (a - basket - three * math.cos(phi));
                l.Seg(f, e * a, -side3, e * (a - run), -side3);
                l.Seg(f, e * a, side3, e * (a - run), side3);
                float mid = e > 0 ? math.PI : 0f;
                l.Arc(f, new float2(bu, 0f), three, mid - phi, mid + phi);
                l.Arc(f, new float2(bu, 0f), 1.25f, mid - math.PI * 0.5f, mid + math.PI * 0.5f);
                if (ctx.B("paintedKeys") && AssetAvailability.Has(AssetCategories.SurfacePaving))
                    FieldKit.Surface(ctx, f.Box(e * (a - 0.1f), -keyW + 0.1f, e * (a - keyD + 0.1f), keyW - 0.1f), SurfaceRole.Decorative, "", AssetCategories.SurfacePaving);
            }

            // Hoops at both ends facing each other.
            string hoop = FieldKit.Cat(ctx, ctx.S("hoopAsset"), AssetCategories.SportsBasketball);
            for (int e = -1; e <= 1; e += 2)
            {
                var p = f.P(e * (a - 1.2f), 0f);
                FieldKit.Equip(j, p, LeisureKit.Yaw(p, f.O), hoop, ctx.S("hoopAsset"), 0.8f, 0x4000u + (uint)(e + 1));
            }
            float side = j.Side;
            float room = place.HalfWidth - b;
            if (ctx.B("stands") && room > 3.2f && !j.HostStands)
            {
                float v = side * (b + math.min(1.4f, room - 1.2f));
                FieldKit.StandRow(j, f.P(-a * 0.75f, v), f.P(a * 0.75f, v), f.O, room - 1.6f);
            }
            if (ctx.B("benches") && room > 2.4f)
            {
                float v = -side * (b + math.min(1.8f, room - 1.5f));
                FieldKit.Benches(j, new[] { f.P(-3.5f, v), f.P(3.5f, v) }, f.O);
            }
            if (ctx.B("floodlights") && !j.HostLights)
            {
                float lv = b + math.min(1.3f, room - 0.7f), lu = a * 0.5f;
                FieldKit.Floodlights(j, new[] { f.P(-lu, -lv), f.P(lu, -lv), f.P(lu, lv), f.P(-lu, lv) }, f.O, false);
            }
            if (ctx.B("fence"))
                RectFence(ctx, new OrientedRect { Center = place.Center, Axis = place.Axis, HalfLength = place.HalfLength - 0.4f, HalfWidth = place.HalfWidth - 0.4f },
                    j.Toward, 2.5f, AssetMix.One(ctx.S("fenceAsset")));
            ctx.Stat("Placemaker.Stat.Courts", 1, Keys.UnitCount);
            ctx.Plan.Facts["hoops"] = j.Equipment;
        }
    }
}
