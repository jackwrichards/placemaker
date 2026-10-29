// s32 — Mall park: a park in the middle of the block, ringed by shops. A closed loop lane runs round the park (an alley,
// or a pedestrian street for a car-free ring), joined to each street round the site by an access lane. Small low-density
// plots line the outside of the loop, every shop facing the park across the lane; behind them, back to back, deep
// high-density plots face the streets ("big buildings outside, little buildings inside"). The park itself is a real park
// (Park planner: lawns, trees, paths, lamps, benches) with a playground and a fountain, its walks opening onto the loop
// at every access junction. Where a side has no street the inner ring backs onto a planted edge.
// Presets: commons · garden · family · promenade.

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Commercial
{
    [AreaGenerator(19, Group = Group)]
    public sealed partial class MallParkLayer : CommercialLayer
    {
        protected override string Area => "MallPark";
        public override string Id => "mallPark";
        public override string Icon => "Media/Game/Icons/ParksAndRecreation.svg";
        public override float MinArea => 6000f;
        protected override float GradeTolerance => 0.04f;

        /// <summary>The middle's styles: a park (Park planner styles), or "plaza": a paved dining plaza (Plaza planner, café square).</summary>
        public static readonly string[] ParkStyles = { "urban", "formal", "natural", "modern", "pocket", "garden", "plaza" };

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("parkStyle", "urban", ControlTier.Essential, "layout", ParkStyles).Tip();
            c.Choice("loop", "lane", ControlTier.Essential, "layout", "lane", "pedestrian").Tip();
            // (rf-shops3) The loop as a road with parking on both sides (the town square: cars parked along the park), or a quiet alley.
            c.Choice("loopRoad", "parking", ControlTier.Essential, "layout", "alley", "parking").Tip();
            c.Toggle("playground", true, ControlTier.More, "layout").Tip();
            c.Toggle("fountain", true, ControlTier.More, "layout").Tip();
            OuterRowControls(c, ZoneKinds.CommercialHigh);
            Shared(c.Slider("outerDepth", 24, 16, 48, 8, "m", ControlTier.More, "shops")).Tip().When("outerRow=true");
            CommonControls(c, ZoneKinds.CommercialLow, 16, null, false, green: false);
            // (rf-shops3) The architect's extras.
            ExtrasControls(c, pads: false, trucks: 2);
            c.Fixed("parkStyle");
            c.Fixed("loop");
        }

        protected override void DefinePresets()
        {
            Preset("commons", ("parkStyle", "urban"), ("loop", "lane"), ("loopRoad", "parking"), ("foodTrucks", 3.0), ("water", "none"));
            // (shops2) Garden square: the Parks category's Garden walk (the user's favourite park); the formal park planted only half
            // the square ("the garden kind of stops").
            Preset("garden", ("parkStyle", "garden"), ("loop", "lane"), ("zoneKind", ZoneKinds.Mixed), ("playground", false), ("loopRoad", "alley"), ("water", "pond"), ("foodTrucks", 1.0));
            Preset("family", ("parkStyle", "natural"), ("loop", "lane"), ("playground", true), ("fountain", false), ("outerZone", ZoneKinds.CommercialLow), ("outerDepth", 16.0),
                ("loopRoad", "parking"), ("water", "pond"), ("foodTrucks", 3.0));
            Preset("promenade", ("parkStyle", "modern"), ("loop", "pedestrian"), ("plotDepth", 24.0), ("outerDepth", 32.0));
            // (Plazas, 2026-10-28; the user's photos of a lifestyle centre: a paved dining plaza by the shops, parasols over the tables)
            // A car-free ring of shops round a paved plaza with café tables, parasols, kiosks and a fountain; parking round the outside.
            Preset("diningPlaza", ("parkStyle", "plaza"), ("loop", "pedestrian"), ("plotDepth", 24.0), ("playground", false), ("fountain", true),
                ("zoneKind", ZoneKinds.CommercialLow), ("foodTrucks", 0.0), ("water", "none"));
        }

        protected override void Compose(LayerContext ctx, Mall mall, List<StreetShops.Run> runs)
        {
            var r = runs[0];
            bool ped = ctx.S("loop") == "pedestrian";
            AssetRef asset; float h; NetRole role;
            if (ped) { var p = Mall.PedLane(ctx, false); asset = p.asset; h = p.half; role = p.pedestrian ? NetRole.Street : NetRole.Alley; }
            else { var p = Mall.ShopLane(ctx); asset = p.asset; h = p.half; role = NetRole.Alley; }
            int inner = Cells(ctx);
            // (shops) The outer rows face the city streets: only with "Shops facing the streets"; else the ring rows take the land.
            int outer = ctx.B("outerRow") && ctx.B("streetShops") ? Mall.Cells(ctx.Raw("outerDepth")) : 0;

            // The site in the main street's frame: s along it, t inward; which sides have a street.
            float s0 = r.From, s1 = r.To;
            float reach = mall.Reach(r, s0 + 2f, s1 - 2f);
            // (shops) On an angled or curved block the stretch of the run with the most land straight behind it, not the whole run.
            var span = mall.BestSpan(r, 60f, 60f, r.From + 2f, r.To - 2f);
            if (span.HasValue && (span.Value.s0 > r.From + 3f || span.Value.s1 < r.To - 3f)) { s0 = span.Value.s0 - 2f; s1 = span.Value.s1 + 2f; reach = span.Value.reach; }
            bool atStart = s0 <= r.From + 0.5f, atEnd = s1 >= r.To - 0.5f;
            var corners = new[] { r.At(s0, 0f), r.At(s1, 0f), r.At(s1, reach), r.At(s0, reach) };
            bool[] street = new bool[4];
            street[0] = true;
            street[1] = (atEnd && r.EndCorner) || mall.StreetAlong(r.At(s1, 0f) + r.Dir * 0.5f, r.At(s1, reach) + r.Dir * 0.5f);
            street[2] = mall.StreetAlong(r.At(s1, reach) + r.In * 0.5f, r.At(s0, reach) + r.In * 0.5f);
            street[3] = (atStart && r.StartCorner) || mall.StreetAlong(r.At(s0, reach) - r.Dir * 0.5f, r.At(s0, 0f) - r.Dir * 0.5f);

            // Insets of the loop's centre line from each side; shrink rows until the park keeps at least 36 × 30 m.
            // (shops) Per side, how far a ring row backing onto that street keeps in (its first zone row: Mall.KerbMargin).
            float cm = 0.5f * (s0 + s1);
            var kerb = new[] { mall.KerbMargin(r.At(cm, 0f), r.Dir), mall.KerbMargin(r.At(s1, reach * 0.5f), r.In), mall.KerbMargin(r.At(cm, reach), r.Dir), mall.KerbMargin(r.At(s0, reach * 0.5f), r.In) };
            float[] inset = new float[4];
            float parkW = 0f, parkD = 0f;
            for (int guard = 0; guard < 12; guard++)
            {
                // (shops) With no outer row a ring row backs onto the street: it keeps clear of the street's first zone row (Mall.KerbBand).
                for (int k = 0; k < 4; k++) inset[k] = (street[k] ? outer : 0) * Mall.Cell + inner * Mall.Cell + h + (street[k] && outer == 0 ? kerb[k] : 0f);
                parkW = (s1 - s0) - inset[1] - inset[3] - 2f * h;
                parkD = reach - inset[0] - inset[2] - 2f * h;
                if (parkW >= 36f && parkD >= 30f) break;
                // Rows back to back stay two cells deep (a one-cell row would hand the row behind it its second zone row).
                if (outer > 2) outer--; else if (inner > 2) inner--; else if (outer > 0) outer = 0; else if (inner > 1) inner--; else break;
            }
            if (parkW < 24f || parkD < 24f)
            {
                if (mall.Extra) return;   // (shops) another frontage: nothing left behind
                mall.ShopRow(r, s0 + 2f, s1 - 2f, 0f, Cells(ctx));
                ctx.Issue(IssueSeverity.Info, FallbackKey);
                return;
            }
            float lx = s0 + inset[3], rx = s1 - inset[1], fy = inset[0], by = reach - inset[2];
            float cx = 0.5f * (lx + rx), cy = 0.5f * (fy + by);
            // The loop starts in the middle of its front side (where the main access joins it).
            var loop = mall.Loop(r.At(cx, fy), new[] { r.At(rx, fy), r.At(rx, by), r.At(lx, by), r.At(lx, fy) }, role, asset, h);
            if (loop == null)
            {
                if (mall.Extra) return;   // (shops) another frontage: nothing left behind
                mall.ShopRow(r, s0 + 2f, s1 - 2f, 0f, Cells(ctx));
                ctx.Issue(IssueSeverity.Info, FallbackKey);
                return;
            }
            // Access lanes: from every street side's middle straight to the loop.
            var mouths = new List<(Mall.Lane lane, float2 onLoop)>();
            void Access(float2 outlinePt, float2 outward, float2 onLoop)
            {
                var a = mall.Connect(Mall.End.Street(outlinePt, outward), null, Mall.End.OnLane(loop, onLoop), role, asset, h);
                if (a != null) mouths.Add((a, onLoop));
            }
            Access(r.At(cx, 0f), -r.In, r.At(cx, fy));
            // (shops) An access lane cuts the ring row it crosses in two; a side shorter than this keeps its row whole instead (on the
            // default city block every side was cut into pieces too short to zone and the loop had no shop at all).
            const float CutSide = 58f;
            if (street[2] && parkW >= CutSide) Access(r.At(cx, reach), r.In, r.At(cx, by));
            if (street[1] && parkD >= CutSide) Access(r.At(s1, cy), r.Dir, r.At(rx, cy));
            if (street[3] && parkD >= CutSide) Access(r.At(s0, cy), -r.Dir, r.At(lx, cy));

            // The park inside the loop, its walks opening onto the loop at the access junctions and mid-sides.
            var park = Mall.Rect(r, lx + h, rx - h, fy + h, by - h);
            var doors = new List<(Mall.Lane, float2)>
            {
                (loop, r.At(cx, fy)), (loop, r.At(cx, by)), (loop, r.At(lx, cy)), (loop, r.At(rx, cy)),
            };
            mall.Taken.Add(park);
            bool parked = false;
            // The park is planned after the rows (it must see them as taken); keep its land now.
            // Rows: the inner ring faces the loop (shallow, the type's zone), the outer ring the streets (deep, the outer zone).
            // On a slope an access lane falls across the ring plots beside it: they keep a planted bank from it.
            var banks = new List<float2[]>();
            if (mall.SlopeGap() > 0f)
                foreach (var (lane, _) in mouths)
                    for (int i = 0; i + 1 < lane.Points.Count; i++)
                    {
                        var f = Mall.Frame(lane.Points[i], lane.Points[i + 1]);
                        float g = lane.Half + mall.SlopeGap();
                        banks.Add(mall.Hold(Mall.Rect(f, 0f, f.To - lane.Half - 0.2f, -g, g)));
                    }
            mall.LaneRows(loop, inner, null, 0, 0f, 0f, 2);
            foreach (var b in banks) mall.Release(b);
            // The access lanes keep planted gateways (no plots: a plot facing an access lane would stand back to back with a
            // ring plot facing the loop — on a slope two grades a step apart).
            if (outer > 0) mall.StreetRows(runs, outer, ctx.S("outerZone"), 3, 2);
            mall.Taken.Remove(park);
            ctx.Check();
            // The park carves the places of its features as it would under the composer (a clearing for the playground
            // beside a walk, a paved plaza on its axis) and they are planned into them.
            if (ctx.S("parkStyle") == "plaza")
            {
                // The dining plaza: the café square paved edge to edge (no lawn, no shop cutouts of its own), many more tables, the
                // centre a fountain (a planted rondel in a game without one), trees round the edge; its walks meet the ring at every door.
                parked = mall.FillOpen(park, "plaza", "cafe", doors, null, 0x7A6u, b =>
                {
                    b.Values["cafe"] = 8.0;
                    b.Values["shade"] = 3.0;
                    b.Values["games"] = false;
                    b.Values["treeLayout"] = "edge";
                    b.Values["centerMode"] = ctx.B("fountain") ? "single" : "none";
                    b.Values["centerSize"] = 6.0;
                    b.Values["streetShops"] = false;
                }, false);
                if (!parked) mall.Green(park, 0x7A7u);
                else ctx.Plan.Facts["diningPlaza"] = 1;
                return;
            }
            var places = new List<PlannedRegion>();
            var children = ctx.B("playground") ? new[] { "playground" } : new string[0];
            var targets = ctx.B("fountain") ? new[] { Placemaker.Core.Layers.Park.ParkLayer.PlazaPlace } : new string[0];
            parked = mall.FillOpen(park, "park", ctx.S("parkStyle"), doors, null, 0x7A1u, b =>
            {
                b.Values["fence"] = false;
                b.Values["belt"] = false;
                // No permanent terrain paint inside a shopping block (the natural park paints its ground otherwise).
                b.Values["naturalGround"] = false;
                b.Values["plazaShape"] = "circle";
                b.Values["plazaAt"] = "centre";
            }, false, children, targets, places);
            if (!parked) parked = mall.FillOpen(park, "plaza", "green", doors, null, 0x7A2u, null, false);
            if (parked)
            {
                uint salt = 0x7A3u;
                foreach (var place in places)
                {
                    var ring = Placemaker.Core.Geometry.Poly.To2D(place.Ring);
                    if (place.Key == Placemaker.Core.Layers.Park.ParkLayer.PlazaPlace)
                    {
                        ctx.Surface(ring, SurfaceRole.Plaza, new AssetRef(""), AssetCategories.SurfacePaving);
                        mall.AddOn(ring, "fountain", salt++);
                    }
                    else if (place.Key.EndsWith("playground")) mall.AddOn(ring, "playground", salt++);
                }
                // No place carved (a small park): the features look for room themselves.
                if (ctx.B("fountain") && !places.Exists(p => p.Key == Placemaker.Core.Layers.Park.ParkLayer.PlazaPlace)) mall.AddOn(park, "fountain", salt++);
                if (ctx.B("playground") && !places.Exists(p => p.Key.EndsWith("playground"))) mall.AddOn(park, "playground", salt++, b => b.Values["size"] = "small");
            }
            else mall.Green(park, 0x7A5u);
        }

        protected override bool KeepsPark(LayerContext ctx) => true;

        // The park is planned in Compose; what is left round it is lot (shops and lots ring the park).
        protected override void FillRest(LayerContext ctx, Mall mall)
        {
            RestLots(ctx, mall, 0x780u, 300f);
            LotEdges(ctx, mall, 0x7C0u);
        }
    }
}
