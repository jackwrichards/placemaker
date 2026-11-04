// Placemaker neighbourhood polish — what an outgrown building really has (GPL-3.0).
//
// For the game runner (lab ops "outgrowAudit" and "outgrowRepaint"): per outgrown building, whether a plan ever wrote its
// garden, how much land past its lot the garden has, and the pieces it owns. "Outgrown but empty" (the lens shows it
// outgrown, nothing was built for it) is counted from these, not guessed from pictures. And per pass, the core houses
// whose every piece a plan took away (TotalEmptied), the dirty houses dropped before they were planned (TotalDropped).

using System;
using System.Collections.Generic;
using Game.Common;
using Game.Prefabs;
using Placemaker.Core.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourPolishSystem
    {
        /// <summary>One outgrown (or not) building as the polish left it.</summary>
        public struct Audit
        {
            public bool Yes, Building, Planned;
            public string Kind, Look;
            /// <summary>The look makes its land past the lot a car park yard or paving (not a garden).</summary>
            public bool HardLook;
            public float LotM2, GardenM2;
            /// <summary>Its own pieces (PolishPiece, kind Garden): surfaces (of them paved) and objects.</summary>
            public int Areas, Paved, Objects;
            public int LinesMoved, LawnsChanged;
        }

        /// <summary>For the game runner: core houses whose every piece a plan took away (it had some, the plan wanted none):
        /// the ones the pass was asked for and the neighbours it re-planned; dirty houses dropped before a plan (not built yet).</summary>
        public static int TotalEmptiedAsked, TotalEmptiedNeighbour, TotalDropped, TotalNeverDirty;
        private static int s_emptiedLogged;

        public Audit AuditOf(Entity house)
        {
            var a = new Audit();
            if (!EntityManager.Exists(house) || EntityManager.HasComponent<Deleted>(house) || !EntityManager.HasComponent<PrefabRef>(house)) return a;
            var prefab = EntityManager.GetComponentData<PrefabRef>(house).m_Prefab;
            a.Yes = IsYes(house);
            a.Building = EntityManager.HasComponent<global::Game.Objects.UnderConstruction>(house);
            a.Kind = ZoneKind(prefab);
            if (EntityManager.HasComponent<PolishedLot>(house))
            {
                var f = EntityManager.GetComponentData<PolishedLot>(house);
                var look = LookByHash(f.Look);
                a.Look = look?.Id ?? "";
                if (look != null)
                {
                    var v = look.Values();
                    a.HardLook = v.TryGetValue("fill", out var fill) && math.round(fill) >= Placemaker.Core.Neighbours.PolishLooks.FillCarPark;
                }
            }
            if (EntityManager.HasComponent<BuildingData>(prefab))
            {
                var lot = (float2)EntityManager.GetComponentData<BuildingData>(prefab).m_LotSize * 8f;
                a.LotM2 = lot.x * lot.y;
            }
            var garden = GardenOf(house);
            a.Planned = garden != null;
            if (garden != null && garden.Length >= 3) a.GardenM2 = math.abs(Poly.Area(garden));
            foreach (var (piece, name, _) in OwnPieces(house, PolishPieceKind.Garden))
            {
                if (EntityManager.HasComponent<global::Game.Areas.Area>(piece))
                {
                    a.Areas++;
                    if (name.IndexOf("grass", StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf("lawn", StringComparison.OrdinalIgnoreCase) < 0
                        && name.IndexOf("forestry", StringComparison.OrdinalIgnoreCase) < 0) a.Paved++;
                }
                else a.Objects++;
            }
            var (_, lines, lawns, _) = Inspect(house);
            a.LinesMoved = lines;
            a.LawnsChanged = lawns;
            return a;
        }

        /// <summary>Why an outgrown building has nothing it should (null: it is fine): never planned, land past its lot with
        /// nothing on it, or a car park look with land and no paving left.</summary>
        public static string EmptyReason(in Audit a)
        {
            if (!a.Yes || a.Building) return null;
            if (!a.Planned) return "neverPlanned";
            float past = a.GardenM2 - a.LotM2;
            if (past > 20f && a.Areas + a.Objects == 0 && a.LawnsChanged == 0) return "landButNothing";
            if (a.HardLook && past > 80f && a.Paved == 0) return "carParkLookNoPaving";
            return null;
        }

        /// <summary>A core house a plan leaves with nothing though it had pieces: counted, and the first few logged with why.</summary>
        private void NoteEmptied(long id, Entity house, bool asked, bool inPlots, int had, string zone)
        {
            if (asked) TotalEmptiedAsked++; else TotalEmptiedNeighbour++;
            if (s_emptiedLogged++ >= 40) return;
            Mod.Log.Info($"Placemaker neighbourhood polish: {id} ({zone}) loses all its {had} pieces: {(asked ? "asked" : "a neighbour re-planned")}, "
                + $"{(inPlots ? "planned (a plot)" : "not stitched in this pass (no plot)")}");
        }
    }
}
