// Placemaker — the legend while choosing the area (area overhaul).
//
// The user: "things turn red when I move my mouse near them, which I guess is fine. I want to keep that, but it's not
// super clear. Maybe the legend could handle that. We do legends for the design, we don't do it for painting, so it's
// hard to tell what's what." The Area stage now has the same legend window as Design and Review (state.areaLegend):
// one row per thing the overlay draws for the current tool, hover isolates it, click hides it (the same
// PreviewLegend.Visible rules; ids start with "area:").

using System.Collections.Generic;
using Placemaker.Core.Build;

namespace Placemaker.Core.Tool
{
    public static class AreaLegend
    {
        public const string Outline = "area:outline", Fill = "area:fill", Doors = "area:doors", Reach = "area:reach",
            Stops = "area:stops", Painted = "area:painted", Fence = "area:fence", LeftOut = "area:leftOut", Node = "area:node";

        public static string Color(string id)
        {
            switch (id)
            {
                case Outline: return "#cceaff";
                case Fill: return "#ffffff";
                case Doors: return "#ffcc33";
                case Reach: return "#8cd9ff";
                case Stops: return "#ff5a46";
                case Painted: return "#4d9eff";
                case Fence: return "#e6f2ff";
                case LeftOut: return "#6b6b75";
                case Node: return "#ffffff";
                default: return "#ffffff";
            }
        }

        public static string LabelKey(string id) => "Placemaker.Legend.Area." + id.Substring(id.IndexOf(':') + 1);

        /// <summary>What the overlay shows right now, per tool.</summary>
        public sealed class Input
        {
            public SelectionMode Mode;
            public bool HasShape;
            public double AreaM2;
            /// <summary>Paint bucket / Paint area: a fill is outlined under the cursor (or fences the brush).</summary>
            public bool HasFill;
            public int Doors;
            public bool Reach;
            public int Stops;
            /// <summary>Points: pieces of the drawn outline left out by the stops; corners on road nodes.</summary>
            public int LeftOut;
            public int Nodes;
        }

        public static List<PreviewLegend.Entry> Compute(Input i)
        {
            var list = new List<PreviewLegend.Entry>();
            void Add(string id, int count, double area = 0)
            {
                list.Add(new PreviewLegend.Entry { Id = id, LabelKey = LabelKey(id), Color = Color(id), Count = count, AreaM2 = area, DefaultOn = true });
            }
            switch (i.Mode)
            {
                case SelectionMode.Block:
                    if (i.HasShape) Add(Outline, 1, i.AreaM2);
                    if (i.HasFill) Add(Fill, 1);
                    if (i.HasFill || i.Doors > 0) Add(Doors, i.Doors);
                    if (i.Reach) Add(Reach, 1);
                    if (i.HasFill || i.Stops > 0) Add(Stops, i.Stops);
                    break;
                case SelectionMode.BlockPaint:
                case SelectionMode.Paint:
                    if (i.HasShape) Add(Painted, 1, i.AreaM2);
                    if (i.HasFill) Add(Fence, 1);
                    if (i.Doors > 0) Add(Doors, i.Doors);
                    if (i.Mode == SelectionMode.BlockPaint) Add(Stops, i.Stops);
                    break;
                default:
                    if (i.HasShape) Add(Outline, 1, i.AreaM2);
                    if (i.LeftOut > 0) Add(LeftOut, i.LeftOut);
                    if (i.Nodes > 0) Add(Node, i.Nodes);
                    break;
            }
            return list;
        }
    }
}
