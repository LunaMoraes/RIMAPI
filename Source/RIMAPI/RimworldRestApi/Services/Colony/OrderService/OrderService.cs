using System;
using System.Collections.Generic;
using System.Linq;
using RIMAPI.Core;
using RIMAPI.Helpers;
using RIMAPI.Models;
using RIMAPI.Services;
using RimWorld;
using Verse;

public class OrderService : IOrderService
{
    private static readonly HashSet<string> SupportedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "mine", "deconstruct", "harvest", "chop", "cut", "cut-plant", "haul",
        "hunt", "tame", "slaughter", "smooth", "claim", "uninstall",
        "forbid", "unforbid", "cancel", "remove-all"
    };

    public ApiResult<DesignateResultDto> DesignateArea(DesignateRequestDto request)
    {
        if (request?.PointA == null || request.PointB == null || string.IsNullOrWhiteSpace(request.Type))
        {
            return ApiResult<DesignateResultDto>.ValidationFail("map_id, type, point_a and point_b are required");
        }

        string type = request.Type.Trim().ToLowerInvariant();
        if (!SupportedTypes.Contains(type))
        {
            return ApiResult<DesignateResultDto>.ValidationFail(
                $"Unsupported designation type '{request.Type}'. Supported: {string.Join(", ", SupportedTypes.OrderBy(s => s))}"
            );
        }

        var map = MapHelper.ResolveMapOrDefault(request.MapId);
        if (map == null)
        {
            return ApiResult<DesignateResultDto>.Fail($"Map with id: {request.MapId} not found");
        }

        IntVec3 start = new IntVec3(request.PointA.X, 0, request.PointA.Z);
        IntVec3 end = new IntVec3(request.PointB.X, 0, request.PointB.Z);
        CellRect rect = CellRect.FromLimits(start, end);

        int designated = 0;
        int affected = 0;
        int alreadyDesignated = 0;
        int skippedFogged = 0;
        int cellsScanned = 0;

        foreach (IntVec3 c in rect)
        {
            if (!c.InBounds(map)) continue;
            cellsScanned++;

            if (!request.Omniscient && c.Fogged(map))
            {
                skippedFogged++;
                continue;
            }

            switch (type)
            {
                case "mine":
                    HandleMine(map, c, ref designated, ref alreadyDesignated);
                    break;
                case "deconstruct":
                    HandleBuildingDesignation(map, c, DesignationDefOf.Deconstruct, b => b is Building building && building.DeconstructibleBy(Faction.OfPlayer), ref designated, ref alreadyDesignated);
                    break;
                case "uninstall":
                    HandleBuildingDesignation(map, c, DesignationDefOf.Uninstall, b => b is Building building && !building.Destroyed && building.def.Minifiable && building.Faction == Faction.OfPlayer, ref designated, ref alreadyDesignated);
                    break;
                case "harvest":
                    HandlePlantDesignation(map, c, DesignationDefOf.HarvestPlant, p => p.def.plant != null && !p.def.plant.IsTree && p.HarvestableNow, ref designated, ref alreadyDesignated);
                    break;
                case "chop":
                    HandlePlantDesignation(map, c, DesignationDefOf.HarvestPlant, p => p.def.plant != null && p.def.plant.IsTree && p.HarvestableNow, ref designated, ref alreadyDesignated);
                    break;
                case "cut":
                case "cut-plant":
                    HandlePlantDesignation(map, c, DesignationDefOf.CutPlant, p => p.def.plant != null, ref designated, ref alreadyDesignated);
                    break;
                case "haul":
                    HandleHaul(map, c, ref designated, ref alreadyDesignated);
                    break;
                case "hunt":
                    HandlePawnDesignation(map, c, DesignationDefOf.Hunt, p => p.RaceProps.Animal && p.Faction == null && !p.Dead && !p.InAggroMentalState, ref designated, ref alreadyDesignated);
                    break;
                case "tame":
                    HandlePawnDesignation(map, c, DesignationDefOf.Tame, p => p.AnimalOrWildMan() && p.Faction == null && !p.Dead && TameUtility.CanTame(p), ref designated, ref alreadyDesignated);
                    break;
                case "slaughter":
                    HandlePawnDesignation(map, c, DesignationDefOf.Slaughter, p => p.RaceProps.Animal && p.Faction == Faction.OfPlayer && !p.Dead && !p.InAggroMentalState, ref designated, ref alreadyDesignated);
                    break;
                case "smooth":
                    HandleSmooth(map, c, ref designated, ref alreadyDesignated);
                    break;
                case "claim":
                    HandleClaim(map, c, ref affected);
                    break;
                case "forbid":
                    HandleForbid(map, c, true, ref affected);
                    break;
                case "unforbid":
                    HandleForbid(map, c, false, ref affected);
                    break;
                case "cancel":
                case "remove-all":
                    HandleCancelOrRemove(map, c, type == "cancel", ref affected);
                    break;
            }
        }

        affected += designated;

        return ApiResult<DesignateResultDto>.Ok(new DesignateResultDto
        {
            MapId = map.uniqueID,
            Type = type,
            DesignatedCount = designated,
            AlreadyDesignatedCount = alreadyDesignated,
            AffectedCount = affected,
            SkippedFoggedCount = skippedFogged,
            CellsScanned = cellsScanned
        });
    }

    private static void HandleMine(Map map, IntVec3 c, ref int designated, ref int alreadyDesignated)
    {
        var edifice = c.GetEdifice(map);
        if (edifice == null || !edifice.def.mineable) return;
        if (map.designationManager.DesignationAt(c, DesignationDefOf.Mine) != null)
        {
            alreadyDesignated++;
            return;
        }
        map.designationManager.AddDesignation(new Designation(c, DesignationDefOf.Mine));
        designated++;
    }

    private static void HandleBuildingDesignation(
        Map map, IntVec3 c, DesignationDef def, Func<Thing, bool> predicate,
        ref int designated, ref int alreadyDesignated)
    {
        foreach (var t in c.GetThingList(map).ToList())
        {
            if (!predicate(t)) continue;
            if (map.designationManager.DesignationOn(t, def) != null)
            {
                alreadyDesignated++;
                continue;
            }
            var conflictDef = def == DesignationDefOf.Deconstruct ? DesignationDefOf.Uninstall
                            : def == DesignationDefOf.Uninstall ? DesignationDefOf.Deconstruct
                            : null;
            if (conflictDef != null)
            {
                var conflict = map.designationManager.DesignationOn(t, conflictDef);
                if (conflict != null) map.designationManager.RemoveDesignation(conflict);
            }
            map.designationManager.AddDesignation(new Designation(t, def));
            designated++;
        }
    }

    private static void HandlePlantDesignation(
        Map map, IntVec3 c, DesignationDef def, Func<Plant, bool> predicate,
        ref int designated, ref int alreadyDesignated)
    {
        foreach (var p in c.GetThingList(map).OfType<Plant>().ToList())
        {
            if (!predicate(p)) continue;
            if (map.designationManager.DesignationOn(p, def) != null)
            {
                alreadyDesignated++;
                continue;
            }
            var conflictDef = def == DesignationDefOf.CutPlant ? DesignationDefOf.HarvestPlant
                            : def == DesignationDefOf.HarvestPlant ? DesignationDefOf.CutPlant
                            : null;
            if (conflictDef != null)
            {
                var conflict = map.designationManager.DesignationOn(p, conflictDef);
                if (conflict != null) map.designationManager.RemoveDesignation(conflict);
            }
            map.designationManager.AddDesignation(new Designation(p, def));
            designated++;
        }
    }

    private static void HandleHaul(Map map, IntVec3 c, ref int designated, ref int alreadyDesignated)
    {
        foreach (var t in c.GetThingList(map).ToList())
        {
            if (!t.def.designateHaulable || t.IsInValidStorage()) continue;
            if (map.designationManager.DesignationOn(t, DesignationDefOf.Haul) != null)
            {
                alreadyDesignated++;
                continue;
            }
            map.designationManager.AddDesignation(new Designation(t, DesignationDefOf.Haul));
            designated++;
        }
    }

    private static void HandlePawnDesignation(
        Map map, IntVec3 c, DesignationDef def, Func<Pawn, bool> predicate,
        ref int designated, ref int alreadyDesignated)
    {
        foreach (var p in c.GetThingList(map).OfType<Pawn>().ToList())
        {
            if (!predicate(p)) continue;
            if (map.designationManager.DesignationOn(p, def) != null)
            {
                alreadyDesignated++;
                continue;
            }
            var conflictDef = def == DesignationDefOf.Hunt ? DesignationDefOf.Tame
                            : def == DesignationDefOf.Tame ? DesignationDefOf.Hunt
                            : null;
            if (conflictDef != null)
            {
                var conflict = map.designationManager.DesignationOn(p, conflictDef);
                if (conflict != null) map.designationManager.RemoveDesignation(conflict);
            }
            map.designationManager.AddDesignation(new Designation(p, def));
            designated++;
        }
    }

    private static void HandleSmooth(Map map, IntVec3 c, ref int designated, ref int alreadyDesignated)
    {
        var edifice = c.GetEdifice(map);
        if (edifice != null && edifice.def.building != null && edifice.def.building.isNaturalRock && !edifice.def.IsSmoothed)
        {
            if (map.designationManager.DesignationAt(c, DesignationDefOf.SmoothWall) != null)
            {
                alreadyDesignated++;
            }
            else
            {
                map.designationManager.AddDesignation(new Designation(c, DesignationDefOf.SmoothWall));
                designated++;
            }
            return;
        }

        var terrain = map.terrainGrid.TerrainAt(c);
        if (terrain != null && terrain.affordances != null && terrain.affordances.Contains(TerrainAffordanceDefOf.SmoothableStone))
        {
            if (map.designationManager.DesignationAt(c, DesignationDefOf.SmoothFloor) != null)
            {
                alreadyDesignated++;
            }
            else
            {
                map.designationManager.AddDesignation(new Designation(c, DesignationDefOf.SmoothFloor));
                designated++;
            }
        }
    }

    private static void HandleClaim(Map map, IntVec3 c, ref int designated)
    {
        foreach (var t in c.GetThingList(map).ToList())
        {
            if (t.ClaimableBy(Faction.OfPlayer) == true)
            {
                t.SetFaction(Faction.OfPlayer);
                designated++;
            }
        }
    }

    private static void HandleForbid(Map map, IntVec3 c, bool forbid, ref int designated)
    {
        foreach (var t in c.GetThingList(map).ToList())
        {
            var comp = t.TryGetComp<CompForbiddable>();
            if (comp != null && comp.Forbidden != forbid)
            {
                t.SetForbidden(forbid, warnOnFail: false);
                designated++;
            }
        }
    }

    private static void HandleCancelOrRemove(Map map, IntVec3 c, bool cancelBlueprints, ref int designated)
    {
        var cellDesignations = map.designationManager.AllDesignationsAt(c).ToList();
        foreach (var des in cellDesignations)
        {
            map.designationManager.RemoveDesignation(des);
            designated++;
        }

        foreach (var t in c.GetThingList(map).ToList())
        {
            foreach (var des in map.designationManager.AllDesignationsOn(t).ToList())
            {
                map.designationManager.RemoveDesignation(des);
                designated++;
            }

            if (cancelBlueprints && (t is Blueprint || t is Frame) && t.Faction == Faction.OfPlayer)
            {
                t.Destroy(DestroyMode.Cancel);
                designated++;
            }
        }
    }
}
