// AI builder (docs/AI-BUILDER.md): what the AI has placed so far, in world space. The agent's tools write it (on a worker),
// the "ai" layer (Core/Layers/Ai/AiLayer) reads a snapshot of it when the tool re-plans, so the AI's work goes through
// the same plan, ghost preview, site plan sketch, Review and Build as every other type.

using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace Placemaker.Core.Ai
{
    public enum AiPieceKind { Object = 0, Path = 1, Ground = 2, Paint = 3, Surface = 4 }

    public sealed class AiPiece
    {
        public int Id;
        public AiPieceKind Kind;
        /// <summary>Prefab name; "" = automatic for the role/category.</summary>
        public string Asset = "";
        public string Category;
        public ObjectRole Role = ObjectRole.Furniture;
        /// <summary>World xz (objects).</summary>
        public float2 Position;
        /// <summary>Radians around +Y, 0 = facing +Z (north).</summary>
        public float Yaw;
        public float Elevation;
        /// <summary>World xz polyline (paths).</summary>
        public float2[] Points;
        public NetRole NetRole = NetRole.Pathway;

        /// <summary>World xz polygon (ground shaping, paint, surfaces).</summary>
        public float2[] Ring;
        /// <summary>Ground: level / raise only / lower only, to <see cref="Height"/> (m above sea level), or a slope through
        /// <see cref="CornerHeights"/> (one per ring corner) when set.</summary>
        public TerraformKind Shape = TerraformKind.Level;
        public float Height;
        public float[] CornerHeights;
        /// <summary>Paint: the map's paint layer 1..4 (0 = erase), strength 0..1, soft edge and edge noise in metres.</summary>
        public int Layer = 1;
        public float Strength = 1f, Feather = 3f, Noise;
        public SurfaceRole SurfaceRole = SurfaceRole.Paving;

        public AiPiece Copy() => (AiPiece)MemberwiseClone();
    }

    public sealed class AiPlan
    {
        private readonly object _lock = new object();
        private readonly List<AiPiece> _pieces = new List<AiPiece>();
        private int _nextId = 1;
        private volatile int _revision;

        /// <summary>The plan the "ai" layer builds from (one at a time: one selection, one AI session).</summary>
        public static readonly AiPlan Current = new AiPlan();

        /// <summary>Bumped by every change; the tool re-plans when it moves.</summary>
        public int Revision => _revision;

        public int Add(AiPiece p)
        {
            lock (_lock)
            {
                p.Id = _nextId++;
                _pieces.Add(p);
                _revision++;
                return p.Id;
            }
        }

        public bool Remove(int id)
        {
            lock (_lock)
            {
                int n = _pieces.RemoveAll(p => p.Id == id);
                if (n > 0) _revision++;
                return n > 0;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                if (_pieces.Count == 0) return;
                _pieces.Clear();
                _revision++;
            }
        }

        public List<AiPiece> Snapshot()
        {
            lock (_lock) return _pieces.Select(p => p.Copy()).ToList();
        }

        public int Count { get { lock (_lock) return _pieces.Count; } }
    }
}
