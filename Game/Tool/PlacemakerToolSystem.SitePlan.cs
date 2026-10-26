// Placemaker — the site plan of the current plan (sketch study, Core/Build/SitePlan.cs): computed once per plan and read by
// the map overlay and the panel's top view, so both draw the same thing.

using System;
using System.Collections.Generic;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Tool;
using Unity.Mathematics;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        private ComposedPlan _sitePlanOf;
        private AreaShape _sitePlanShape;
        private int _sitePlanAssets = int.MinValue;
        private SitePlan _sitePlan;
        private int _sitePlanVersion;

        /// <summary>Changes whenever <see cref="SitePlan"/> is computed again.</summary>
        public int SitePlanVersion => _sitePlanVersion;

        /// <summary>The site plan of the current plan, null without a plan or an area.</summary>
        public SitePlan SitePlan
        {
            get
            {
                var plan = _plan;
                if (plan == null || _shape == null || _shape.Ring == null || _shape.Ring.Length < 3) return null;
                int assets = Services.Assets?.Version ?? -1;
                if (_sitePlan != null && ReferenceEquals(plan, _sitePlanOf) && ReferenceEquals(_shape, _sitePlanShape) && assets == _sitePlanAssets) return _sitePlan;
                _sitePlanOf = plan;
                _sitePlanShape = _shape;
                _sitePlanAssets = assets;
                IReadOnlyDictionary<string, AssetInfo> info = null;
                try { info = Services.Assets?.InfoMap(); } catch (Exception) { info = null; }
                var entrances = new List<float2>();
                if (_recipe != null)
                    foreach (var kv in RecipePaths.Walk(_recipe))
                        foreach (var m in kv.Value.Markers) entrances.Add(m.Position.xz);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    _sitePlan = SitePlan.Compute(new SitePlan.Input { Plan = plan, Outline = Poly.To2D(_shape.Ring), Assets = info, Entrances = entrances });
                }
                catch (Exception e)
                {
                    Mod.Log.Warn("Placemaker: site plan failed (" + e.Message + ")");
                    _sitePlan = new SitePlan();
                }
                if (sw.ElapsedMilliseconds > 20) Mod.Log.Info($"Placemaker site plan: computed in {sw.ElapsedMilliseconds} ms ({_sitePlan.Stalls.Count} stall lines, {_sitePlan.TreeCrowns.Count} crowns, {_sitePlan.Bushes.Count} bushes)");
                _sitePlanVersion++;
                return _sitePlan;
            }
        }
    }
}
