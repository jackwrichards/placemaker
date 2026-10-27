// AI builder (docs/AI-BUILDER.md): the "ai" type. No controls and no presets: what it builds is whatever the AI placed
// (Core/Ai/AiPlan), turned into plan objects and paths here so the ghost preview, the site plan sketch, Review and Build
// treat it like any other type. Shown in the Make row only while the AI builder option is on.

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

        protected override void DefineControls(ControlSet c) { }

        protected override void PlanCore(LayerContext ctx)
        {
            var pieces = AiPlan.Current.Snapshot();
            // the ground first, so everything else sits on the reshaped ground
            foreach (var p in pieces)
            {
                if (p.Kind != AiPieceKind.Ground || p.Ring == null || p.Ring.Length < 3) continue;
                bool sloped = p.CornerHeights != null && p.CornerHeights.Length == p.Ring.Length;
                var ring = new float3[p.Ring.Length];
                for (int i = 0; i < ring.Length; i++) ring[i] = new float3(p.Ring[i].x, sloped ? p.CornerHeights[i] : p.Height, p.Ring[i].y);
                ctx.Plan.Terraform.Add(new PlannedTerraform
                {
                    Ring = ring, Kind = p.Shape, TargetHeight = sloped ? p.CornerHeights.Average() : p.Height, Sloped = sloped, Permanent = true,
                });
            }
            var after = TerrainField.FromTerraform(ctx.Plan.Terraform, ctx.H);
            if (after != null)
            {
                float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
                foreach (var t in ctx.Plan.Terraform) foreach (var q in t.Ring) { lo = math.min(lo, q.xz); hi = math.max(hi, q.xz); }
                ctx.Ground.Override(lo, hi, after);
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
                }
                else if (p.Points != null && p.Points.Length >= 2)
                {
                    for (int i = 0; i + 1 < p.Points.Length; i++)
                    {
                        if (math.distance(p.Points[i], p.Points[i + 1]) < 0.5f) continue;
                        var n = ctx.StraightNet(p.Points[i], p.Points[i + 1], p.NetRole, new AssetRef(p.Asset));
                        if (string.IsNullOrEmpty(p.Asset)) n.Category = p.Category ?? AssetCategories.Pathway;
                    }
                }
            }
        }
    }
}
