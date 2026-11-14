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

        var map = MapHelper.GetMapByID(request.MapId);
        if (map == null)
        {
            return ApiResult<DesignateResultDto>.Fail($"Map with id: {request.MapId} not found");
        }

        IntVec3 start = new IntVec3(request.PointA.X, 0, request.PointA.Z);
        IntVec3 end = new IntVec3(request.PointB.X, 0, request.PointB.Z);
        CellRect rect = CellRect.FromLimits(start, end);

        int designated = 0;
        int alreadyDesignated = 0;
        int cellsScanned = 0;

        foreach (IntVec3 c in rect)
        {
            if (!c.InBounds(map)) continue;
            cellsScanned++;

            switch (type)
            {
                case "mine":
                    HandleMine(map, c, ref designated, ref alreadyDesignated);
                    break;
                case "deconstruct":
                    HandleBuildingDesignation(map, c, DesignationDefOf.Deconstruct, b => b.def.category == ThingCategory.Building, ref designated, ref alreadyDesignated);
                    break;
                case "uninstall":
                    HandleBuildingDesignation(map, c, DesignationDefOf.Uninstall, b => b.def.Minifiable && b.Faction == Faction.OfPlayer, ref designated, ref alreadyDesignated);
                    break;
                case "harvest":
                    HandlePlantDesignation(map, c, DesignationDefOf.HarvestPlant, p => p.HarvestableNow, ref designated, ref alreadyDesignated);
                    break;
                case "chop":
                    HandlePlantDesignation(map, c, DesignationDefOf.CutPlant, p => p.def.plant != null && p.def.plant.IsTree, ref designated, ref alreadyDesignated);
                    break;
                case "cut":
                case "cut-plant":
                    HandlePlantDesignation(map, c, DesignationDefOf.CutPlant, _ => true, ref designated, ref alreadyDesignated);
                    break;
                case "haul":
                    HandleHaul(map, c, ref designated, ref alreadyDesignated);
                    break;
                case "hunt":
                    HandleAnimalDesignation(map, c, DesignationDefOf.Hunt, p => p.Faction != Faction.OfPlayer, ref designated, ref alreadyDesignated);
                    break;
                case "tame":
                    HandleAnimalDesignation(map, c, DesignationDefOf.Tame, p => p.Faction == null && p.AnimalOrWildMan(), ref designated, ref alreadyDesignated);
                    break;
                case "slaughter":
                    HandleAnimalDesignation(map, c, DesignationDefOf.Slaughter, p => p.Faction == Faction.OfPlayer, ref designated, ref alreadyDesignated);
                    break;
                case "smooth":
                    HandleSmooth(map, c, ref designated, ref alreadyDesignated);
                    break;
                case "claim":
                    HandleClaim(map, c, ref designated);
                    break;
                case "forbid":
                    HandleForbid(map, c, true, ref designated);
                    break;
                case "unforbid":
                    HandleForbid(map, c, false, ref designated);
                    break;
                case "cancel":
                case "remove-all":
                    HandleCancelOrRemove(map, c, type == "cancel", ref designated);
                    break;
            }
        }

        return ApiResult<DesignateResultDto>.Ok(new DesignateResultDto
        {
            MapId = request.MapId,
            Type = type,
            DesignatedCount = designated,
            AlreadyDesignatedCount = alreadyDesignated,
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
            map.designationManager.AddDesignation(new Designation(p, def));
            designated++;
        }
    }

    private static void HandleHaul(Map map, IntVec3 c, ref int designated, ref int alreadyDesignated)
    {
        foreach (var t in c.GetThingList(map).ToList())
        {
            if (!t.def.EverHaulable) continue;
            if (map.designationManager.DesignationOn(t, DesignationDefOf.Haul) != null)
            {
                alreadyDesignated++;
                continue;
            }
            map.designationManager.AddDesignation(new Designation(t, DesignationDefOf.Haul));
            designated++;
        }
    }

    private static void HandleAnimalDesignation(
        Map map, IntVec3 c, DesignationDef def, Func<Pawn, bool> predicate,
        ref int designated, ref int alreadyDesignated)
    {
        foreach (var p in c.GetThingList(map).OfType<Pawn>().ToList())
        {
            if (!p.RaceProps.Animal || !predicate(p)) continue;
            if (map.designationManager.DesignationOn(p, def) != null)
            {
                alreadyDesignated++;
                continue;
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
