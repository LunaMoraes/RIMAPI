using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RIMAPI.Core;
using RIMAPI.Helpers;
using RIMAPI.Models;
using RimWorld;
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

            var options = FloatMenuMakerMap.GetOptions(new List<Pawn> { pawn }, cell.ToVector3Shifted(), out _) ?? new List<FloatMenuOption>();
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

        public ApiResult<List<AiGizmoDto>> GetGizmos(int? mapId, int? pawnId, int? thingId, int? zoneId, bool omniscient)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return ApiResult<List<AiGizmoDto>>.ServiceUnavailable("Game is not currently playing.");
            }

            if (!TryResolveSelectable(mapId, pawnId, thingId, zoneId, omniscient, out var gizmos, out string err, out bool isNotFound))
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

            if (!TryResolveSelectable(request.MapId == 0 ? (int?)null : request.MapId, request.PawnId, request.ThingId, request.ZoneId, request.Omniscient, out var gizmos, out string err, out bool isNotFound))
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

            if (pawn.Map == null || pawn.Dead || pawn.Downed || pawn.abilities == null)
            {
                return ApiResult<AiPawnAbilityResultDto>.ValidationFail($"Pawn {pawn.LabelShort} cannot cast abilities right now");
            }

            string abilityKey = request.AbilityDefName.Trim();
            Ability ability = pawn.abilities.abilities?
                .FirstOrDefault(a => a.def.defName.Equals(abilityKey, StringComparison.OrdinalIgnoreCase)
                                  || a.def.label.Equals(abilityKey, StringComparison.OrdinalIgnoreCase));
            if (ability == null)
            {
                return ApiResult<AiPawnAbilityResultDto>.Fail($"Ability '{request.AbilityDefName}' not found on pawn {pawn.LabelShort}");
            }

            if (!ability.CanCast)
            {
                return ApiResult<AiPawnAbilityResultDto>.ValidationFail($"Ability '{ability.def.defName}' cannot be cast right now");
            }

            LocalTargetInfo target = LocalTargetInfo.Invalid;
            if (request.TargetPawnId.HasValue)
            {
                Pawn targetPawn = PawnHelper.FindPawnById(request.TargetPawnId.Value);
                if (targetPawn == null || targetPawn.Map != pawn.Map)
                {
                    return ApiResult<AiPawnAbilityResultDto>.Fail($"Target pawn not found on same map: {request.TargetPawnId.Value}");
                }
                if (!request.Omniscient && targetPawn.Position.Fogged(pawn.Map))
                {
                    return ApiResult<AiPawnAbilityResultDto>.ValidationFail("Target pawn is in fog of war (pass omniscient: true to override)");
                }
                target = targetPawn;
            }
            else if (request.TargetPosition != null)
            {
                IntVec3 cell = new IntVec3(request.TargetPosition.X, 0, request.TargetPosition.Z);
                if (!cell.InBounds(pawn.Map))
                {
                    return ApiResult<AiPawnAbilityResultDto>.ValidationFail("target_position is out of map bounds");
                }
                if (!request.Omniscient && cell.Fogged(pawn.Map))
                {
                    return ApiResult<AiPawnAbilityResultDto>.ValidationFail("target_position is in fog of war (pass omniscient: true to override)");
                }
                target = cell;
            }
            else
            {
                target = pawn;
            }

            LocalTargetInfo destination = LocalTargetInfo.Invalid;
            if (request.DestinationPosition != null)
            {
                IntVec3 destCell = new IntVec3(request.DestinationPosition.X, 0, request.DestinationPosition.Z);
                if (!destCell.InBounds(pawn.Map))
                {
                    return ApiResult<AiPawnAbilityResultDto>.ValidationFail("destination_position is out of map bounds");
                }
                if (!request.Omniscient && destCell.Fogged(pawn.Map))
                {
                    return ApiResult<AiPawnAbilityResultDto>.ValidationFail("destination_position is in fog of war (pass omniscient: true to override)");
                }
                destination = destCell;
            }

            ability.QueueCastingJob(target, destination);

            return ApiResult<AiPawnAbilityResultDto>.Ok(new AiPawnAbilityResultDto
            {
                PawnId = pawn.thingIDNumber,
                PawnName = pawn.LabelShort,
                AbilityDefName = ability.def.defName,
                Queued = true
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

        private static bool TryResolveSelectable(int? mapId, int? pawnId, int? thingId, int? zoneId, bool omniscient, out List<Gizmo> gizmos, out string error, out bool isNotFound)
        {
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
                gizmos = pawn.GetGizmos()?.ToList() ?? new List<Gizmo>();
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
                gizmos = thing.GetGizmos()?.ToList() ?? new List<Gizmo>();
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
                gizmos = zone.GetGizmos()?.ToList() ?? new List<Gizmo>();
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
    }
}
