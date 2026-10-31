// Placemaker — asset catalog (GPL-3.0).
// Classification adapted from ParkManager's ParkAssetCatalogSystem (tomkpunkt, GPL-3.0),
// including its trick of reading vanilla park palettes from ParkData prefabs' sub-objects.
//
// Phase: UIUpdate. GameSimulation does not tick while paused, and the catalog must be ready in
// the editor-like pause the player uses to plan. Rescans when the prefab count changes (asset
// packs/mods load late) and after every game load.

using System;
using System.Collections.Generic;
using System.Text;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.UI;
using Placemaker.Core;
using Placemaker.Core.Build;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace Placemaker.Game.Assets
{
    public sealed partial class AssetCatalogSystem : GameSystemBase, IAssetCatalog
    {
        private const int RecheckFrames = 120;

        private PrefabSystem _prefabs;
        private int _findItFrames;
        private EntityQuery _prefabQuery;
        private int _lastPrefabCount = -1;
        private int _framesSinceCheck;
        private bool _scanRequested = true;
        private bool _ready;
        private int _version;

        private readonly Dictionary<string, List<CatalogItem>> _byCategory = new Dictionary<string, List<CatalogItem>>(StringComparer.Ordinal);
        private readonly Dictionary<string, CatalogItem> _byName = new Dictionary<string, CatalogItem>(StringComparer.Ordinal);
        private readonly Dictionary<string, Entity> _allPrefabsByName = new Dictionary<string, Entity>(StringComparer.Ordinal);
        private readonly Dictionary<string, Entity> _defaults = new Dictionary<string, Entity>(StringComparer.Ordinal);
        private Dictionary<string, AssetInfo> _infoMap = new Dictionary<string, AssetInfo>(StringComparer.Ordinal);
        private static readonly IReadOnlyList<CatalogItem> Empty = Array.Empty<CatalogItem>();

        public bool Ready => _ready;
        public int Version => _version;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _prefabQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PrefabData>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            Services.Assets = this;
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (ReferenceEquals(Services.Assets, this)) Services.Assets = null;
            base.OnDestroy();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            _scanRequested = true;
        }

        public void RequestRescan() => _scanRequested = true;

        /// <summary>
        /// (core) Prefab entities Placemaker registered itself (decal see-through copies, surface overlay copies, park and parking
        /// companions, the owner and zone-block lots). They never make the catalog rescan: teardown 09, a full synchronous Scan()
        /// of every prefab ran within 2 s of every first use of a decal tier (a 0.2-2 s freeze when switching styles or types),
        /// and its new version rebuilt the ghost a second time. They are never catalogue items either (ResolveName still finds
        /// them by name through the all-prefabs map; the clone systems resolve them directly).
        /// </summary>
        private static int s_ownPrefabs;

        /// <summary>
        /// PrefabSystem.AddPrefab for one of our own prefabs, counting the prefab entities it really created (PrefabData, not
        /// Deleted: the same query the rescan check counts), so the next check subtracts them.
        /// </summary>
        public static bool AddOwnPrefab(PrefabSystem prefabs, PrefabBase prefab)
        {
            EntityQuery q = default;
            int before = -1;
            try
            {
                q = prefabs.World.EntityManager.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<PrefabData>() },
                    None = new[] { ComponentType.ReadOnly<Deleted>() },
                });
                before = q.CalculateEntityCount();
            }
            catch (Exception) { before = -1; }
            // never in a menu: our helpers are placed by Placemaker only (the companions already did this; now every one)
            if (prefab.Has<UIObject>()) prefab.Remove<UIObject>();
            bool ok = prefabs.AddPrefab(prefab);
            if (ok) FindItCompat.Track(prefabs, prefab);
            // (P2-1) by name for plans that name one (the slow drive paths): a copy registered after the catalog's scan never
            // triggers a rescan (above), so the scan's name list does not have it
            if (ok) try { s_ownByName[prefab.name] = prefabs.GetEntity(prefab); } catch (Exception) { }
            try
            {
                if (before >= 0) s_ownPrefabs += math.max(0, q.CalculateEntityCount() - before);
                if (before >= 0) q.Dispose();
            }
            catch (Exception) { }
            return ok;
        }

        private static readonly Dictionary<string, Entity> s_ownByName = new Dictionary<string, Entity>(StringComparer.Ordinal);
        public static int OwnCount => s_ownByName.Count;

        /// <summary>Our own prefab (registered by Placemaker): never a catalogue item.</summary>
        public static bool IsOwnPrefabName(string name) => name != null && name.StartsWith("Placemaker ", StringComparison.Ordinal);

        [Preserve]
        protected override void OnUpdate()
        {
            if (_ready)
            {
                try { UpdateIcons(); MaybeWriteDump(false); }
                catch (Exception e) { Mod.Log.Warn("Placemaker icon update failed: " + e.Message); }
            }
            // Find It indexes a loaded game in its own OnGameLoaded: hide our helpers from it once that has run, and now and then
            if (++_findItFrames % 600 == 120) FindItCompat.Sweep(_prefabs);
            if (!_scanRequested && ++_framesSinceCheck < RecheckFrames) return;
            _framesSinceCheck = 0;
            // (core) Our own prefabs (decal and surface copies, companions, lots) never trigger a rescan.
            int count = _prefabQuery.CalculateEntityCount() - s_ownPrefabs;
            if (!_scanRequested && count == _lastPrefabCount) return;
            if (!_scanRequested && _lastPrefabCount >= 0)
                Mod.Log.Info($"Placemaker asset catalog: {count - _lastPrefabCount:+#;-#;0} prefab(s) from the game or other mods since the last scan; rescanning.");
            _scanRequested = false;
            _lastPrefabCount = count;
            try
            {
                Scan();
            }
            catch (Exception e)
            {
                Mod.Log.Error(e, "Placemaker asset scan failed; keeping the previous catalog");
            }
        }

        // ───────────────────────── IAssetCatalog ─────────────────────────

        public IReadOnlyList<CatalogItem> Items(string category) =>
            category != null && _byCategory.TryGetValue(category, out var list) ? list : Empty;

        public Entity Resolve(AssetRef asset, string category)
        {
            // s14: zone plans name the EU prefab; a city on another theme gets its own (NA …) when it exists.
            if (category == AssetCategories.Zone) asset = new AssetRef(Core.Layers.Common.ZoneKinds.ForTheme(asset.Id, ThemePrefix()), Fallbacks(asset, asset.Id));
            var e = ResolveName(asset.Id, category);
            if (e != Entity.Null) return e;
            if (asset.Fallbacks != null)
                foreach (var f in asset.Fallbacks)
                {
                    e = ResolveName(f, category);
                    if (e != Entity.Null) return e;
                }
            return DefaultFor(category);
        }

        private static string[] Fallbacks(AssetRef asset, string original)
        {
            var list = new List<string>();
            if (asset.Fallbacks != null) list.AddRange(asset.Fallbacks);
            list.Add(original);
            return list.ToArray();
        }

        /// <summary>City theme prefix ("EU" / "NA", ThemePrefab.assetPrefix); "" when unknown.</summary>
        private string ThemePrefix()
        {
            try
            {
                var config = World.GetExistingSystemManaged<global::Game.City.CityConfigurationSystem>();
                var theme = config != null ? config.defaultTheme : Entity.Null;
                if (theme != Entity.Null && _prefabs.TryGetPrefab(theme, out ThemePrefab prefab) && prefab != null) return prefab.assetPrefix ?? "";
            }
            catch (Exception) { }
            return "";
        }

        public Dictionary<string, AssetInfo> InfoMap() => new Dictionary<string, AssetInfo>(_infoMap, StringComparer.Ordinal);

        // ───────────────────────── automatic assets (US5) ─────────────────────────

        // FEEDBACK-2 #1: automatic picks come ONLY from items tagged "auto:<category>" (AssetHeuristics.
        // AutoEligible: no stadium seats, signs, advertising, construction props…), vanilla before
        // modded, and from one vanilla park palette per step (AutoAssets). Explicit picks may use any
        // item of the category (Items()).
        private const int AutoPoolSize = 12;
        private Dictionary<string, List<string>> _autoPools = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private Dictionary<string, Dictionary<string, List<string>>> _paletteItems = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
        private List<string> _palettes = new List<string>();
        private static readonly IReadOnlyList<string> NoNames = Array.Empty<string>();

        public int ResolveAutomatic(ComposedPlan plan) => ResolveAutomatic(plan, new AutoAssetSource
        {
            Pool = AutomaticPool, Exists = PrefabExists, Palettes = _palettes, PaletteItems = PaletteItems, Vanilla = IsVanillaName,
        });

        /// <summary>True for a built-in (vanilla) prefab of that name: Placemaker's own choices are vanilla (the user, 26 Oct).</summary>
        public bool IsVanillaName(string name) => !string.IsNullOrEmpty(name) && _vanillaNames.Contains(name);
        private HashSet<string> _vanillaNames = new HashSet<string>(StringComparer.Ordinal);

        // (perf) The preview planner resolves assets on its worker (30–60 ms of the game's thread per plan before): the same
        // resolver with "is this prefab loaded and alive" read from a set of names made here, once per catalog version.
        private HashSet<string> _usableNames;
        private int _usableVersion = -1;

        /// <summary>Main thread: a source the worker may use (pools, palettes and info are only read; prefab life from a snapshot).</summary>
        public AutoAssetSource WorkerSource()
        {
            if (!_ready) return null;
            if (_usableNames == null || _usableVersion != _version)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var kv in _allPrefabsByName) if (Usable(kv.Value)) names.Add(kv.Key);
                foreach (var kv in s_ownByName) if (Usable(kv.Value)) names.Add(kv.Key);   // (P2-1) our copies registered after the scan
                _usableNames = names;
                _usableVersion = _version;
            }
            var usable = _usableNames;
            var vanilla = _vanillaNames;
            return new AutoAssetSource
            {
                Pool = AutomaticPool, Exists = n => !string.IsNullOrEmpty(n) && usable.Contains(n), Palettes = _palettes, PaletteItems = PaletteItems,
                Vanilla = n => !string.IsNullOrEmpty(n) && vanilla.Contains(n),
            };
        }

        /// <summary>ResolveAutomatic with a given source (WorkerSource on the preview worker).</summary>
        public int ResolveAutomatic(ComposedPlan plan, AutoAssetSource source)
        {
            if (!_ready || plan == null || source == null) return 0;
            try
            {
                int resolved = AutoAssets.Resolve(plan, source);
                // FEEDBACK-4: fit objects with their real collision boxes once per plan, right here, so the
                // tool's preview/price, the ghost and the build (all call this resolver) see the same objects.
                if (!plan.Decluttered)
                {
                    // s21: decorative decals lie along their host (long axis = planned yaw) whatever pack resolved them.
                    Core.Decals.DecalFit.Orient(plan, _infoMap);
                    var report = Declutter.Run(plan, _infoMap);
                    if (report.TotalDropped > 0 || report.TotalMoved > 0)
                        Mod.Log.Info("Placemaker declutter: " + report);
                }
                return resolved;
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker could not resolve automatic assets: " + e.Message);
                return 0;
            }
        }

        /// <summary>
        /// Ordered automatic candidates of a category (eligible items only; vanilla first, then by
        /// score; fences: network fences first). The first item is the category default.
        /// </summary>
        public IReadOnlyList<string> AutomaticPool(string category)
        {
            if (category != null && _autoPools.TryGetValue(category, out var list)) return list;
            // Sub-categories fall back to their parent ("surface.grass" → "surface").
            int dot = category?.LastIndexOf('.') ?? -1;
            if (dot > 0 && category.StartsWith("surface", StringComparison.Ordinal)) return AutomaticPool(category.Substring(0, dot));
            return NoNames;
        }

        /// <summary>Eligible items of a category seen in a vanilla park palette (best first).</summary>
        public IReadOnlyList<string> PaletteItems(string palette, string category) =>
            palette != null && category != null && _paletteItems.TryGetValue(palette, out var byCat) && byCat.TryGetValue(category, out var list)
                ? list : NoNames;

        /// <summary>Sort key of an automatic candidate: lower tier first, then higher score, then name.</summary>
        // Ranking lives in Core (AutoPoolRanking) so the curation test replays exactly the game's order.
        private PoolCandidate Candidate(string category, CatalogItem it) =>
            new PoolCandidate(it.Id, IsVanilla(it),
                category == AssetCategories.Fence && EntityManager.HasComponent<NetData>(it.Prefab), it.Info.NetWidth,
                Math.Max(it.Info.BoundsMax.x - it.Info.BoundsMin.x, it.Info.BoundsMax.z - it.Info.BoundsMin.z));

        private int CompareAuto(string category, CatalogItem a, CatalogItem b) =>
            AutoPoolRanking.Compare(category, Candidate(category, a), Candidate(category, b));

        private static bool IsVanilla(CatalogItem it) => it.Tags != null && Array.IndexOf(it.Tags, "source:vanilla") >= 0;
        private static bool IsAutoFor(CatalogItem it, string category) => it.Tags != null && Array.IndexOf(it.Tags, "auto:" + category) >= 0;

        /// <summary>Builds the automatic pools, palette index and defaults from the classified items.</summary>
        private void BuildAutomatic(Dictionary<string, List<CatalogItem>> byCategory, Dictionary<string, Entity> defaults)
        {
            var pools = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var kv in byCategory)
            {
                var eligible = new List<CatalogItem>();
                foreach (var it in kv.Value) if (IsAutoFor(it, kv.Key)) eligible.Add(it);
                // A category with no eligible item keeps a default (layers may still name it), but no automatic pool. Defaults are
                // vanilla only: a category vanilla has nothing for has none, and a missing named asset is then left out.
                var ranked = new List<CatalogItem>();
                foreach (var it in eligible.Count > 0 ? eligible : kv.Value) if (IsVanilla(it)) ranked.Add(it);
                ranked.Sort((a, b) => CompareAuto(kv.Key, a, b));
                if (ranked.Count > 0) defaults[kv.Key] = ranked[0].Prefab;
                if (eligible.Count == 0) continue;
                // Vanilla only (AutoPoolRanking.Build).
                var cands = new List<PoolCandidate>(eligible.Count);
                foreach (var it in eligible) cands.Add(Candidate(kv.Key, it));
                pools[kv.Key] = AutoPoolRanking.Build(kv.Key, cands, AutoPoolSize);
            }

            // Palette index: palette → category → eligible items (same ranking).
            var palettes = new Dictionary<string, Dictionary<string, List<CatalogItem>>>(StringComparer.Ordinal);
            foreach (var kv in byCategory)
            {
                if (!AutoAssets.PaletteCategories.Contains(kv.Key)) continue;
                foreach (var it in kv.Value)
                {
                    if (!IsAutoFor(it, kv.Key) || it.Tags == null) continue;
                    foreach (var tag in it.Tags)
                    {
                        if (!tag.StartsWith("palette:", StringComparison.Ordinal)) continue;
                        if (!palettes.TryGetValue(tag, out var byCat)) palettes[tag] = byCat = new Dictionary<string, List<CatalogItem>>(StringComparer.Ordinal);
                        if (!byCat.TryGetValue(kv.Key, out var l)) byCat[kv.Key] = l = new List<CatalogItem>();
                        l.Add(it);
                    }
                }
            }
            var paletteItems = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
            var usable = new List<string>();
            foreach (var kv in palettes)
            {
                var byCat = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var c in kv.Value)
                {
                    c.Value.Sort((a, b) => CompareAuto(c.Key, a, b));
                    var names = new List<string>(c.Value.Count);
                    foreach (var it in c.Value) if (AssetCuration.FitsSize(c.Key, Candidate(c.Key, it).Footprint)) names.Add(it.Id);
                    byCat[c.Key] = names;
                }
                paletteItems[kv.Key] = byCat;
                // A palette worth dressing a park with: general (not a sports ground / skate park…) and
                // offering at least two of the everyday categories.
                int core = 0;
                foreach (var c in new[] { AssetCategories.Tree, AssetCategories.Bush, AssetCategories.Bench, AssetCategories.Lamp, AssetCategories.Bin })
                    if (byCat.ContainsKey(c)) core++;
                if (core >= 2 && AssetHeuristics.IsGeneralParkPalette(kv.Key.Substring("palette:".Length))) usable.Add(kv.Key);
            }
            usable.Sort(string.CompareOrdinal);

            _autoPools = pools;
            _paletteItems = paletteItems;
            _palettes = usable;
            LogAutomatic(byCategory);
        }

        private void LogAutomatic(Dictionary<string, List<CatalogItem>> byCategory)
        {
            var sb = new StringBuilder("Placemaker automatic pools (eligible/total: first picks):");
            var keys = new List<string>(byCategory.Keys);
            keys.Sort(string.CompareOrdinal);
            foreach (var c in keys)
            {
                int eligible = 0;
                foreach (var it in byCategory[c]) if (IsAutoFor(it, c)) eligible++;
                sb.Append("\n  ").Append(c).Append(' ').Append(eligible).Append('/').Append(byCategory[c].Count).Append(": ");
                if (_autoPools.TryGetValue(c, out var pool))
                    for (int i = 0; i < pool.Count && i < 4; i++) sb.Append(i == 0 ? "" : ", ").Append(pool[i]);
                else sb.Append("(none — explicit picks only)");
            }
            sb.Append("\n  palettes usable for automatic dressing: ").Append(_palettes.Count).Append(" of ").Append(_paletteItems.Count);
            for (int i = 0; i < _palettes.Count && i < 6; i++) sb.Append(i == 0 ? " (" : ", ").Append(_palettes[i]).Append(i == Math.Min(_palettes.Count, 6) - 1 ? ")" : "");
            Mod.Log.Info(sb.ToString());
        }

        // ───────────────────────── extra API for build/groups ─────────────────────────

        /// <summary>True when any loaded prefab has this name (missing-asset detection).</summary>
        public bool PrefabExists(string name) =>
            !string.IsNullOrEmpty(name) && (_allPrefabsByName.TryGetValue(name, out var e) && Usable(e) || s_ownByName.TryGetValue(name, out e) && Usable(e));

        public bool TryGetItem(string name, out CatalogItem item) => _byName.TryGetValue(name ?? "", out item);

        public string NameOf(Entity prefab)
        {
            if (prefab == Entity.Null) return "";
            try { return _prefabs.TryGetPrefab<PrefabBase>(prefab, out var p) && p != null ? p.name : ""; }
            catch (Exception) { return ""; }
        }

        public Entity DefaultFor(string category)
        {
            if (category == null) return Entity.Null;
            if (_defaults.TryGetValue(category, out var e) && Usable(e)) return e;
            // Sub-categories fall back to their parent ("surface.grass" → "surface").
            int dot = category.LastIndexOf('.');
            if (dot > 0 && category.StartsWith("surface", StringComparison.Ordinal))
                return DefaultFor(category.Substring(0, dot));
            return Entity.Null;
        }

        /// <summary>
        /// (AI builder, docs/AI-BUILDER.md) Every placeable static object the game has loaded (vanilla, DLC, mods), catalogued
        /// or not: props, trees, plants, decals, fountains... (no buildings, nets, areas, vehicles, placeholders), with its
        /// bounds. Main thread.
        /// </summary>
        public sealed class PlaceableObject
        {
            public string Name, DisplayName, Group, Icon;   // Group: the game's own menu group (UIObjectData) the asset is listed in, "" = none; Icon: its UIObject icon url
            public float3 Min, Max;
            public bool Tree, Plant, StreetLight, Decal, Animated;
        }

        public List<PlaceableObject> AllPlaceableObjects()
        {
            var list = new List<PlaceableObject>();
            foreach (var kv in _allPrefabsByName)
            {
                var e = kv.Value;
                if (!Usable(e) || !EntityManager.HasComponent<StaticObjectData>(e) || !EntityManager.HasComponent<ObjectGeometryData>(e)) continue;
                if (EntityManager.HasComponent<BuildingData>(e) || EntityManager.HasComponent<PlaceholderObjectData>(e) || EntityManager.HasComponent<NetData>(e)) continue;
                var g = EntityManager.GetComponentData<ObjectGeometryData>(e);
                string group = "";
                if (EntityManager.HasComponent<UIObjectData>(e))
                {
                    var ge = EntityManager.GetComponentData<UIObjectData>(e).m_Group;
                    if (ge != Entity.Null && _prefabs.TryGetPrefab<PrefabBase>(ge, out var gp) && gp != null) group = gp.name;
                }
                string display = kv.Key, icon = "";
                if (_prefabs.TryGetPrefab<PrefabBase>(e, out var pb) && pb != null)
                {
                    display = DisplayName(pb);
                    if (pb.TryGet<UIObject>(out var ui) && ui != null) icon = ui.m_Icon ?? "";
                }
                list.Add(new PlaceableObject
                {
                    Name = kv.Key, DisplayName = display, Group = group, Icon = icon, Min = g.m_Bounds.min, Max = g.m_Bounds.max,
                    Tree = EntityManager.HasComponent<TreeData>(e), Plant = EntityManager.HasComponent<PlantData>(e),
                    StreetLight = EntityManager.HasComponent<StreetLightData>(e),
                    Decal = kv.Key.IndexOf("decal", StringComparison.OrdinalIgnoreCase) >= 0 || (g.m_Bounds.max.y - g.m_Bounds.min.y) < 0.05f,
                    Animated = EntityManager.HasComponent<EffectData>(e),
                });
            }
            return list;
        }

        /// <summary>(AI builder) Every road and path line asset loaded (vanilla and mods; tracks, waterways, taxiways left out), with its width. Main thread.</summary>
        public List<(string name, string display, bool road, float width)> AllLineNets()
        {
            var list = new List<(string, string, bool, float)>();
            foreach (var kv in _allPrefabsByName)
            {
                var e = kv.Value;
                if (!Usable(e) || !EntityManager.HasComponent<NetData>(e) || !EntityManager.HasComponent<NetGeometryData>(e)) continue;
                bool road = EntityManager.HasComponent<RoadData>(e), path = EntityManager.HasComponent<PathwayData>(e);
                if (!road && !path) continue;
                if (EntityManager.HasComponent<TrackData>(e) || EntityManager.HasComponent<TaxiwayData>(e) || EntityManager.HasComponent<WaterwayData>(e)) continue;
                string display = kv.Key;
                if (_prefabs.TryGetPrefab<PrefabBase>(e, out var pb) && pb != null) display = DisplayName(pb);
                // a bridge, a pier or a quay is no line to draw on the ground
                if (EntityManager.HasComponent<BridgeData>(e)) continue;
                var geo = EntityManager.GetComponentData<NetGeometryData>(e);
                float width = geo.m_DefaultWidth > 0f ? geo.m_DefaultWidth : _byName.TryGetValue(kv.Key, out var item) ? item.Info.NetWidth : 0f;
                list.Add((kv.Key, display, road, width));
            }
            return list;
        }

        /// <summary>Maximum edge length of a net prefab (0 = unknown). CS2 rejects longer edges.</summary>
        public float MaxEdgeLength(Entity prefab)
        {
            if (prefab == Entity.Null || !EntityManager.HasComponent<NetGeometryData>(prefab)) return 0f;
            float max = EntityManager.GetComponentData<NetGeometryData>(prefab).m_EdgeLengthRange.max;
            return math.isfinite(max) && max > 1f ? max : 0f;
        }

        private Entity ResolveName(string name, string category)
        {
            if (string.IsNullOrEmpty(name)) return Entity.Null;
            if (_byName.TryGetValue(name, out var item) && Usable(item.Prefab)) return item.Prefab;
            // Not catalogued (e.g. a prefab a layer names explicitly that our heuristics skip):
            // still honour it when it exists and is of a usable kind for the category.
            if (_allPrefabsByName.TryGetValue(name, out var e) && Usable(e) && KindMatches(e, category)) return e;
            if (s_ownByName.TryGetValue(name, out e) && Usable(e) && KindMatches(e, category)) return e;
            return Entity.Null;
        }

        private bool KindMatches(Entity e, string category)
        {
            if (string.IsNullOrEmpty(category)) return true;
            if (category.StartsWith("surface", StringComparison.Ordinal) || category.StartsWith("area.", StringComparison.Ordinal))
                return EntityManager.HasComponent<AreaGeometryData>(e);
            if (category.StartsWith("net.", StringComparison.Ordinal))
                return EntityManager.HasComponent<NetData>(e);
            if (category == AssetCategories.Zone) return EntityManager.HasComponent<ZoneData>(e);
            if (category == AssetCategories.Fence) return EntityManager.HasComponent<NetData>(e) || EntityManager.HasComponent<ObjectGeometryData>(e);
            // Objects: must be a real, non-building static object (mesh-less static objects crash).
            return EntityManager.HasComponent<ObjectGeometryData>(e) && !EntityManager.HasComponent<BuildingData>(e)
                && !EntityManager.HasComponent<PlaceholderObjectData>(e);
        }

        private bool Usable(Entity e) => e != Entity.Null && EntityManager.Exists(e) && !EntityManager.HasComponent<Deleted>(e);

        // ───────────────────────── scan ─────────────────────────

        private void Scan()
        {
            var byCategory = new Dictionary<string, List<CatalogItem>>(StringComparer.Ordinal);
            var byName = new Dictionary<string, CatalogItem>(StringComparer.Ordinal);
            var allByName = new Dictionary<string, Entity>(StringComparer.Ordinal);
            var info = new Dictionary<string, AssetInfo>(StringComparer.Ordinal);
            var tags = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var iconSources = new Dictionary<string, string>(StringComparer.Ordinal);
            int failed = 0;
            using var all = _prefabQuery.ToEntityArray(Allocator.Temp);
            // Built-in prefabs first: a mod that reuses a vanilla name never wins a name lookup (duplicate names: first wins).
            var entities = new List<Entity>(all.Length);
            var later = new List<Entity>();
            var vanillaNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < all.Length; i++)
            {
                try
                {
                    if (_prefabs.TryGetPrefab<PrefabBase>(all[i], out var p) && p != null && p.isBuiltin) { entities.Add(all[i]); if (!string.IsNullOrWhiteSpace(p.name)) vanillaNames.Add(p.name); }
                    else later.Add(all[i]);
                }
                catch (Exception) { later.Add(all[i]); }
            }
            entities.AddRange(later);
            _vanillaNames = vanillaNames;

            for (int i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                // One broken prefab (half-loaded pack, missing DLC reference) must not empty every list (PM).
                try
                {
                    if (!_prefabs.TryGetPrefab<PrefabBase>(entity, out var prefab) || prefab == null || string.IsNullOrWhiteSpace(prefab.name)) continue;
                    if (!allByName.ContainsKey(prefab.name)) allByName[prefab.name] = entity;
                    if (IsOwnPrefabName(prefab.name)) continue;   // (core) our copies and lots are not pickable assets
                    var traits = Traits(entity, prefab);
                    var categories = AssetHeuristics.Classify(prefab.name, traits);
                    if (categories.Count == 0) continue;
                    if (byName.ContainsKey(prefab.name)) continue;   // duplicate names: first wins (saves resolve by name too)
                    var item = new CatalogItem
                    {
                        Id = prefab.name,
                        DisplayName = DisplayName(prefab),
                        Icon = ResolveIcon(prefab, entity, (traits & AssetTraits.Surface) != 0, iconSources),
                        Prefab = entity,
                        Info = Info(entity, prefab.name),
                    };
                    // The icon url names the prefab class for props made from vehicles ("coui://ail/CarPrefab.Tractor01.png").
                    traits |= AssetHeuristics.TraitsFromIcon(item.Icon);
                    var t = new List<string>(categories);
                    t.Add(prefab.isBuiltin ? "source:vanilla" : "source:mod");
                    // Automatic eligibility per category ("auto" = eligible for at least one).
                    bool anyAuto = false;
                    foreach (var c in categories)
                        if (AssetHeuristics.AutoEligible(c, prefab.name, traits)) { t.Add("auto:" + c); anyAuto = true; }
                    if (anyAuto) t.Add("auto");
                    // Decal picker tabs (UI): "theme:leaves", "theme:dirt", … from the themed sub-category.
                    foreach (var c in categories)
                        if (c.StartsWith(AssetCategories.Decal + ".", StringComparison.Ordinal)) t.Add("theme:" + c.Substring(AssetCategories.Decal.Length + 1));
                    tags[prefab.name] = t;
                    byName[prefab.name] = item;
                    info[prefab.name] = item.Info;
                    foreach (var c in categories)
                    {
                        if (!byCategory.TryGetValue(c, out var list)) byCategory[c] = list = new List<CatalogItem>();
                        list.Add(item);
                    }
                }
                catch (Exception e)
                {
                    if (failed++ < 5) Mod.Log.Warn($"Placemaker skipped prefab {entity} while scanning assets: {e.Message}");
                }
            }

            try { ScanParkPalettes(entities, byName, tags); }
            catch (Exception e) { Mod.Log.Warn("Placemaker could not read vanilla park palettes: " + e.Message); }

            foreach (var kv in byName) kv.Value.Tags = tags.TryGetValue(kv.Key, out var t) ? t.ToArray() : Array.Empty<string>();

            // Defaults = best automatic-eligible item (vanilla first; network fences before prop pieces, PM).
            var defaults = new Dictionary<string, Entity>(StringComparer.Ordinal);
            foreach (var kv in byCategory)
                kv.Value.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase));
            BuildAutomatic(byCategory, defaults);

            bool changed = !SameContent(byName);
            _byCategory.Clear(); foreach (var kv in byCategory) _byCategory[kv.Key] = kv.Value;
            _byName.Clear(); foreach (var kv in byName) _byName[kv.Key] = kv.Value;
            _allPrefabsByName.Clear(); foreach (var kv in allByName) _allPrefabsByName[kv.Key] = kv.Value;
            foreach (var kv in s_ownByName) if (!_allPrefabsByName.ContainsKey(kv.Key)) _allPrefabsByName[kv.Key] = kv.Value;   // (P2-1) our copies, also when registered after a scan
            _defaults.Clear(); foreach (var kv in defaults) _defaults[kv.Key] = kv.Value;
            _infoMap = info;
            if (changed || !_ready) _version++;
            _ready = true;
            PublishAvailability(tags);
            QueueIconWork(_byName, iconSources);
            MaybeWriteDump(true);

            var summary = new StringBuilder();
            foreach (var kv in _byCategory) summary.Append(kv.Key).Append('=').Append(kv.Value.Count).Append(' ');
            Mod.Log.Info($"Placemaker asset catalog v{_version}: {summary}({failed} skipped)");
            // (s26) Which lane roads carry zone blocks (shops must front one that does).
            {
                var zb = new System.Text.StringBuilder();
                foreach (var id in new[] { "Alley", "Pedestrian Street", "Pedestrian Street Small", "Small Road", "Gravel Road" })
                    if (_infoMap.TryGetValue(id, out var ri))
                        zb.Append(zb.Length > 0 ? ", " : "").Append(id).Append(ri.ZoneBlocks == 1 ? " yes" : ri.ZoneBlocks == 2 ? " NO" : " ?");
                if (zb.Length > 0) Mod.Log.Info("Placemaker zone blocks by road: " + zb + ".");
            }
        }

        /// <summary>
        /// Tells Core which categories have automatically usable items, so layers report "no … installed"
        /// (Info) and place nothing instead of planning objects the build would drop. Every declared
        /// category is published, 0 when empty.
        /// </summary>
        private void PublishAvailability(Dictionary<string, List<string>> tags)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var f in typeof(AssetCategories).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                if (f.IsLiteral && f.GetValue(null) is string c) counts[c] = 0;
            foreach (var kv in tags)
                foreach (var tag in kv.Value)
                    if (tag.StartsWith("auto:", StringComparison.Ordinal))
                    {
                        var c = tag.Substring(5);
                        counts[c] = (counts.TryGetValue(c, out var n) ? n : 0) + 1;
                    }
            Placemaker.Core.Layers.Common.AssetAvailability.Publish(counts);
        }

        private int _lastSignature;

        private bool SameContent(Dictionary<string, CatalogItem> byName)
        {
            unchecked
            {
                int sig = byName.Count;
                foreach (var kv in byName) sig = sig * 31 + kv.Key.GetHashCode() + kv.Value.Prefab.Index;
                bool same = sig == _lastSignature && _ready;
                _lastSignature = sig;
                return same;
            }
        }

        private AssetTraits Traits(Entity e, PrefabBase prefab)
        {
            var t = AssetTraits.None;
            if (EntityManager.HasComponent<PlaceholderObjectData>(e) || EntityManager.HasBuffer<PlaceholderObjectElement>(e)) t |= AssetTraits.Placeholder;
            if (ImageSystem.GetIcon(prefab) != null) t |= AssetTraits.HasIcon;

            if (EntityManager.HasComponent<ZoneData>(e)) return t | AssetTraits.Zone;

            if (EntityManager.HasComponent<AreaGeometryData>(e))
            {
                var type = EntityManager.GetComponentData<AreaGeometryData>(e).m_Type;
                if (type == global::Game.Areas.AreaType.Surface) t |= AssetTraits.Surface;
                else if (type == global::Game.Areas.AreaType.Space) t |= AssetTraits.SpaceArea;
                else if (type == global::Game.Areas.AreaType.Lot) t |= AssetTraits.LotArea;
                // Surfaces/areas need a valid archetype or the definition is silently dropped.
                if (EntityManager.HasComponent<AreaData>(e) && !EntityManager.GetComponentData<AreaData>(e).m_Archetype.Valid) t |= AssetTraits.Placeholder;
                return t;
            }

            if (EntityManager.HasComponent<NetData>(e))
            {
                t |= AssetTraits.Net;
                if (!EntityManager.HasComponent<NetGeometryData>(e)) t |= AssetTraits.Placeholder;
                if (EntityManager.HasComponent<FenceData>(e) || EntityManager.HasComponent<NetFenceData>(e)) t |= AssetTraits.NetFence;
                if (EntityManager.HasComponent<PathwayData>(e)) t |= AssetTraits.Pathway;
                if (EntityManager.HasComponent<RoadData>(e)) t |= AssetTraits.Road;
                return t;
            }

            if (!(prefab is StaticObjectPrefab) || !EntityManager.HasComponent<ObjectGeometryData>(e)) return t;
            t |= AssetTraits.StaticObject;
            if (prefab is BuildingPrefab || EntityManager.HasComponent<BuildingData>(e)) t |= AssetTraits.Building;
            if (prefab is ObjectGeometryPrefab g && g.m_Meshes != null && g.m_Meshes.Length > 0) t |= AssetTraits.HasMesh;
            if (IsDecalOnly(e))
            {
                t |= AssetTraits.DecalMesh;
                // s23: a decal whose projection mask lacks Terrain (EAI sets DecalProperties.m_LayerMask from the pack's
                // colossal_DecalLayerMask) never shows on the ground: building dressing, never an automatic ground decal.
                if (!ProjectsOnTerrain(prefab)) t |= AssetTraits.WallOrHanging;
            }
            if (EntityManager.HasComponent<PlantData>(e)) t |= AssetTraits.Plant;
            if (EntityManager.HasComponent<TreeData>(e)) t |= AssetTraits.Tree;
            if (EntityManager.HasComponent<StreetLightData>(e)) t |= AssetTraits.StreetLight;
            if (prefab.TryGet<ObjectSubObjects>(out var subs) && subs?.m_SubObjects != null && subs.m_SubObjects.Length > 0) t |= AssetTraits.HasSubObjects;

            if (prefab.isBuiltin) t |= AssetTraits.Vanilla;
            // Prefab kind (pool hygiene): building extensions / upgrades, vehicles, citizens' activity props.
            for (var type = prefab.GetType(); type != null && type != typeof(PrefabBase); type = type.BaseType)
                t |= AssetHeuristics.TraitsFromTypeName(type.Name);
            if (EntityManager.HasComponent<BrandObjectData>(e)) t |= AssetTraits.BrandObject;
            if (EntityManager.HasComponent<PlaceableObjectData>(e))
            {
                var flags = EntityManager.GetComponentData<PlaceableObjectData>(e).m_Flags;
                const global::Game.Objects.PlacementFlags netAttached = global::Game.Objects.PlacementFlags.RoadSide | global::Game.Objects.PlacementFlags.RoadNode
                    | global::Game.Objects.PlacementFlags.RoadEdge | global::Game.Objects.PlacementFlags.NetObject;
                const global::Game.Objects.PlacementFlags wall = global::Game.Objects.PlacementFlags.Wall | global::Game.Objects.PlacementFlags.Hanging;
                const global::Game.Objects.PlacementFlags water = global::Game.Objects.PlacementFlags.Shoreline | global::Game.Objects.PlacementFlags.Floating
                    | global::Game.Objects.PlacementFlags.Underwater | global::Game.Objects.PlacementFlags.Waterway;
                if ((flags & netAttached) != 0) t |= AssetTraits.NetAttached;
                if ((flags & wall) != 0) t |= AssetTraits.WallOrHanging;
                if ((flags & water) != 0) t |= AssetTraits.WaterPlacement;
            }

            if (EntityManager.HasBuffer<ActivityLocationElement>(e))
            {
                uint sitting = new ActivityMask(ActivityType.BenchSitting).m_Mask;
                uint exercise = 0;
                foreach (var a in new[] { ActivityType.PushUps, ActivityType.PullUps, ActivityType.SitUps, ActivityType.JumpingJacks, ActivityType.Squats })
                    exercise |= new ActivityMask(a).m_Mask;
                var locations = EntityManager.GetBuffer<ActivityLocationElement>(e, true);
                for (int i = 0; i < locations.Length; i++)
                {
                    if ((locations[i].m_ActivityMask.m_Mask & sitting) != 0) t |= AssetTraits.BenchActivity;
                    if ((locations[i].m_ActivityMask.m_Mask & exercise) != 0) t |= AssetTraits.ExerciseActivity;
                }
            }

            if (EntityManager.HasBuffer<SubLane>(e))
            {
                t |= AssetTraits.HasSubLane;
                var lanes = EntityManager.GetBuffer<SubLane>(e, true);
                for (int i = 0; i < lanes.Length; i++)
                {
                    var lane = lanes[i].m_Prefab;
                    if (lane != Entity.Null && EntityManager.Exists(lane) && EntityManager.HasComponent<ParkingLaneData>(lane))
                    { t |= AssetTraits.ParkingLaneSubLane; break; }
                }
            }
            return t;
        }

        /// <summary>
        /// A decal object: it has sub meshes and every one is a decal mesh. MeshSystem sets MeshFlags.Decal
        /// on a RenderPrefab's MeshData when the prefab has DecalProperties (decal material/projector), so
        /// this is the game's own signal, independent of names.
        /// </summary>
        private static bool ProjectsOnTerrain(PrefabBase prefab)
        {
            if (!(prefab is ObjectGeometryPrefab g) || g.m_Meshes == null) return true;
            foreach (var m in g.m_Meshes)
            {
                if (!(m?.m_Mesh is RenderPrefab r)) continue;
                var dp = r.GetComponent<DecalProperties>();
                if (dp != null && (dp.m_LayerMask & global::Game.Rendering.DecalLayers.Terrain) != 0) return true;
                if (dp == null) return true;
            }
            return false;
        }

        private bool IsDecalOnly(Entity e)
        {
            if (!EntityManager.HasBuffer<SubMesh>(e)) return false;
            var meshes = EntityManager.GetBuffer<SubMesh>(e, true);
            if (meshes.Length == 0) return false;
            for (int i = 0; i < meshes.Length; i++)
            {
                var m = meshes[i].m_SubMesh;
                if (m == Entity.Null || !EntityManager.HasComponent<MeshData>(m)) return false;
                if ((EntityManager.GetComponentData<MeshData>(m).m_State & MeshFlags.Decal) == 0) return false;
            }
            return true;
        }

        private AssetInfo Info(Entity e, string name)
        {
            var info = new AssetInfo { Id = name };
            if (EntityManager.HasComponent<ObjectGeometryData>(e))
            {
                var g = EntityManager.GetComponentData<ObjectGeometryData>(e);
                info.BoundsMin = g.m_Bounds.min;
                info.BoundsMax = g.m_Bounds.max;
            }
            if (EntityManager.HasComponent<PlaceableObjectData>(e))
                info.Cost = EntityManager.GetComponentData<PlaceableObjectData>(e).m_ConstructionCost;
            if (EntityManager.HasComponent<NetGeometryData>(e))
                info.NetWidth = EntityManager.GetComponentData<NetGeometryData>(e).m_DefaultWidth;
            // (s26) Does the road carry zone blocks (BlockSystem.CreateBlocks needs RoadData.m_ZoneBlockPrefab)?
            if (EntityManager.HasComponent<global::Game.Prefabs.RoadData>(e))
                info.ZoneBlocks = EntityManager.GetComponentData<global::Game.Prefabs.RoadData>(e).m_ZoneBlockPrefab != Entity.Null ? (byte)1 : (byte)2;
            if (EntityManager.HasComponent<PlaceableNetData>(e))
            {
                // Nets are charged per rounded 8 m cell, max(1, round(len/8)) (NetUtils.GetConstructionCost); the UI's
                // "per km" figure is cost × 125. AssetInfo wants per metre.
                var p = EntityManager.GetComponentData<PlaceableNetData>(e);
                info.Cost = p.m_DefaultConstructionCost / 8f;
                info.Upkeep = p.m_DefaultUpkeepCost / 8f;
            }
            return info;
        }

        private static string DisplayName(PrefabBase prefab)
        {
            try
            {
                var dict = global::Game.SceneFlow.GameManager.instance?.localizationManager?.activeDictionary;
                if (dict != null && dict.TryGetValue("Assets.NAME[" + prefab.name + "]", out var value) && !string.IsNullOrWhiteSpace(value))
                    return value;
            }
            catch (Exception) { }
            return prefab.name;
        }

        /// <summary>
        /// Walks the managed sub-objects of every ParkData prefab (vanilla parks) and tags each
        /// catalogued asset found there with "palette:&lt;park name&gt;". Layers use these to dress a
        /// park in a consistent vanilla style.
        /// </summary>
        private void ScanParkPalettes(IReadOnlyList<Entity> prefabs, Dictionary<string, CatalogItem> byName, Dictionary<string, List<string>> tags)
        {
            int palettes = 0;
            for (int i = 0; i < prefabs.Count; i++)
            {
                var entity = prefabs[i];
                if (!EntityManager.HasComponent<ParkData>(entity) || !_prefabs.TryGetPrefab<PrefabBase>(entity, out var park) || park == null) continue;
                if (!park.isBuiltin) continue;   // a modded park is no vanilla palette
                var tag = AssetHeuristics.PaletteTag(park.name);
                var visited = new HashSet<Entity>();
                int found = 0;
                CollectSubObjects(park, tag, visited, 0, byName, tags, ref found);
                if (found > 0) palettes++;
            }
            Mod.Log.Info($"Placemaker discovered {palettes} vanilla park palettes.");
        }

        private void CollectSubObjects(PrefabBase parent, string tag, HashSet<Entity> visited, int depth,
            Dictionary<string, CatalogItem> byName, Dictionary<string, List<string>> tags, ref int found)
        {
            if (parent == null || depth > 8 || !parent.TryGet<ObjectSubObjects>(out var component) || component?.m_SubObjects == null) return;
            foreach (var sub in component.m_SubObjects)
            {
                var child = sub?.m_Object;
                // GetEntity throws for prefabs the game never registered (content not installed).
                if (child == null || !_prefabs.TryGetEntity(child, out var childEntity)) continue;
                CollectPaletteObject(childEntity, tag, visited, depth + 1, byName, tags, ref found);
            }
        }

        private void CollectPaletteObject(Entity entity, string tag, HashSet<Entity> visited, int depth,
            Dictionary<string, CatalogItem> byName, Dictionary<string, List<string>> tags, ref int found)
        {
            if (entity == Entity.Null || depth > 8 || !visited.Add(entity) || !EntityManager.Exists(entity)) return;
            if (EntityManager.HasBuffer<PlaceholderObjectElement>(entity))
            {
                var variants = EntityManager.GetBuffer<PlaceholderObjectElement>(entity, true);
                var copy = new List<Entity>(variants.Length);
                for (int i = 0; i < variants.Length; i++) copy.Add(variants[i].m_Object);
                foreach (var v in copy) CollectPaletteObject(v, tag, visited, depth + 1, byName, tags, ref found);
            }
            if (!_prefabs.TryGetPrefab<PrefabBase>(entity, out var prefab) || prefab == null || !prefab.isBuiltin) return;
            if (byName.ContainsKey(prefab.name) && tags.TryGetValue(prefab.name, out var list) && !list.Contains(tag))
            {
                list.Add(tag);
                found++;
            }
            CollectSubObjects(prefab, tag, visited, depth, byName, tags, ref found);
        }
    }
}
