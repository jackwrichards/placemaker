// Running track: a standard 400 m oval (84.39 m straights, 36.5 m bend radius, 1.22 m lanes, 8 or 6
// lanes), a 300 m school track or a 200 m mini track. The track surface is laid as two U-shaped halves
// (no holes), the infield is turf with a full-size football pitch (it fits inside a 400 m oval by
// design), field events (long-jump runway and sand pit along the back straight, shot-put circle and
// sector in a D) or plain lawn. Lines: kerb and outer edge, lane lines (home straight or all round),
// finish line. Stands along the home straight on the arrival side, floodlights round the oval.

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns.Fields
{
    [AreaGenerator(182)]
    public sealed class TrackAddOn : FieldAddOn
    {
        protected override string Area => "Field.Track";
        public override string Id => "runningTrack";

        public const float Lane = 1.22f;

        protected override FieldFormat[] Formats(LayerContext ctx) => FieldSpecs.Track;

        /// <summary>Straight length, kerb radius, lanes of a format.</summary>
        public static (float straight, float radius, int lanes) Geometry(string format)
        {
            switch (format)
            {
                case "sixLane": return (84.39f, 36.5f, 6);
                case "school": return (60f, 28.35f, 6);
                case "mini": return (40f, 18.8f, 4);
                default: return (84.39f, 36.5f, 8);
            }
        }

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("format", "auto", ControlTier.Essential, "field", WithAuto(FieldSpecs.Track)).Tip();
            c.Choice("infield", "pitch", ControlTier.Essential, "field", "pitch", "events", "lawn").Tip();
            c.Asset("trackAsset", AssetCategories.SurfaceNatural, ControlTier.More, "field");
            c.Toggle("turf", true, ControlTier.More, "field").Tip();
            c.Asset("turfAsset", AssetCategories.SurfaceGrass, ControlTier.More, "field").When("turf=true");
            c.Asset("goalAsset", AssetCategories.SportsGoal, ControlTier.More, "field");
            LineControls(c);
            c.Choice("laneLines", "straight", ControlTier.More, "lines", "straight", "all").Tip();
            c.Choice("stands", "one", ControlTier.More, "spectators", "none", "one", "both").Tip();
            c.Asset("standAsset", AssetCategories.SportsStand, ControlTier.More, "spectators");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "spectators");
            LightControls(c, "lighting", true, "four");
            c.Toggle("fence", false, ControlTier.More, "boundary").Tip();
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "boundary");
            GroundControls(c);
            PlacementControls(c);
        }

        protected override void DefinePresets()
        {
            Preset("stadium", ("format", "standard"), ("infield", "pitch"), ("stands", "both"), ("floodlights", "six"), ("laneLines", "all"), ("fence", true));
            Preset("community", ("format", "auto"), ("infield", "pitch"), ("stands", "one"), ("floodlights", "four"));
            Preset("athletics", ("format", "standard"), ("infield", "events"), ("stands", "one"), ("laneLines", "all"));
            Preset("school", ("format", "school"), ("infield", "lawn"), ("stands", "none"), ("floodlights", "none"));
            Preset("jogging", ("format", "mini"), ("infield", "lawn"), ("stands", "none"), ("floodlights", "none"));
        }

        protected override void Lay(FieldJob j)
        {
            var ctx = j.Ctx;
            var place = j.Place;
            var f = Frame.Of(place);
            var (s, r, n) = Geometry(j.Format);
            float h = s * 0.5f;
            float ro = r + n * Lane;
            // Smaller places (a host place at a lower rung) shrink the bends, never the lane width.
            float fit = math.min((place.HalfLength - 3f - h) / ro, (place.HalfWidth - 3f) / ro);
            if (fit < 1f) { r = r * fit - n * Lane * (1f - fit); ro = r + n * Lane; }
            float side = j.Side;

            // Track surface: two U-shaped halves.
            for (int e = -1; e <= 1; e += 2)
                FieldKit.Surface(ctx, U(f, e, h, r, ro), SurfaceRole.Ground, ctx.S("trackAsset"),
                    AssetCategories.SurfaceDirt, AssetCategories.SurfaceNatural, AssetCategories.SurfaceAsphalt);
            if (ctx.B("turf"))
                FieldKit.Surface(ctx, Stadium(f, h, r - 0.05f, 16), SurfaceRole.Grass, ctx.S("turfAsset"), AssetCategories.SurfaceGrass);

            // Lines.
            var l = j.Lines;
            l.Path(Stadium(f, h, r, 20), true);
            l.Path(Stadium(f, h, ro, 20), true);
            for (int i = 1; i < n; i++)
            {
                float lr = r + i * Lane;
                if (ctx.S("laneLines") == "all") l.Path(Stadium(f, h, lr, 20), true);
                else l.Seg(f, -h, side * lr, h, side * lr);
            }
            // Finish line at the end of the home straight; 100 m start stagger marks at its other end.
            l.Seg(f, h, side * r, h, side * ro);
            for (int i = 0; i < n; i++) l.Spot(f, -h, side * (r + (i + 0.5f) * Lane), f.YawOf(new float2(0, 1)));

            // Infield.
            string infield = ctx.S("infield");
            float pitchHalfW = 0f;
            if (infield == "pitch")
            {
                float w = math.min(68f, 2f * r - 9f);
                float a = math.min(52.5f, h + math.sqrt(math.max(0f, (r - 1.5f) * (r - 1.5f) - w * w * 0.25f)));
                w = math.min(w, a * 2f * 0.68f);
                if (a > 12f && w > 10f)
                {
                    PitchAddOn.SoccerLines(l, f, a, w * 0.5f);
                    pitchHalfW = w * 0.5f;
                    string goal = FieldKit.Cat(ctx, ctx.S("goalAsset"), AssetCategories.SportsGoal);
                    for (int e = -1; e <= 1; e += 2)
                        FieldKit.Equip(j, f.P(e * (a + 0.4f), 0f), f.Yaw + (e > 0 ? math.PI : 0f), goal, ctx.S("goalAsset"), 1.0f, 0x6A10u + (uint)(e + 1));
                    ctx.Plan.Facts["pitchLength"] = a * 2f;
                }
            }
            if ((infield == "events" || infield == "pitch") && r > 25f)
                FieldEvents(j, f, h, r, side, pitchHalfW);

            // Stands along the home straight, floodlights round the oval, optional perimeter fence.
            float room = place.HalfWidth - ro;
            string stands = ctx.S("stands");
            if (j.HostStands) { }
            else if (stands != "none" && room > 4f)
            {
                // (2026-10-29) The game's stands, front row 1.5 m off the outer lane, as deep as the place allows.
                float v = ro + 1.5f;
                FieldKit.StandRow(j, f.P(-h * 0.95f, side * v), f.P(h * 0.95f, side * v), f.O, room - 1.8f);
                if (stands == "both") FieldKit.StandRow(j, f.P(-h * 0.7f, -side * v), f.P(h * 0.7f, -side * v), f.O, room - 1.8f);
            }
            else if (room > 2.5f)
                FieldKit.Benches(j, new[] { f.P(-h * 0.4f, side * (ro + 1.6f)), f.P(0f, side * (ro + 1.6f)), f.P(h * 0.4f, side * (ro + 1.6f)) }, f.O);
            string lights = ctx.S("floodlights");
            if (lights != "none" && room > 1.5f && !j.HostLights)
            {
                float lv = ro + math.min(1.4f, room - 0.9f);
                var pts = new List<float2> { f.P(-h, -lv), f.P(h, -lv), f.P(h, lv), f.P(-h, lv) };
                if (lights == "six")
                {
                    float endRoom = place.HalfLength - h - ro;
                    if (endRoom > 1.5f) { float lu = h + ro + math.min(1.4f, endRoom - 0.9f); pts.Add(f.P(lu, 0f)); pts.Add(f.P(-lu, 0f)); }
                }
                FieldKit.Floodlights(j, pts, f.O);
            }
            if (ctx.B("fence"))
                RectFence(ctx, new OrientedRect { Center = place.Center, Axis = place.Axis, HalfLength = place.HalfLength - 0.7f, HalfWidth = place.HalfWidth - 0.7f },
                    j.Toward, 4f, AssetMix.One(ctx.S("fenceAsset")));
            ctx.Plan.Facts["trackLength"] = 2f * s + 2f * math.PI * (r + 0.3f);
            ctx.Plan.Facts["lanes"] = n;
        }

        /// <summary>Long-jump runway + sand pit along the back straight inside the kerb; shot-put circle and sector in a D.</summary>
        private static void FieldEvents(FieldJob j, Frame f, float h, float r, float side, float pitchHalfW)
        {
            var ctx = j.Ctx;
            float v = -side * (r - 2.2f);
            if (pitchHalfW <= 0f || r - 2.2f - 1.4f > pitchHalfW + 0.3f)
            {
                FieldKit.Surface(ctx, f.Box(-h + 4f, v - 0.61f, h - 13f, v + 0.61f), SurfaceRole.Paving, "", AssetCategories.SurfacePaving, AssetCategories.SurfaceAsphalt);
                FieldKit.Surface(ctx, f.Box(h - 12f, v - 1.375f, h - 3f, v + 1.375f), SurfaceRole.Ground, "", AssetCategories.SurfaceSand, AssetCategories.SurfaceNatural);
                j.Lines.Seg(f, h - 13f, v - 0.61f, h - 13f, v + 0.61f);   // take-off board
            }
            // Shot put in the far D: 2.135 m circle, 34.92° sector opening towards the middle.
            var c = new float2(-(h + r * 0.55f), 0f);
            FieldKit.Surface(ctx, World(f, Circle(c, 1.4f, 16)), SurfaceRole.Paving, "", AssetCategories.SurfacePaving, AssetCategories.SurfaceAsphalt);
            j.Lines.Circle(f, c, 1.0675f);
            float len = math.min(16f, r * 0.55f + h * 0.2f);
            for (int s = -1; s <= 1; s += 2)
            {
                float a = s * math.radians(17.46f);
                var d = new float2(math.cos(a), math.sin(a));
                j.Lines.Seg(f, c.x + d.x * 1.1f, c.y + d.y * 1.1f, c.x + d.x * len, c.y + d.y * len);
            }
            ctx.Plan.Facts["fieldEvents"] = 1;
        }

        private static float2[] World(Frame f, List<float2> l)
        {
            var r = new float2[l.Count];
            for (int i = 0; i < l.Count; i++) r[i] = f.P(l[i]);
            return r;
        }

        private static List<float2> Circle(float2 c, float r, int n)
        {
            var l = new List<float2>(n);
            for (int i = 0; i < n; i++) { float a = i * 2f * math.PI / n; l.Add(c + new float2(math.cos(a), math.sin(a)) * r); }
            return l;
        }

        /// <summary>Stadium outline (two straights of 2h, semicircular bends of radius rad) as a world ring.</summary>
        public static float2[] Stadium(Frame f, float h, float rad, int perBend)
        {
            var pts = new List<float2>();
            for (int i = 0; i <= perBend; i++)
            {
                float a = -math.PI * 0.5f + math.PI * i / perBend;
                pts.Add(f.P(h + math.cos(a) * rad, math.sin(a) * rad));
            }
            for (int i = 0; i <= perBend; i++)
            {
                float a = math.PI * 0.5f + math.PI * i / perBend;
                pts.Add(f.P(-h + math.cos(a) * rad, math.sin(a) * rad));
            }
            return pts.ToArray();
        }

        /// <summary>Half of the track (end e): a U from the middle of one straight round the bend to the middle of the other.</summary>
        private static float2[] U(Frame f, int e, float h, float r, float ro)
        {
            var pts = new List<float2>();
            const int bend = 18;
            pts.Add(f.P(0f, -ro));
            for (int i = 0; i <= bend; i++)
            {
                float a = -math.PI * 0.5f + math.PI * i / bend;
                pts.Add(f.P(e * (h + math.cos(a) * ro), math.sin(a) * ro));
            }
            pts.Add(f.P(0f, ro));
            pts.Add(f.P(0f, r));
            for (int i = bend; i >= 0; i--)
            {
                float a = -math.PI * 0.5f + math.PI * i / bend;
                pts.Add(f.P(e * (h + math.cos(a) * r), math.sin(a) * r));
            }
            pts.Add(f.P(0f, -r));
            return Poly.EnsureCcw(Poly.Clean(pts.ToArray(), 0.05f));
        }
    }
}
