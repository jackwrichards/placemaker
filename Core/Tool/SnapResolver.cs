// Placemaker — snap candidate scoring for draw mode, engine-free.
//
// Port of ParkingLotTool's ParkingLotSnapping.cs (KruemelGames, GPL-3.0), which itself mirrors
// how vanilla CS2 tools rank snaps: every candidate carries a level (areas > nets/objects >
// directions/guides/grid) and a closeness score; lines are remembered so that the
// intersection of two snap lines becomes its own candidate with double weight — that is what
// makes "at the curb AND at a right angle" corners win. The game layer feeds candidates from
// the ECS (road curbs, building sides, other areas, zone cells); direction and guide candidates
// come from the outline itself and are computed here.

using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Tool
{
    public enum SnapKind { None = 0, RoadEdge = 1, ObjectSide = 2, AreaEdge = 3, Direction = 4, Guide = 5, ZoneGrid = 6, Crossing = 7, RoadNode = 8 }

    public struct SnapResult
    {
        public float3 Position;
        public SnapKind Kind;
        public float2 Direction;
        public bool HasGuide;
        public float3 GuideA, GuideB;
        /// <summary>(Area overhaul) Kind RoadNode: the road node snapped to (packed entity).</summary>
        public long Node;
    }

    public sealed class SnapResolver
    {
        public const float LevelDirection = 0f, LevelGuide = 0f, LevelZoneGrid = 0f, LevelNet = 1f, LevelObject = 1f, LevelArea = 2f;
        private const float WeightSingle = 1f, WeightCrossing = 2f;

        /// <summary>Capture radius in metres (PLT: 8 m, same as vanilla area tool).</summary>
        public float SnapDistance = 8f;
        public float GuideLength = 40f;

        private struct Candidate
        {
            public float3 Position;
            public float2 Direction;
            public float2 Priority;
            public SnapKind Kind;
            public bool HasGuide;
            public float2 GuideDirection;
        }

        private struct Line
        {
            public float3 Position;
            public float2 Direction;
            public float Level;
            public bool IsGuide;
        }

        private readonly List<Line> _lines = new List<Line>();
        private Candidate _best;
        private float3 _raw;

        public float3 Raw => _raw;

        public void Begin(float3 raw)
        {
            _raw = raw;
            _lines.Clear();
            _best = new Candidate { Position = raw, Priority = new float2(float.MinValue, float.MinValue), Kind = SnapKind.None };
        }

        /// <summary>
        /// Offer a snap onto a line (curb, building side, area edge…): <paramref name="position"/> is
        /// the closest point on the line to the cursor, <paramref name="direction"/> its unit direction.
        /// </summary>
        public void Offer(float level, SnapKind kind, float3 position, float2 direction, bool isGuide = false)
        {
            if (!math.all(math.isfinite(position)) || math.lengthsq(direction) < 0.5f) return;
            Consider(level, WeightSingle, new Candidate
            {
                Position = position, Direction = direction, Kind = kind, HasGuide = isGuide, GuideDirection = direction,
            });
            var line = new Line { Position = position, Direction = direction, Level = level, IsGuide = isGuide };
            for (int i = 0; i < _lines.Count; i++)
            {
                var other = _lines[i];
                if (math.abs(math.dot(line.Direction, other.Direction)) > 0.999999f) continue;
                if (!Intersect(line.Position.xz, line.Direction, other.Position.xz, other.Direction, out float tLine, out float tOther)) continue;
                bool leading = line.Level >= other.Level;
                var source = leading ? line : other;
                var pos = source.Position;
                pos.xz += source.Direction * (leading ? tLine : tOther);
                Consider(math.max(line.Level, other.Level), WeightCrossing, new Candidate
                {
                    Position = pos,
                    Direction = source.Direction,
                    Kind = SnapKind.Crossing,
                    HasGuide = line.IsGuide || other.IsGuide,
                    GuideDirection = line.IsGuide ? line.Direction : other.Direction,
                });
            }
            _lines.Add(line);
        }

        /// <summary>Offer the line through <paramref name="origin"/> along <paramref name="direction"/>, if the cursor is within reach.</summary>
        public void OfferLine(float level, SnapKind kind, float3 origin, float2 direction, bool isGuide = false)
        {
            if (math.lengthsq(direction) < 0.5f) return;
            float t = math.dot(_raw.xz - origin.xz, direction);
            float2 foot = origin.xz + direction * t;
            if (math.distance(foot, _raw.xz) >= SnapDistance) return;
            Offer(level, kind, new float3(foot.x, origin.y, foot.y), t < 0 ? -direction : direction, isGuide);
        }

        /// <summary>Offer a segment a→b (clamped to its ends).</summary>
        public void OfferSegment(float level, SnapKind kind, float3 a, float3 b)
        {
            var d = b.xz - a.xz;
            float len = math.length(d);
            if (len < 0.1f) return;
            d /= len;
            float t = math.clamp(math.dot(_raw.xz - a.xz, d), 0, len);
            var foot = math.lerp(a, b, t / len);
            if (math.distance(foot.xz, _raw.xz) >= SnapDistance) return;
            Offer(level, kind, foot, d);
        }

        /// <summary>
        /// Right-angle / straight-continuation snaps from the outline (PLT CollectDirections):
        /// while drawing, from the last corner along its stored axis and along the last edge (and
        /// perpendicular to both); while dragging corner <paramref name="dragIndex"/>, from both
        /// neighbours along their outer edges.
        /// </summary>
        public void OfferDirections(IReadOnlyList<float3> points, IReadOnlyList<float2> axes, int dragIndex, bool closed)
        {
            int count = points.Count;
            if (count == 0) return;
            float bestDistance = float.MaxValue;
            float2 position = _raw.xz;
            float2 direction = float2.zero;
            float height = _raw.y;
            if (dragIndex >= 0)
            {
                if (count < 3) return;
                int prev = (dragIndex - 1 + count) % count, prev2 = (dragIndex - 2 + count) % count;
                int next = (dragIndex + 1) % count, next2 = (dragIndex + 2) % count;
                if (closed || dragIndex > 0)
                    DirectionSnap(ref bestDistance, ref position, ref direction, points[prev].xz, math.normalizesafe(points[prev2].xz - points[prev].xz));
                if (closed || dragIndex < count - 1)
                    DirectionSnap(ref bestDistance, ref position, ref direction, points[next].xz, math.normalizesafe(points[next2].xz - points[next].xz));
            }
            else
            {
                var anchor = points[count - 1].xz;
                if (axes != null && axes.Count == count) DirectionSnap(ref bestDistance, ref position, ref direction, anchor, axes[count - 1]);
                if (count >= 2) DirectionSnap(ref bestDistance, ref position, ref direction, anchor, math.normalizesafe(points[count - 2].xz - anchor));
            }
            if (math.lengthsq(direction) < 0.5f) return;
            Offer(LevelDirection, SnapKind.Direction, new float3(position.x, height, position.y), direction, true);
        }

        /// <summary>Guide lines through earlier corners along their axes / edges (PLT CollectGuideLines).</summary>
        public void OfferGuides(IReadOnlyList<float3> points, IReadOnlyList<float2> axes, int dragIndex)
        {
            int count = points.Count;
            if (count < 2) return;
            for (int i = 0; i < count; i++)
            {
                if (i == dragIndex) continue;
                if (dragIndex < 0 && i == count - 1) continue;
                float bestDistance = float.MaxValue;
                float2 position = _raw.xz;
                float2 direction = float2.zero;
                var anchor = points[i].xz;
                if (axes != null && axes.Count == count) DirectionSnap(ref bestDistance, ref position, ref direction, anchor, axes[i]);
                DirectionSnap(ref bestDistance, ref position, ref direction, anchor, math.normalizesafe(points[(i + 1) % count].xz - anchor));
                if (math.lengthsq(direction) < 0.5f) continue;
                Offer(LevelGuide, SnapKind.Guide, new float3(position.x, _raw.y, position.y), direction, true);
            }
        }

        private void DirectionSnap(ref float bestDistance, ref float2 resultPosition, ref float2 resultDirection, float2 origin, float2 axis)
        {
            if (math.lengthsq(axis) < 0.5f) return;
            float2 across = Right(axis);
            float alongT = math.dot(_raw.xz - origin, axis);
            float sideT = math.dot(_raw.xz - origin, across);
            float alongDistance = math.abs(sideT);   // distance to the line through origin along axis
            float sideDistance = math.abs(alongT);   // distance to the perpendicular line
            if (alongDistance < bestDistance)
            {
                bestDistance = alongDistance;
                if (alongDistance < SnapDistance)
                {
                    resultDirection = alongT < 0f ? -axis : axis;
                    resultPosition = origin + axis * alongT;
                }
            }
            if (sideDistance < bestDistance)
            {
                bestDistance = sideDistance;
                if (sideDistance < SnapDistance)
                {
                    resultDirection = sideT < 0f ? -across : across;
                    resultPosition = origin + across * sideT;
                }
            }
        }

        public SnapResult Result()
        {
            var r = new SnapResult { Position = _raw, Kind = SnapKind.None };
            if (_best.Kind == SnapKind.None) return r;
            r.Position = _best.Position;
            r.Kind = _best.Kind;
            r.Direction = _best.Direction;
            if (_best.HasGuide)
            {
                r.HasGuide = true;
                r.GuideA = _best.Position; r.GuideA.xz -= _best.GuideDirection * GuideLength;
                r.GuideB = _best.Position; r.GuideB.xz += _best.GuideDirection * GuideLength;
            }
            return r;
        }

        private void Consider(float level, float weight, Candidate c)
        {
            // Crossings of two long lines can lie far from the cursor; never jump there.
            if (math.distance(c.Position.xz, _raw.xz) > SnapDistance * 1.5f) return;
            c.Priority = Priority(level, weight, _raw, c.Position, c.Direction);
            if (Better(c.Priority, _best.Priority)) _best = c;
        }

        private float2 Priority(float level, float weight, float3 hit, float3 snapped, float2 direction)
        {
            var delta = snapped - hit;
            var offset = new float2(math.dot(delta.xz, direction), math.dot(delta.xz, Right(direction))) / SnapDistance;
            offset *= offset;
            float sum = math.min(1f, offset.x + offset.y);
            float worst = math.max(offset.x, offset.y) + math.min(offset.x, offset.y) * 0.001f;
            return new float2(level, weight * (2f - sum - worst));
        }

        private static bool Better(float2 candidate, float2 current) =>
            candidate.x > current.x || (candidate.y > current.y && candidate.x == current.x);

        /// <summary>Right-hand perpendicular (Colossal MathUtils.Right convention).</summary>
        public static float2 Right(float2 v) => new float2(v.y, -v.x);

        private static bool Intersect(float2 p, float2 d, float2 q, float2 e, out float tp, out float tq)
        {
            float den = d.x * e.y - d.y * e.x;
            if (math.abs(den) < 1e-9f) { tp = tq = 0; return false; }
            var w = q - p;
            tp = (w.x * e.y - w.y * e.x) / den;
            tq = (w.x * d.y - w.y * d.x) / den;
            return math.isfinite(tp) && math.isfinite(tq);
        }
    }
}
