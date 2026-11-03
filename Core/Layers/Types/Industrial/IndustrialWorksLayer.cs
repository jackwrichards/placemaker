// Industrial category: the combined sites, where the other yards meet in one planned works. Each block is still one thing;
// the mix is in which block gets what:
//   recycling  a recycling centre: skips along the front band, the bin lorries' row, a sorting plant (plant block, tanks,
//              chimney, conveyor) in the biggest block, sorted bays (paper, rubbish, metal, drums) against the back fence;
//   salvage    scrap and salvage works: wreck rows on one side of the spine, container rows on the other, the scrap
//              mountain in the middle, metal bays at the back;
//   works      old heavy works: the plant with its tall chimney centre stage, low container stacks in front, rubble and
//              stone heaps behind, pipes and cable drums in rows.

using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Yards
{
    [AreaGenerator(67, Group = "industrial")]
    public sealed class IndustrialWorksLayer : IndustrialYardLayer
    {
        public const string LayerId = "industrialWorks";
        private static readonly string[] StyleIds = { "recycling", "salvage", "works" };

        protected override string Area => "IndustrialWorks";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/ZoneIndustrialManufacturing.svg";
        protected override string[] Styles => StyleIds;

        protected override void DefinePresets()
        {
            Preset("recycling", ("style", "recycling"), ("ground", "gravel"), ("edge", "trees"), ("aisle", 8.0), ("fill", 90.0), ("weighbridge", true));
            Preset("salvage", ("style", "salvage"), ("ground", "dirt"), ("edge", "containers"), ("aisle", 8.0), ("fill", 85.0), ("weighbridge", true));
            Preset("works", ("style", "works"), ("ground", "dirt"), ("edge", "boulders"), ("aisle", 9.0), ("fill", 85.0), ("weighbridge", false));
        }

        protected override SiteSpec Spec(LayerContext ctx, string style)
        {
            switch (style)
            {
                case "salvage":
                    return new SiteSpec { Edge = 4.2f, Front = 26f, BandDepth = 13.8f, LastBand = 14.5f, MaxBlock = 56f, MinBlock = 10f, BlockGap = 0.5f };
                case "works":
                    return new SiteSpec { Edge = 3.5f, Front = 26f, BandDepth = 17f, MaxBlock = 70f, MinBlock = 12f, BlockGap = 0.6f };
                default:
                    return new SiteSpec { Edge = 5f, Front = 26f, BandDepth = 17f, LastBand = 14.5f, MaxBlock = 60f, MinBlock = 10f, BlockGap = 0.5f };
            }
        }

        protected override YardFill.FrontSpec Front(LayerContext ctx, string style) => new YardFill.FrontSpec
        {
            Office = style == "works" ? IndustrialAssets.BigBooth : IndustrialAssets.ContainerOffice, Offices = style == "works" ? 1 : 3,
            Booth = true, Parking = true, Toilet = style != "works", Flag = true, ParkedCar = IndustrialAssets.Car,
        };

        private static readonly YardItem[] RecyclingBays =
        {
            IndustrialAssets.Boxes, IndustrialAssets.Trash, IndustrialAssets.MetalHeap, IndustrialAssets.Barrels,
            IndustrialAssets.Pallets, IndustrialAssets.WoodRubble,
        };

        protected override void Fill(IndustrialSite s, string style)
        {
            float fill = s.Ctx.F("fill") / 100f;
            int last = LastBand(s);
            // The centrepiece first, in the biggest block not against the back fence.
            var centre = Biggest(s, b => b.Band >= 0 && (b.Band != last || last == 0)) ?? Biggest(s, b => b.Band >= 0) ?? Biggest(s);
            if (centre != null)
            {
                bool ok = style == "salvage" ? YardFill.Mountain(s, centre, s.Salt(0x3A1u)) : YardFill.Plant(s, centre, s.Salt(0x3A2u), style == "works");
                if (ok) centre.Use = style == "salvage" ? "mountain" : "plant";
            }
            int k = 0;
            foreach (var b in Ordered(s))
            {
                if (b.Use.Length > 0) continue;
                uint salt = s.Salt(0x3000u + (uint)b.Index * 7919u);
                bool back = b.Band == last && last > 0;
                switch (style)
                {
                    case "recycling":
                        if (back && YardFill.Bays(s, b, 9.4f, fill, salt, (i, c, face, w, d) => Bay(s, RecyclingBays, i, c, face, w, d, salt)) > 0) { b.Use = "bays"; break; }
                        if (b.Band == 0 && b.Side != s.OfficeSide && YardFill.RowsAcross(s, b, IndustrialAssets.GarbageTruck, 1.4f, math.min(fill, 0.8f), salt, new YardFill.Mess { Pos = 0.25f, Yaw = 0.06f, Missing = 0.1f }) > 0)
                        { b.Use = "lorries"; break; }
                        if (YardFill.RowsAcross(s, b, IndustrialAssets.OpenSkip, 1.0f, fill, salt, new YardFill.Mess { Pos = 0.35f, Yaw = 0.12f, Missing = 0.12f }) > 0) b.Use = "skips";
                        break;
                    case "salvage":
                        if (back && YardFill.Bays(s, b, 9.4f, fill, salt, (i, c, face, w, d) => Bay(s, new[] { IndustrialAssets.MetalHeap, IndustrialAssets.Tyres, IndustrialAssets.Barrels }, i, c, face, w, d, salt)) > 0)
                        { b.Use = "bays"; break; }
                        if (b.Side == s.OfficeSide)
                        {
                            int n = YardFill.RowsAcross(s, b, IndustrialAssets.Box20, 0.35f, fill, salt, YardFill.Mess.Containers, true, 0.5f, 2);
                            if (n > 0) { b.Use = "containers"; s.Containers += n; }
                        }
                        else
                        {
                            int n = YardFill.RowsAcross(s, b, IndustrialAssets.Car, 0.75f, fill, salt, YardFill.Mess.Wrecks(0.35f));
                            if (n > 0) { b.Use = "wrecks"; s.Wrecks += n; }
                        }
                        break;
                    default:
                        if (b.Band <= 0 && YardFill.RowsAcross(s, b, IndustrialAssets.Box20, 0.35f, fill, salt, YardFill.Mess.Containers, true, 0.5f, 2) > 0) { b.Use = "stacks"; break; }
                        if (back)
                        {
                            if ((k++ & 1) == 0 && YardFill.Stockpiles(s, b, salt) > 0) { b.Use = "stockpiles"; break; }
                            if (YardFill.Jumble(s, b, YardFill.RubbleMix, fill, salt) > 0) { b.Use = "heaps"; break; }
                        }
                        var goods = (b.Index & 1) == 0 ? IndustrialAssets.Pipes : IndustrialAssets.Reels;
                        if ((goods.Z > 5f ? YardFill.RowsAcross(s, b, goods, 0.6f, fill, salt, YardFill.Mess.Goods) : YardFill.Piles(s, b, goods, fill, salt)) > 0) b.Use = "goods";
                        break;
                }
            }
        }

        private static void Bay(IndustrialSite s, YardItem[] kinds, int i, float2 c, float2 face, float w, float d, uint salt)
        {
            var item = kinds[i % kinds.Length];
            uint v = Hashing.Mix(salt, (uint)i);
            if (item.X >= 7f) YardFill.Heap(s, c, face, w, d, item, v);
            else if (item.Category == AssetCategories.ScrapPallet) YardFill.Grid(s, c, face, w, d, item, 0.3f, 0.85f, v);
            else YardFill.Clump(s, c, math.min(w, d) * 0.4f, item, 6 + (int)(v % 5u), v);
        }
    }
}
