using System;
using System.Collections.Generic;
using System.Linq;
using RIMAPI.Core;
using RIMAPI.Helpers;
using RIMAPI.Models;
using RimWorld;
using Verse;
using Verse.AI;

namespace RIMAPI.Services
{
    public class AiPawnService : IAiPawnService
    {
        public ApiResult<AiDraftResultDto> SetDraft(AiDraftRequestDto request)
        {
            if (request == null || request.PawnId <= 0)
            {
                return ApiResult<AiDraftResultDto>.ValidationFail("pawn_id is required");
            }

            Pawn pawn = PawnHelper.FindPawnById(request.PawnId);
            if (pawn == null)
            {
                return ApiResult<AiDraftResultDto>.Fail($"Pawn not found: {request.PawnId}");
            }

            if (!pawn.IsColonistPlayerControlled || pawn.drafter == null)
            {
                return ApiResult<AiDraftResultDto>.ValidationFail($"Pawn {pawn.LabelShort} cannot be drafted");
            }

            if (request.Drafted && (pawn.Dead || pawn.Downed || pawn.InMentalState))
            {
                return ApiResult<AiDraftResultDto>.ValidationFail($"Pawn {pawn.LabelShort} is incapacitated or in a mental state");
            }

            pawn.drafter.Drafted = request.Drafted;

            return ApiResult<AiDraftResultDto>.Ok(new AiDraftResultDto
            {
                PawnId = pawn.thingIDNumber,
                Name = pawn.LabelShort,
                IsDrafted = pawn.Drafted
            });
        }

        public ApiResult<AiPawnCommandResultDto> CommandPawn(AiPawnCommandRequestDto request)
        {
            if (request == null || request.PawnId <= 0 || string.IsNullOrWhiteSpace(request.Action))
            {
                return ApiResult<AiPawnCommandResultDto>.ValidationFail("pawn_id and action are required");
            }

            Pawn pawn = PawnHelper.FindPawnById(request.PawnId);
            if (pawn == null)
            {
                return ApiResult<AiPawnCommandResultDto>.Fail($"Pawn not found: {request.PawnId}");
            }

            if (!pawn.IsColonistPlayerControlled || pawn.drafter == null || pawn.Map == null)
            {
                return ApiResult<AiPawnCommandResultDto>.ValidationFail($"Pawn {pawn.LabelShort} is not a controllable spawned colonist");
            }

            if (pawn.Dead || pawn.Downed || pawn.InMentalState)
            {
                return ApiResult<AiPawnCommandResultDto>.ValidationFail($"Pawn {pawn.LabelShort} is incapacitated or in a mental state");
            }

            if (!pawn.Drafted)
            {
                if (request.AutoDraft)
                {
                    pawn.drafter.Drafted = true;
                }
                else
                {
                    return ApiResult<AiPawnCommandResultDto>.ValidationFail($"Pawn {pawn.LabelShort} is not drafted (draft first or pass auto_draft: true)");
                }
            }

            string action = request.Action.Trim().ToLowerInvariant();
            Job job;

            switch (action)
            {
                case "move":
                {
                    if (request.TargetPosition == null)
                    {
                        return ApiResult<AiPawnCommandResultDto>.ValidationFail("target_position is required for move action");
                    }
                    IntVec3 dest = new IntVec3(request.TargetPosition.X, 0, request.TargetPosition.Z);
                    if (!dest.InBounds(pawn.Map))
                    {
                        return ApiResult<AiPawnCommandResultDto>.ValidationFail("target_position is out of map bounds");
                    }
                    if (!pawn.CanReach(dest, PathEndMode.OnCell, Danger.Deadly))
                    {
                        return ApiResult<AiPawnCommandResultDto>.ValidationFail($"Pawn {pawn.LabelShort} cannot reach ({dest.x}, {dest.z})");
                    }
                    job = JobMaker.MakeJob(JobDefOf.Goto, dest);
                    break;
                }
                case "attack":
                {
                    LocalTargetInfo target = LocalTargetInfo.Invalid;
                    if (request.TargetPawnId.HasValue)
                    {
                        Pawn targetPawn = PawnHelper.FindPawnById(request.TargetPawnId.Value);
                        if (targetPawn == null || targetPawn.Map != pawn.Map || targetPawn.Dead)
                        {
                            return ApiResult<AiPawnCommandResultDto>.Fail($"Target pawn not found or dead on map: {request.TargetPawnId.Value}");
                        }
                        target = targetPawn;
                    }
                    else if (request.TargetPosition != null)
                    {
                        IntVec3 cell = new IntVec3(request.TargetPosition.X, 0, request.TargetPosition.Z);
                        if (!cell.InBounds(pawn.Map))
                        {
                            return ApiResult<AiPawnCommandResultDto>.ValidationFail("target_position is out of map bounds");
                        }
                        target = cell;
                    }
                    else
                    {
                        return ApiResult<AiPawnCommandResultDto>.ValidationFail("target_pawn_id or target_position is required for attack action");
                    }

                    bool hasRanged = pawn.equipment?.Primary?.def?.IsRangedWeapon == true;
                    JobDef attackJobDef = hasRanged ? JobDefOf.AttackStatic : JobDefOf.AttackMelee;
                    job = JobMaker.MakeJob(attackJobDef, target);
                    break;
                }
                case "wait":
                {
                    job = JobMaker.MakeJob(JobDefOf.Wait_Combat);
                    break;
                }
                default:
                    return ApiResult<AiPawnCommandResultDto>.ValidationFail($"Unsupported action '{request.Action}'. Supported: move, attack, wait");
            }

            if (request.Queue)
            {
                pawn.jobs.jobQueue.EnqueueLast(job, JobTag.Misc);
            }
            else
            {
                bool accepted = pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                if (!accepted)
                {
                    return ApiResult<AiPawnCommandResultDto>.Fail($"Pawn {pawn.LabelShort} could not accept {action} job");
                }
            }

            return ApiResult<AiPawnCommandResultDto>.Ok(new AiPawnCommandResultDto
            {
                PawnId = pawn.thingIDNumber,
                Name = pawn.LabelShort,
                Action = action,
                AssignedJob = job.def.defName,
                IsDrafted = pawn.Drafted
            });
        }

        public ApiResult<AiPawnPolicyResultDto> UpdatePolicy(AiPawnPolicyRequestDto request)
        {
            if (request == null || request.PawnId <= 0)
            {
                return ApiResult<AiPawnPolicyResultDto>.ValidationFail("pawn_id is required");
            }

            Pawn pawn = PawnHelper.FindPawnById(request.PawnId);
            if (pawn == null)
            {
                return ApiResult<AiPawnPolicyResultDto>.Fail($"Pawn not found: {request.PawnId}");
            }

            if (pawn.Faction != Faction.OfPlayer || pawn.playerSettings == null)
            {
                return ApiResult<AiPawnPolicyResultDto>.ValidationFail($"Pawn {pawn.LabelShort} does not have player settings");
            }

            if (!string.IsNullOrWhiteSpace(request.HostilityResponse))
            {
                if (!Enum.TryParse(request.HostilityResponse.Trim(), true, out HostilityResponseMode mode))
                {
                    return ApiResult<AiPawnPolicyResultDto>.ValidationFail($"Invalid hostility_response '{request.HostilityResponse}'. Valid: Ignore, Attack, Flee");
                }
                pawn.playerSettings.hostilityResponse = mode;
            }

            if (request.SelfTend.HasValue)
            {
                pawn.playerSettings.selfTend = request.SelfTend.Value;
            }

            if (!string.IsNullOrWhiteSpace(request.MedicalCare))
            {
                if (!Enum.TryParse(request.MedicalCare.Trim(), true, out MedicalCareCategory medCare))
                {
                    return ApiResult<AiPawnPolicyResultDto>.ValidationFail($"Invalid medical_care '{request.MedicalCare}'. Valid: NoCare, NoMeds, HerbalOrWorse, NormalOrWorse, Best");
                }
                pawn.playerSettings.medCare = medCare;
            }

            if (request.AllowedArea != null && pawn.Map != null)
            {
                string areaName = request.AllowedArea.Trim();
                if (string.IsNullOrEmpty(areaName) || areaName.Equals("Unrestricted", StringComparison.OrdinalIgnoreCase))
                {
                    pawn.playerSettings.AreaRestrictionInPawnCurrentMap = null;
                }
                else
                {
                    Area area = pawn.Map.areaManager.AllAreas.FirstOrDefault(
                        a => a.Label.Equals(areaName, StringComparison.OrdinalIgnoreCase)
                    );
                    if (area == null)
                    {
                        return ApiResult<AiPawnPolicyResultDto>.Fail($"Allowed area not found: {areaName}");
                    }
                    pawn.playerSettings.AreaRestrictionInPawnCurrentMap = area;
                }
            }

            if (!string.IsNullOrWhiteSpace(request.FoodPolicy) && pawn.foodRestriction != null && Current.Game?.foodRestrictionDatabase != null)
            {
                var food = Current.Game.foodRestrictionDatabase.AllFoodRestrictions.FirstOrDefault(
                    f => f.label.Equals(request.FoodPolicy.Trim(), StringComparison.OrdinalIgnoreCase)
                );
                if (food == null)
                {
                    return ApiResult<AiPawnPolicyResultDto>.Fail($"Food policy not found: {request.FoodPolicy}");
                }
                pawn.foodRestriction.CurrentFoodPolicy = food;
            }

            if (!string.IsNullOrWhiteSpace(request.DrugPolicy) && pawn.drugs != null && Current.Game?.drugPolicyDatabase != null)
            {
                var drug = Current.Game.drugPolicyDatabase.AllPolicies.FirstOrDefault(
                    d => d.label.Equals(request.DrugPolicy.Trim(), StringComparison.OrdinalIgnoreCase)
                );
                if (drug == null)
                {
                    return ApiResult<AiPawnPolicyResultDto>.Fail($"Drug policy not found: {request.DrugPolicy}");
                }
                pawn.drugs.CurrentPolicy = drug;
            }

            if (!string.IsNullOrWhiteSpace(request.ApparelPolicy) && pawn.outfits != null && Current.Game?.outfitDatabase != null)
            {
                var outfit = Current.Game.outfitDatabase.AllOutfits.FirstOrDefault(
                    o => o.label.Equals(request.ApparelPolicy.Trim(), StringComparison.OrdinalIgnoreCase)
                );
                if (outfit == null)
                {
                    return ApiResult<AiPawnPolicyResultDto>.Fail($"Apparel policy not found: {request.ApparelPolicy}");
                }
                pawn.outfits.CurrentApparelPolicy = outfit;
            }

            return ApiResult<AiPawnPolicyResultDto>.Ok(new AiPawnPolicyResultDto
            {
                PawnId = pawn.thingIDNumber,
                Name = pawn.LabelShort,
                HostilityResponse = pawn.playerSettings.hostilityResponse.ToString(),
                SelfTend = pawn.playerSettings.selfTend,
                MedicalCare = pawn.playerSettings.medCare.ToString(),
                AllowedArea = pawn.playerSettings.AreaRestrictionInPawnCurrentMap?.Label ?? "Unrestricted",
                FoodPolicy = pawn.foodRestriction?.CurrentFoodPolicy?.label,
                DrugPolicy = pawn.drugs?.CurrentPolicy?.label,
                ApparelPolicy = pawn.outfits?.CurrentApparelPolicy?.label
            });
        }

        public ApiResult<List<AiAreaDto>> GetAreas(int? mapId)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return ApiResult<List<AiAreaDto>>.Fail("Game is not currently playing.");
            }

            Map map = mapId.HasValue ? MapHelper.GetMapByID(mapId.Value) : Find.CurrentMap;
            if (map == null)
            {
                return ApiResult<List<AiAreaDto>>.Fail($"Map not found: {mapId?.ToString() ?? "current"}");
            }

            var list = map.areaManager.AllAreas.Select(ToAreaDto).ToList();
            return ApiResult<List<AiAreaDto>>.Ok(list);
        }

        public ApiResult<AiAreaDto> CreateArea(AiAreaCreateRequestDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Label))
            {
                return ApiResult<AiAreaDto>.ValidationFail("label is required");
            }

            Map map = MapHelper.GetMapByID(request.MapId);
            if (map == null)
            {
                return ApiResult<AiAreaDto>.Fail($"Map not found: {request.MapId}");
            }

            if (map.areaManager.AllAreas.Any(a => a.Label.Equals(request.Label.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return ApiResult<AiAreaDto>.ValidationFail($"Area with label '{request.Label}' already exists");
            }

            if (!map.areaManager.TryMakeNewAllowed(out Area_Allowed newArea))
            {
                return ApiResult<AiAreaDto>.Fail("Failed to create allowed area (maximum area limit reached)");
            }

            newArea.SetLabel(request.Label.Trim());

            if (request.PointA != null && request.PointB != null)
            {
                ApplyRectToArea(map, newArea, request.PointA, request.PointB, true);
            }

            return ApiResult<AiAreaDto>.Ok(ToAreaDto(newArea));
        }

        public ApiResult<AiAreaDto> UpdateAreaCells(AiAreaCellsRequestDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Label) || string.IsNullOrWhiteSpace(request.Mode))
            {
                return ApiResult<AiAreaDto>.ValidationFail("label and mode are required");
            }

            Map map = MapHelper.GetMapByID(request.MapId);
            if (map == null)
            {
                return ApiResult<AiAreaDto>.Fail($"Map not found: {request.MapId}");
            }

            Area area = map.areaManager.AllAreas.FirstOrDefault(
                a => a.Label.Equals(request.Label.Trim(), StringComparison.OrdinalIgnoreCase)
            );
            if (area == null)
            {
                return ApiResult<AiAreaDto>.Fail($"Area not found: {request.Label}");
            }

            string mode = request.Mode.Trim().ToLowerInvariant();
            if (mode == "clear")
            {
                area.Clear();
                return ApiResult<AiAreaDto>.Ok(ToAreaDto(area));
            }

            if (request.PointA == null || request.PointB == null)
            {
                return ApiResult<AiAreaDto>.ValidationFail("point_a and point_b are required for add/remove modes");
            }

            if (mode != "add" && mode != "remove")
            {
                return ApiResult<AiAreaDto>.ValidationFail("mode must be 'add', 'remove', or 'clear'");
            }

            ApplyRectToArea(map, area, request.PointA, request.PointB, mode == "add");
            return ApiResult<AiAreaDto>.Ok(ToAreaDto(area));
        }

        public ApiResult<AiPrisonerInteractionResultDto> SetPrisonerInteraction(AiPrisonerInteractionRequestDto request)
        {
            if (request == null || request.PawnId <= 0 || string.IsNullOrWhiteSpace(request.Mode))
            {
                return ApiResult<AiPrisonerInteractionResultDto>.ValidationFail("pawn_id and mode are required");
            }

            Pawn pawn = PawnHelper.FindPawnById(request.PawnId);
            if (pawn == null)
            {
                return ApiResult<AiPrisonerInteractionResultDto>.Fail($"Pawn not found: {request.PawnId}");
            }

            if (!pawn.IsPrisonerOfColony || pawn.guest == null)
            {
                return ApiResult<AiPrisonerInteractionResultDto>.ValidationFail($"Pawn {pawn.LabelShort} is not a colony prisoner");
            }

            PrisonerInteractionModeDef def = DefDatabase<PrisonerInteractionModeDef>.AllDefsListForReading
                .FirstOrDefault(d => d.defName.Equals(request.Mode.Trim(), StringComparison.OrdinalIgnoreCase));
            if (def == null)
            {
                string valid = string.Join(", ", DefDatabase<PrisonerInteractionModeDef>.AllDefsListForReading.Select(d => d.defName));
                return ApiResult<AiPrisonerInteractionResultDto>.ValidationFail($"Invalid prisoner interaction mode '{request.Mode}'. Valid: {valid}");
            }

            pawn.guest.SetExclusiveInteraction(def);

            return ApiResult<AiPrisonerInteractionResultDto>.Ok(new AiPrisonerInteractionResultDto
            {
                PawnId = pawn.thingIDNumber,
                Name = pawn.LabelShort,
                InteractionMode = def.defName,
                Resistance = pawn.guest.resistance
            });
        }

        private static void ApplyRectToArea(Map map, Area area, PositionDto pointA, PositionDto pointB, bool value)
        {
            CellRect rect = CellRect.FromLimits(
                new IntVec3(pointA.X, 0, pointA.Z),
                new IntVec3(pointB.X, 0, pointB.Z)
            );
            foreach (IntVec3 c in rect)
            {
                if (c.InBounds(map))
                {
                    area[c] = value;
                }
            }
        }

        private static AiAreaDto ToAreaDto(Area area)
        {
            return new AiAreaDto
            {
                Id = area.ID,
                Label = area.Label,
                Type = area.GetType().Name,
                CellCount = area.TrueCount,
                IsHome = area is Area_Home
            };
        }
    }
}
