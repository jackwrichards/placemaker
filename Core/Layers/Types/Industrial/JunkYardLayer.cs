// Industrial category: the junk yard, sorted the way a working yard sorts it — never one blotchy pile:
//   sorted    a scrap-metal yard: concrete-block bays along the back, one material to a bay (metal, concrete, wood,
//             drums, crates, truck loads), the scrap mountain in the biggest block with its loader and skips, rows of
//             skips and heaps in between, a weighbridge at the gate;
//   reclaim   a reclamation yard: timber, pipes, pallets, cable drums, crates and firewood each in their own block, in
//             straight rows a forklift can work, trees along the fence;
//   tip       the town tip: skips in lines on both sides of the aisles, garbage heaps in bays at the back, the bin lorries
//             parked in a row.

using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Yards
{
    [AreaGenerator(66, Group = "industrial")]
    public sealed class JunkYardLayer : IndustrialYardLayer
    {
        public const string LayerId = "junkYard";
        private static readonly string[] StyleIds = { "sorted", "reclaim", "tip" };

        protected override string Area => "JunkYard";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/PropsIndustrial.svg";
        protected override string[] Styles => StyleIds;

        protected override void DefinePresets()
        {
            Preset("sorted", ("style", "sorted"), ("ground", "dirt"), ("edge", "boulders"), ("aisle", 8.0), ("fill", 90.0), ("weighbridge", true));
            Preset("reclaim", ("style", "reclaim"), ("ground", "gravel"), ("edge", "trees"), ("aisle", 7.0), ("fill", 85.0), ("weighbridge", false));
            Preset("tip", ("style", "tip"), ("ground", "concrete"), ("edge", "blocks"), ("aisle", 8.0), ("fill", 90.0), ("weighbridge", true));
        }

        /// <summary>The scrap-metal yard is a scrapyard (tracks through a field of scrap); the reclamation yard and the tip keep their order.</summary>
        protected override bool Organic(string style) => style == "sorted";

        protected override SiteSpec Spec(LayerContext ctx, string style)
        {
            switch (style)
            {
                case "reclaim":
                    return new SiteSpec { Edge = 5f, Front = 24f, BandDepth = 10.4f, MaxBlock = 40f, MinBlock = 8f, BlockGap = 0.5f };
                case "tip":
                    return new SiteSpec { Edge = 3f, Front = 26f, BandDepth = 10f, LastBand = 15f, MaxBlock = 60f, MinBlock = 10f, BlockGap = 0.5f };
                default:
                    return new SiteSpec { Edge = 3f, Front = 26f, BandDepth = 15.5f, LastBand = 14.5f, MaxBlock = 64f, MinBlock = 12f, BlockGap = 0.5f };
            }
        }

        protected override YardFill.FrontSpec Front(LayerContext ctx, string style) => new YardFill.FrontSpec
        {
            Office = style == "reclaim" ? IndustrialAssets.Shed : IndustrialAssets.ContainerOffice, Offices = 2, Booth = true,
            Parking = true, Toilet = true, Flag = false, ParkedCar = IndustrialAssets.Car,
        };

        /// <summary>The scrap yard's bays, in order along the back wall.</summary>
        private static readonly YardItem[] SortedBays =
        {
            IndustrialAssets.MetalHeap, IndustrialAssets.Rubble, IndustrialAssets.Barrels, IndustrialAssets.WoodRubble,
            IndustrialAssets.TruckLoad, IndustrialAssets.Crates, IndustrialAssets.MixedRubble, IndustrialAssets.Tyres,
        };

        /// <summary>The reclamation yard's goods, one to a block, in the order the blocks fill.</summary>
        private static readonly YardItem[] Goods =
        {
            IndustrialAssets.Timber, IndustrialAssets.Pipes, IndustrialAssets.Pallets, IndustrialAssets.Reels,
            IndustrialAssets.Crates, IndustrialAssets.Firewood, IndustrialAssets.Boxes, IndustrialAssets.Barrels,
        };

        protected override void Fill(IndustrialSite s, string style)
        {
            float fill = s.Ctx.F("fill") / 100f;
            int last = LastBand(s);
            switch (style)
            {
                case "reclaim": Reclaim(s, fill); return;
                case "tip": Tip(s, fill, last); return;
            }
            if (s.Organic)
            {
                OrganicYard.WorkPad(s, false, s.Salt(0x9AD1u));
                OrganicYard.Field(s, new[]
                {
                    new OrganicYard.Zone(YardFill.MetalMix, 0.9f, 0.5f), new OrganicYard.Zone(YardFill.ScrapMix, 0.9f, 0.5f),
                    new OrganicYard.Zone(YardFill.RubbleMix, 0.85f, 0f), new OrganicYard.Zone(YardFill.TrashMix, 0.75f, 0.3f),
                }, fill, s.Salt(0xF1E2u));
                OrganicYard.Spill(s, fill, s.Salt(0x5912u));
                return;
            }
            // The mountain in the biggest block that is not against the back fence (the back is for the bays).
            var mountain = Biggest(s, b => b.Band >= 0 && (b.Band != last || last == 0)) ?? Biggest(s, b => b.Band >= 0) ?? Biggest(s);
            if (mountain != null && YardFill.Mountain(s, mountain, s.Salt(0x307u))) mountain.Use = "mountain";
            int heapRows = 0;
            foreach (var b in Ordered(s))
            {
                if (b.Use.Length > 0) continue;
                uint salt = s.Salt(0x5C00u + (uint)b.Index * 7919u);
                if (b.Band == last && last > 0)
                {
                    if (YardFill.Bays(s, b, 9.4f, fill, salt, (i, c, face, w, d) => Bay(s, SortedBays, i + b.Index, c, face, w, d, salt)) > 0) b.Use = "bays";
                    continue;
                }
                // Between the bays and the gate: rows of skips on the spine side, heaps of one kind per block further out.
                if (b.Band == 0 && YardFill.RowsAcross(s, b, IndustrialAssets.OpenSkip, 1.0f, fill, salt, Skips) > 0) { b.Use = "skips"; continue; }
                // Further out: metal heaps, loose material (sand, dirt, spoil, a rock pile), rubble, in turn.
                int kind = heapRows++ % 3;
                if (kind == 1 && YardFill.Stockpiles(s, b, salt) > 0) { b.Use = "stockpiles"; continue; }
                if (YardFill.Jumble(s, b, kind == 0 ? YardFill.ScrapMix : YardFill.RubbleMix, fill, salt) > 0) b.Use = "heaps";
            }
        }

        private static readonly YardFill.Mess Skips = new YardFill.Mess { Pos = 0.35f, Yaw = 0.12f, Missing = 0.12f };
        private static readonly YardFill.Mess Lorries = new YardFill.Mess { Pos = 0.25f, Yaw = 0.06f, Missing = 0.1f };

        private static void Bay(IndustrialSite s, YardItem[] kinds, int i, float2 c, float2 face, float w, float d, uint salt)
        {
            var item = kinds[((i % kinds.Length) + kinds.Length) % kinds.Length];
            uint v = Hashing.Mix(salt, (uint)i);
            if (item.X >= 7f) YardFill.Heap(s, c, face, w, d, item, v);
            else if (item.Category == AssetCategories.ScrapPallet) YardFill.Grid(s, c, face, w, d, item, 0.3f, 0.85f, v);
            else YardFill.Clump(s, c, math.min(w, d) * 0.4f, item, 6 + (int)(v % 5u), v);
        }

        private static void Reclaim(IndustrialSite s, float fill)
        {
            int k = 0;
            foreach (var b in Ordered(s))
            {
                uint salt = s.Salt(0x2EC0u + (uint)b.Index * 7919u);
                // Every fourth block is the builders' end of the yard: sand, gravel and a rock pile.
                if (k % 4 == 2 && YardFill.Stockpiles(s, b, salt) > 0) { b.Use = "stockpiles"; k++; continue; }
                var item = Goods[k % Goods.Length];
                int n = item.Z > 5f
                    ? YardFill.RowsAcross(s, b, item, 0.6f, fill, salt, YardFill.Mess.Goods)          // timber bundles and pipe stacks nose to the aisle
                    : YardFill.Piles(s, b, item, fill, salt);                                       // small goods in loose piles
                if (n > 0) { b.Use = "goods"; k++; }
            }
        }

        private static void Tip(IndustrialSite s, float fill, int last)
        {
            foreach (var b in Ordered(s))
            {
                uint salt = s.Salt(0x7190u + (uint)b.Index * 7919u);
                if (b.Band == last && last > 0)
                {
                    if (YardFill.Bays(s, b, 10.2f, fill, salt, (i, c, face, w, d) =>
                    {
                        // Rubbish in the bays, a mixed-rubble bay and a wood bay at the ends.
                        var item = i % 4 == 3 ? IndustrialAssets.WoodRubble : i % 4 == 2 ? IndustrialAssets.MixedRubble : IndustrialAssets.Trash;
                        if (item.X >= 7f) YardFill.Heap(s, c, face, w, d, item, Hashing.Mix(salt, (uint)i));
                        else YardFill.Grid(s, c, face, w, d, item, 0.8f, 0.7f, Hashing.Mix(salt, (uint)i), true);
                    }) > 0) b.Use = "bays";
                    continue;
                }
                // The bin lorries park in the band nearest the gate on the office side; skips everywhere else.
                if (b.Band == 0 && b.Side == s.OfficeSide && YardFill.RowsAcross(s, b, IndustrialAssets.GarbageTruck, 1.4f, math.min(fill, 0.8f), salt, Lorries) > 0)
                { b.Use = "lorries"; s.Machines++; continue; }
                var skip = (b.Index & 1) == 0 ? IndustrialAssets.OpenSkip : IndustrialAssets.BigSkip;
                if (YardFill.RowsAcross(s, b, skip, 1.2f, fill, salt, Skips) > 0) b.Use = "skips";
            }
        }
    }
}
