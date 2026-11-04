// Industrial category: the container yard. Three ways to keep boxes, each laid out like the real thing:
//   terminal  container blocks in long bands: lines of 40 ft boxes end to end, five wide, two high at the street rising to
//             four high at the back, each block stepped down at its outer end, concrete throughout;
//   depot     a container depot on gravel: rows of 20 ft boxes doors-to-aisle, the same two high, 40 ft boxes two high
//             along the aisle, and a two-high wall of boxes along the back and side fences;
//   storage   self storage: neat back-to-back rows of 20 ft units with doors on the drive aisles, customer parking at the
//             office, a screen of trees, everything square.

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Yards
{
    [AreaGenerator(64, Group = "industrial")]
    public sealed class ContainerYardLayer : IndustrialYardLayer
    {
        public const string LayerId = "containerYard";
        private static readonly string[] StyleIds = { "terminal", "depot", "storage" };

        protected override string Area => "ContainerYard";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/ZoneIndustrialWarehouses.svg";
        protected override string[] Styles => StyleIds;

        protected override void DefinePresets()
        {
            Preset("terminal", ("style", "terminal"), ("ground", "concrete"), ("edge", "blocks"), ("aisle", 10.0), ("fill", 85.0), ("weighbridge", false), ("banks", false), ("stones", 10.0));
            Preset("depot", ("style", "depot"), ("ground", "gravel"), ("edge", "containers"), ("aisle", 8.0), ("fill", 80.0), ("weighbridge", false));
            Preset("storage", ("style", "storage"), ("ground", "concrete"), ("edge", "trees"), ("aisle", 7.0), ("fill", 100.0), ("weighbridge", false), ("banks", false), ("stones", 0.0));
        }

        protected override SiteSpec Spec(LayerContext ctx, string style)
        {
            switch (style)
            {
                case "depot":
                    return new SiteSpec { Edge = 4.2f, Front = 24f, BandDepth = 13.6f, MaxBlock = 62f, MinBlock = 14f, BlockGap = 0.5f };
                case "storage":
                    return new SiteSpec { Edge = 5f, Front = 26f, BandDepth = 12.8f, LastBand = 6.6f, MaxBlock = 46f, MinBlock = 10f, BlockGap = 0.4f };
                default:
                    return new SiteSpec { Edge = 3.5f, Front = 26f, BandDepth = 16f, MaxBlock = 74f, MinBlock = 14f, BlockGap = 0.5f };
            }
        }

        protected override YardFill.FrontSpec Front(LayerContext ctx, string style) => new YardFill.FrontSpec
        {
            Office = IndustrialAssets.ContainerOffice, Offices = style == "storage" ? 1 : 3, Booth = true, Parking = true,
            Toilet = style != "storage", Flag = style == "terminal", ParkedCar = IndustrialAssets.Car,
        };

        protected override void Fill(IndustrialSite s, string style)
        {
            float fill = s.Ctx.F("fill") / 100f;
            int last = LastBand(s);
            foreach (var b in Ordered(s))
            {
                uint salt = s.Salt(0xC0A0u + (uint)b.Index * 7919u);
                switch (style)
                {
                    case "depot":
                    {
                        // Bands in turn: 20 ft rows, two-high stacks, 40 ft lines.
                        int k = b.Band % 3;
                        // Every band one way of keeping boxes: 20 ft rows doors to the aisle, the same two high, 40 ft lines two high.
                        int n = k == 2 ? YardFill.RowsAlong(s, b, IndustrialAssets.Box20, 0.3f, 0.4f, fill, salt, YardFill.Mess.Containers, 3, true, 2)
                            : YardFill.RowsAcross(s, b, IndustrialAssets.Box20, 0.35f, fill, salt, YardFill.Mess.Containers, true, 0.5f, k == 1 ? 2 : 1);
                        if (n > 0) { b.Use = k == 2 ? "box40" : "box20"; s.Containers += n; }
                        break;
                    }
                    case "storage":
                    {
                        // Units with a hand's breadth between them, every unit on the drive aisle.
                        int n = YardFill.RowsAcross(s, b, IndustrialAssets.Box20, 0.5f, fill, salt, YardFill.Mess.Tidy, true, 1.2f);
                        if (n > 0) { b.Use = "units"; s.Containers += n; }
                        break;
                    }
                    default:
                    {
                        // Terminal blocks: lines of 40 ft boxes end to end, five wide, stacked; a skyline that steps up away from
                        // the street (two high at the front, three, four at the back) and down at each block's outer end.
                        int tiers = b.Band < 0 ? 2 : b.Band == last && last > 0 ? 4 : math.clamp(b.Band + 2, 2, 4);
                        // (The 40 ft box props render empty on their own, seen in the game: pairs of 20 ft boxes instead.)
                        int n = YardFill.RowsAlong(s, b, IndustrialAssets.Box20, 0.3f, 0.35f, fill, salt, YardFill.Mess.Containers, 99, true, tiers);
                        if (n > 0) { b.Use = "stacks"; s.Containers += n; s.Anchors.Add((s.W(b.UMid, b.VMid), b.Length * 0.25f)); }
                        break;
                    }
                }
            }
        }
    }
}
