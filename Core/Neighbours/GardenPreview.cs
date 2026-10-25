// Placemaker neighbourhood polish — the garden picture in Outgrow's editor (pure geometry, engine-free).
//
// The user, 25 Oct: "aren't there sliders in the game to just get whatever behavior I exactly want? ... show a diagram or
// something that's changing live while I'm moving sliders around ... And only relevant sliders are there". A small made-up
// street: a row of houses of three depths with one plot empty, a side street with a row of its own, open land behind and at
// the row's end, every house planned with the outgrow type's values as the editor's rows have them (the real planner, the
// same call the game makes). The panel draws the roads, lots, houses and gardens (PainterView.Preview).

using System.Collections.Generic;
using System.Diagnostics;
using Unity.Mathematics;

namespace Placemaker.Core.Neighbours
{
    public static class GardenPreview
    {
        public sealed class Picture
        {
            /// <summary>The corner of the picture (x, z: z north) and its far corner.</summary>
            public float2 Lo, Hi;
            public readonly List<float2[]> Roads = new List<float2[]>(), Lots = new List<float2[]>(), Houses = new List<float2[]>(), Gardens = new List<float2[]>();
            /// <summary>Where the two sliders reach, as lines: Outgrow depth behind each row (from the street), Grow sideways past
            /// each row's open end.</summary>
            public readonly List<float2[]> Limits = new List<float2[]>();
            /// <summary>How long the plan took (ms).</summary>
            public double Ms;
        }

        // (x of the middle, half width, half depth) of the houses along the main street (north side, facing south), and along
        // the side street (west side, facing east: z of the middle); one plot of the main row is empty (between -44 and -8)
        private static readonly (float At, float HalfX, float HalfY)[] MainRow = { (-80f, 8f, 16f), (-62f, 8f, 20f), (-44f, 8f, 12f), (-8f, 8f, 16f), (10f, 8f, 20f), (28f, 8f, 16f), (46f, 8f, 12f) };
        private static readonly (float At, float HalfX, float HalfY)[] SideRow = { (52f, 8f, 16f), (70f, 8f, 20f), (88f, 8f, 16f), (106f, 8f, 12f) };
        private const float MainNorth = 6f, SideWest = 64f;

        public static Picture Plan(IReadOnlyDictionary<string, float> values)
        {
            var pic = new Picture { Lo = new float2(-150f, -14f), Hi = new float2(86f, 126f) };
            var roads = new List<float2[]> { Rect(-150f, -6f, 86f, MainNorth), Rect(SideWest, MainNorth, SideWest + 12f, 126f) };
            pic.Roads.AddRange(roads);
            var houses = new List<StitchHouse>();
            long id = 1;
            foreach (var (at, hx, hy) in MainRow) houses.Add(House(id++, new float2(at, MainNorth + 1f + hy), new float2(0f, -1f), new float2(hx, hy), values));
            foreach (var (at, hx, hy) in SideRow) houses.Add(House(id++, new float2(SideWest - 1f - hy, at), new float2(1f, 0f), new float2(hx, hy), values));
            var s = new StitchSettings { Barriers = roads, BarrierKinds = new List<int> { 0, 0 }, BarrierGroups = new List<long> { 1, 2 }, BarrierHeights = new List<float> { 0f, 0f } };
            var clock = Stopwatch.StartNew();
            var plan = YardStitch.Plan(houses, s);
            pic.Ms = clock.Elapsed.TotalMilliseconds;
            // the limits: the depth line behind the main row and the side row (measured from their streets), the width line past
            // each row's open end, one line round the land the outgrow may take
            float V(string k) => values != null && values.TryGetValue(k, out var v) ? v : PolishLooks.Defaults.TryGetValue(k, out var d) ? d : 0f;
            float depth = V("gardenDepth"), width = V("gardenWidth");
            float front = MainNorth + 1f, sideFront = SideWest - 1f;
            float rowEnd = MainRow[0].At - MainRow[0].HalfX - width, sideEnd = SideRow[SideRow.Length - 1].At + SideRow[SideRow.Length - 1].HalfX + width;
            float back = front + depth, sideBack = math.max(sideFront - depth, rowEnd);
            pic.Limits.Add(new[] { new float2(rowEnd, front), new float2(rowEnd, back), new float2(sideBack, back), new float2(sideBack, sideEnd), new float2(sideFront, sideEnd) });
            foreach (var h in houses)
            {
                pic.Lots.Add(LotOf(h));
                pic.Houses.Add(h.Footprint);
                pic.Gardens.Add(plan.Plots.TryGetValue(h.Id, out var g) && g != null && g.Length >= 3 ? g : LotOf(h));
            }
            return pic;
        }

        private static StitchHouse House(long id, float2 pos, float2 fwd, float2 half, IReadOnlyDictionary<string, float> values)
        {
            var h = new StitchHouse { Id = id, Position = pos, Forward = fwd, HalfLot = half, Edit = true, Polished = true, Zone = "home", Theme = "garden" };
            h.Options = new Dictionary<string, float>();
            if (values != null) foreach (var kv in values) h.Options[kv.Key] = kv.Value;
            // the house: 10 m wide, 12 m deep, 3 m behind the lot's front
            var r = h.Right;
            float2 W(float x, float z) => pos + r * x + fwd * z;
            float front = half.y - 3f;
            h.Footprint = new[] { W(-5f, front - 12f), W(5f, front - 12f), W(5f, front), W(-5f, front) };
            return h;
        }

        private static float2[] LotOf(StitchHouse h)
        {
            var r = h.Right; var f = h.Forward; var a = h.HalfLot;
            return new[] { h.Position - r * a.x - f * a.y, h.Position + r * a.x - f * a.y, h.Position + r * a.x + f * a.y, h.Position - r * a.x + f * a.y };
        }

        private static float2[] Rect(float x0, float z0, float x1, float z1) => new[] { new float2(x0, z0), new float2(x1, z0), new float2(x1, z1), new float2(x0, z1) };
    }
}
