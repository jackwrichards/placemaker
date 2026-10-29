// Strip malls on small sites (the user, 2026-10-28: "test really small, tiny strip malls too. They should at least do, you know, a
// 2x2 zoning, even if they're really small ... and I guess eventually they get parking"). Below the lot-first layout's size (a lot
// one aisle deep, the shop lane and two-cell plots behind it: about 50 m of depth), the shops stand on the street itself:
//   • a row of plots facing the street the site is longest along (or the one a car entrance is on), two to four cells deep as the
//     depth allows: at least one 2 x 2 plot on 16 x 16 m;
//   • from about 40 m of frontage and 19 m of depth, a small lot beside the row (one aisle, stalls both sides), entered from the
//     same street (at the player's car entrance when one is on that edge), on the side of the street corner;
//   • what is left behind is the shops' back side (Mall.BackOfHouse), the street front a hedge and a sign (MallFront).
// A composition with only street plots keeps them (Mall.PlantStreetPlots): the shops face the city street by design here.

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Commercial
{
    public sealed partial class StripMallLayer
    {
        /// <summary>The smallest site that takes one 2 x 2 plot (m²).</summary>
        private const float TinyMin = 16f * 16f;
        /// <summary>A lot beside the row: at least this wide along the street (m) and one aisle bay deep.</summary>
        private const float TinyLotWidth = 22f;
        public const string TinyFact = "tinyStrip";

        private static bool Tiny(LayerContext ctx, Mall mall, List<StreetShops.Run> runs)
        {
            if (runs == null || runs.Count == 0) return false;
            // The street: the one a car entrance is on, else the longest frontage.
            var r = runs[0];
            if (mall.Gates != null)
                foreach (var g in mall.Gates)
                {
                    if (!g.Cars) continue;
                    foreach (var run in runs)
                    {
                        float s = math.dot(g.At - run.A, run.Dir), t = math.dot(g.At - run.A, run.In);
                        if (s >= run.From - 1f && s <= run.To + 1f && math.abs(t) < 3f) { r = run; break; }
                    }
                    break;
                }
            // The stretch with the most land behind it (a triangle's tip has none).
            float s0 = r.From + (r.StartCorner ? 1f : 0.5f), s1 = r.To - (r.EndCorner ? 1f : 0.5f);
            var span = mall.BestSpan(r, 16f, 16f, s0, s1);
            float reach;
            if (span.HasValue) { s0 = span.Value.s0; s1 = span.Value.s1; reach = span.Value.reach; }
            else reach = mall.Reach(r, s0, s1);
            float len = s1 - s0;
            if (len < 2f * Mall.Cell || reach < 2f * Mall.Cell) return false;
            int cells = math.clamp((int)math.floor((reach - 0.5f) / Mall.Cell), 2, math.max(2, Cells(ctx)));
            // The lot beside the row (when there is room for one and the row keeps two cells): on the corner side, where a car entrance
            // on the street is, or at the start.
            float lotW = 0f;
            bool lotAtStart = r.StartCorner || !r.EndCorner;
            float lotDepth = math.min(reach - 0.5f, Mall.LotDepth(1) + 1f);
            if (len >= TinyLotWidth + 2f * Mall.Cell && reach >= Mall.LotDepth(1))
            {
                if (mall.Gates != null)
                    foreach (var g in mall.Gates)
                        if (g.Cars)
                        {
                            float s = math.dot(g.At - r.A, r.Dir);
                            if (s > s0 - 1f && s < s1 + 1f) { lotAtStart = s < 0.5f * (s0 + s1); break; }
                        }
                // about half the frontage in whole cells; a row on a street corner keeps three (its first column is the side street's:
                // two left one, which the game blocks: 48 x 36 m, "CutoutNotZonable")
                bool rowOnCorner = lotAtStart ? r.EndCorner : r.StartCorner;
                float minRow = (rowOnCorner ? 3f : 2f) * Mall.Cell;
                float rowW = math.max(minRow, math.floor(len * 0.5f / Mall.Cell) * Mall.Cell);
                lotW = len - rowW;
                if (lotW < TinyLotWidth) { rowW = math.floor((len - TinyLotWidth) / Mall.Cell) * Mall.Cell; lotW = len - rowW; }
                if (rowW < minRow) lotW = 0f;
            }
            float rowA = lotW > 0f && lotAtStart ? s0 + lotW : s0, rowB = lotW > 0f && !lotAtStart ? s1 - lotW : s1;
            int made = mall.ShopRow(r, rowA, rowB, 0f, cells);
            if (made == 0) return false;
            if (lotW > 0f)
            {
                float la = lotAtStart ? s0 : rowB + 0.5f, lb = lotAtStart ? rowA - 0.5f : s1;
                var entries = new List<(float2, string)>();
                if (mall.Gates != null)
                    foreach (var g in mall.Gates)
                    {
                        float s = math.dot(g.At - r.A, r.Dir), t = math.dot(g.At - r.A, r.In);
                        if (math.abs(t) < 3f && s > la + 3f && s < lb - 3f && (g.Cars || g.Kind == ShopEntrances.Footpath)) { entries.Add((g.At, g.Kind)); g.Landed = "lot"; }
                    }
                if (!entries.Exists(e => e.Item2 != ShopEntrances.Footpath)) entries.Add((r.At(0.5f * (la + lb), 0f), "entrance"));
                if (mall.Lot(Mall.Rect(r, la, lb, 0f, lotDepth), entries, Mall.Parking(ctx), 0xA7u)) ctx.Plan.Facts["tinyLot"] = 1;
            }
            ctx.Plan.Facts[TinyFact] = 1;
            ctx.Plan.Facts[RowsFact] = 1;
            return true;
        }
    }
}
