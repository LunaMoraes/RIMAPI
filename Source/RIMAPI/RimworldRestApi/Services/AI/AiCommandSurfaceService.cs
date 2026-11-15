using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using RIMAPI.Core;
using RIMAPI.Helpers;
using RIMAPI.Models;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RIMAPI.Services
{
    public class AiCommandSurfaceService : IAiCommandSurfaceService
    {
        private const int MaxAdvanceTicks = 2500;
        private const int MaxAdvanceWallClockMs = 400;

        public ApiResult<List<AiDesignatorDto>> GetDesignators(string category, bool includeHidden)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return ApiResult<List<AiDesignatorDto>>.ServiceUnavailable("Game is not currently playing.");
            }

            var result = new List<AiDesignatorDto>();
            foreach (var cat in DefDatabase<DesignationCategoryDef>.AllDefsListForReading)
            {
                if (!includeHidden && !cat.Visible) continue;
                if (!string.IsNullOrWhiteSpace(category) && !cat.defName.Equals(category.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                var designators = cat.AllResolvedDesignators;
                if (designators == null) continue;

                for (int i = 0; i < designators.Count; i++)
                {
                    var d = designators[i];
                    if (d == null) continue;
                    if (!includeHidden && !d.Visible) continue;

                    result.Add(new AiDesignatorDto
                    {
                        Category = cat.defName,
                        Index = i,
                        ClassName = d.GetType().FullName,
                        Label = d.LabelCap.ToString(),
                        Desc = d.Desc,
                        Visible = d.Visible,
                        Disabled = d.Disabled,
                        DisabledReason = d.disabledReason,
                        BuildDefName = (d as Designator_Build)?.PlacingDef?.defName
                    });
                }
            }

            return ApiResult<List<AiDesignatorDto>>.Ok(result);
        }

        public ApiResult<AiDesignatorInvokeResultDto> InvokeDesignator(AiDesignatorInvokeRequestDto request)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return ApiResult<AiDesignatorInvokeResultDto>.ServiceUnavailable("Game is not currently playing.");
            }

            if (request == null || (string.IsNullOrWhiteSpace(request.ClassName) && string.IsNullOrWhiteSpace(request.Label) && string.IsNullOrWhiteSpace(request.BuildDefName)))
            {
                return ApiResult<AiDesignatorInvokeResultDto>.ValidationFail("class_name, build_def_name, or label is required");
            }

            Map map = MapHelper.ResolveMapOrDefault(request.MapId);
            if (map == null)
            {
                return ApiResult<AiDesignatorInvokeResultDto>.Fail($"Map not found: {request.MapId}");
            }

            Designator designator = ResolveDesignator(request);
            if (designator == null)
            {
                return ApiResult<AiDesignatorInvokeResultDto>.Fail("Matching Designator not found");
            }

            if (designator.Disabled)
            {
                return ApiResult<AiDesignatorInvokeResultDto>.ValidationFail($"Designator '{designator.LabelCap}' is disabled: {designator.disabledReason}");
            }

            if (designator is Designator_Build buildDesignator && !string.IsNullOrWhiteSpace(request.StuffDefName))
            {
                var stuff = DefDatabase<ThingDef>.GetNamedSilentFail(request.StuffDefName.Trim());
                if (stuff == null)
                {
                    return ApiResult<AiDesignatorInvokeResultDto>.ValidationFail($"stuff_def_name not found: {request.StuffDefName}");
                }
                buildDesignator.SetStuffDef(stuff);
            }

            var warnings = new List<string>();
            if (Find.CurrentMap != map)
            {
                warnings.Add($"Designator invoked while Find.CurrentMap is {Find.CurrentMap?.uniqueID}; some vanilla designators target CurrentMap.");
            }

            int designated = 0;
            int rejected = 0;
            int skippedFogged = 0;

            if (request.TargetThingId.HasValue)
            {
                Thing targetThing = map.listerThings.AllThings.FirstOrDefault(t => t.thingIDNumber == request.TargetThingId.Value);
                if (targetThing == null)
                {
                    return ApiResult<AiDesignatorInvokeResultDto>.Fail($"Thing not found on map: {request.TargetThingId.Value}");
                }

                if (!request.Omniscient && targetThing.Position.Fogged(map))
                {
                    skippedFogged++;
                }
                else
                {
                    using (new SelectionScope(targetThing))
                    {
                        AcceptanceReport report = designator.CanDesignateThing(targetThing);
                        if (report.Accepted)
                        {
                            designator.DesignateThing(targetThing);
                            designated++;
                        }
                        else
                        {
                            rejected++;
                            if (!string.IsNullOrEmpty(report.Reason)) warnings.Add(report.Reason);
                        }
                    }
                }
            }
            else
            {
                var targetCells = CollectTargetCells(request);
                if (targetCells.Count == 0)
                {
                    return ApiResult<AiDesignatorInvokeResultDto>.ValidationFail("target_thing_id, cells, or point_a/point_b are required");
                }

                var acceptedCells = new List<IntVec3>();
                foreach (IntVec3 c in targetCells)
                {
                    if (!c.InBounds(map))
                    {
                        rejected++;
                        continue;
                    }
                    if (!request.Omniscient && c.Fogged(map))
                    {
                        skippedFogged++;
                        continue;
                    }

                    AcceptanceReport report = designator.CanDesignateCell(c);
                    if (report.Accepted)
                    {
                        acceptedCells.Add(c);
                    }
                    else
                    {
                        rejected++;
                        if (!string.IsNullOrEmpty(report.Reason) && warnings.Count < 5)
                        {
                            warnings.Add($"({c.x},{c.z}): {report.Reason}");
                        }
                    }
                }

                if (acceptedCells.Count > 0)
                {
                    if (acceptedCells.Count > 1 && designator is Designator_ZoneAdd)
                    {
                        designator.DesignateMultiCell(acceptedCells);
                        designated = acceptedCells.Count;
                    }
                    else
                    {
                        foreach (IntVec3 c in acceptedCells)
                        {
                            designator.DesignateSingleCell(c);
                            designated++;
                        }
                    }
                }
            }

            var res = ApiResult<AiDesignatorInvokeResultDto>.Ok(new AiDesignatorInvokeResultDto
            {
                MapId = map.uniqueID,
                DesignatorLabel = designator.LabelCap.ToString(),
                ClassName = designator.GetType().FullName,
                DesignatedCount = designated,
                RejectedCount = rejected,
                SkippedFoggedCount = skippedFogged
            });
            if (warnings.Count > 0) res.Warnings.AddRange(warnings);
            return res;
        }

        public ApiResult<List<AiFloatMenuOptionDto>> GetFloatMenuOptions(int pawnId, int x, int z, bool omniscient)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return ApiResult<List<AiFloatMenuOptionDto>>.ServiceUnavailable("Game is not currently playing.");
            }

            if (!TryResolveFloatMenuCell(pawnId, x, z, omniscient, out Pawn pawn, out IntVec3 cell, out string err))
            {
                return ApiResult<List<AiFloatMenuOptionDto>>.ValidationFail(err);
            }

            List<FloatMenuOption> options;
            using (new SelectionScope(pawn))
            {
                options = FloatMenuMakerMap.GetOptions(new List<Pawn> { pawn }, cell.ToVector3Shifted(), out _) ?? new List<FloatMenuOption>();
            }

            var list = new List<AiFloatMenuOptionDto>(options.Count);
            for (int i = 0; i < options.Count; i++)
            {
                var opt = options[i];
                list.Add(new AiFloatMenuOptionDto
                {
                    Index = i,
                    Label = opt.Label,
                    Disabled = opt.Disabled,
                    Priority = opt.Priority.ToString()
                });
            }

            return ApiResult<List<AiFloatMenuOptionDto>>.Ok(list);
        }

        public ApiResult<AiFloatMenuInvokeResultDto> InvokeFloatMenuOption(AiFloatMenuInvokeRequestDto request)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return ApiResult<AiFloatMenuInvokeResultDto>.ServiceUnavailable("Game is not currently playing.");
            }

            if (request == null || (!request.OptionIndex.HasValue && string.IsNullOrWhiteSpace(request.OptionLabel)))
            {
                return ApiResult<AiFloatMenuInvokeResultDto>.ValidationFail("option_index or option_label is required");
            }

            if (!TryResolveFloatMenuCell(request.PawnId, request.X, request.Z, request.Omniscient, out Pawn pawn, out IntVec3 cell, out string err))
            {
                return ApiResult<AiFloatMenuInvokeResultDto>.ValidationFail(err);
            }

            using (new SelectionScope(pawn))
            {
                var options = FloatMenuMakerMap.GetOptions(new List<Pawn> { pawn }, cell.ToVector3Shifted(), out _) ?? new List<FloatMenuOption>();
                int chosenIndex = -1;
                FloatMenuOption chosen = null;

                if (request.OptionIndex.HasValue)
                {
                    chosenIndex = request.OptionIndex.Value;
                    if (chosenIndex < 0 || chosenIndex >= options.Count)
                    {
                        return ApiResult<AiFloatMenuInvokeResultDto>.ValidationFail($"option_index {chosenIndex} out of range (0..{options.Count - 1})");
                    }
                    chosen = options[chosenIndex];
                }
                else
                {
                    string target = request.OptionLabel.Trim();
                    chosenIndex = options.FindIndex(o => string.Equals(o.Label, target, StringComparison.OrdinalIgnoreCase));
                    if (chosenIndex < 0)
                    {
                        chosenIndex = options.FindIndex(o => o.Label != null && o.Label.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0);
                    }
                    if (chosenIndex < 0)
                    {
                        return ApiResult<AiFloatMenuInvokeResultDto>.Fail($"Float menu option not found: {request.OptionLabel}");
                    }
                    chosen = options[chosenIndex];
                }

                if (chosen.Disabled)
                {
                    return ApiResult<AiFloatMenuInvokeResultDto>.ValidationFail($"Float menu option '{chosen.Label}' is disabled");
                }

                if (chosen.action == null)
                {
                    return ApiResult<AiFloatMenuInvokeResultDto>.ValidationFail($"Float menu option '{chosen.Label}' has no executable action");
                }

                chosen.action.Invoke();

                return ApiResult<AiFloatMenuInvokeResultDto>.Ok(new AiFloatMenuInvokeResultDto
                {
                    PawnId = pawn.thingIDNumber,
                    PawnName = pawn.LabelShort,
                    OptionIndex = chosenIndex,
                    Label = chosen.Label,
                    Invoked = true,
                    AssignedJob = pawn.CurJobDef?.defName
                });
            }
        }

        public ApiResult<List<AiGizmoDto>> GetGizmos(int? mapId, int? pawnId, int? thingId, int? zoneId, bool omniscient)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return ApiResult<List<AiGizmoDto>>.ServiceUnavailable("Game is not currently playing.");
            }

            if (!TryResolveSelectable(mapId, pawnId, thingId, zoneId, omniscient, out _, out var gizmos, out string err, out bool isNotFound))
            {
                return isNotFound ? ApiResult<List<AiGizmoDto>>.Fail(err) : ApiResult<List<AiGizmoDto>>.ValidationFail(err);
            }

            var list = new List<AiGizmoDto>(gizmos.Count);
            for (int i = 0; i < gizmos.Count; i++)
            {
                list.Add(ToGizmoDto(gizmos[i], i));
            }
            return ApiResult<List<AiGizmoDto>>.Ok(list);
        }

        public ApiResult<AiGizmoInvokeResultDto> InvokeGizmo(AiGizmoInvokeRequestDto request)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return ApiResult<AiGizmoInvokeResultDto>.ServiceUnavailable("Game is not currently playing.");
            }

            if (request == null || (!request.GizmoIndex.HasValue && string.IsNullOrWhiteSpace(request.GizmoLabel)))
            {
                return ApiResult<AiGizmoInvokeResultDto>.ValidationFail("gizmo_index or gizmo_label is required");
            }

            if (!TryResolveSelectable(request.MapId == 0 ? (int?)null : request.MapId, request.PawnId, request.ThingId, request.ZoneId, request.Omniscient, out object selectedTarget, out var gizmos, out string err, out bool isNotFound))
            {
                return isNotFound ? ApiResult<AiGizmoInvokeResultDto>.Fail(err) : ApiResult<AiGizmoInvokeResultDto>.ValidationFail(err);
            }

            int idx = -1;
            Gizmo chosen = null;

            if (request.GizmoIndex.HasValue)
            {
                idx = request.GizmoIndex.Value;
                if (idx < 0 || idx >= gizmos.Count)
                {
                    return ApiResult<AiGizmoInvokeResultDto>.ValidationFail($"gizmo_index {idx} out of range (0..{gizmos.Count - 1})");
                }
                chosen = gizmos[idx];
            }
            else
            {
                string label = request.GizmoLabel.Trim();
                idx = gizmos.FindIndex(g => (g as Command)?.LabelCap.ToString().Equals(label, StringComparison.OrdinalIgnoreCase) == true);
                if (idx < 0)
                {
                    idx = gizmos.FindIndex(g => (g as Command)?.Label.Equals(label, StringComparison.OrdinalIgnoreCase) == true);
                }
                if (idx < 0)
                {
                    return ApiResult<AiGizmoInvokeResultDto>.Fail($"Gizmo not found: {request.GizmoLabel}");
                }
                chosen = gizmos[idx];
            }

            if (chosen is Command cmd && cmd.Disabled)
            {
                return ApiResult<AiGizmoInvokeResultDto>.ValidationFail($"Gizmo '{cmd.LabelCap}' is disabled: {cmd.disabledReason}");
            }

            bool invoked = false;
            using (new SelectionScope(selectedTarget))
            {
                if (chosen is Command_Toggle toggle && toggle.toggleAction != null)
                {
                    toggle.toggleAction.Invoke();
                    invoked = true;
                }
                else if (chosen is Command_Action actionCmd && actionCmd.action != null)
                {
                    actionCmd.action.Invoke();
                    invoked = true;
                }
                else if (chosen is Command command)
                {
                    command.ProcessInput(Event.current ?? new Event());
                    invoked = true;
                }
                else
                {
                    return ApiResult<AiGizmoInvokeResultDto>.ValidationFail($"Gizmo of type '{chosen.GetType().Name}' cannot be invoked via API");
                }
            }

            return ApiResult<AiGizmoInvokeResultDto>.Ok(new AiGizmoInvokeResultDto
            {
                GizmoIndex = idx,
                Label = (chosen as Command)?.LabelCap.ToString() ?? chosen.ToString(),
                GizmoType = chosen.GetType().Name,
                Invoked = invoked,
                IsActive = (chosen as Command_Toggle)?.isActive?.Invoke()
            });
        }

        public ApiResult<AiMechWorkModeResultDto> SetMechWorkMode(AiMechWorkModeRequestDto request)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return ApiResult<AiMechWorkModeResultDto>.ServiceUnavailable("Game is not currently playing.");
            }

            if (request == null || string.IsNullOrWhiteSpace(request.WorkMode) || (!request.MechanitorPawnId.HasValue && !request.MechPawnId.HasValue))
            {
                return ApiResult<AiMechWorkModeResultDto>.ValidationFail("work_mode and either mechanitor_pawn_id or mech_pawn_id are required");
            }

            if (request.MechPawnId.HasValue && TryHandleModRobotWorkMode(request, out ApiResult<AiMechWorkModeResultDto> robotResult))
            {
                return robotResult;
            }

            MechWorkModeDef modeDef = DefDatabase<MechWorkModeDef>.AllDefsListForReading
                .FirstOrDefault(m => m.defName.Equals(request.WorkMode.Trim(), StringComparison.OrdinalIgnoreCase)
                                  || m.label.Equals(request.WorkMode.Trim(), StringComparison.OrdinalIgnoreCase));
            if (modeDef == null)
            {
                string valid = string.Join(", ", DefDatabase<MechWorkModeDef>.AllDefsListForReading.Select(m => m.defName));
                return ApiResult<AiMechWorkModeResultDto>.ValidationFail($"Invalid mech work_mode '{request.WorkMode}'. Valid: {valid}");
            }

            Pawn mechanitor = null;
            MechanitorControlGroup group = null;

            if (request.MechPawnId.HasValue)
            {
                Pawn mech = PawnHelper.FindPawnById(request.MechPawnId.Value);
                if (mech == null)
                {
                    return ApiResult<AiMechWorkModeResultDto>.Fail($"Mech not found: {request.MechPawnId.Value}");
                }
                if (Find.Maps != null)
                {
                    foreach (var m in Find.Maps)
                    {
                        foreach (var col in m.mapPawns.FreeColonistsSpawned)
                        {
                            if (col.mechanitor?.controlGroups == null) continue;
                            var matched = col.mechanitor.controlGroups.FirstOrDefault(cg => cg.MechsForReading.Contains(mech));
                            if (matched != null)
                            {
                                mechanitor = col;
                                group = matched;
                                break;
                            }
                        }
                        if (group != null) break;
                    }
                }
                if (mechanitor == null || group == null)
                {
                    return ApiResult<AiMechWorkModeResultDto>.ValidationFail($"Mech {mech.LabelShort} does not currently belong to a colony mechanitor control group");
                }
            }
            else
            {
                mechanitor = PawnHelper.FindPawnById(request.MechanitorPawnId.Value);
                if (mechanitor == null)
                {
                    return ApiResult<AiMechWorkModeResultDto>.Fail($"Mechanitor pawn not found: {request.MechanitorPawnId.Value}");
                }
                if (mechanitor.mechanitor == null)
                {
                    return ApiResult<AiMechWorkModeResultDto>.ValidationFail($"Pawn {mechanitor.LabelShort} is not a mechanitor");
                }
                int targetIndex = request.ControlGroupIndex ?? 0;
                group = mechanitor.mechanitor.controlGroups?.FirstOrDefault(cg => cg.Index == targetIndex);
            }

            if (group == null)
            {
                return ApiResult<AiMechWorkModeResultDto>.Fail("Mechanitor control group not found");
            }

            group.SetWorkMode(modeDef);

            return ApiResult<AiMechWorkModeResultDto>.Ok(new AiMechWorkModeResultDto
            {
                MechanitorPawnId = mechanitor.thingIDNumber,
                MechanitorName = mechanitor.LabelShort,
                ControlGroupIndex = group.Index,
                WorkMode = group.WorkMode?.defName,
                MechIds = group.MechsForReading.Select(m => m.thingIDNumber).ToList()
            });
        }

        public ApiResult<List<AiPawnAbilityDto>> GetPawnAbilities(int pawnId)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return ApiResult<List<AiPawnAbilityDto>>.ServiceUnavailable("Game is not currently playing.");
            }

            if (pawnId <= 0)
            {
                return ApiResult<List<AiPawnAbilityDto>>.ValidationFail("pawn_id must be a positive integer");
            }

            Pawn pawn = PawnHelper.FindPawnById(pawnId);
            if (pawn == null)
            {
                return ApiResult<List<AiPawnAbilityDto>>.Fail($"Pawn not found: {pawnId}");
            }

            var list = new List<AiPawnAbilityDto>();

            if (pawn.abilities?.abilities != null)
            {
                foreach (var a in pawn.abilities.abilities)
                {
                    if (a == null) continue;
                    string reason = null;
                    bool disabled = false;
                    try
                    {
                        disabled = a.GizmoDisabled(out reason);
                    }
                    catch
                    {
                        disabled = !a.CanCast;
                    }

                    list.Add(new AiPawnAbilityDto
                    {
                        DefName = a.def?.defName,
                        Label = a.def?.LabelCap.ToString(),
                        Source = "vanilla",
                        CanCast = a.CanCast,
                        DisabledReason = disabled ? reason : null,
                        Range = a.verb?.verbProps?.range ?? 0f,
                        Radius = a.def?.EffectRadius ?? 0f
                    });
                }
            }

            ThingComp vefComp = AiSnapshotService.FindCompByFullName(pawn, "VEF.Abilities.CompAbilities");
            if (vefComp != null && AiSnapshotService.TryGetPropertyValue(vefComp, "LearnedAbilities", out IEnumerable learned) && learned != null)
            {
                foreach (object vefAbility in learned)
                {
                    if (vefAbility == null) continue;
                    AiSnapshotService.TryGetFieldValue(vefAbility, "def", out Def vefDef);

                    bool canCast = true;
                    string disabledReason = null;
                    MethodInfo isEnabledMethod = vefAbility.GetType().GetMethod(
                        "IsEnabledForPawn",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new[] { typeof(string).MakeByRefType() },
                        null);
                    if (isEnabledMethod != null)
                    {
                        object[] args = new object[] { null };
                        if (isEnabledMethod.Invoke(vefAbility, args) is bool enabled)
                        {
                            canCast = enabled;
                        }
                        disabledReason = args[0] as string;
                    }

                    float range = AiSnapshotService.TryInvokeMethod(vefAbility, "GetRangeForPawn", out float r) ? r : 0f;
                    float radius = AiSnapshotService.TryInvokeMethod(vefAbility, "GetRadiusForPawn", out float rad) ? rad : 0f;

                    list.Add(new AiPawnAbilityDto
                    {
                        DefName = vefDef?.defName,
                        Label = vefDef?.LabelCap.ToString() ?? vefDef?.label,
                        Source = "vef",
                        CanCast = canCast,
                        DisabledReason = canCast ? null : disabledReason,
                        Range = range,
                        Radius = radius
                    });
                }
            }

            return ApiResult<List<AiPawnAbilityDto>>.Ok(list);
        }

        public ApiResult<AiPawnAbilityResultDto> CastAbility(AiPawnAbilityRequestDto request)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return ApiResult<AiPawnAbilityResultDto>.ServiceUnavailable("Game is not currently playing.");
            }

            if (request == null || request.PawnId <= 0 || string.IsNullOrWhiteSpace(request.AbilityDefName))
            {
                return ApiResult<AiPawnAbilityResultDto>.ValidationFail("pawn_id and ability_def_name are required");
            }

            Pawn pawn = PawnHelper.FindPawnById(request.PawnId);
            if (pawn == null)
            {
                return ApiResult<AiPawnAbilityResultDto>.Fail($"Pawn not found: {request.PawnId}");
            }

            if (pawn.Map == null || pawn.Dead || pawn.Downed)
            {
                return ApiResult<AiPawnAbilityResultDto>.ValidationFail($"Pawn {pawn.LabelShort} cannot cast abilities right now");
            }

            if (!TryResolveAbilityTargets(pawn, request, out LocalTargetInfo target, out LocalTargetInfo destination, out string targetErr, out bool targetNotFound))
            {
                return targetNotFound
                    ? ApiResult<AiPawnAbilityResultDto>.Fail(targetErr)
                    : ApiResult<AiPawnAbilityResultDto>.ValidationFail(targetErr);
            }

            string abilityKey = request.AbilityDefName.Trim();
            Ability vanillaAbility = pawn.abilities?.abilities?
                .FirstOrDefault(a => a.def.defName.Equals(abilityKey, StringComparison.OrdinalIgnoreCase)
                                  || (a.def.label != null && a.def.label.Equals(abilityKey, StringComparison.OrdinalIgnoreCase))
                                  || a.def.LabelCap.ToString().Equals(abilityKey, StringComparison.OrdinalIgnoreCase));

            if (vanillaAbility != null)
            {
                if (!vanillaAbility.CanCast)
                {
                    return ApiResult<AiPawnAbilityResultDto>.ValidationFail($"Ability '{vanillaAbility.def.defName}' cannot be cast right now");
                }

                vanillaAbility.QueueCastingJob(target, destination);

                return ApiResult<AiPawnAbilityResultDto>.Ok(new AiPawnAbilityResultDto
                {
                    PawnId = pawn.thingIDNumber,
                    PawnName = pawn.LabelShort,
                    AbilityDefName = vanillaAbility.def.defName,
                    Source = "vanilla",
                    Queued = true
                });
            }

            // Soft bridge for VEF.Abilities / Vanilla Psycasts Expanded
            ThingComp vefComp = AiSnapshotService.FindCompByFullName(pawn, "VEF.Abilities.CompAbilities");
            object matchedVef = null;
            Def matchedDef = null;

            if (vefComp != null && AiSnapshotService.TryGetPropertyValue(vefComp, "LearnedAbilities", out IEnumerable learned) && learned != null)
            {
                foreach (object vefAbility in learned)
                {
                    if (vefAbility == null) continue;
                    if (!AiSnapshotService.TryGetFieldValue(vefAbility, "def", out Def vefDef) || vefDef == null) continue;

                    if (vefDef.defName.Equals(abilityKey, StringComparison.OrdinalIgnoreCase)
                        || (vefDef.label != null && vefDef.label.Equals(abilityKey, StringComparison.OrdinalIgnoreCase))
                        || vefDef.LabelCap.ToString().Equals(abilityKey, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedVef = vefAbility;
                        matchedDef = vefDef;
                        break;
                    }
                }
            }

            if (matchedVef == null)
            {
                return ApiResult<AiPawnAbilityResultDto>.Fail($"Ability '{request.AbilityDefName}' not found on pawn {pawn.LabelShort}");
            }

            MethodInfo isEnabledMethod = matchedVef.GetType().GetMethod(
                "IsEnabledForPawn",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(string).MakeByRefType() },
                null);
            if (isEnabledMethod != null)
            {
                object[] args = new object[] { null };
                if (isEnabledMethod.Invoke(matchedVef, args) is bool enabled && !enabled)
                {
                    string reason = args[0] as string;
                    string suffix = string.IsNullOrEmpty(reason) ? "" : $": {reason}";
                    return ApiResult<AiPawnAbilityResultDto>.ValidationFail($"Ability '{matchedDef.defName}' cannot be cast right now{suffix}");
                }
            }

            if (request.TargetPawnId.HasValue || request.TargetPosition != null)
            {
                MethodInfo canHitMethod = matchedVef.GetType().GetMethod(
                    "CanHitTarget",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(LocalTargetInfo) },
                    null);
                if (canHitMethod != null && canHitMethod.Invoke(matchedVef, new object[] { target }) is bool canHit && !canHit)
                {
                    return ApiResult<AiPawnAbilityResultDto>.ValidationFail($"Ability '{matchedDef.defName}' cannot hit the specified target (range, line of sight, or target constraints)");
                }
            }

            GlobalTargetInfo[] globalTargets = destination.IsValid
                ? new[] { target.ToGlobalTargetInfo(pawn.Map), destination.ToGlobalTargetInfo(pawn.Map) }
                : new[] { target.ToGlobalTargetInfo(pawn.Map) };

            using (new SelectionScope(pawn))
            {
                MethodInfo createCastJob = matchedVef.GetType().GetMethod(
                    "CreateCastJob",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(GlobalTargetInfo[]) },
                    null);
                if (createCastJob != null)
                {
                    createCastJob.Invoke(matchedVef, new object[] { globalTargets });
                }
                else
                {
                    MethodInfo startAbilityJob = matchedVef.GetType().GetMethod(
                        "StartAbilityJob",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new[] { typeof(GlobalTargetInfo[]) },
                        null);
                    if (startAbilityJob == null)
                    {
                        return ApiResult<AiPawnAbilityResultDto>.Fail($"VEF ability '{matchedDef.defName}' does not expose CreateCastJob or StartAbilityJob");
                    }
                    startAbilityJob.Invoke(matchedVef, new object[] { globalTargets });
                }
            }

            return ApiResult<AiPawnAbilityResultDto>.Ok(new AiPawnAbilityResultDto
            {
                PawnId = pawn.thingIDNumber,
                PawnName = pawn.LabelShort,
                AbilityDefName = matchedDef.defName,
                Source = "vef",
                Queued = true
            });
        }

        public ApiResult<AiHospitalityGuestResultDto> UpdateHospitalityGuest(AiHospitalityGuestRequestDto request)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return ApiResult<AiHospitalityGuestResultDto>.ServiceUnavailable("Game is not currently playing.");
            }

            if (request == null || request.PawnId <= 0)
            {
                return ApiResult<AiHospitalityGuestResultDto>.ValidationFail("pawn_id is required");
            }

            Pawn pawn = PawnHelper.FindPawnById(request.PawnId);
            if (pawn == null)
            {
                return ApiResult<AiHospitalityGuestResultDto>.Fail($"Pawn not found: {request.PawnId}");
            }

            ThingComp compGuest = AiSnapshotService.FindCompByFullName(pawn, "Hospitality.CompGuest");
            if (compGuest == null)
            {
                return ApiResult<AiHospitalityGuestResultDto>.ValidationFail($"Hospitality CompGuest not found on pawn {pawn.LabelShort} (is Hospitality active and is this a guest?)");
            }

            if (request.GuestAreaId.HasValue || request.GuestArea != null)
            {
                if (!TryResolveMapArea(pawn.Map, request.GuestAreaId, request.GuestArea, out Area guestArea, out string err))
                {
                    return ApiResult<AiHospitalityGuestResultDto>.ValidationFail(err);
                }
                AiSnapshotService.SetPropertyValue(compGuest, "GuestArea", guestArea);
            }

            if (request.ShoppingAreaId.HasValue || request.ShoppingArea != null)
            {
                if (!TryResolveMapArea(pawn.Map, request.ShoppingAreaId, request.ShoppingArea, out Area shoppingArea, out string err))
                {
                    return ApiResult<AiHospitalityGuestResultDto>.ValidationFail(err);
                }
                AiSnapshotService.SetPropertyValue(compGuest, "ShoppingArea", shoppingArea);
            }

            if (request.Entertain.HasValue)
            {
                AiSnapshotService.SetFieldValue(compGuest, "entertain", request.Entertain.Value);
            }

            if (request.MakeFriends.HasValue)
            {
                AiSnapshotService.SetFieldValue(compGuest, "makeFriends", request.MakeFriends.Value);
            }

            bool sentHome = false;
            if (request.SendHome == true)
            {
                AiSnapshotService.SetFieldValue(compGuest, "sentAway", true);
                AiSnapshotService.InvokeVoidMethod(compGuest, "Leave", false);
                sentHome = true;
            }

            AiSnapshotService.TryGetFieldValue(compGuest, "entertain", out bool entertainNow);
            AiSnapshotService.TryGetFieldValue(compGuest, "makeFriends", out bool makeFriendsNow);
            AiSnapshotService.TryGetPropertyValue(compGuest, "GuestArea", out Area guestAreaNow);
            AiSnapshotService.TryGetPropertyValue(compGuest, "ShoppingArea", out Area shoppingAreaNow);

            return ApiResult<AiHospitalityGuestResultDto>.Ok(new AiHospitalityGuestResultDto
            {
                PawnId = pawn.thingIDNumber,
                PawnName = pawn.LabelShort,
                Entertain = entertainNow,
                MakeFriends = makeFriendsNow,
                GuestArea = guestAreaNow?.Label,
                ShoppingArea = shoppingAreaNow?.Label,
                SentHome = sentHome
            });
        }

        public ApiResult<List<AiHospitalityBedDto>> GetHospitalityBeds(int? mapId)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return ApiResult<List<AiHospitalityBedDto>>.ServiceUnavailable("Game is not currently playing.");
            }

            Map map = MapHelper.ResolveMap(mapId);
            if (map == null)
            {
                return ApiResult<List<AiHospitalityBedDto>>.Fail($"Map not found: {mapId?.ToString() ?? "current"}");
            }

            var list = new List<AiHospitalityBedDto>();
            if (map.listerBuildings?.allBuildingsColonist != null)
            {
                foreach (var b in map.listerBuildings.allBuildingsColonist)
                {
                    if (b == null || b.GetType().FullName != "Hospitality.Building_GuestBed") continue;
                    if (!(b is Building_Bed bed)) continue;

                    int rentalFee = AiSnapshotService.TryInvokeMethod(bed, "GetRentalFee", out int fee) ? fee : 0;
                    list.Add(new AiHospitalityBedDto
                    {
                        BedId = bed.thingIDNumber,
                        DefName = bed.def?.defName,
                        Label = bed.LabelCap.ToString(),
                        Position = new PositionDto { X = bed.Position.x, Y = bed.Position.y, Z = bed.Position.z },
                        RentalFee = rentalFee,
                        Owners = bed.OwnersForReading?.Select(o => o.LabelShort).ToList() ?? new List<string>()
                    });
                }
            }

            return ApiResult<List<AiHospitalityBedDto>>.Ok(list);
        }

        public ApiResult<AiHospitalityBedResultDto> UpdateHospitalityBed(AiHospitalityBedRequestDto request)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return ApiResult<AiHospitalityBedResultDto>.ServiceUnavailable("Game is not currently playing.");
            }

            if (request == null || request.BedId <= 0)
            {
                return ApiResult<AiHospitalityBedResultDto>.ValidationFail("bed_id is required");
            }

            Map map = MapHelper.ResolveMap(request.MapId);
            if (map == null)
            {
                return ApiResult<AiHospitalityBedResultDto>.Fail($"Map not found: {request.MapId?.ToString() ?? "current"}");
            }

            Building_Bed bed = map.listerBuildings?.allBuildingsColonist?
                .OfType<Building_Bed>()
                .FirstOrDefault(b => b.thingIDNumber == request.BedId);
            if (bed == null)
            {
                return ApiResult<AiHospitalityBedResultDto>.Fail($"Bed not found on map: {request.BedId}");
            }

            Type guestBedType = GenTypes.GetTypeInAnyAssembly("Hospitality.Building_GuestBed", null);
            if (guestBedType == null)
            {
                return ApiResult<AiHospitalityBedResultDto>.ValidationFail("Hospitality mod (Hospitality.Building_GuestBed) is not loaded");
            }

            bool isCurrentlyGuestBed = bed.GetType().FullName == "Hospitality.Building_GuestBed";
            if (request.AsGuestBed.HasValue && request.AsGuestBed.Value != isCurrentlyGuestBed)
            {
                if (request.AsGuestBed.Value && bed.ForPrisoners)
                {
                    return ApiResult<AiHospitalityBedResultDto>.ValidationFail("Cannot convert a prisoner bed into a Hospitality guest bed");
                }

                MethodInfo swapMethod = guestBedType.GetMethod(
                    "Swap",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(Building_Bed) },
                    null) ?? guestBedType.GetMethod("Swap", BindingFlags.Public | BindingFlags.Static);
                if (swapMethod == null)
                {
                    return ApiResult<AiHospitalityBedResultDto>.Fail("Hospitality.Building_GuestBed.Swap method not found");
                }

                IntVec3 pos = bed.Position;
                swapMethod.Invoke(null, new object[] { bed });
                bed = pos.GetThingList(map).OfType<Building_Bed>().FirstOrDefault() ?? bed;
                isCurrentlyGuestBed = bed.GetType().FullName == "Hospitality.Building_GuestBed";
            }

            if (request.RentalFee.HasValue)
            {
                if (!isCurrentlyGuestBed)
                {
                    return ApiResult<AiHospitalityBedResultDto>.ValidationFail("Cannot set rental_fee on a non-guest bed (pass as_guest_bed: true first)");
                }
                AiSnapshotService.InvokeVoidMethod(bed, "SetRentalFee", Math.Max(0, request.RentalFee.Value));
            }

            int fee = isCurrentlyGuestBed && AiSnapshotService.TryInvokeMethod(bed, "GetRentalFee", out int curFee)
                ? curFee
                : 0;

            return ApiResult<AiHospitalityBedResultDto>.Ok(new AiHospitalityBedResultDto
            {
                BedId = bed.thingIDNumber,
                DefName = bed.def?.defName,
                IsGuestBed = isCurrentlyGuestBed,
                RentalFee = fee
            });
        }

        public ApiResult<AiTimeAdvanceResultDto> AdvanceTime(AiTimeAdvanceRequestDto request)
        {
            if (Current.ProgramState != ProgramState.Playing || Find.TickManager == null)
            {
                return ApiResult<AiTimeAdvanceResultDto>.ServiceUnavailable("Game is not currently playing.");
            }

            if (request == null || request.Ticks <= 0 || request.Ticks > MaxAdvanceTicks)
            {
                return ApiResult<AiTimeAdvanceResultDto>.ValidationFail($"ticks must be between 1 and {MaxAdvanceTicks}");
            }

            Map map = MapHelper.ResolveMap(request.MapId);
            if (request.MapId.HasValue && map == null)
            {
                return ApiResult<AiTimeAdvanceResultDto>.Fail($"Map not found: {request.MapId.Value}");
            }

            int startTick = Find.TickManager.TicksGame;
            var initialLetterIds = new HashSet<int>();
            if (Find.LetterStack?.LettersListForReading != null)
            {
                foreach (var l in Find.LetterStack.LettersListForReading) initialLetterIds.Add(l.ID);
            }

            var initialHostileIds = SnapshotVisibleHostileIds(map, request.Omniscient);

            int ticksExecuted = 0;
            int newLettersCount = 0;
            string stopReason = "completed";
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < request.Ticks; i++)
            {
                if (i > 0 && sw.ElapsedMilliseconds >= MaxAdvanceWallClockMs)
                {
                    stopReason = "wall_clock_budget";
                    break;
                }

                Find.TickManager.DoSingleTick();
                ticksExecuted++;

                if (request.StopOnLetter && Find.LetterStack?.LettersListForReading != null)
                {
                    int newLetters = Find.LetterStack.LettersListForReading.Count(l => !initialLetterIds.Contains(l.ID));
                    if (newLetters > 0)
                    {
                        newLettersCount = newLetters;
                        stopReason = "new_letter";
                        break;
                    }
                }

                if (request.StopOnHostile && map != null)
                {
                    bool hasNewHostile = false;
                    foreach (var p in map.mapPawns.AllPawnsSpawned)
                    {
                        if (p.Dead || !p.HostileTo(Faction.OfPlayer)) continue;
                        if (!request.Omniscient && p.Position.Fogged(map)) continue;
                        if (!initialHostileIds.Contains(p.thingIDNumber))
                        {
                            hasNewHostile = true;
                            break;
                        }
                    }

                    if (hasNewHostile)
                    {
                        stopReason = "new_hostile";
                        break;
                    }
                }
            }

            sw.Stop();
            if (request.StopOnLetter && newLettersCount == 0 && Find.LetterStack?.LettersListForReading != null)
            {
                newLettersCount = Find.LetterStack.LettersListForReading.Count(l => !initialLetterIds.Contains(l.ID));
            }

            if (request.PauseAfter && !Find.TickManager.Paused)
            {
                Find.TickManager.Pause();
            }

            return ApiResult<AiTimeAdvanceResultDto>.Ok(new AiTimeAdvanceResultDto
            {
                StartTick = startTick,
                EndTick = Find.TickManager.TicksGame,
                TicksRequested = request.Ticks,
                TicksExecuted = ticksExecuted,
                StopReason = stopReason,
                ElapsedMs = sw.ElapsedMilliseconds,
                IsPaused = Find.TickManager.Paused,
                NewLettersCount = newLettersCount
            });
        }

        private static HashSet<int> SnapshotVisibleHostileIds(Map map, bool omniscient)
        {
            var ids = new HashSet<int>();
            if (map?.mapPawns?.AllPawnsSpawned == null) return ids;
            foreach (var p in map.mapPawns.AllPawnsSpawned)
            {
                if (p.Dead || !p.HostileTo(Faction.OfPlayer)) continue;
                if (!omniscient && p.Position.Fogged(map)) continue;
                ids.Add(p.thingIDNumber);
            }
            return ids;
        }

        private static Designator ResolveDesignator(AiDesignatorInvokeRequestDto request)
        {
            foreach (var cat in DefDatabase<DesignationCategoryDef>.AllDefsListForReading)
            {
                if (!string.IsNullOrWhiteSpace(request.Category) &&
                    !cat.defName.Equals(request.Category.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var designators = cat.AllResolvedDesignators;
                if (designators == null) continue;

                foreach (var d in designators)
                {
                    if (d == null) continue;
                    if (!string.IsNullOrWhiteSpace(request.BuildDefName))
                    {
                        if (d is Designator_Build bd && bd.PlacingDef != null &&
                            bd.PlacingDef.defName.Equals(request.BuildDefName.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            return d;
                        }
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(request.ClassName))
                    {
                        string cn = request.ClassName.Trim();
                        if (string.Equals(d.GetType().FullName, cn, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(d.GetType().Name, cn, StringComparison.OrdinalIgnoreCase))
                        {
                            return d;
                        }
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(request.Label) &&
                        string.Equals(d.LabelCap.ToString(), request.Label.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        return d;
                    }
                }
            }
            return null;
        }

        private static List<IntVec3> CollectTargetCells(AiDesignatorInvokeRequestDto request)
        {
            var list = new List<IntVec3>();
            if (request.Cells != null && request.Cells.Count > 0)
            {
                foreach (var pos in request.Cells)
                {
                    if (pos != null) list.Add(new IntVec3(pos.X, 0, pos.Z));
                }
            }
            else if (request.PointA != null && request.PointB != null)
            {
                var rect = CellRect.FromLimits(
                    new IntVec3(request.PointA.X, 0, request.PointA.Z),
                    new IntVec3(request.PointB.X, 0, request.PointB.Z));
                foreach (IntVec3 c in rect) list.Add(c);
            }
            else if (request.PointA != null)
            {
                list.Add(new IntVec3(request.PointA.X, 0, request.PointA.Z));
            }
            return list;
        }

        private static bool TryResolveFloatMenuCell(int pawnId, int x, int z, bool omniscient, out Pawn pawn, out IntVec3 cell, out string error)
        {
            cell = new IntVec3(x, 0, z);
            pawn = PawnHelper.FindPawnById(pawnId);
            if (pawn == null || pawn.Map == null || pawn.Dead || pawn.Downed)
            {
                error = $"Controllable spawned pawn not found: {pawnId}";
                return false;
            }
            if (!pawn.IsColonistPlayerControlled)
            {
                error = $"Pawn {pawn.LabelShort} is not player-controlled";
                return false;
            }
            if (!cell.InBounds(pawn.Map))
            {
                error = $"Cell ({x}, {z}) is out of map bounds";
                return false;
            }
            if (!omniscient && cell.Fogged(pawn.Map))
            {
                error = $"Cell ({x}, {z}) is in fog of war (pass omniscient: true to override)";
                return false;
            }
            error = null;
            return true;
        }

        private static bool TryHandleModRobotWorkMode(AiMechWorkModeRequestDto request, out ApiResult<AiMechWorkModeResultDto> result)
        {
            result = null;
            int targetId = request.MechPawnId.Value;
            Pawn robot = null;
            Thing station = null;

            if (Find.Maps != null)
            {
                foreach (var m in Find.Maps)
                {
                    if (m?.listerBuildings?.allBuildingsColonist != null)
                    {
                        foreach (var b in m.listerBuildings.allBuildingsColonist)
                        {
                            if (b == null || b.GetType().FullName != "AIRobot.X2_Building_AIRobotRechargeStation") continue;
                            AiSnapshotService.TryGetPropertyValue(b, "GetRobot", out Pawn linked);
                            if (b.thingIDNumber == targetId || (linked != null && linked.thingIDNumber == targetId))
                            {
                                station = b;
                                robot = linked;
                                break;
                            }
                        }
                    }

                    if (robot == null && m?.mapPawns?.AllPawnsSpawned != null)
                    {
                        foreach (var p in m.mapPawns.AllPawnsSpawned)
                        {
                            if (p != null && p.thingIDNumber == targetId && p.GetType().FullName == "AIRobot.X2_AIRobot")
                            {
                                robot = p;
                                AiSnapshotService.TryGetFieldValue(robot, "rechargeStation", out station);
                                break;
                            }
                        }
                    }

                    if (robot != null || station != null) break;
                }
            }

            if (robot == null && station == null)
            {
                return false;
            }

            if (station == null && robot != null)
            {
                AiSnapshotService.TryGetFieldValue(robot, "rechargeStation", out station);
            }
            if (robot == null && station != null)
            {
                AiSnapshotService.TryGetPropertyValue(station, "GetRobot", out robot);
            }

            string modeKey = request.WorkMode.Trim();
            string modeUsed;
            if (modeKey.Equals("Work", StringComparison.OrdinalIgnoreCase)
                || modeKey.Equals("Spawn", StringComparison.OrdinalIgnoreCase)
                || modeKey.Equals("Activate", StringComparison.OrdinalIgnoreCase))
            {
                if (station != null)
                {
                    AiSnapshotService.InvokeVoidMethod(station, "Button_SpawnBot");
                }
                else if (robot == null || !robot.Spawned)
                {
                    result = ApiResult<AiMechWorkModeResultDto>.ValidationFail($"Recharge station not found to activate AIRobot {targetId}");
                    return true;
                }
                modeUsed = "Work";
            }
            else if (modeKey.Equals("Recharge", StringComparison.OrdinalIgnoreCase)
                     || modeKey.Equals("DormantSelfCharge", StringComparison.OrdinalIgnoreCase)
                     || modeKey.Equals("Recall", StringComparison.OrdinalIgnoreCase)
                     || modeKey.Equals("Stop", StringComparison.OrdinalIgnoreCase))
            {
                if (station != null)
                {
                    AiSnapshotService.InvokeVoidMethod(station, "Button_CallBotForShutdown", false);
                }
                else if (robot != null)
                {
                    AiSnapshotService.InvokeVoidMethod(robot, "Button_StopAllJobs");
                    AiSnapshotService.TryInvokeMethod<bool>(robot, "TryRecallToHomeMap", out _);
                }
                modeUsed = "Recharge";
            }
            else
            {
                result = ApiResult<AiMechWorkModeResultDto>.ValidationFail($"Invalid AIRobot work_mode '{request.WorkMode}'. Valid: Work, Recharge, Stop");
                return true;
            }

            result = ApiResult<AiMechWorkModeResultDto>.Ok(new AiMechWorkModeResultDto
            {
                MechanitorPawnId = station?.thingIDNumber ?? 0,
                MechanitorName = station?.LabelShort ?? "AIRobotRechargeStation",
                ControlGroupIndex = 0,
                WorkMode = modeUsed,
                MechIds = new List<int> { robot?.thingIDNumber ?? targetId }
            });
            return true;
        }

        private static bool TryResolveAbilityTargets(
            Pawn pawn,
            AiPawnAbilityRequestDto request,
            out LocalTargetInfo target,
            out LocalTargetInfo destination,
            out string error,
            out bool isNotFound)
        {
            target = LocalTargetInfo.Invalid;
            destination = LocalTargetInfo.Invalid;
            error = null;
            isNotFound = false;

            if (request.TargetPawnId.HasValue)
            {
                Pawn targetPawn = PawnHelper.FindPawnById(request.TargetPawnId.Value);
                if (targetPawn == null || targetPawn.Map != pawn.Map)
                {
                    isNotFound = true;
                    error = $"Target pawn not found on same map: {request.TargetPawnId.Value}";
                    return false;
                }
                if (!request.Omniscient && targetPawn.Position.Fogged(pawn.Map))
                {
                    error = "Target pawn is in fog of war (pass omniscient: true to override)";
                    return false;
                }
                target = targetPawn;
            }
            else if (request.TargetPosition != null)
            {
                IntVec3 cell = new IntVec3(request.TargetPosition.X, 0, request.TargetPosition.Z);
                if (!cell.InBounds(pawn.Map))
                {
                    error = "target_position is out of map bounds";
                    return false;
                }
                if (!request.Omniscient && cell.Fogged(pawn.Map))
                {
                    error = "target_position is in fog of war (pass omniscient: true to override)";
                    return false;
                }
                target = cell;
            }
            else
            {
                target = pawn;
            }

            if (request.DestinationPosition != null)
            {
                IntVec3 destCell = new IntVec3(request.DestinationPosition.X, 0, request.DestinationPosition.Z);
                if (!destCell.InBounds(pawn.Map))
                {
                    error = "destination_position is out of map bounds";
                    return false;
                }
                if (!request.Omniscient && destCell.Fogged(pawn.Map))
                {
                    error = "destination_position is in fog of war (pass omniscient: true to override)";
                    return false;
                }
                destination = destCell;
            }

            return true;
        }

        private static bool TryResolveMapArea(Map map, int? areaId, string areaLabel, out Area area, out string error)
        {
            area = null;
            error = null;

            if (areaId.HasValue)
            {
                if (areaId.Value <= 0)
                {
                    area = null;
                    return true;
                }
                if (map?.areaManager?.AllAreas == null)
                {
                    error = "Map has no area manager";
                    return false;
                }
                area = map.areaManager.AllAreas.FirstOrDefault(a => a.ID == areaId.Value);
                if (area == null)
                {
                    error = $"Area ID not found on map: {areaId.Value}";
                    return false;
                }
                return true;
            }

            string trimmed = areaLabel?.Trim();
            if (string.IsNullOrEmpty(trimmed)
                || trimmed.Equals("None", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("Unrestricted", StringComparison.OrdinalIgnoreCase))
            {
                area = null;
                return true;
            }

            if (map?.areaManager?.AllAreas == null)
            {
                error = "Map has no area manager";
                return false;
            }

            area = map.areaManager.AllAreas.FirstOrDefault(a => string.Equals(a.Label, trimmed, StringComparison.OrdinalIgnoreCase));
            if (area == null)
            {
                error = $"Area not found on map: {areaLabel}";
                return false;
            }
            return true;
        }

        private static bool TryResolveSelectable(int? mapId, int? pawnId, int? thingId, int? zoneId, bool omniscient, out object selectedTarget, out List<Gizmo> gizmos, out string error, out bool isNotFound)
        {
            selectedTarget = null;
            gizmos = null;
            isNotFound = false;

            if (pawnId.HasValue)
            {
                Pawn pawn = PawnHelper.FindPawnById(pawnId.Value);
                if (pawn == null || pawn.Map == null)
                {
                    isNotFound = true;
                    error = $"Spawned pawn not found: {pawnId.Value}";
                    return false;
                }
                if (!omniscient && pawn.Faction != Faction.OfPlayer && pawn.Position.Fogged(pawn.Map))
                {
                    error = $"Pawn {pawnId.Value} is in fog of war (pass omniscient: true to override)";
                    return false;
                }
                selectedTarget = pawn;
                using (new SelectionScope(pawn))
                {
                    gizmos = pawn.GetGizmos()?.ToList() ?? new List<Gizmo>();
                }
                error = null;
                return true;
            }

            Map map = MapHelper.ResolveMap(mapId);
            if (map == null)
            {
                isNotFound = true;
                error = $"Map not found: {mapId?.ToString() ?? "current"}";
                return false;
            }

            if (thingId.HasValue)
            {
                Thing thing = map.listerThings.AllThings.FirstOrDefault(t => t.thingIDNumber == thingId.Value);
                if (thing == null)
                {
                    isNotFound = true;
                    error = $"Thing not found on map: {thingId.Value}";
                    return false;
                }
                if (!omniscient && thing.Faction != Faction.OfPlayer && thing.Position.Fogged(map))
                {
                    error = $"Thing {thingId.Value} is in fog of war (pass omniscient: true to override)";
                    return false;
                }
                selectedTarget = thing;
                using (new SelectionScope(thing))
                {
                    gizmos = thing.GetGizmos()?.ToList() ?? new List<Gizmo>();
                }
                error = null;
                return true;
            }

            if (zoneId.HasValue)
            {
                Zone zone = map.zoneManager.AllZones.FirstOrDefault(z => z.ID == zoneId.Value);
                if (zone == null)
                {
                    isNotFound = true;
                    error = $"Zone not found on map: {zoneId.Value}";
                    return false;
                }
                selectedTarget = zone;
                using (new SelectionScope(zone))
                {
                    gizmos = zone.GetGizmos()?.ToList() ?? new List<Gizmo>();
                }
                error = null;
                return true;
            }

            error = "One of pawn_id, thing_id, or zone_id is required";
            return false;
        }

        private static AiGizmoDto ToGizmoDto(Gizmo g, int index)
        {
            Command cmd = g as Command;
            Command_Toggle toggle = g as Command_Toggle;
            return new AiGizmoDto
            {
                Index = index,
                Label = cmd?.LabelCap.ToString() ?? g.ToString(),
                Desc = cmd?.Desc,
                GizmoType = g.GetType().Name,
                Visible = g.Visible,
                Disabled = cmd?.Disabled ?? false,
                DisabledReason = cmd?.disabledReason,
                IsActive = toggle?.isActive?.Invoke()
            };
        }

        private readonly struct SelectionScope : IDisposable
        {
            private readonly List<object> _saved;
            private readonly bool _active;

            public SelectionScope(object target)
            {
                if (target == null || Find.Selector == null || Find.Selector.SelectedObjects == null)
                {
                    _saved = null;
                    _active = false;
                    return;
                }

                _saved = new List<object>(Find.Selector.SelectedObjects);
                _active = true;
                try
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(target, playSound: false, forceDesignatorDeselect: false);
                }
                catch
                {
                    // Ignore UI selection side effects
                }
            }

            public void Dispose()
            {
                if (!_active || Find.Selector == null) return;
                try
                {
                    Find.Selector.ClearSelection();
                    foreach (var obj in _saved)
                    {
                        if (obj != null)
                        {
                            Find.Selector.Select(obj, playSound: false, forceDesignatorDeselect: false);
                        }
                    }
                }
                catch
                {
                    // Ignore UI selection restoration errors
                }
            }
        }
    }
}
