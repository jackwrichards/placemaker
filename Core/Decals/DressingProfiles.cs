// Placemaker — decal dressing: settings, controls and per-type profiles (s21). GPL-3.0.
//
// Decals are a sub-feature of every other feature, not something sprinkled on their own. Every fill
// type gets the same two controls in its "Decals" section — Decals (overall amount, 0–300 %) and
// Style (Clean / Lived-in / Gritty) — plus one amount per dressing family the type has, shown in the
// section of the thing it complements (Leaf litter under Planting, Path wear under Paths, Oil & tyres
// under Layout of a lot…; the Decals section when the type has no such section). Ids are save format:
// "dressAmount", "dressStyle", "dress<Family>". A profile per type says which families apply and the
// default style; presets may declare their own style (an abandoned lot is gritty, a formal garden clean).

using System;
using System.Collections.Generic;

namespace Placemaker.Core.Decals
{
    public enum DressStyle { Clean = 0, LivedIn = 1, Gritty = 2 }

    /// <summary>What a dressing family complements. Order is the order of the controls.</summary>
    public enum DressFamily { Trees = 0, Paths = 1, Paved = 2, Parking = 3, Play = 4, Sports = 5, Ground = 6, Shore = 7, Works = 8, Ruins = 9, Garden = 10 }

    /// <summary>Effective dressing settings of one fill step (shared by its features).</summary>
    public sealed class DressSettings
    {
        public const int FamilyCount = 11;
        /// <summary>Overall amount, 0 = no decorative decals at all, 1 = default, up to 3.</summary>
        public float Master = 1f;
        public DressStyle Style = DressStyle.LivedIn;
        /// <summary>Per family amount (0..3); 0 for families the type does not have.</summary>
        public readonly float[] Amount = new float[FamilyCount];
        public string LayerId = "";
        /// <summary>(s28) Terrain-paint ground wear (worn verges, patches, edge variation), 0..3; independent of decal packs.</summary>
        public float PaintWear = 1f;
        /// <summary>(s28) Faint dirt desire lines where people cut corners and walk to features across the lawn.</summary>
        public bool DesirePaths = true;
        /// <summary>(s33) Dark forest-floor mulch under tree crowns (on bare terrain).</summary>
        public bool Mulch = true;
        /// <summary>(s33) Lawn variation: lush patches in hollows and shade, dry patches on knolls (only on a painted lawn).</summary>
        public bool LawnTint = true;
        /// <summary>(s33) The step's lawn is bare terrain + paint (Lawn = Painted terrain), not a grass surface.</summary>
        public bool LawnPainted;
        /// <summary>(s33) Clear older ground paint inside the area first (also the player's own).</summary>
        public bool ClearFirst;
        /// <summary>Outline of the fill step (open-ground scatter and shore sampling stay inside it). Null = the whole area.</summary>
        public Unity.Mathematics.float2[] Ring;
        /// <summary>(dynamic decals) The step gets weathering (Core/Decals/Dynamic/Weathering): the organized paint wear is left out.</summary>
        public bool Weathering;
        /// <summary>(dynamic decals) The player's Grime amount, 0..3.</summary>
        public float Grime = 1f;
        /// <summary>Set by the pass when the step's own plan was dressed (open-ground scatter runs once per step).</summary>
        internal bool MainTaken;

        public float Of(DressFamily f) => Amount[(int)f];
        public bool Any
        {
            get { if (Master <= 0f) return false; foreach (var a in Amount) if (a > 0f) return true; return false; }
        }
    }

    public sealed class DressProfile
    {
        public DressFamily[] Families = Array.Empty<DressFamily>();
        public DressStyle Style = DressStyle.LivedIn;
        /// <summary>Preset id → style (presets not listed use <see cref="Style"/>).</summary>
        public Dictionary<string, DressStyle> PresetStyles = new Dictionary<string, DressStyle>(StringComparer.Ordinal);
        public bool Has(DressFamily f) => Array.IndexOf(Families, f) >= 0;
    }

    public static class DressingProfiles
    {
        private static readonly Dictionary<string, DressProfile> Profiles = Build();

        private static DressProfile P(DressStyle style, params DressFamily[] f) => new DressProfile { Style = style, Families = f };

        private static DressProfile With(this DressProfile p, params (string preset, DressStyle style)[] presets)
        {
            foreach (var (id, s) in presets) p.PresetStyles[id] = s;
            return p;
        }

        private static Dictionary<string, DressProfile> Build()
        {
            const DressStyle C = DressStyle.Clean, L = DressStyle.LivedIn, G = DressStyle.Gritty;
            const DressFamily Tr = DressFamily.Trees, Pa = DressFamily.Paths, Pv = DressFamily.Paved, Pk = DressFamily.Parking, Pl = DressFamily.Play,
                Sp = DressFamily.Sports, Gr = DressFamily.Ground, Sh = DressFamily.Shore, Wk = DressFamily.Works, Ru = DressFamily.Ruins, Ga = DressFamily.Garden;
            var d = new Dictionary<string, DressProfile>(StringComparer.Ordinal)
            {
                // Parks and gardens
                ["park"] = P(L, Tr, Pa, Pl, Gr, Ga, Sh, Pv, Sp),
                ["pond"] = P(L, Sh, Tr, Pa),
                ["campground"] = P(L, Tr, Pa, Gr, Pl, Sh),
                ["plaza"] = P(L, Pv, Tr, Pa, Pl, Ga),
                ["botanicalGarden"] = P(C, Ga, Tr, Pa, Pv, Sh),
                ["communityGarden"] = P(L, Ga, Pa, Gr, Tr),
                ["dogPark"] = P(L, Gr, Pa, Tr, Pl),
                ["cemeteryGrounds"] = P(C, Tr, Pa, Ga),
                ["greenwayPark"] = P(L, Tr, Pa, Gr, Pl, Sh),
                ["skatePark"] = P(G, Pv, Pa, Tr, Sp),
                ["formalGarden"] = P(C, Ga, Pa, Tr, Pv, Sh),
                ["marketSquare"] = P(L, Pv, Tr, Pa, Pk),
                ["natureReserve"] = P(L, Tr, Pa, Gr, Sh),
                ["picnicGrounds"] = P(L, Pl, Tr, Pa, Gr, Sh),
                ["sportsPark"] = P(L, Sp, Pa, Tr, Pl, Pk, Gr),
                ["adventurePlayground"] = P(L, Pl, Tr, Pa, Gr),
                ["amphitheatre"] = P(L, Pv, Pa, Tr, Gr),
                ["animalPark"] = P(L, Gr, Pa, Tr, Wk, Pk),
                ["beach"] = P(L, Sh, Gr, Pa, Pl, Pv, Pk),
                ["fairground"] = P(L, Gr, Pv, Pa, Wk, Pl),
                ["golf"] = P(C, Tr, Pa, Sh, Pk),
                ["hikingTrail"] = P(L, Pa, Tr, Gr, Sh),
                ["lookoutHill"] = P(L, Pa, Tr, Gr),
                ["memorialPark"] = P(C, Tr, Pa, Pv, Ga),
                ["orchardPark"] = P(L, Tr, Pa, Gr, Pl),
                ["restArea"] = P(L, Pk, Pv, Tr, Pa, Pl),
                ["transitPlaza"] = P(L, Pv, Tr, Pa, Pk),
                ["waterfront"] = P(L, Sh, Pv, Pa, Tr),
                ["wetland"] = P(L, Sh, Tr, Pa, Gr),
                ["zenGarden"] = P(C, Ga, Pa, Tr),
                ["urbanFarm"] = P(L, Ga, Wk, Pa, Gr),
                // Lots, commerce
                ["parking"] = P(L, Pk, Tr, Pa),
                ["stripMall"] = P(G, Pk, Pv, Tr, Pa, Gr, Ga),   // (shops4) gritty, ground and garden wear too: the user saw no decals
                ["shoppingPlaza"] = P(L, Pv, Pk, Tr, Pa),
                ["outdoorMall"] = P(L, Pv, Tr, Pa, Pk),
                ["shopsPark"] = P(L, Pv, Tr, Pa, Gr, Pl),
                // Work sites and decay
                ["abandonedLot"] = P(G, Ru, Gr, Tr, Pa, Wk),
                ["urbanRuins"] = P(G, Ru, Pv, Pa, Tr, Gr),
                ["constructionSite"] = P(G, Wk, Gr, Pk),
                ["landfill"] = P(G, Wk, Ru, Gr),
                ["logisticsYard"] = P(G, Wk, Pk, Pv),
                ["quarry"] = P(G, Wk, Gr),
                ["scrapyard"] = P(G, Wk, Ru, Gr),
                ["farmstead"] = P(L, Wk, Gr, Tr, Pa, Ga),
            };
            // Preset styles: the name promises a look.
            d["abandonedLot"].With(("demolished", L), ("fencedWasteland", L));
            d["park"].With(("formal", C), ("garden", C), ("victorian", C), ("modern", C), ("urban", G), ("pocket", L));
            d["plaza"].With(("civic", C), ("podium", C), ("garden", C), ("market", L), ("cafe", L), ("grungy", G));   // grungy: the plazas makeover
            d["parking"].With(("green", C));
            d["constructionSite"].With(("compound", L));
            d["farmstead"].With(("hobby", C));
            d["landfill"].With(("capped", L), ("compost", L));
            d["memorialPark"].With(("war", C), ("reflection", C));
            d["beach"].With(("resort", C), ("rocky", L), ("river", L));
            d["sportsPark"].With(("stadium", C), ("tennisClub", C), ("neighbourhood", L));
            return d;
        }

        public static DressProfile For(string layerId) => layerId != null && Profiles.TryGetValue(layerId, out var p) ? p : null;

        public static DressStyle StyleFor(string layerId, string presetId)
        {
            var p = For(layerId);
            if (p == null) return DressStyle.LivedIn;
            return presetId != null && p.PresetStyles.TryGetValue(presetId, out var s) ? s : p.Style;
        }

        public static IEnumerable<string> LayerIds => Profiles.Keys;
    }

    public static class DressingControls
    {
        public const string AmountId = "dressAmount";
        public const string StyleId = "dressStyle";
        public const string PaintWearId = "dressPaintWear";
        public const string DesirePathsId = "dressDesirePaths";
        /// <summary>(s33) Lawn: "painted" (bare terrain + paint) or "surface" (a grass surface). Types with a lawn only.</summary>
        public const string LawnId = "dressLawn";
        public const string MulchId = "dressMulch";
        public const string LawnTintId = "dressLawnTint";
        public const string ClearPaintId = "dressPaintClear";
        /// <summary>(s33) The "Ground paint" section (the terrain-texture controls).</summary>
        public const string PaintGroup = "groundPaint";
        public const string Group = "decals";
        public const string KeyPrefix = "Placemaker.Dress.";
        public static readonly string[] Styles = { "clean", "livedIn", "gritty" };

        public static string FamilyId(DressFamily f) => "dress" + f;

        /// <summary>Sections a family's slider joins when the type has one (first match wins), else the Decals section.</summary>
        private static readonly Dictionary<DressFamily, string[]> HostGroups = new Dictionary<DressFamily, string[]>
        {
            [DressFamily.Trees] = new[] { "planting", "trees", "greens" },
            [DressFamily.Paths] = new[] { "paths", "path", "trails", "promenade" },
            [DressFamily.Paved] = new[] { "paving", "surfaces", "ground" },
            [DressFamily.Parking] = new[] { "parking", "spaces" },
            [DressFamily.Play] = new[] { "play", "playground", "picnic", "furniture", "amenities" },
            [DressFamily.Sports] = new[] { "sports", "field", "fields", "pitches", "skate" },
            [DressFamily.Ground] = new[] { "ground", "terrain" },
            [DressFamily.Shore] = new[] { "shore", "water" },
            [DressFamily.Works] = new[] { "yard", "site", "yardLayout", "operations", "heaps" },
            [DressFamily.Ruins] = new[] { "ground", "rubble", "debris", "lot" },
            [DressFamily.Garden] = new[] { "beds", "parterres", "plots", "planting" },
        };

        /// <summary>
        /// The dressing controls of a fill type (empty when the type has no profile). <paramref name="hasGroup"/> tells
        /// whether the type already has a section of that id.
        /// </summary>
        public static List<ControlSchema> For(string layerId, Func<string, bool> hasGroup)
        {
            var list = new List<ControlSchema>();
            var p = DressingProfiles.For(layerId);
            if (p == null) return list;
            // (issue #2, 2026-10-19) No decals from decal packs any more: the Decals amount and the per-family decal sliders
            // are gone. The style (Clean / Lived-in / Gritty) still sets how worn the painted ground looks: it joined the
            // Ground paint section.
            var style = new ControlSchema
            {
                Id = StyleId, Kind = ControlKind.Choice, Tier = ControlTier.More, Group = PaintGroup, Default = Styles[(int)p.Style],
                LabelKey = KeyPrefix + StyleId, TooltipKey = KeyPrefix + StyleId + ".Tooltip",
            };
            foreach (var s in Styles)
                style.Options.Add(new ControlOption { Value = s, LabelKey = KeyPrefix + StyleId + "." + s, TooltipKey = KeyPrefix + StyleId + "." + s + ".Tooltip" });
            list.Add(style);
            // s33: one "Ground paint" section: the lawn choice (types with a lawn), the amount of everything painted into
            // the terrain texture as dressing (s28 wear + s33 mulch / lawn variation / type extras) and a few toggles.
            // Permanent, like all Placemaker paint.
            string lawnDefault = Layers.Common.Lawn.DefaultFor(layerId, null);
            if (lawnDefault != null)
            {
                var lawn = new ControlSchema
                {
                    Id = LawnId, Kind = ControlKind.Choice, Tier = ControlTier.More, Group = PaintGroup, Default = lawnDefault,
                    LabelKey = KeyPrefix + LawnId, TooltipKey = KeyPrefix + LawnId + ".Tooltip",
                };
                foreach (var v in new[] { Layers.Common.Lawn.Painted, Layers.Common.Lawn.Surface })
                    lawn.Options.Add(new ControlOption { Value = v, LabelKey = KeyPrefix + LawnId + "." + v, TooltipKey = KeyPrefix + LawnId + "." + v + ".Tooltip" });
                list.Add(lawn);
            }
            list.Add(Slider(PaintWearId, 100, PaintGroup));
            list.Add(PaintToggle(DesirePathsId, p.Style != DressStyle.Clean));
            list.Add(PaintToggle(MulchId, true));
            if (lawnDefault != null) list.Add(PaintToggle(LawnTintId, true));
            var clear = PaintToggle(ClearPaintId, false);
            clear.EnabledWhen = "";
            list.Add(clear);
            return list;
        }

        private static ControlSchema PaintToggle(string id, bool def) => new ControlSchema
        {
            Id = id, Kind = ControlKind.Toggle, Tier = ControlTier.More, Group = PaintGroup, Default = def,
            LabelKey = KeyPrefix + id, TooltipKey = KeyPrefix + id + ".Tooltip",
            EnabledWhen = PaintWearId + "!=0", DisabledReasonKey = KeyPrefix + DesirePathsId + ".Off",
        };

        private static ControlSchema Slider(string id, double def, string group) => new ControlSchema
        {
            Id = id, Kind = ControlKind.Slider, Tier = ControlTier.More, Group = group, Default = def, Min = 0, Max = 300, Step = 10, Unit = "%",
            LabelKey = KeyPrefix + id, TooltipKey = KeyPrefix + id + ".Tooltip",
        };

        public static bool IsDressingId(string id) => id != null && id.StartsWith("dress", StringComparison.Ordinal);

        public static DressStyle ParseStyle(string s, DressStyle fallback)
        {
            int i = Array.IndexOf(Styles, s ?? "");
            return i >= 0 ? (DressStyle)i : fallback;
        }

        /// <summary>
        /// Effective settings of a step: the generator's dressing defaults overlaid with the step's values.
        /// Null when the generator has no dressing controls (roads, splits, add-ons inherit their parent's).
        /// </summary>
        public static DressSettings Read(IAreaGenerator gen, SettingsBag values)
        {
            if (gen == null) return null;
            var p = DressingProfiles.For(gen.Id);
            if (p == null) return null;
            bool has = false;
            if (gen.Controls != null) foreach (var c in gen.Controls) if (c != null && c.Id == StyleId) { has = true; break; }
            if (!has) return null;
            var s = new DressSettings { LayerId = gen.Id };
            s.Master = Pct(values, AmountId);
            s.Style = ParseStyle(values?.S(StyleId, null), p.Style);
            foreach (var f in p.Families) s.Amount[(int)f] = Pct(values, FamilyId(f));
            s.PaintWear = Pct(values, PaintWearId);
            s.DesirePaths = values == null ? p.Style != DressStyle.Clean : values.B(DesirePathsId, p.Style != DressStyle.Clean);
            s.Mulch = values == null || values.B(MulchId, true);
            s.LawnTint = values == null || values.B(LawnTintId, true);
            s.ClearFirst = values != null && values.B(ClearPaintId, false);
            s.Weathering = Dynamic.Weathering.Wants(gen.Id, values);
            s.Grime = Dynamic.Weathering.Amount(values);
            string lawn = Layers.Common.Lawn.DefaultFor(gen.Id, null);
            s.LawnPainted = lawn != null && (values?.S(LawnId, lawn) ?? lawn) == Layers.Common.Lawn.Painted;
            // The park's older "Grass surface" toggle off also leaves a bare lawn.
            if (lawn != null && !s.LawnPainted && values != null && values.Values.ContainsKey(Layers.Common.Lawn.LegacyToggle)
                && !values.B(Layers.Common.Lawn.LegacyToggle, true)) s.LawnPainted = true;
            return s;
        }

        private static float Pct(SettingsBag values, string id)
        {
            double v = values == null ? 100 : values.Num(id, 100);
            if (double.IsNaN(v)) v = 100;
            return (float)Math.Max(0, Math.Min(300, v)) / 100f;
        }
    }
}
