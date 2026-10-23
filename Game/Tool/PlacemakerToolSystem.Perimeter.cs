// Perimeter mode (Grid Road Generator's node selection): click road nodes in order; the roads
// between two clicks are filled in with the shortest path (Dijkstra), clicking the first node again
// closes the loop. Right-click removes the last node. The area is the inside of the loop, at the
// curbs, exactly like block mode.
// s43 ("closable at any moment, it just bridges that gap"): from 3 nodes on, the open chain already gives an area:
// the gap back to the first node is closed along the roads when a short route exists (< 1.5x the straight
// distance, never back through the chain), else by a straight chord across open land (RoadGraphAlgorithms.Closing).
// The overlay shows that closing dashed; clicking the first node (or Next) uses exactly that closing.

using System.Collections.Generic;
using Game.Net;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Tool;
using Placemaker.Game.Selection;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        private const float MaxNodeSnap = 30f;
        private const int MaxPathVisited = 6000;

        private Entity _hoverNode;
        public Entity HoverNode => _hoverNode;
        private List<HalfEdge> _perimeterClosing;
        /// <summary>How the open chain closes right now (dashed in the overlay), or null.</summary>
        public IReadOnlyList<HalfEdge> PerimeterClosing => _perimeter.Closed ? null : _perimeterClosing;

        public float3 NodePositionOf(long node) => _roads.NodePosition(node);

        private List<HalfEdge> ComputePerimeterClosing()
        {
            if (_perimeter.Closed || _perimeter.Nodes.Count < 3) return null;
            try { return RoadGraphAlgorithms.Closing(_roads, _perimeter.Nodes, _perimeter.Path, n => _roads.NodePosition(n).xz, MaxPathVisited); }
            catch (System.Exception e) { Mod.Log.Warn("Placemaker: road nodes closing failed: " + e.Message); return null; }
        }
        public PerimeterSelectionState Perimeter => _perimeter;

        private void UpdatePerimeter()
        {
            _cursorValid = _in.HasHit;
            if (_in.HasHit) _cursor = _in.RawHit.m_Position;
            _hoverNode = ResolveHoveredNode();

            if (_in.LeftPressed)
            {
                if (_hoverNode == Entity.Null) { Flash(new LocText("Placemaker.Problem.ClickANode")); return; }
                if (_perimeter.Closed) { Flash(new LocText("Placemaker.Problem.PerimeterClosed")); return; }
                long id = EntityIds.Pack(_hoverNode);
                var pos = EntityManager.GetComponentData<Node>(_hoverNode).m_Position;
                if (_perimeter.Nodes.Count == 0)
                {
                    _perimeter.Nodes.Add(id);
                    _perimeter.Positions.Add(pos);
                    ShapeChanged();
                    Commit("Placemaker.Undo.AddNode");
                    return;
                }
                long last = _perimeter.Nodes[_perimeter.Nodes.Count - 1];
                if (id == last) return;
                if (id == _perimeter.Nodes[0] && _perimeter.Nodes.Count >= 3)
                {
                    // Close with the previewed closing (road route or straight chord): what you see is what you get.
                    var closeVia = ComputePerimeterClosing();
                    if (closeVia == null || closeVia.Count == 0) { Flash(new LocText("Placemaker.Problem.NoRoadPath")); return; }
                    _perimeter.Path.AddRange(closeVia);
                    _perimeter.Closed = true;
                    ShapeChanged();
                    Commit("Placemaker.Undo.ClosePerimeter");
                    RebuildShapeIfDirty(true);
                    if (_shape == null) Flash(new LocText("Placemaker.Problem.Crossing"));
                    else if (_stage == ToolStage.Area) SetStage(ToolStage.Design);
                    return;
                }
                var path = new List<HalfEdge>();
                if (!RoadGraphAlgorithms.ShortestPath(_roads, last, id, MaxPathVisited, path) || path.Count == 0)
                {
                    Flash(new LocText("Placemaker.Problem.NoRoadPath"));
                    return;
                }
                bool closing = id == _perimeter.Nodes[0];
                // The fill must not run through nodes already on the outline (that would pinch it).
                var used = new HashSet<long>(_perimeter.Nodes);
                for (int i = 0; i < path.Count; i++)
                {
                    bool isLastStep = i == path.Count - 1;
                    if (used.Contains(path[i].To) && !(closing && isLastStep))
                    {
                        Flash(new LocText("Placemaker.Problem.NodeAlreadyUsed"));
                        return;
                    }
                }
                if (closing && _perimeter.Path.Count + path.Count < 3) { Flash(new LocText("Placemaker.Problem.NeedThreePoints")); return; }
                foreach (var h in path)
                {
                    _perimeter.Path.Add(h);
                    if (closing && h.To == _perimeter.Nodes[0]) break;
                    _perimeter.Nodes.Add(h.To);
                    _perimeter.Positions.Add(_roads.NodePosition(h.To));
                }
                if (closing) _perimeter.Closed = true;
                ShapeChanged();
                Commit(closing ? "Placemaker.Undo.ClosePerimeter" : "Placemaker.Undo.AddNode");
                if (closing)
                {
                    RebuildShapeIfDirty(true);
                    if (_shape == null) Flash(new LocText("Placemaker.Problem.Crossing"));
                    else if (_stage == ToolStage.Area) SetStage(ToolStage.Design);
                }
                return;
            }
            RightClickStartsOver();
        }

        /// <summary>
        /// Node under the cursor like NetworkTools/GRG: a node hit directly, or the nearer end of a
        /// hovered edge (within reach). Only nodes of block-bounding roads count.
        /// </summary>
        private Entity ResolveHoveredNode()
        {
            if (!_in.HasHit) return Entity.Null;
            var e = _in.HitEntity;
            var hit = _in.RawHit;
            if (e != Entity.Null && EntityManager.HasComponent<Node>(e)) return e;
            if (hit.m_HitEntity != e && hit.m_HitEntity != Entity.Null && EntityManager.HasComponent<Node>(hit.m_HitEntity)) return hit.m_HitEntity;
            if (e != Entity.Null && EntityManager.HasComponent<Edge>(e) && _roads.IsBoundaryEdge(e))
            {
                var edge = EntityManager.GetComponentData<Edge>(e);
                if (!EntityManager.HasComponent<Node>(edge.m_Start) || !EntityManager.HasComponent<Node>(edge.m_End)) return Entity.Null;
                var a = EntityManager.GetComponentData<Node>(edge.m_Start).m_Position;
                var b = EntityManager.GetComponentData<Node>(edge.m_End).m_Position;
                float da = math.distance(hit.m_Position, a), db = math.distance(hit.m_Position, b);
                float reach = math.max(MaxNodeSnap, HandleRadius(hit.m_Position) * 2f);
                if (math.min(da, db) <= reach) return da <= db ? edge.m_Start : edge.m_End;
            }
            return Entity.Null;
        }

        private AreaShape BuildPerimeterShape()
        {
            _perimeterClosing = ComputePerimeterClosing();
            List<HalfEdge> loop;
            if (_perimeter.Closed)
            {
                if (_perimeter.Path.Count < 3) return null;
                loop = new List<HalfEdge>(_perimeter.Path);
            }
            else
            {
                if (_perimeterClosing == null) return null;
                loop = new List<HalfEdge>(_perimeter.Path);
                loop.AddRange(_perimeterClosing);
                if (loop.Count < 3) return null;
            }
            // The face must be on the left of the walk: make the loop counter-clockwise.
            var center = Poly.To2D(_roads.CenterRing(loop));
            if (center.Length >= 3 && Poly.SignedArea(center) < 0f)
            {
                loop.Reverse();
                for (int i = 0; i < loop.Count; i++) loop[i] = loop[i].Reversed;
            }
            try { return _roads.BuildArea(loop, AreaSource.Perimeter); }
            catch (System.Exception e) { Mod.Log.Warn("Placemaker: perimeter outline failed: " + e.Message); return null; }
        }
    }
}
