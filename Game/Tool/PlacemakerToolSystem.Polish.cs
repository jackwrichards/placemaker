// Placemaker — the Zoning category's looks in the tool (GPL-3.0).
//
// A Zoning type (Core/Layers/Types/Zoning/PolishLookLayer: Garden, Natural, Tidy…) goes through Placemaker's steps like
// every type: the area picks the buildings (as the polish painter's: lots and buildings are not stops, so the area takes the
// houses in it), Design shows the look's rows and styles, Review counts the buildings. Build polishes them with the look and
// the values the rows have (saved on each building: the look and every value its style changed), and the selection stays,
// so it can be built again with another look or style.
// "Zone this area" on: the plan has the zone cells; Build polishes the buildings there now, leaves a look area behind (the
// buildings that grow in it get the look and the changes: NeighbourPolishSystem.Areas) and zones the cells as every build
// does (a loose build: ordinary city zoning, no group).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Layers.Types.Zoning;
using Placemaker.Core.Neighbours;
using Placemaker.Core.Tool;
using Placemaker.Game.Neighbours;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        /// <summary>The design is a Zoning type (a look of the area's buildings).</summary>
        public bool PolishType => _recipe != null && PolishLookLayer.Of(_recipe.Root.Layer) != null;
        private bool _wasPolishType;

        /// <summary>Every frame: a Zoning type picked or left, the area tools' stops change (lots and buildings are not stops
        /// for a look: the area takes the houses), and a picked area is fenced again with them.</summary>
        private void CheckPolishType()
        {
            bool now = PolishType;
            if (now == _wasPolishType) return;
            _wasPolishType = now;
            if (now && _painterRules == null) _painterRules = PainterRulesFrom(PlayerBrushRules);
            _fills?.Invalidate();
            _hoverFace = null;
            if (_mode == SelectionMode.Block) RepickBlocks();
            else if (_mode == SelectionMode.Draw && _draw.Closed) ShapeChanged();
            else if ((_mode == SelectionMode.Paint || _mode == SelectionMode.BlockPaint) && _shape != null) ShapeChanged();
            _countShape = null;
            Touch();
        }

        /// <summary>A setting of the design's root (the type's default when not set).</summary>
        private object PolishSetting(PolishLookLayer layer, string id)
        {
            if (_recipe.Root.Settings.Values.TryGetValue(id, out var v) && v != null) return v;
            return layer.Controls.FirstOrDefault(c => c.Id == id)?.Default;
        }

        private static float AsFloat(object v)
        {
            switch (v)
            {
                case bool b: return b ? 1f : 0f;
                case string str: return float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;
                case IConvertible c: return (float)c.ToDouble(CultureInfo.InvariantCulture);
                default: return 0f;
            }
        }

        /// <summary>The values the type's Outgrow rows have (its look's for any row not set).</summary>
        private Dictionary<string, float> PolishValues(PolishLookLayer layer)
        {
            var values = (NeighbourPolishSystem.LookById(layer.LookId) ?? PolishLooks.BuiltIn[0]).Values();
            var bag = _recipe.Root.Settings;
            foreach (var k in PolishLooks.Knobs)
                if (bag.Values.TryGetValue(k.Id, out var v) && v != null) values[k.Id] = AsFloat(v);
            return values;
        }

        /// <summary>The design's zones in use (share above 0): slot, the zone it zones (prefab name).</summary>
        private List<(int Slot, string Zone)> PolishZones(PolishLookLayer layer)
        {
            var list = new List<(int, string)>();
            for (int i = 0; i < 3; i++)
            {
                string sl = PolishLookLayer.SlotNames[i];
                if (AsFloat(PolishSetting(layer, "share" + sl)) <= 0f) continue;
                string asset = PolishSetting(layer, "zoneAsset" + sl) as string;
                string kind = PolishSetting(layer, "zone" + sl) as string ?? "";
                list.Add((i, !string.IsNullOrEmpty(asset) ? asset : Placemaker.Core.Layers.Common.ZoneKinds.PrefabId(kind)));
            }
            return list;
        }

        /// <summary>Each zone type's own Outgrow (zoneOutgrow): its switch, the look it takes (null: the type's) and its changes
        /// over that look (the type's settings when it takes the type's).</summary>
        private Dictionary<string, (bool On, string Look, Dictionary<string, float> Values)> ZoneOutgrow(PolishLookLayer layer)
        {
            var map = new Dictionary<string, (bool, string, Dictionary<string, float>)>();
            if (!(PolishSetting(layer, "zoneOutgrow") is string json) || string.IsNullOrWhiteSpace(json)) return map;
            try
            {
                foreach (var p in JObject.Parse(json).Properties())
                {
                    if (!(p.Value is JObject o)) continue;
                    bool on = o["on"] == null || (bool)o["on"];
                    var v = new Dictionary<string, float>();
                    if (o["v"] is JObject vo) foreach (var q in vo.Properties()) v[q.Name] = AsFloat(q.Value.Type == JTokenType.Boolean ? (object)(bool)q.Value : (q.Value.Type == JTokenType.String ? (object)(string)q.Value : (double)q.Value));
                    map[p.Name] = (on, o["look"]?.Type == JTokenType.String ? (string)o["look"] : null, v);
                }
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker zoning: the zones' Outgrow settings could not be read: " + e.Message); }
            return map;
        }

        /// <summary>Build for a Zoning type: the buildings in the area outgrow their lots, each zone type by its own switch and
        /// settings (the type's when it has none). False when the design is not a Zoning type, or zones the area too (then the
        /// normal build zones it).</summary>
        private bool BuildPolish()
        {
            var layer = _recipe == null ? null : PolishLookLayer.Of(_recipe.Root.Layer);
            if (layer == null) return false;
            var polish = World.GetExistingSystemManaged<NeighbourPolishSystem>();
            if (polish == null || _shape == null) return true;
            var look = (NeighbourPolishSystem.LookById(layer.LookId) ?? PolishLooks.BuiltIn[0]).Values();
            var baseValues = PolishValues(layer);
            var outgrow = ZoneOutgrow(layer);
            // a zone type's look (its own pick, else the type's) and its changes over that look
            (bool On, string Look, Dictionary<string, float> Changes) Of(string zoneKey)
            {
                var values = new Dictionary<string, float>(baseValues);
                var lookId = layer.LookId;
                var lookValues = look;
                bool on = true;
                if (zoneKey != "" && outgrow.TryGetValue(zoneKey, out var z))
                {
                    on = z.On;
                    var picked = string.IsNullOrEmpty(z.Look) ? null : NeighbourPolishSystem.LookById(z.Look);
                    if (picked != null) { lookId = picked.Id; lookValues = picked.Values(); values = new Dictionary<string, float>(lookValues); }
                    foreach (var kv in z.Values) values[kv.Key] = kv.Value;
                }
                if (on) NeighbourPolishSystem.NoteReach(values);
                return (on, lookId, PolishLooks.Diff(values, lookValues));
            }
            bool zoning = _plan != null && _plan.Layers.Any(l => l.Zoning.Count > 0);
            var lots = PainterLots(polish);
            // each building by its zone type
            var byKey = new Dictionary<string, List<global::Unity.Entities.Entity>>();
            foreach (var lot in lots)
            {
                string key = NeighbourPolishSystem.ZoneKey(polish.ZoneOfLot(lot));
                if (!byKey.TryGetValue(key, out var list)) byKey[key] = list = new List<global::Unity.Entities.Entity>();
                list.Add(lot);
            }
            int stamped = 0;
            var parts = new List<string>();
            foreach (var kv in byKey)
            {
                var (on, lookId, ch) = Of(kv.Key);
                if (!on) { parts.Add($"{kv.Value.Count} {(kv.Key == "" ? "unzoned" : kv.Key)} left as they are"); continue; }
                NeighbourPolishSystem.Stamp(kv.Value, true, lookId, ch.Count > 0 ? ch : null);
                stamped += kv.Value.Count;
                parts.Add($"{kv.Value.Count} {(kv.Key == "" ? "unzoned" : kv.Key)} as {lookId}{(ch.Count > 0 ? $" ({ch.Count} changes)" : "")}");
            }
            if (stamped == 0 && !zoning)
            {
                Flash(new LocText(lots.Count == 0 ? "Placemaker.Painter.NoLots" : "Placemaker.Polish.Nothing"));
                return true;
            }
            string perZone = string.Join(", ", parts);
            if (zoning)
            {
                // buildings that grow here later: their zone type's switch and settings (any other zone: the type's)
                int batch = NeighbourPolishSystem.NewLookBatch();
                var ring = _shape.Ring2D();
                foreach (var (_, zone) in PolishZones(layer))
                {
                    var (on, lookId, ch) = Of(NeighbourPolishSystem.ZoneKey(zone));
                    NeighbourPolishSystem.AddLookArea(ring, on ? lookId : NeighbourPolishSystem.LookOff, on && ch.Count > 0 ? ch : null, zone, batch);
                }
                var (_, _, baseCh) = Of("");
                NeighbourPolishSystem.AddLookArea(ring, layer.LookId, baseCh.Count > 0 ? baseCh : null, "", batch);
                int cells = _plan.Layers.Sum(l => l.Zoning.Count);
                Mod.Log.Info($"Placemaker zoning: {cells} zone cells to zone with {layer.LookId}; buildings now: {(perZone == "" ? "none" : perZone)} ({AreaM2:F0} m² picked with {SelectionModes.Id(_mode)})");
                return false;
            }
            LastStamp = (stamped, true);
            Mod.Log.Info($"Placemaker zoning: {stamped} buildings outgrown with {layer.LookId} by zone type: {perZone} ({AreaM2:F0} m² picked with {SelectionModes.Id(_mode)})");
            Flash(NeighbourPolishSystem.Paused ? new LocText("Placemaker.Painter.Paused") : new LocText("Placemaker.Polish.Built", stamped), 5f);
            SuiteNoteBuilt(0);
            _countShape = null;
            Touch();
            return true;
        }
    }
}
