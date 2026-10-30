// Football pitch: soccer (105×68 full, 9-, 7- and 5-a-side), American football (100 yd + end zones,
// 53⅓ yd wide) or rugby (100×70 + in-goal). Real markings: touch/goal lines, halfway, centre circle
// 9.15 m, penalty and goal areas, penalty spots and arcs, corner arcs — gridiron yard lines every 5 yd
// with hash marks — rugby 22 m, 10 m dashed, 5 m dashed lines. Goals / posts at both ends, ball-stop
// fences behind them, team benches on the far touchline, stands on the side people arrive from,
// floodlight masts at the corners (four) or corners + halfway (six).

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns.Fields
{
    [AreaGenerator(180)]
    public sealed class PitchAddOn : FieldAddOn
    {
        protected override string Area => "Field.Pitch";
        public override string Id => "pitch";

        protected override FieldFormat[] Formats(LayerContext ctx) => FieldSpecs.Pitch;

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("code", "soccer", ControlTier.Essential, "field", "soccer", "gridiron", "rugby").Tip();
            c.Choice("format", "auto", ControlTier.Essential, "field", WithAuto(FieldSpecs.Pitch)).Tip();
            c.Toggle("turf", true, ControlTier.More, "field").Tip();
            c.Asset("turfAsset", AssetCategories.SurfaceGrass, ControlTier.More, "field").When("turf=true");
            c.Asset("goalAsset", AssetCategories.SportsGoal, ControlTier.More, "field");
            LineControls(c);
            c.Choice("stands", "one", ControlTier.More, "spectators", "none", "one", "both").Tip();
            c.Asset("standAsset", AssetCategories.SportsStand, ControlTier.More, "spectators");
            c.Toggle("teamBenches", true, ControlTier.More, "spectators").Tip();
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "spectators");
            LightControls(c, "lighting", true, "four");
            c.Toggle("ballStops", true, ControlTier.More, "boundary").Tip();
            c.Toggle("rail", false, ControlTier.More, "boundary").Tip();
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "boundary");
            GroundControls(c);
            PlacementControls(c);
            c.Fixed("code");
            c.Chance("rail", 0.2f);
        }

        protected override void DefinePresets()
        {
            Preset("stadium", ("code", "soccer"), ("format", "full"), ("stands", "both"), ("floodlights", "six"), ("rail", true));
            Preset("community", ("code", "soccer"), ("format", "auto"), ("stands", "one"), ("floodlights", "four"));
            Preset("kickabout", ("code", "soccer"), ("format", "seven"), ("stands", "none"), ("floodlights", "none"), ("teamBenches", false), ("ballStops", false));
            Preset("gridiron", ("code", "gridiron"), ("format", "full"), ("stands", "both"), ("floodlights", "six"));
            Preset("rugby", ("code", "rugby"), ("format", "full"), ("stands", "one"), ("floodlights", "four"));
        }

        /// <summary>Playing area (length incl. end zones / in-goal, width) of a code and format that fits the place.</summary>
        public static float2 PlayingArea(string code, string format, OrientedRect place)
        {
            float2 soccer;
            switch (format)
            {
                case "nine": soccer = new float2(73f, 46f); break;
                case "seven": soccer = new float2(55f, 37f); break;
                case "five": soccer = new float2(37f, 27f); break;
                default: soccer = new float2(105f, 68f); break;
            }
            float k = soccer.x / 105f;
            float2 area;
            switch (code)
            {
                case "gridiron": area = new float2(109.7f, 48.8f) * k; break;
                case "rugby": area = new float2(100f + 2f * 10f, 70f) * k; break;
                default: area = soccer; break;
            }
            // Never closer than 4 m to the place's ends / 5 m to its sides.
            area.x = math.min(area.x, place.HalfLength * 2f - 8f);
            area.y = math.min(area.y, place.HalfWidth * 2f - 10f);
            return area;
        }

        protected override void Lay(FieldJob j)
        {
            var ctx = j.Ctx;
            var place = j.Place;
            string code = ctx.S("code");
            var area = PlayingArea(code, j.Format, place);
            float a = area.x * 0.5f, b = area.y * 0.5f;
            var f = Frame.Of(place);
            var fence = AssetMix.One(ctx.S("fenceAsset"));

            if (ctx.B("turf"))
                FieldKit.Surface(ctx, f.Box(-a - 1.5f, -b - 1.5f, a + 1.5f, b + 1.5f), SurfaceRole.Grass, ctx.S("turfAsset"), AssetCategories.SurfaceGrass);

            float goalU;
            // (rf-sports) No line pieces installed: a soccer pitch at least the size of the game's own football pitch decal
            // (68 x 52.5 m) gets that decal; smaller ones stay unmarked (no line pieces to paint them with).
            var lines = j.Lines;
            // (2026-10-29, game runner) The decal is HALF a regulation pitch (68 m across its X, 52.5 m from the halfway
            // line to the goal line along its Z, the goal line at +Z): stamped once in the middle and turned a quarter it lay
            // across the field, its penalty box on a touchline. Two halves, each from halfway to its own goal line, and only
            // on a full-size pitch (the lines do not scale).
            if (code == "soccer" && FieldKit.StampMarkings(ctx) && area.x >= 104f && area.y >= 67f)
            {
                for (int e = -1; e <= 1; e += 2)
                    ctx.Object(f.P(e * 26.25f, 0f), f.YawOf(new float2(e, 0f)), ObjectRole.Decal, AssetMix.One("", "FootballFieldDecal01"), j.V(0xF0Du), -1, AssetCategories.DecalMarkings);
                lines = new LinePainter();
                ctx.Plan.Facts["pitchDecal"] = 2;
            }
            switch (code)
            {
                case "gridiron": goalU = GridironLines(lines, f, a, b); break;
                case "rugby": goalU = RugbyLines(lines, f, a, b, area.x / 120f); break;
                default: SoccerLines(lines, f, a, b); goalU = a; break;
            }

            // Goals (soccer: on the goal line; gridiron: posts at the back of the end zones; rugby: on the try lines).
            // (2026-10-29) The game's own full-size goals by default: the automatic goal pool dealt 2 m mini goals and ice
            // hockey goals onto full pitches. The sports-park goal's net reaches back along its +Z: it looks out of the field.
            string goalCat = FieldKit.Cat(ctx, ctx.S("goalAsset"), AssetCategories.SportsGoal);
            bool own = string.IsNullOrEmpty(ctx.S("goalAsset"));
            for (int e = -1; e <= 1; e += 2)
            {
                if (goalCat == null) continue;
                if (own && code == "soccer")
                {
                    string g = a >= 30f ? SportsProps.GoalFull : SportsProps.GoalSmall;
                    var gp = f.P(e * goalU, 0f);
                    if (SportsProps.Prop(ctx, j.Occ, gp, f.Dir(new float2(e, 0f)), g, ObjectRole.Furniture, 1.0f, j.V(0x6A10u + (uint)(e + 1)), AssetCategories.SportsGoal, true, SportsProps.GoalSmall, "FootballGoal01"))
                        j.Equipment++;
                }
                else if (own)
                {
                    var gp = f.P(e * goalU, 0f);
                    if (SportsProps.Prop(ctx, j.Occ, gp, f.Dir(new float2(-e, 0f)), SportsProps.GoalPosts, ObjectRole.Furniture, 1.0f, j.V(0x6A10u + (uint)(e + 1)), AssetCategories.SportsGoal))
                        j.Equipment++;
                }
                else FieldKit.Equip(j, f.P(e * (goalU + (code == "soccer" ? 0.4f : 0f)), 0f), f.Yaw + (e > 0 ? math.PI : 0f), goalCat, ctx.S("goalAsset"), 1.0f, 0x6A10u + (uint)(e + 1));
            }
            // Corner flags, a scoreboard behind the far goal (2026-10-29).
            if (code == "soccer")
                for (int e = -1; e <= 1; e += 2)
                    for (int s = -1; s <= 1; s += 2)
                        SportsProps.Prop(ctx, j.Occ, f.P(e * a, s * b), f.Dir(new float2(0f, 1f)), SportsProps.CornerFlag, ObjectRole.Furniture, 0.2f, j.V(0xF1A6u + (uint)(e * 3 + s + 4)), AssetCategories.SportsStand, false);
            float endRoomS = place.HalfLength - a;
            if (endRoomS > 3.2f && !j.HostStands)
            {
                float e = -j.End;
                var sp = f.P(e * (a + math.min(endRoomS - 1.2f, 6f)), b * 0.55f);
                SportsProps.Prop(ctx, j.Occ, sp, f.Dir(new float2(-e, 0f)), SportsProps.Scoreboard, ObjectRole.Furniture, 1.4f, j.V(0x5C0Bu), AssetCategories.SportsStand);
            }

            // Ball-stop fences behind the goals.
            float endRoom = place.HalfLength - a;
            if (ctx.B("ballStops") && endRoom > 2.5f)
            {
                float u = a + math.min(endRoom - 0.8f, 4.5f), half = math.min(b, math.max(9f, b * 0.55f));
                for (int e = -1; e <= 1; e += 2)
                    ctx.StraightNet(f.P(e * u, -half), f.P(e * u, half), NetRole.Fence, fence);
            }

            float side = j.Side;
            float sideRoom = place.HalfWidth - b;
            // Team benches (dugout benches) either side of halfway on the far touchline, then stands.
            if (ctx.B("teamBenches") && sideRoom > 2.8f)
            {
                float v = -side * (b + math.min(2.4f, sideRoom - 1.5f));
                // (2026-10-29) The game's covered dugouts, one per team either side of halfway; garden benches only when the
                // player chose a bench.
                if (string.IsNullOrEmpty(ctx.S("benchAsset")))
                {
                    // the dugout's seats look along its −Z (SportsProps.StandFacing): its +Z points away from the pitch
                    var inward = -f.Dir(new float2(0f, side));
                    foreach (float u in new[] { -7f, 7f })
                        if (SportsProps.Prop(ctx, j.Occ, f.P(u, v), inward, SportsProps.Dugout, ObjectRole.Bench, 1.6f, j.V(0xD06Au + (uint)(u + 8f)), null, true, SportsProps.OpenBench))
                            j.Benches++;
                }
                else FieldKit.Benches(j, new[] { f.P(-5.5f, v), f.P(-8.5f, v), f.P(5.5f, v), f.P(8.5f, v) }, f.P(0f, 0f));
            }
            string stands = ctx.S("stands");
            if (stands != "none" && sideRoom > 4.5f && !j.HostStands)
            {
                // (2026-10-29) Stands with their front row 1.5 m off the touchline, as deep as the place allows.
                float v = side * (b + 1.5f);
                float span = a * 0.7f;
                FieldKit.StandRow(j, f.P(-span, v), f.P(span, v), f.O, sideRoom - 1.8f);
                if (stands == "both" && sideRoom > 6.5f)
                {
                    float v2 = -side * (b + 1.5f);
                    // Leave the dugouts' stretch of the far side free.
                    FieldKit.StandRow(j, f.P(-span, v2), f.P(-11.5f, v2), f.O, sideRoom - 1.8f);
                    FieldKit.StandRow(j, f.P(11.5f, v2), f.P(span, v2), f.O, sideRoom - 1.8f);
                }
            }
            string lights = j.HostLights ? "none" : ctx.S("floodlights");
            if (lights != "none")
            {
                float lu = a + math.min(2.5f, endRoom - 1f), lv = b + math.min(2.0f, sideRoom - 1f);
                var pts = new List<float2> { f.P(-lu, -lv), f.P(lu, -lv), f.P(lu, lv), f.P(-lu, lv) };
                if (lights == "six") { pts.Add(f.P(0f, lv)); pts.Add(f.P(0f, -lv)); }
                FieldKit.Floodlights(j, pts, f.O);
            }
            if (ctx.B("rail") && sideRoom > 2f && endRoom > 2f)
            {
                var rail = new OrientedRect { Center = place.Center, Axis = place.Axis, HalfLength = a + math.min(3f, endRoom - 0.8f), HalfWidth = b + math.min(1.6f, sideRoom - 0.8f) };
                RectFence(ctx, rail, j.Toward, 3f, fence);
            }
            ctx.Plan.Facts["pitchLength"] = area.x;
            ctx.Plan.Facts["pitchWidth"] = area.y;
        }

        /// <summary>Soccer markings of a pitch 2a × 2b (boxes scale with small-sided formats).</summary>
        public static void SoccerLines(LinePainter l, Frame f, float a, float b)
        {
            float k = math.clamp(a / 52.5f, 0.35f, 1f);
            l.Box(f, -a, -b, a, b);
            l.Seg(f, 0f, -b, 0f, b);
            l.Circle(f, float2.zero, 9.15f * k);
            l.Spot(f, 0f, 0f);
            for (int e = -1; e <= 1; e += 2)
            {
                float pd = 16.5f * k, pw = math.min(20.16f * k, b - 1f);
                float gd = 5.5f * k, gw = math.min(9.16f * k, pw - 1.5f);
                l.Seg(f, e * a, -pw, e * (a - pd), -pw); l.Seg(f, e * (a - pd), -pw, e * (a - pd), pw); l.Seg(f, e * (a - pd), pw, e * a, pw);
                l.Seg(f, e * a, -gw, e * (a - gd), -gw); l.Seg(f, e * (a - gd), -gw, e * (a - gd), gw); l.Seg(f, e * (a - gd), gw, e * a, gw);
                float spot = a - 11f * k;
                l.Spot(f, e * spot, 0f);
                // Penalty arc: the part of the 9.15 m circle round the spot outside the penalty area.
                float r = 9.15f * k, half = math.acos(math.saturate((pd - 11f * k) / r));
                float mid = e > 0 ? math.PI : 0f;
                l.Arc(f, new float2(e * spot, 0f), r, mid - half, mid + half);
                // Corner arcs (1 m).
                for (int s = -1; s <= 1; s += 2)
                {
                    // Quarter circle from the goal line (towards −e·u) to the touchline (towards −s·v).
                    if (e > 0) l.Arc(f, new float2(a, s * b), 1f * k, s > 0 ? math.PI : 0.5f * math.PI, s > 0 ? 1.5f * math.PI : math.PI);
                    else l.Arc(f, new float2(-a, s * b), 1f * k, s > 0 ? 1.5f * math.PI : 0f, s > 0 ? 2f * math.PI : 0.5f * math.PI);
                }
            }
        }

        /// <summary>American football: end lines, sidelines, goal lines, yard lines every 5 yd, hash marks. Returns the post line (end line).</summary>
        private static float GridironLines(LinePainter l, Frame f, float a, float b)
        {
            float k = a / 54.85f;
            float yd = 0.9144f * k, ez = 10f * yd;
            l.Box(f, -a, -b, a, b);
            float g = a - ez;
            for (int i = 0; i <= 20; i++)
            {
                float u = -g + i * 5f * yd;
                l.Seg(f, u, -b, u, b);
            }
            // Hash marks every yard between the 5-yard lines, at the college width (60 ft from the sidelines).
            float hv = math.max(1f, b - 18.29f * k);
            for (int i = 1; i < 100; i++)
            {
                if (i % 5 == 0) continue;
                float u = -g + i * yd;
                l.Spot(f, u, hv, f.YawOf(new float2(0, 1)));
                l.Spot(f, u, -hv, f.YawOf(new float2(0, 1)));
                l.Spot(f, u, b - 0.4f, f.YawOf(new float2(0, 1)));
                l.Spot(f, u, -b + 0.4f, f.YawOf(new float2(0, 1)));
            }
            return a;
        }

        /// <summary>Rugby: dead-ball, try, 22 m, halfway lines, dashed 10 m, 5 m and 15 m lines. Returns the try line.</summary>
        private static float RugbyLines(LinePainter l, Frame f, float a, float b, float k)
        {
            float inGoal = math.clamp(a - 50f * k, 3f * k, 10f * k);
            float t = a - inGoal;
            l.Box(f, -a, -b, a, b);
            l.Seg(f, 0f, -b, 0f, b);
            for (int e = -1; e <= 1; e += 2)
            {
                l.Seg(f, e * t, -b, e * t, b);
                l.Seg(f, e * (t - 22f * k), -b, e * (t - 22f * k), b);
                l.Dashed(f, e * 10f * k, -b, e * 10f * k, b, 2f * k, 3f * k);
                l.Dashed(f, e * (t - 5f * k), -b + 5f * k, e * (t - 5f * k), b - 5f * k, 1f, 4f * k);
            }
            for (int s = -1; s <= 1; s += 2)
            {
                l.Dashed(f, -t + 5f * k, s * (b - 5f * k), t - 5f * k, s * (b - 5f * k), 1f, 4f * k);
                l.Dashed(f, -t + 5f * k, s * (b - 15f * k), t - 5f * k, s * (b - 15f * k), 1f, 4f * k);
            }
            return t;
        }
    }
}
