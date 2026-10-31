// Placemaker — ordering of group removal (GPL-3.0).
// Encodes the engine rules from RESEARCH-NOTES 11–12 (learned the hard way by ParkingLotTool and
// ParkManager): objects/areas first, then edges in bounded passes; a node is deleted only in
// the same pass as its last remaining edge and never while a foreign edge uses it.
// Pure so the ordering can be unit-tested; the game layer feeds it the live graph every pass.

using System.Collections.Generic;

namespace Placemaker.Core.Build
{
    public enum MemberKind : byte
    {
        Other = 0, Object = 1, Area = 2, Edge = 3, Node = 4,
        /// <summary>Water source, carrier, companion: deleted after everything else.</summary>
        Auxiliary = 5,
    }

    public struct RemovalMember
    {
        public long Id;
        public MemberKind Kind;
        /// <summary>Edges only: end nodes (may be foreign nodes).</summary>
        public long StartNode, EndNode;
    }

    public sealed class RemovalPass
    {
        public readonly List<long> Delete = new List<long>();
        /// <summary>Member nodes still used by foreign edges: drop the tag, keep the node.</summary>
        public readonly List<long> Release = new List<long>();
        /// <summary>True when nothing of the group is left after this pass.</summary>
        public bool Final;
    }

    public static class RemovalPlanner
    {
        public const int MaxPerPass = 32;
        /// <summary>
        /// Objects and areas per pass (p1-bulldoze). They hold no graph references (rule 12 is about edges and nodes), so
        /// they go in large batches: the 32-per-frame pace made a big park take 120+ frames, several seconds in a busy city.
        /// </summary>
        public const int MaxObjectsPerPass = 4096;

        /// <summary>
        /// Plans the next pass from the current live state.
        /// <paramref name="nodeEdges"/> lists, for every member node, ALL live edges connected to it
        /// (member and foreign). Call again next pass with the refreshed graph.
        /// </summary>
        public static RemovalPass NextPass(IReadOnlyList<RemovalMember> members, IReadOnlyDictionary<long, List<long>> nodeEdges, int maxPerPass = MaxPerPass,
            int maxObjectsPerPass = MaxObjectsPerPass)
        {
            var pass = new RemovalPass();
            if (maxPerPass < 1) maxPerPass = 1;
            if (maxObjectsPerPass < 1) maxObjectsPerPass = maxPerPass;
            var memberIds = new HashSet<long>();
            var memberEdges = new HashSet<long>();
            var memberNodes = new HashSet<long>();
            int objects = 0, edges = 0, aux = 0;
            foreach (var m in members)
            {
                memberIds.Add(m.Id);
                if (m.Kind == MemberKind.Edge) { memberEdges.Add(m.Id); edges++; }
                else if (m.Kind == MemberKind.Node) memberNodes.Add(m.Id);
                else if (m.Kind == MemberKind.Auxiliary) aux++;
                else objects++;
            }

            // 1) Objects and areas. Attached objects would be moved/culled when their nets go first.
            if (objects > 0)
            {
                foreach (var m in members)
                {
                    if (m.Kind == MemberKind.Edge || m.Kind == MemberKind.Node || m.Kind == MemberKind.Auxiliary) continue;
                    if (pass.Delete.Count >= maxObjectsPerPass) break;
                    pass.Delete.Add(m.Id);
                }
                return pass;
            }

            // 2) Edges with the nodes they free. A node goes when every live edge on it is a member
            //    edge deleted now or earlier; a node with any foreign edge is released.
            var deleting = new HashSet<long>();
            if (edges > 0)
            {
                foreach (var m in members)
                {
                    if (m.Kind != MemberKind.Edge) continue;
                    // Nodes this edge would free, counted against the budget so a pass stays ≤ max
                    // (only the first edge of a pass may exceed it — otherwise we could stall).
                    var freed = new List<long>(2);
                    var released = new List<long>(2);
                    deleting.Add(m.Id);
                    TryFree(m.StartNode, memberNodes, nodeEdges, memberEdges, deleting, freed, released, pass);
                    if (m.EndNode != m.StartNode) TryFree(m.EndNode, memberNodes, nodeEdges, memberEdges, deleting, freed, released, pass);
                    if (pass.Delete.Count > 0 && pass.Delete.Count + 1 + freed.Count > maxPerPass)
                    {
                        deleting.Remove(m.Id);
                        break;
                    }
                    pass.Delete.Add(m.Id);
                    foreach (var f in freed) if (!pass.Delete.Contains(f)) pass.Delete.Add(f);
                    foreach (var r in released) if (!pass.Release.Contains(r)) pass.Release.Add(r);
                }
                return pass;
            }

            // 3) Leftover nodes (their edges are gone, e.g. bulldozed by the player).
            foreach (var m in members)
            {
                if (m.Kind != MemberKind.Node) continue;
                if (HasForeignEdge(m.Id, nodeEdges, memberEdges, deleting)) { pass.Release.Add(m.Id); continue; }
                if (pass.Delete.Count >= maxPerPass) break;
                pass.Delete.Add(m.Id);
            }
            if (pass.Delete.Count > 0 || pass.Release.Count > 0) return pass;

            // 4) Auxiliary entities last (they may be referenced by members until now).
            foreach (var m in members)
            {
                if (m.Kind != MemberKind.Auxiliary) continue;
                if (pass.Delete.Count >= maxObjectsPerPass) break;
                pass.Delete.Add(m.Id);
            }
            pass.Final = pass.Delete.Count == aux;
            return pass;
        }

        private static void TryFree(long node, HashSet<long> memberNodes, IReadOnlyDictionary<long, List<long>> nodeEdges,
            HashSet<long> memberEdges, HashSet<long> deleting, List<long> freed, List<long> released, RemovalPass pass)
        {
            if (node == 0 || !memberNodes.Contains(node) || freed.Contains(node) || released.Contains(node)
                || pass.Delete.Contains(node) || pass.Release.Contains(node)) return;
            if (HasForeignEdge(node, nodeEdges, memberEdges, deleting))
            {
                // Foreign edge: the node belongs to the city now. Release only once no member edge
                // on it remains, so a later pass still sees it as ours until then.
                if (!HasLiveMemberEdge(node, nodeEdges, memberEdges, deleting)) released.Add(node);
                return;
            }
            if (!HasLiveMemberEdge(node, nodeEdges, memberEdges, deleting)) freed.Add(node);
        }

        private static bool HasForeignEdge(long node, IReadOnlyDictionary<long, List<long>> nodeEdges, HashSet<long> memberEdges, HashSet<long> deleting)
        {
            if (nodeEdges == null || !nodeEdges.TryGetValue(node, out var list) || list == null) return false;
            foreach (var e in list) if (!memberEdges.Contains(e) && !deleting.Contains(e)) return true;
            return false;
        }

        private static bool HasLiveMemberEdge(long node, IReadOnlyDictionary<long, List<long>> nodeEdges, HashSet<long> memberEdges, HashSet<long> deleting)
        {
            if (nodeEdges == null || !nodeEdges.TryGetValue(node, out var list) || list == null) return false;
            foreach (var e in list) if (memberEdges.Contains(e) && !deleting.Contains(e)) return true;
            return false;
        }
    }
}
