// Placemaker neighbourhood polish — looks and zone looks in the game (GPL-3.0).
//
// The looks (Core/Neighbours/PolishLooks): the built-in ones and the player's own, kept in ModsData/Placemaker/
// polish-looks.json for every city, with the player's changes to built-in looks (Save on a built-in look changes what that
// look is everywhere it is used; Back to default drops the changes). A polished lot keeps its look by id (PolishedLot.Look, a hash) and the changes it was
// polished with that the look does not have (PolishLookOverride); the planner gets every knob per house. Zone looks: each
// zone type of the game (mods' zones too) gets a look or Off for new buildings and Polish the whole city; a zone without
// its own pick follows its kind (homes, shops, offices, industry) when the kind has a look picked, else gets its own
// built-in look (low houses Houses, towers Towers, shops Shops…: PolishLooks.DefaultZoneLook). Any look on any zone: the
// look decides what the land becomes (a garden, a car park yard, paving), not the building.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Prefabs;
using Placemaker.Core.Neighbours;
using Placemaker.Core.Tool;
using Placemaker.Game.Settings;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourPolishSystem
    {
        // ───────────── the looks ─────────────

        private static List<PolishLook> s_custom;
        /// <summary>The player's changes to built-in looks, by id.</summary>
        private static Dictionary<string, PolishLook> s_edits;
        private static Dictionary<int, PolishLook> s_byHash;
        /// <summary>Bumped when a look is saved, renamed or deleted (the panel redraws).</summary>
        public static int LooksVersion { get; private set; }
        private static readonly Dictionary<int, string> s_knobOfHash = PolishLooks.Defaults.Keys.Concat(new[] { "theme" }).ToDictionary(k => PolishLooks.Hash(k), k => k);

        private static string LooksPath => Path.Combine(ModPaths.DataFolder, "polish-looks.json");

        private static void EnsureLooks()
        {
            if (s_custom != null) return;
            s_edits = new Dictionary<string, PolishLook>();
            try
            {
                string json = File.Exists(LooksPath) ? File.ReadAllText(LooksPath) : "";
                s_custom = PolishLooks.Read(json);
                foreach (var e in PolishLooks.ReadEdits(json)) s_edits[e.Id] = e;
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker polish: looks not read: " + e.Message); s_custom = new List<PolishLook>(); }
            s_byHash = null;
        }

        /// <summary>Every look: the built-in ones (as the player changed them), then the player's own.</summary>
        public static List<PolishLook> Looks
        {
            get
            {
                EnsureLooks();
                return PolishLooks.BuiltIn.Select(b => s_edits.TryGetValue(b.Id, out var e) ? e : b).Concat(s_custom).ToList();
            }
        }

        public static PolishLook LookById(string id) => string.IsNullOrEmpty(id) ? null : Looks.FirstOrDefault(l => l.Id == id);

        public static PolishLook LookByHash(int hash)
        {
            if (s_byHash == null)
            {
                s_byHash = new Dictionary<int, PolishLook>();
                foreach (var l in Looks) s_byHash[PolishLooks.Hash(l.Id)] = l;
            }
            return s_byHash.TryGetValue(hash, out var look) ? look : null;
        }

        private static void PersistLooks()
        {
            try { File.WriteAllText(LooksPath, PolishLooks.Write(s_custom, s_edits.Values)); }
            catch (Exception e) { Mod.Log.Warn("Placemaker polish: looks not saved: " + e.Message); }
            s_byHash = null;
            LooksVersion++;
        }

        /// <summary>The player's own look saved: a new one (<paramref name="id"/> null) or over one of theirs. Returns its id.</summary>
        public static string SaveLook(string id, string name, string theme, IReadOnlyDictionary<string, float> values)
        {
            EnsureLooks();
            var builtIn = id == null ? null : PolishLooks.BuiltIn.FirstOrDefault(l => l.Id == id);
            if (builtIn != null)
            {
                // a built-in look changed: what that look is from now on, wherever it is used (the same as the built-in: no edit)
                var edit = new PolishLook { Id = id, Name = builtIn.Name, Edited = true, Theme = Array.IndexOf(PolishLooks.Themes, theme) >= 0 ? theme : builtIn.Theme, Knobs = PolishLooks.Diff(values, PolishLooks.Defaults), Color = s_edits.TryGetValue(id, out var was) ? was.Color : "" };
                bool same = edit.Theme == builtIn.Theme && string.IsNullOrEmpty(edit.Color) && PolishLooks.Diff(edit.Values(), builtIn.Values()).Count == 0;
                if (same) s_edits.Remove(id); else s_edits[id] = edit;
                SettingsChanged = true;
                PersistLooks();
                RedoLook(id);
                Mod.Log.Info($"Placemaker polish: built-in look {id} {(same ? "back as it ships" : $"changed ({PolishLooks.Diff(edit.Values(), builtIn.Values()).Count} settings)")}");
                return id;
            }
            var look = id == null ? null : s_custom.FirstOrDefault(l => l.Id == id);
            if (look == null)
            {
                string baseId = "my-" + new string((name ?? "look").ToLowerInvariant().Where(char.IsLetterOrDigit).Take(24).ToArray());
                string newId = baseId;
                for (int k = 2; Looks.Any(l => l.Id == newId); k++) newId = baseId + "-" + k;
                // a new look takes the first colour no look has yet
                var used = new HashSet<string>(Looks.Select(PolishLooks.ColorOf));
                look = new PolishLook { Id = newId, Custom = true, Color = PolishLooks.Swatches.FirstOrDefault(c => !used.Contains(c)) ?? PolishLooks.Swatches[s_custom.Count % PolishLooks.Swatches.Length] };
                s_custom.Add(look);
            }
            if (!string.IsNullOrWhiteSpace(name)) look.Name = name.Trim();
            look.Theme = Array.IndexOf(PolishLooks.Themes, theme) >= 0 ? theme : "garden";
            look.Knobs = PolishLooks.Diff(values, PolishLooks.Defaults);
            SettingsChanged = true;
            PersistLooks();
            RedoLook(look.Id);
            Mod.Log.Info($"Placemaker polish: look '{look.Name}' ({look.Id}) saved, {look.Knobs.Count} settings of its own");
            return look.Id;
        }

        /// <summary>Outgrow types saved (or put back as they ship) since the last frame, by hash: every building outgrown with
        /// one is outgrown again with its new settings (LotsUpdate), first in the line, a patch of town at a time. Saving
        /// promised it ("they change to the new settings right away"): before, nothing was planned again and the player had
        /// to erase and paint the buildings to see the change.</summary>
        private static readonly HashSet<int> s_redoLooks = new HashSet<int>();

        private static void RedoLook(string id)
        {
            if (!string.IsNullOrEmpty(id)) s_redoLooks.Add(PolishLooks.Hash(id));
        }

        /// <summary>The buildings outgrown with a type just saved: planned again (their cached description dropped, so the plan
        /// reads the type's new settings), before other waiting work.</summary>
        private void RedoSavedLooks()
        {
            int n = 0;
            using (var all = _flaggedLots.ToEntityArray(Allocator.Temp))
            using (var flags = _flaggedLots.ToComponentDataArray<PolishedLot>(Allocator.Temp))
                for (int i = 0; i < all.Length; i++)
                {
                    if (!flags[i].Yes || !s_redoLooks.Contains(flags[i].Look)) continue;
                    _described.Remove(all[i]);
                    if (_dirty.Add(all[i])) DirtyBy[8]++;
                    _first.Add(all[i]);
                    n++;
                }
            Mod.Log.Info($"Placemaker neighbourhood polish: {s_redoLooks.Count} outgrow type(s) saved, {n} buildings outgrown with them again{(FeatureEnabled && !Paused ? "" : " (Outgrow is paused: once it runs)")}.");
            s_redoLooks.Clear();
        }

        /// <summary>A look's colour on Global outgrow's map and charts (a built-in look: kept with the player's changes to it).</summary>
        public static bool SetLookColor(string id, string color)
        {
            EnsureLooks();
            if (string.IsNullOrEmpty(color) || color.Length != 7 || color[0] != '#') return false;
            var custom = s_custom.FirstOrDefault(l => l.Id == id);
            if (custom != null) custom.Color = color;
            else
            {
                var builtIn = PolishLooks.BuiltIn.FirstOrDefault(l => l.Id == id);
                if (builtIn == null) return false;
                if (!s_edits.TryGetValue(id, out var edit))
                    s_edits[id] = edit = new PolishLook { Id = id, Name = builtIn.Name, Edited = true, Theme = builtIn.Theme, Knobs = new Dictionary<string, float>(builtIn.Knobs) };
                edit.Color = color;
            }
            PersistLooks();
            return true;
        }

        /// <summary>A built-in look back as it ships (the player's changes to it dropped).</summary>
        public static bool ResetLook(string id)
        {
            EnsureLooks();
            if (!s_edits.Remove(id)) return false;
            SettingsChanged = true;
            PersistLooks();
            RedoLook(id);
            Mod.Log.Info($"Placemaker polish: built-in look {id} back as it ships");
            return true;
        }

        public static bool RenameLook(string id, string name)
        {
            EnsureLooks();
            var look = s_custom.FirstOrDefault(l => l.Id == id);
            if (look == null || string.IsNullOrWhiteSpace(name)) return false;
            look.Name = name.Trim();
            PersistLooks();
            return true;
        }

        public static bool DeleteLook(string id)
        {
            EnsureLooks();
            if (s_custom.RemoveAll(l => l.Id == id) == 0) return false;
            // zones that used it follow their kind again
            foreach (var z in s_zoneLooks.Where(kv => kv.Value == id).Select(kv => kv.Key).ToList()) s_zoneLooks.Remove(z);
            PersistLooks();
            return true;
        }

        // each look's values (and how far its gardens reach), worked out once per version of the looks and shared by every lot
        // with that look and no changes of its own (read only: the planner never writes them). A dictionary of every knob made
        // for every house of every pass was most of a pass's time on the frame
        private static readonly Dictionary<PolishLook, (Dictionary<string, float> Values, float Reach)> s_lookValues = new Dictionary<PolishLook, (Dictionary<string, float>, float)>();
        private static int s_lookValuesVersion = -1;

        private static (Dictionary<string, float> Values, float Reach) ValuesOf(PolishLook look)
        {
            if (s_lookValuesVersion != LooksVersion) { s_lookValues.Clear(); s_lookValuesVersion = LooksVersion; }
            if (!s_lookValues.TryGetValue(look, out var have))
            {
                var values = look.Values();
                s_lookValues[look] = have = (values, ReachOf(values));
            }
            return have;
        }

        /// <summary>The biggest reach setting among these values (PolishLooks.ReachKnobs).</summary>
        private static float ReachOf(IReadOnlyDictionary<string, float> values)
        {
            float r = 0f;
            foreach (var k in PolishLooks.ReachKnobs) if (values.TryGetValue(k, out var v)) r = Math.Max(r, v);
            return r;
        }

        private static PolishLook LookOfLot(in PolishedLot f) =>
            LookByHash(f.Look) ?? LookById(f.Theme > 0 && f.Theme < ThemeNames.Length ? ThemeNames[f.Theme] : "garden") ?? PolishLooks.BuiltIn[0];

        /// <summary>A look's values for a lot: the look's, then the changes it was polished with. Its theme too.</summary>
        private Dictionary<string, float> LookValues(Entity lot, in PolishedLot f, out string theme)
        {
            var look = LookOfLot(f);
            var values = ValuesOf(look).Values;
            theme = look.Theme;
            if (EntityManager.HasBuffer<PolishLookOverride>(lot))
            {
                var changes = EntityManager.GetBuffer<PolishLookOverride>(lot, true);
                if (changes.Length > 0) values = new Dictionary<string, float>(values);
                foreach (var o in changes)
                {
                    if (!s_knobOfHash.TryGetValue(o.Knob, out var knob)) continue;
                    if (knob == "theme") { int t = (int)Math.Round(o.Value); if (t >= 0 && t < PolishLooks.Themes.Length) theme = PolishLooks.Themes[t]; }
                    else values[knob] = o.Value;
                }
            }
            return values;
        }

        /// <summary>How far a lot's garden may reach: the biggest reach setting of its look with its own changes (a lot outgrown
        /// before looks: the settings').</summary>
        private float LotReach(Entity lot)
        {
            if (!EntityManager.HasComponent<PolishedLot>(lot)) return BaseReach();
            var f = EntityManager.GetComponentData<PolishedLot>(lot);
            if (f.Look == 0) return BaseReach();
            if (!EntityManager.HasBuffer<PolishLookOverride>(lot) || EntityManager.GetBuffer<PolishLookOverride>(lot, true).Length == 0) return ValuesOf(LookOfLot(f)).Reach;
            return ReachOf(LookValues(lot, f, out _));
        }

        /// <summary>The reach of a lot with no look of its own: the settings', else the looks' defaults.</summary>
        private static float BaseReach()
        {
            float r = 0f;
            foreach (var k in PolishLooks.ReachKnobs) r = Math.Max(r, Settings.Extra != null && Settings.Extra.TryGetValue(k, out var v) ? v : PolishLooks.Defaults[k]);
            return r;
        }

        /// <summary>The furthest any garden may reach (every look, the tool's values): how far around something built to look
        /// for gardens it may change.</summary>
        private static float MostReach()
        {
            float r = Math.Max(ReachInUse, BaseReach());
            foreach (var look in Looks) r = Math.Max(r, ValuesOf(look).Reach);
            return r;
        }


        /// <summary>The planner's options and theme for a lot: its look (a lot polished before looks: its parts and theme).</summary>
        private (Dictionary<string, float> Options, string Theme) PlannerLook(Entity lot, in PolishedLot f, string zone)
        {
            if (f.Look != 0) { var v = LookValues(lot, f, out var theme); return (v, theme); }
            return (OptionsOf(f), ThemeFor(lot, zone));
        }

        /// <summary>The lot's look and changes written (a yes stamp).</summary>
        private void WriteLook(Entity lot, int look, IReadOnlyDictionary<string, float> overrides)
        {
            bool has = EntityManager.HasBuffer<PolishLookOverride>(lot);
            if (overrides == null || overrides.Count == 0) { if (has) EntityManager.GetBuffer<PolishLookOverride>(lot).Clear(); return; }
            var buf = has ? EntityManager.GetBuffer<PolishLookOverride>(lot) : EntityManager.AddBuffer<PolishLookOverride>(lot);
            buf.Clear();
            foreach (var kv in overrides) buf.Add(new PolishLookOverride { Knob = PolishLooks.Hash(kv.Key), Value = kv.Value });
        }

        // ───────────── zone looks ─────────────

        /// <summary>Zone prefab name → look id or "off" (the player's picks; a zone not here follows its kind).</summary>
        private static readonly Dictionary<string, string> s_zoneLooks = new Dictionary<string, string>();

        /// <summary>Settings: the zone looks (JSON object zone → look).</summary>
        public static void SetZoneLooks(string json)
        {
            s_zoneLooks.Clear();
            if (string.IsNullOrWhiteSpace(json)) return;
            try
            {
                foreach (var kv in JsonValue.Parse(json).Properties)
                    if (kv.Value.Kind == JsonKind.String && !string.IsNullOrEmpty(kv.Value.AsString())) s_zoneLooks[kv.Key] = kv.Value.AsString();
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker polish: zone looks not read: " + e.Message); }
        }

        public static string ZoneLooksJson()
        {
            var o = JsonValue.Obj();
            foreach (var kv in s_zoneLooks.OrderBy(k => k.Key)) o.Set(kv.Key, kv.Value);
            return o.ToString();
        }

        /// <summary>A zone's look: the player's pick, else its kind's (a look picked for every zone of the kind, or Off), else the
        /// zone type's own built-in look.</summary>
        public static string ZoneLook(string zone, string kind)
        {
            if (zone != null && s_zoneLooks.TryGetValue(zone, out var look) && (look == "off" || LookById(look) != null)) return look;
            var k = ZoneTheme(kind);
            if (k == "off" || k != KindAuto && LookById(k) != null) return k;
            return DefaultZoneLook(zone, kind);
        }

        /// <summary>The zone type's own built-in look (what it gets with no pick of its own nor of its kind).</summary>
        public static string DefaultZoneLook(string zone, string kind) => PolishLooks.DefaultZoneLook(ZoneKey(zone), kind);

        public static bool ZoneHasOwnLook(string zone) => zone != null && s_zoneLooks.ContainsKey(zone);

        public static void SetZoneLook(string zone, string look)
        {
            if (string.IsNullOrEmpty(zone)) return;
            if (string.IsNullOrEmpty(look)) s_zoneLooks.Remove(zone);
            else s_zoneLooks[zone] = look;
        }

        private readonly Dictionary<Entity, string> _zoneNameOf = new Dictionary<Entity, string>();

        /// <summary>The zone prefab's name a building prefab grows in ("" when none).</summary>
        private string ZoneName(Entity buildingPrefab)
        {
            if (_zoneNameOf.TryGetValue(buildingPrefab, out var n)) return n;
            n = "";
            if (EntityManager.HasComponent<SpawnableBuildingData>(buildingPrefab))
            {
                var zone = EntityManager.GetComponentData<SpawnableBuildingData>(buildingPrefab).m_ZonePrefab;
                if (zone != Entity.Null) n = NameOf(zone);
            }
            _zoneNameOf[buildingPrefab] = n;
            return n;
        }

        /// <summary>The look a building gets by its zone ("off": none).</summary>
        private string LookOfZone(Entity buildingPrefab) => ZoneLook(ZoneName(buildingPrefab), ZoneKind(buildingPrefab));

        private EntityQuery _zonePrefabs;
        private List<(string Id, string Kind, string Icon)> _zones;
        private int _zonesCount = -1;

        /// <summary>Every zone type in the game (mods' zones too): its prefab name and kind, homes first.</summary>
        public List<(string Id, string Kind, string Icon)> Zones()
        {
            if (_zonePrefabs == default) _zonePrefabs = GetEntityQuery(ComponentType.ReadOnly<ZoneData>(), ComponentType.ReadOnly<PrefabData>());
            int count = _zonePrefabs.CalculateEntityCount();
            if (_zones != null && count == _zonesCount) return _zones;
            _zonesCount = count;
            var list = new List<(string, string, string)>();
            using (var all = _zonePrefabs.ToEntityArray(Allocator.Temp))
                foreach (var z in all)
                {
                    var zd = EntityManager.GetComponentData<ZoneData>(z);
                    if (zd.m_AreaType == global::Game.Zones.AreaType.None) continue;
                    string kind = zd.m_AreaType == global::Game.Zones.AreaType.Residential ? "home" : zd.m_AreaType == global::Game.Zones.AreaType.Commercial ? "shop" : zd.IsOffice() ? "office" : "industry";
                    var name = NameOf(z);
                    if (string.IsNullOrEmpty(name) || list.Any(x => x.Item1 == name)) continue;
                    string icon = "";
                    try { icon = _prefabs.GetPrefab<PrefabBase>(z)?.GetComponent<UIObject>()?.m_Icon ?? ""; } catch (Exception) { }
                    list.Add((name, kind, icon));
                }
            string[] order = { "home", "shop", "office", "industry" };
            _zones = list.OrderBy(x => Array.IndexOf(order, x.Item2)).ThenBy(x => x.Item1, StringComparer.OrdinalIgnoreCase).ToList();
            return _zones;
        }
    }
}
