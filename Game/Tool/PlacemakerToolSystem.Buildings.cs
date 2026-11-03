// Building awareness for every selection mode (s15, playtest: "Paint ... isn't aware of buildings. It goes
// through buildings"). After each outline rebuild (never per frame, never during a brush stroke) the
// buildings whose lot the selection overlaps are read from the object search tree; the overlay shades
// them ("kept") and a hint says nothing is built on them. The build itself keeps off them in any mode
// (Core/Build/SiteSafety + BuildService.Protect). Plain Paint additionally cuts them out of its outline
// (PlacemakerToolSystem.Paint), Block paint never paints them (brush rule "buildings").

using System.Collections.Generic;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Tool;
using Placemaker.Game.Selection;
using Unity.Mathematics;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        private List<float3[]> _keptBuildings = new List<float3[]>();
        private int _keptBuildingCount;
        private int _keptVersion;
        private List<float3[]> _paintBuildings3 = new List<float3[]>();
        private int _paintBuildings3For = -1;

        /// <summary>Lots / footprints of existing buildings the current outline overlaps (kept; shaded by the overlay).</summary>
        public IReadOnlyList<float3[]> BuildingsInSelection => _keptBuildings;
        public int BuildingsInSelectionVersion => _keptVersion;
        /// <summary>Distinct buildings the outline overlaps.</summary>
        public int BuildingsInSelectionCount => _keptBuildingCount;

        /// <summary>Plain Paint: the buildings gathered around the stroke, with terrain heights (overlay while painting).</summary>
        public IReadOnlyList<float3[]> PaintBuildings3D
        {
            get
            {
                if (_paintBuildings3For != _paintBuildingsVersion)
                {
                    _paintBuildings3For = _paintBuildingsVersion;
                    var list = new List<float3[]>(_paintBuildings.Count);
                    foreach (var b in _paintBuildings) list.Add(Poly.To3D(b, p => _site.SampleHeight(p)));
                    _paintBuildings3 = list;
                }
                return _paintBuildings3;
            }
        }

        private void UpdateBuildingsInSelection()
        {
            var list = new List<float3[]>();
            int count = 0;
            if (_shape?.Ring != null && _shape.Ring.Length >= 3)
            {
                try
                {
                    var ring = _shape.Ring2D();
                    Poly.Bounds(ring, out var min, out var max);
                    _bpObstacles ??= new BlockObstacles(EntityManager, World);
                    var centres = new List<float2>();
                    foreach (var b in _bpObstacles.BuildingsIn(min - 1f, max + 1f, _editingGroup))
                    {
                        if (!ObstacleCut.Overlaps(ring, b)) continue;
                        list.Add(Poly.To3D(b, p => _site.SampleHeight(p)));
                        // A lot and its larger footprint are one building.
                        float2 c = Poly.Centroid(b);
                        bool dup = false;
                        foreach (var o in centres) if (math.distance(o, c) < 4f) { dup = true; break; }
                        if (!dup) { centres.Add(c); count++; }
                    }
                }
                catch (System.Exception e) { Mod.Log.Warn("Placemaker: could not check buildings under the selection: " + e.Message); }
            }
            if (list.Count == 0 && _keptBuildings.Count == 0) return;
            _keptBuildings = list;
            _keptBuildingCount = count;
            _keptVersion++;
        }

        private void AddBuildingHint(List<HintInfo> h)
        {
            if (_keptBuildingCount <= 0 || _shape == null || Painting || PolishType) return;
            if (_stage != ToolStage.Area && _stage != ToolStage.Design && _stage != ToolStage.Review) return;
            h.Add(new HintInfo("Placemaker.Hint.BuildingsKept") { Text = new LocText("Placemaker.Hint.BuildingsKept", _keptBuildingCount) });
        }
    }
}
