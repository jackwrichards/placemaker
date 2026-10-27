// AI builder (docs/AI-BUILDER.md): the "ai" type. No controls and no presets: what it builds is whatever the AI placed
// (Core/Ai/AiPlan), turned into plan objects and paths here so the ghost preview, the site plan sketch, Review and Build
// treat it like any other type. Shown in the Make row only while the AI builder option is on.

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
            foreach (var p in AiPlan.Current.Snapshot())
            {
                ctx.Check();
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
