// Placemaker — the zone grid drawn faint (setting "Zone grid", Options → Placemaker). GPL-3.0.
//
// User (in game, zoning): "The grids are way too prominent. Can we make them nearly 100% transparent or a slider", then
// "the transparency should work for filled grid as well. Not just empty grid." The game draws zone cells from two
// global colour arrays (colossal_ZoneFillColors / colossal_ZoneEdgeColors) that Game.Prefabs.ZoneSystem fills from each
// ZonePrefab's m_Color / m_Edge, with occupied / selected / highlight shades derived from their alpha. Its own
// opacities (UserState.zoneFillAlphas) cover Residential, Commercial, Industrial and Office only, never the empty cells
// (the unzoned prefab, AreaType.None, zone type 0). So we scale every zone prefab's colour alphas in memory (originals
// kept, the setting is absolute; the game's own opacities still multiply on top) and raise ZoneSystem's private
// m_UpdateColors so it rebuilds the arrays itself, as after a load. Saves reference prefabs by name: nothing of this
// reaches a save.

using System;
using System.Collections.Generic;
using System.Reflection;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Zones;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace Placemaker.Game.Overlay
{
    public sealed partial class ZoneGridOpacitySystem : GameSystemBase
    {
        /// <summary>Setting "Empty zone grid" in percent (100 = the game's own look).</summary>
        public static int OpacityPercent = 100;

        private PrefabSystem _prefabs;
        private ZoneSystem _zones;
        private EntityQuery _query;
        private FieldInfo _updateColors;
        private readonly Dictionary<ZonePrefab, (Color fill, Color edge)> _original = new Dictionary<ZonePrefab, (Color, Color)>();
        private int _applied = -1, _count = -1;
        private bool _failed;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _zones = World.GetOrCreateSystemManaged<ZoneSystem>();
            _query = GetEntityQuery(ComponentType.ReadOnly<ZoneData>(), ComponentType.ReadOnly<PrefabData>(), ComponentType.Exclude<Deleted>());
            _updateColors = typeof(ZoneSystem).GetField("m_UpdateColors", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_updateColors == null) Mod.Log.Warn("Placemaker zone grid: ZoneSystem.m_UpdateColors not found; the grid opacity applies after the next load only.");
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_failed) return;
            int pct = Math.Max(0, Math.Min(100, OpacityPercent));
            int count = _query.CalculateEntityCount();
            if (pct == _applied && count == _count) return;
            try
            {
                Apply(pct);
                _applied = pct;
                _count = count;
            }
            catch (Exception e)
            {
                _failed = true;
                Mod.Log.Warn("Placemaker zone grid: opacity not applied: " + e.Message);
            }
        }

        private void Apply(int pct)
        {
            float k = pct / 100f;
            int faded = 0;
            string empty = "";
            using (var entities = _query.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in entities)
                {
                    if (!_prefabs.TryGetPrefab(EntityManager.GetComponentData<PrefabData>(e), out ZonePrefab prefab)) continue;
                    if (!_original.TryGetValue(prefab, out var o))
                        _original[prefab] = o = (prefab.m_Color, prefab.m_Edge);
                    var fill = o.fill; fill.a *= k;
                    var edge = o.edge; edge.a *= k;
                    prefab.m_Color = fill;
                    prefab.m_Edge = edge;
                    faded++;
                    if (prefab.m_AreaType == AreaType.None)
                        empty += $" {prefab.name} fill {o.fill.a:0.###}→{fill.a:0.###}, lines {o.edge.a:0.###}→{edge.a:0.###};";
                }
            }
            if (faded == 0) return;   // zone prefabs not loaded yet (main menu): applied when they are
            _updateColors?.SetValue(_zones, true);
            Mod.Log.Info($"Placemaker zone grid: {faded} zone type(s) at {pct}%; empty cells:{empty}");
        }
    }
}
