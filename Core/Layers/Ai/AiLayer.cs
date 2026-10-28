// AI builder (docs/AI-BUILDER.md): the "ai" type. No controls and no presets: what it builds is whatever the AI placed
// (Core/Ai/AiPlan), turned into plan objects and paths here so the ghost preview, the site plan sketch, Review and Build
// treat it like any other type. Shown in the Make row only while the AI builder option is on.

using System.Collections.Generic;
using System.Linq;
using Placemaker.Core.Ai;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Ai
{
    [AreaGenerator(95, Group = "ai")]
    public sealed class AiLayer : LayerBase
    {
        public const string LayerId = "ai";

        protected override string Area => "Ai";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/Commercial.svg";
        public override LayerKind Kind => LayerKind.Fill;
        public override float MinArea => 20f;

        /// <summary>Entrance kinds the player may mark before sending (all optional): walking ones as the parks have them, car
        /// ones as the parking type has them. The AI is told where they are and builds its entrances there.</summary>
        public static readonly string[] EntranceKinds = { "main", "pedestrian", "bike", "service", "entrance", "entry", "exit", "alley" };

        protected override void DefineControls(ControlSet c)
        {
            var m = c.Markers("entrances", ControlTier.Essential, EntranceKinds);
            m.Icon = "entrance";
            m.LabelKey = "Placemaker.Ai.entrances";
            m.TooltipKey = "Placemaker.Ai.entrances.Tooltip";
            Common.EntranceKinds.WithOptions(m, new[] { "main", "pedestrian", "bike", "service" });
            foreach (var k in new[] { "entrance", "entry", "exit", "alley" })
            {
                string key = "Placemaker.Parking.Marker." + Tool.MarkerKinds.Pascal(k);
                m.Options.Add(new ControlOption { Value = k, LabelKey = key, TooltipKey = key + ".Tip", Icon = Tool.MarkerKinds.IconFor(k) });
            }
        }

        /// <summary>An end within 4 m of a city road's centre line moves onto it and is attached there.</summary>
        private static void Attach(LayerContext ctx, ref float2 end, out long id, out float t)
        {
            id = -1; t = 0f;
            float best = 4f;
            foreach (var n in ctx.Site?.NearbyNets ?? new List<NearbyNet>())
            {
                if (n.Category != NetCategory.Road) continue;
                for (int k = 0; k <= 64; k++)
                {
                    float u = k / 64f;
                    var q = SiteBrief.Bez(n, u).xz;
                    float d = math.distance(q, end);
                    if (d < best) { best = d; id = n.ExternalId; t = u; }
                }
            }
            if (id < 0) return;
            foreach (var n in ctx.Site.NearbyNets) if (n.ExternalId == id) { end = SiteBrief.Bez(n, t).xz; break; }
        }

        private static bool IsWalk(AiPiece p) =>
            p.Kind == AiPieceKind.Path && p.Points != null && p.Points.Length >= 2 && (p.NetRole == NetRole.Pathway || p.NetRole == NetRole.PathwayWide);

        /// <summary>
        /// A path end that meets another path (within 3 m) at a shallow angle arrives square-on instead: the end goes exactly onto
        /// the other path, and the last stretch turns to meet it at a right angle. A slanting junction is drawn by the game as a
        /// long spike of paving (Opus park close-ups, 27 Oct), and an end a little off the other path was bent onto it with a kink.
        /// </summary>
        public static float2[] Approach(float2[] pts, List<AiPiece> others)
        {
            if (others.Count == 0 || math.distance(pts[0], pts[pts.Length - 1]) < 1f) return pts;   // none, or a loop
            var list = pts.ToList();
            for (int side = 0; side < 2; side++)
            {
                if (side == 1) list.Reverse();
                int last = list.Count - 1;
                if (last < 1) continue;
                var end = list[last];
                float best = 3f; float2 at = default, dir = default; bool atEnd = false;
                foreach (var o in others)
                    for (int k = 0; k + 1 < o.Points.Length; k++)
                    {
                        float2 a = o.Points[k], b = o.Points[k + 1], ab = b - a;
                        float len2 = math.lengthsq(ab);
                        if (len2 < 1e-4f) continue;
                        float t = math.saturate(math.dot(end - a, ab) / len2);
                        var q = a + ab * t;
                        float d = math.distance(q, end);
                        if (d < best) { best = d; at = q; dir = math.normalize(ab); atEnd = (k == 0 && t * math.sqrt(len2) < 2f) || (k == o.Points.Length - 2 && (1f - t) * math.sqrt(len2) < 2f); }
                    }
                if (best >= 3f) continue;
                var nrm = new float2(-dir.y, dir.x);
                // the side the path comes from
                var from = list[math.max(0, last - 1)];
                if (math.dot(from - at, nrm) < 0f) nrm = -nrm;
                var come = math.normalizesafe(end - from, -nrm);
                list[last] = at;
                if (atEnd) continue;   // it carries the other path on (end to end): no corner
                if (math.abs(math.dot(come, dir)) < 0.26f) continue;   // already 75° or steeper (at 60° the game still draws a notch)
                // drop the points within the new approach, then come in along the normal
                float reach = 6f;
                while (list.Count > 2 && math.distance(list[list.Count - 2], at) < reach * 1.8f) list.RemoveAt(list.Count - 2);
                reach = math.min(reach, math.distance(list[list.Count - 2], at) * 0.55f);   // a short path keeps a shorter approach
                if (reach >= 1.5f) list.Insert(list.Count - 1, at + nrm * reach);
            }
            list.Reverse();
            return list.ToArray();
        }

        private static float2 Centroid(float2[] ring)
        {
            float2 s = 0;
            foreach (var q in ring) s += q;
            return s / math.max(1, ring.Length);
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var pieces = AiPlan.Current.Snapshot();
            var waterRings = new List<(PlannedTerraform t, float2[] ring)>();
            // the ground first, so everything else sits on the reshaped ground
            // each piece shapes the ground the earlier ones left (a dip inside a raised mound, a channel through a terrace)
            System.Func<float2, float> shaped = ctx.H;
            foreach (var p in pieces)
            {
                if (p.Kind != AiPieceKind.Ground || p.Ring == null || p.Ring.Length < 3) continue;
                if (AiTerrain.IsField(p))
                {
                    // a soft edge or a line: the field, as terraform cells
                    var cells = AiTerrain.Cells(p, shaped, out var deepest);
                    ctx.Plan.Terraform.AddRange(cells);
                    if (deepest != null && math.isfinite(p.WaterLevel)) { deepest.WaterLevel = p.WaterLevel; waterRings.Add((deepest, p.Ring)); }
                    shaped = TerrainField.FromTerraform(ctx.Plan.Terraform, ctx.H) ?? ctx.H;
                    continue;
                }
                bool sloped = p.CornerHeights != null && p.CornerHeights.Length == p.Ring.Length;
                var ring = new float3[p.Ring.Length];
                for (int i = 0; i < ring.Length; i++) ring[i] = new float3(p.Ring[i].x, sloped ? p.CornerHeights[i] : p.Height, p.Ring[i].y);
                ctx.Plan.Terraform.Add(new PlannedTerraform
                {
                    Ring = ring, Kind = p.Shape, TargetHeight = sloped ? p.CornerHeights.Average() : p.Height, Sloped = sloped, Permanent = true,
                    WaterLevel = p.WaterLevel,
                });
                waterRings.Add((ctx.Plan.Terraform[ctx.Plan.Terraform.Count - 1], p.Ring));
                shaped = TerrainField.FromTerraform(ctx.Plan.Terraform, ctx.H) ?? ctx.H;
            }
            var after = TerrainField.FromTerraform(ctx.Plan.Terraform, ctx.H);
            if (after != null)
            {
                float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
                foreach (var t in ctx.Plan.Terraform) foreach (var q in t.Ring) { lo = math.min(lo, q.xz); hi = math.max(hi, q.xz); }
                ctx.Ground.Override(lo, hi, after);
                // water: anchored at the deepest point of its basin after all the shaping (an island raised in the middle of
                // a pond must not carry the anchor: the build then finds no basin and adds no water)
                foreach (var (t, ring) in waterRings)
                {
                    if (!math.isfinite(t.WaterLevel)) continue;
                    float2 rlo = new float2(float.MaxValue), rhi = new float2(float.MinValue);
                    foreach (var q in ring) { rlo = math.min(rlo, q); rhi = math.max(rhi, q); }
                    float best = float.MaxValue; float2 at = Centroid(ring);
                    float step = math.max(1f, math.cmax(rhi - rlo) / 24f);
                    for (float x = rlo.x; x <= rhi.x; x += step)
                        for (float z = rlo.y; z <= rhi.y; z += step)
                        {
                            var q = new float2(x, z);
                            if (!Geometry.Poly.Contains(ring, q)) continue;
                            float g = after(q);
                            if (g < best) { best = g; at = q; }
                        }
                    t.WaterAnchor = new float3(at.x, t.WaterLevel, at.y);
                }
            }
            foreach (var p in pieces)
            {
                ctx.Check();
                if (p.Kind == AiPieceKind.Ground) continue;
                if (p.Kind == AiPieceKind.Paint && p.Ring != null && p.Ring.Length >= 3)
                {
                    ctx.Plan.Paint.Add(new PlannedPaint
                    {
                        Ring = p.Ring.Select(q => ctx.P3(q)).ToArray(), Layer = math.clamp(p.Layer, 0, 4), Strength = math.saturate(p.Strength),
                        Feather = math.max(0f, p.Feather), Noise = math.max(0f, p.Noise), Seed = (uint)p.Id,
                    });
                    continue;
                }
                if (p.Kind == AiPieceKind.Surface && p.Ring != null && p.Ring.Length >= 3)
                {
                    ctx.Surface(p.Ring, p.SurfaceRole, new AssetRef(p.Asset), p.Category);
                    continue;
                }
                if (p.Kind == AiPieceKind.Object)
                {
                    var o = ctx.Object(p.Position, p.Yaw, p.Role, new AssetRef(p.Asset), (uint)p.Id, category: p.Category);
                    o.Elevation = p.Elevation;
                    o.Scale = p.Scale > 0.05f ? p.Scale : 1f;
                    o.Roll = p.Roll;
                    o.Pitch = p.Pitch;
                }
                else if (p.Points != null && p.Points.Length >= 2)
                {
                    // a road-type line is joined to a city road its end lies on
                    var pts = (float2[])p.Points.Clone();
                    if (IsWalk(p) && !math.isfinite(p.LineHeight)) pts = Approach(pts, pieces.Where(o => o.Id != p.Id && IsWalk(o)).ToList());
                    long startAttach = -1, endAttach = -1; float startT = 0f, endT = 0f;
                    if (p.NetRole == NetRole.DriveAisle)
                    {
                        Attach(ctx, ref pts[0], out startAttach, out startT);
                        Attach(ctx, ref pts[pts.Length - 1], out endAttach, out endT);
                    }
                    for (int i = 0; i + 1 < pts.Length; i++)
                    {
                        if (math.distance(pts[i], pts[i + 1]) < 0.5f) continue;
                        var n = ctx.StraightNet(pts[i], pts[i + 1], p.NetRole, new AssetRef(p.Asset));
                        if (i == 0 && startAttach >= 0) { n.StartAttach = startAttach; n.StartAttachT = startT; }
                        if (i + 2 == pts.Length && endAttach >= 0) { n.EndAttach = endAttach; n.EndAttachT = endT; }
                        n.Category = p.Category ?? AssetCategories.Pathway;
                        if (math.isfinite(p.LineHeight))
                        {
                            // raised and level (a boardwalk, a pier): fixed height, not following the ground
                            n.FollowTerrain = false;
                            n.A.y = n.B.y = n.C.y = n.D.y = p.LineHeight;
                        }
                    }
                }
            }
        }
    }
}
