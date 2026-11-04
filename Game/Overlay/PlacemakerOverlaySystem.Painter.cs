// Placemaker — the neighbourhood-polish painter's tint: every polished lot green while "Polish gardens" is the pick, like
// the game's zoning overlay; nothing for lots left as the game made them. Gone when the painter (or the tool) closes.

using System.Collections.Generic;
using System.Linq;
using Placemaker.Game.Neighbours;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Overlay
{
    public sealed partial class PlacemakerOverlaySystem
    {
        private static readonly Color PolishedTint = new Color(0.47f, 0.88f, 0.33f, 0.34f);
        private readonly List<float3[]> _polished = new List<float3[]>();
        private int _polishedFlags = -1, _polishedFrame;
        private float2 _polishedAt = new float2(float.MaxValue);
        private long _polishedSig;

        /// <summary>For the game runner: polished lots the tint shows now.</summary>
        public static int PolishedShown { get; private set; }

        // Outgrow's status lens (NeighbourPolishSystem.LensRingsNear): outgrown, not, being planned now, just put in line, then
        // waiting by how long (the line's heat map: pale to red). Half as strong as it was ("50% more transparent"): the
        // brush's lit buildings (DrawOutgrowBrush) stay as strong, so what a stroke takes stands out
        private static readonly Color[] LensTints =
        {
            new Color(0.47f, 0.88f, 0.33f, 0.17f),   // outgrown
            new Color(0.75f, 0.78f, 0.82f, 0.07f),   // not outgrown
            new Color(0.22f, 0.53f, 0.95f, 0.3f),    // being planned now
            new Color(1f, 0.95f, 0.45f, 0.35f),      // just put in line
            new Color(1f, 0.78f, 0.42f, 0.21f),      // waiting under 10 s
            new Color(1f, 0.58f, 0.22f, 0.24f),      // under 30 s
            new Color(0.93f, 0.36f, 0.13f, 0.27f),   // under 90 s
            new Color(0.82f, 0.13f, 0.13f, 0.3f),    // longer
        };
        private readonly List<float3[]>[] _lens = System.Linq.Enumerable.Range(0, NeighbourPolishSystem.LensLayers).Select(_ => new List<float3[]>()).ToArray();
        private readonly long[] _lensSig = new long[NeighbourPolishSystem.LensLayers];
        private int _lensFrame;

        private readonly Dictionary<string, List<float3[]>> _types = new Dictionary<string, List<float3[]>>();
        private readonly Dictionary<string, long> _typeSig = new Dictionary<string, long>();
        // the lens's rings are read again only when something they show changed: the flags, the camera's spot, the line (the
        // status map's waiting colours, while there is work in line); else now and then
        private int _lensFlags = -1;
        private float2 _lensAt = new float2(float.MaxValue);
        private string _lensKind = "";

        private bool LensStale(string kind, float2 at, float radius, bool busy)
        {
            bool stale = kind != _lensKind || _lensFlags != NeighbourPolishSystem.FlagsVersion || math.distance(at, _lensAt) > radius * 0.15f
                || _lensFrame >= (busy ? 30 : 600);
            if (!stale) { _lensFrame++; return false; }
            _lensKind = kind;
            _lensFlags = NeighbourPolishSystem.FlagsVersion;
            _lensAt = at;
            _lensFrame = 0;
            return true;
        }

        /// <summary>The types map: every building its outgrow type's colour (not outgrown: faint grey).</summary>
        private void DrawOutgrowTypes()
        {
            var polish = World.GetExistingSystemManaged<NeighbourPolishSystem>();
            var cam = Camera.main;
            if (polish == null || cam == null) return;
            float3 cp = cam.transform.position;
            float3 fw = cam.transform.forward;
            float2 at = cp.xz + (fw.y < -0.05f ? fw.xz * math.min(cp.y / -fw.y, 3000f) : float2.zero);
            float radius = math.clamp(cp.y * 3f, 500f, 3000f);
            if (LensStale("types", at, radius, false))
            {
                polish.TypeRingsNear(at, radius * 1.3f, _types);
                foreach (var kv in _types) _typeSig[kv.Key] = FilledPreviewSystem.Signature(kv.Value) ^ kv.Key.GetHashCode();
            }
            foreach (var kv in _types)
            {
                if (kv.Value.Count == 0) continue;
                Color color = LensTints[NeighbourPolishSystem.LensNot];
                if (kv.Key != "-" && ColorUtility.TryParseHtmlString(PolishLooksColor(kv.Key), out var c)) color = new Color(c.r, c.g, c.b, 0.25f);
                _fill.SetLayer("outgrowType:" + kv.Key, kv.Value, color, -94.4f, _typeSig.TryGetValue(kv.Key, out var sig) ? sig : 0);
            }
        }

        private static string PolishLooksColor(string id) => Placemaker.Core.Neighbours.PolishLooks.ColorOf(NeighbourPolishSystem.LookById(id));

        private void DrawOutgrowLens()
        {
            if (_tool.OutgrowLensMode == "types") { DrawOutgrowTypes(); return; }
            var polish = World.GetExistingSystemManaged<NeighbourPolishSystem>();
            var cam = Camera.main;
            if (polish == null || cam == null) return;
            float3 cp = cam.transform.position;
            float3 fw = cam.transform.forward;
            float2 at = cp.xz + (fw.y < -0.05f ? fw.xz * math.min(cp.y / -fw.y, 3000f) : float2.zero);
            float radius = math.clamp(cp.y * 3f, 500f, 3000f);
            // twice a second while there is work in line (its waiting colours move), else when something changed
            if (LensStale("status", at, radius, polish.LensBusy))
            {
                polish.LensRingsNear(at, radius * 1.3f, _lens);
                for (int k = 0; k < _lens.Length; k++) _lensSig[k] = FilledPreviewSystem.Signature(_lens[k]) ^ (k * 7919L);
            }
            for (int k = 0; k < _lens.Length; k++)
                if (_lens[k].Count > 0) _fill.SetLayer("outgrowLens" + k, _lens[k], LensTints[k], -94.4f + k * 0.02f, _lensSig[k]);
        }

        private readonly List<float3[]> _brushLots = new List<float3[]>();
        private int _brushVersion = -1;
        private long _brushSig;

        /// <summary>Global outgrow's brush: its disc at the cursor and every building the stroke has touched so far, lit in
        /// the brush's outgrow type colour (erasing: white) until let go.</summary>
        private void DrawOutgrowBrush(global::Game.Rendering.OverlayRenderSystem.Buffer buffer, float w)
        {
            Color c = Color.white;
            // (the default-type brush, "zone": white, each building takes its own zone's type)
            if (_tool.OutgrowBrushPainting && _tool.OutgrowBrushLook != "zone" && ColorUtility.TryParseHtmlString(PolishLooksColor(_tool.OutgrowBrushLook), out var lc)) c = lc;
            if (_brushVersion != _tool.OutgrowBrushVersion)
            {
                _brushVersion = _tool.OutgrowBrushVersion;
                _brushLots.Clear();
                _brushLots.AddRange(_tool.OutgrowBrushRings);
                _brushSig = FilledPreviewSystem.Signature(_brushLots) ^ ((long)_brushVersion << 20);
            }
            if (_brushLots.Count > 0) _fill.SetLayer("outgrowBrush", _brushLots, new Color(c.r, c.g, c.b, 0.38f), -94.0f, _brushSig);
            if (_tool.CursorValid) buffer.DrawCircle(new Color(c.r, c.g, c.b, 0.95f), new Color(c.r, c.g, c.b, 0.12f), w * 0.6f, 0, new float2(0, 1), _tool.Cursor, _tool.OutgrowBrushWidth);
        }

        private void DrawPolishedLots()
        {
            var polish = World.GetExistingSystemManaged<NeighbourPolishSystem>();
            var cam = Camera.main;
            if (polish == null || cam == null) return;
            // around the ground point the camera looks at (as far as it can see well)
            float3 cp = cam.transform.position;
            float3 fw = cam.transform.forward;
            float2 at = cp.xz + (fw.y < -0.05f ? fw.xz * math.min(cp.y / -fw.y, 3000f) : float2.zero);
            float radius = math.clamp(cp.y * 3f, 500f, 3000f);
            if (_polishedFlags != NeighbourPolishSystem.FlagsVersion || math.distance(at, _polishedAt) > radius * 0.2f || ++_polishedFrame > 180)
            {
                _polishedFlags = NeighbourPolishSystem.FlagsVersion;
                _polishedAt = at;
                _polishedFrame = 0;
                polish.PolishedRingsNear(at, radius * 1.3f, _polished);
                _polishedSig = FilledPreviewSystem.Signature(_polished) ^ _polishedFlags;
                PolishedShown = _polished.Count;
            }
            if (_polished.Count > 0) _fill.SetLayer("polishedLots", _polished, PolishedTint, -94.2f, _polishedSig);
        }
    }
}
