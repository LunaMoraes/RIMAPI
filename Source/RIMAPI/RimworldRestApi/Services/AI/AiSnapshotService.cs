using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RIMAPI.Core;
using RIMAPI.Helpers;
using RIMAPI.Models;
using RIMAPI.Services.Interfaces;
using RimWorld;
using UnityEngine;
using Verse;

namespace RIMAPI.Services
{
    public class AiSnapshotService : IAiSnapshotService
    {
        private readonly IUIService _uiService;

        public AiSnapshotService(IUIService uiService)
        {
            _uiService = uiService;
        }

        public ApiResult<AiSnapshotDto> GetSnapshot(int? mapId, bool omniscient = false, string include = null, int? sinceTick = null)
        {
            try
            {
                if (Current.ProgramState != ProgramState.Playing || Current.Game == null || Find.Maps == null)
                {
                    return ApiResult<AiSnapshotDto>.ServiceUnavailable("Game is not currently playing.");
                }

                Map map = MapHelper.ResolveMap(mapId);
                if (map == null)
                {
                    return ApiResult<AiSnapshotDto>.Fail($"Map not found: {mapId?.ToString() ?? "current"}");
                }

                var includeSet = ParseIncludeSet(include);
                bool includeAll = includeSet.Contains("all");
                int currentTick = Find.TickManager?.TicksGame ?? 0;

                var snapshot = new AiSnapshotDto
                {
                    MapId = map.uniqueID,
                    ProgramState = Current.ProgramState.ToString(),
                    Omniscient = omniscient,
                    SinceTick = sinceTick,
                    TicksElapsed = sinceTick.HasValue ? Math.Max(0, currentTick - sinceTick.Value) : (int?)null,
                    Time = BuildTime(map),
                    Weather = BuildWeather(map),
                    Power = BuildPower(map),
                    Resources = BuildResources(map, omniscient),
                    Colonists = BuildColonists(map),
                    Threats = BuildThreats(map, omniscient),
                    Alerts = BuildAlerts(),
                    Letters = BuildLetters(),
                    Research = BuildResearch(),
                    Zones = BuildZones(map)
                };

                if (includeAll || includeSet.Contains("game_conditions"))
                    snapshot.GameConditions = BuildGameConditions(map);
                if (includeAll || includeSet.Contains("designations"))
                    snapshot.Designations = BuildDesignationsList(map, omniscient, maxEntries: 200);
                if (includeAll || includeSet.Contains("prisoners"))
                    snapshot.Prisoners = BuildPrisoners(map);
                if (includeAll || includeSet.Contains("guests"))
                    snapshot.Guests = BuildGuests(map, omniscient);
                if (includeAll || includeSet.Contains("animals"))
                    snapshot.Animals = BuildAnimals(map, omniscient);
                if (includeAll || includeSet.Contains("mechs"))
                    snapshot.Mechs = BuildMechs(map);

                return ApiResult<AiSnapshotDto>.Ok(snapshot);
            }
            catch (Exception ex)
            {
                LogApi.Error($"Error building AI snapshot: {ex}");
                return ApiResult<AiSnapshotDto>.Fail($"Failed to build AI snapshot: {ex.Message}");
            }
        }

        public ApiResult<List<AiDesignationSummaryDto>> GetDesignations(int? mapId, bool omniscient = false)
        {
            if (Current.ProgramState != ProgramState.Playing || Current.Game == null || Find.Maps == null)
            {
                return ApiResult<List<AiDesignationSummaryDto>>.ServiceUnavailable("Game is not currently playing.");
            }

            Map map = MapHelper.ResolveMap(mapId);
            if (map == null)
            {
                return ApiResult<List<AiDesignationSummaryDto>>.Fail($"Map not found: {mapId?.ToString() ?? "current"}");
            }

            return ApiResult<List<AiDesignationSummaryDto>>.Ok(BuildDesignationsList(map, omniscient, maxEntries: null));
        }

        private static HashSet<string> ParseIncludeSet(string include)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(include)) return set;
            foreach (var part in include.Split(','))
            {
                string trimmed = part?.Trim();
                if (!string.IsNullOrEmpty(trimmed)) set.Add(trimmed);
            }
            return set;
        }

        private static AiTimeDto BuildTime(Map map)
        {
            long absTicks = Find.TickManager.TicksAbs;
            Vector2 longLat = Find.WorldGrid.LongLatOf(map.Tile);
            return new AiTimeDto
            {
                Tick = Find.TickManager.TicksGame,
                DayOfQuadrum = GenDate.DayOfQuadrum(absTicks, longLat.x) + 1,
                Quadrum = GenDate.Quadrum(absTicks, longLat.x).ToString(),
                Year = GenDate.Year(absTicks, longLat.x),
                Hour = (float)Math.Round(GenDate.HourFloat(absTicks, longLat.x), 1),
                GameSpeed = Find.TickManager.CurTimeSpeed.ToString(),
                IsPaused = Find.TickManager.Paused
            };
        }

        private static AiWeatherDto BuildWeather(Map map)
        {
            return new AiWeatherDto
            {
                Weather = map.weatherManager?.curWeather?.defName ?? "Unknown",
                TemperatureC = (float)Math.Round(map.mapTemperature?.OutdoorTemp ?? 0f, 1),
                WindSpeed = (float)Math.Round(map.windManager?.WindSpeed ?? 0f, 2)
            };
        }

        private static AiPowerDto BuildPower(Map map)
        {
            float generated = 0f;
            float consumed = 0f;
            float stored = 0f;

            if (map.powerNetManager?.AllNetsListForReading != null)
            {
                foreach (var net in map.powerNetManager.AllNetsListForReading)
                {
                    foreach (var comp in net.powerComps)
                    {
                        if (!comp.PowerOn) continue;
                        if (comp.PowerOutput > 0f) generated += comp.PowerOutput;
                        else consumed += -comp.PowerOutput;
                    }
                    foreach (var bat in net.batteryComps)
                    {
                        stored += bat.StoredEnergy;
                    }
                }
            }

            return new AiPowerDto
            {
                GeneratedW = (float)Math.Round(generated, 0),
                ConsumedW = (float)Math.Round(consumed, 0),
                NetW = (float)Math.Round(generated - consumed, 0),
                StoredWd = (float)Math.Round(stored, 1)
            };
        }

        private static AiResourceSummaryDto BuildResources(Map map, bool omniscient)
        {
            var counter = map.resourceCounter;
            if (counter == null) return new AiResourceSummaryDto();

            int meals = 0;
            foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree))
            {
                if ((!omniscient && thing.Position.Fogged(map)) || thing.IsForbidden(Faction.OfPlayer)) continue;
                if (thing.def.IsIngestible && thing.def.ingestible?.preferability >= FoodPreferability.MealAwful)
                {
                    meals += thing.stackCount;
                }
            }

            int medicine = 0;
            foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Medicine))
            {
                if ((!omniscient && thing.Position.Fogged(map)) || thing.IsForbidden(Faction.OfPlayer)) continue;
                medicine += thing.stackCount;
            }

            int storedMeals = 0;
            int storedMedicine = 0;
            var topStored = new List<AiResourceCountDto>();
            var counted = counter.AllCountedAmounts;
            if (counted != null)
            {
                foreach (var kv in counted)
                {
                    if (kv.Value <= 0 || kv.Key == null) continue;
                    if (kv.Key.IsIngestible && kv.Key.ingestible?.preferability >= FoodPreferability.MealAwful)
                    {
                        storedMeals += kv.Value;
                    }
                    if (kv.Key.IsMedicine)
                    {
                        storedMedicine += kv.Value;
                    }
                }

                topStored = counted
                    .Where(kv => kv.Key != null && kv.Value > 0)
                    .OrderByDescending(kv => kv.Value)
                    .Take(12)
                    .Select(kv => new AiResourceCountDto
                    {
                        DefName = kv.Key.defName,
                        Label = kv.Key.label,
                        Count = kv.Value
                    })
                    .ToList();
            }

            return new AiResourceSummaryDto
            {
                TotalNutrition = Mathf.RoundToInt(counter.TotalHumanEdibleNutrition),
                Meals = meals,
                StoredMeals = storedMeals,
                Wood = counter.GetCount(ThingDefOf.WoodLog),
                Steel = counter.GetCount(ThingDefOf.Steel),
                Components = counter.GetCount(ThingDefOf.ComponentIndustrial),
                Medicine = medicine,
                StoredMedicine = storedMedicine,
                Silver = counter.GetCount(ThingDefOf.Silver),
                TopStoredResources = topStored
            };
        }

        private static List<AiColonistSummaryDto> BuildColonists(Map map)
        {
            var result = new List<AiColonistSummaryDto>();
            foreach (var p in map.mapPawns.FreeColonistsSpawned)
            {
                var topSkills = new List<string>();
                if (p.skills?.skills != null)
                {
                    topSkills = p.skills.skills
                        .Where(s => !s.TotallyDisabled && s.Level > 0)
                        .OrderByDescending(s => s.Level)
                        .Take(3)
                        .Select(s => $"{s.def.defName}:{s.Level}")
                        .ToList();
                }

                var traits = p.story?.traits?.allTraits?
                    .Select(t => t.LabelCap.ToString())
                    .ToList() ?? new List<string>();

                var injuries = new List<AiInjurySummaryDto>();
                if (p.health?.hediffSet?.hediffs != null)
                {
                    foreach (var h in p.health.hediffSet.hediffs)
                    {
                        if (!h.Visible) continue;
                        if (!(h is Hediff_Injury) && !h.Bleeding && !h.TendableNow() && h.def.lethalSeverity <= 0f && !h.def.makesSickThought)
                            continue;

                        float? immunity = null;
                        var immComp = h.TryGetComp<HediffComp_Immunizable>();
                        if (immComp != null)
                        {
                            immunity = (float)Math.Round(immComp.Immunity, 2);
                        }

                        injuries.Add(new AiInjurySummaryDto
                        {
                            Label = h.LabelCap.ToString(),
                            Part = h.Part?.Label,
                            Severity = (float)Math.Round(h.Severity, 2),
                            BleedRate = (float)Math.Round(h.BleedRate, 2),
                            TendableNow = h.TendableNow(),
                            Immunity = immunity
                        });
                        if (injuries.Count >= 10) break;
                    }
                }

                result.Add(new AiColonistSummaryDto
                {
                    Id = p.thingIDNumber,
                    Name = p.LabelShort,
                    DevelopmentalStage = p.DevelopmentalStage.ToString(),
                    Position = new PositionDto { X = p.Position.x, Y = 0, Z = p.Position.z },
                    HealthPct = Mathf.RoundToInt((p.health?.summaryHealth?.SummaryHealthPercent ?? 1f) * 100f),
                    MoodPct = Mathf.RoundToInt((p.needs?.mood?.CurLevelPercentage ?? 1f) * 100f),
                    FoodPct = Mathf.RoundToInt((p.needs?.food?.CurLevelPercentage ?? 1f) * 100f),
                    RestPct = Mathf.RoundToInt((p.needs?.rest?.CurLevelPercentage ?? 1f) * 100f),
                    BleedingRate = (float)Math.Round(p.health?.hediffSet?.BleedRateTotal ?? 0f, 2),
                    IsDrafted = p.Drafted,
                    IsDowned = p.Downed,
                    InMentalBreak = p.InMentalState,
                    CurrentJob = p.CurJobDef?.defName ?? "Idle",
                    AllowedArea = p.playerSettings?.AreaRestrictionInPawnCurrentMap?.Label ?? "Unrestricted",
                    HostilityResponse = p.playerSettings?.hostilityResponse.ToString() ?? "Flee",
                    RoyalTitle = p.royalty?.MostSeniorTitle?.Label,
                    IsMechanitor = p.mechanitor != null,
                    TopSkills = topSkills,
                    Traits = traits,
                    Injuries = injuries
                });
            }
            return result;
        }

        private static AiThreatsSummaryDto BuildThreats(Map map, bool omniscient)
        {
            const int MaxHostiles = 25;
            var allHostiles = new List<AiHostileDto>();
            foreach (var p in map.mapPawns.AllPawnsSpawned)
            {
                if (p.Dead || !p.HostileTo(Faction.OfPlayer)) continue;
                if (!omniscient && p.Position.Fogged(map)) continue;

                allHostiles.Add(new AiHostileDto
                {
                    Id = p.thingIDNumber,
                    Name = p.LabelShort,
                    Faction = p.Faction?.Name ?? "None",
                    Position = new PositionDto { X = p.Position.x, Y = 0, Z = p.Position.z },
                    IsDowned = p.Downed,
                    Weapon = p.equipment?.Primary?.def?.defName
                });
            }

            return new AiThreatsSummaryDto
            {
                HostileCount = allHostiles.Count,
                TruncatedHostiles = allHostiles.Count > MaxHostiles,
                Hostiles = allHostiles.Take(MaxHostiles).ToList()
            };
        }

        private List<AiAlertDto> BuildAlerts()
        {
            var alertsResult = _uiService.GetActiveAlerts();
            if (!alertsResult.Success || alertsResult.Data == null) return new List<AiAlertDto>();

            return alertsResult.Data
                .Select(a => new AiAlertDto { Label = a.Label, Priority = a.Priority })
                .ToList();
        }

        private static List<AiLetterDto> BuildLetters()
        {
            var list = new List<AiLetterDto>();
            if (Find.LetterStack?.LettersListForReading == null) return list;

            foreach (var letter in Find.LetterStack.LettersListForReading)
            {
                list.Add(new AiLetterDto
                {
                    Id = letter.ID,
                    Label = letter.Label.ToString(),
                    Type = letter.def?.defName ?? "Letter"
                });
            }
            return list;
        }

        private static AiResearchSummaryDto BuildResearch()
        {
            var proj = Find.ResearchManager?.GetProject();
            if (proj == null)
            {
                return new AiResearchSummaryDto
                {
                    CurrentProject = null,
                    ProgressPct = 0,
                    PointsRemaining = 0
                };
            }

            float progress = Find.ResearchManager.GetProgress(proj);
            int pct = proj.CostApparent > 0f ? Mathf.RoundToInt((progress / proj.CostApparent) * 100f) : 0;
            return new AiResearchSummaryDto
            {
                CurrentProject = proj.defName,
                ProgressPct = pct,
                PointsRemaining = (float)Math.Round(Math.Max(0f, proj.CostApparent - progress), 1)
            };
        }

        private static AiZonesSummaryDto BuildZones(Map map)
        {
            int growing = 0;
            int stockpiles = 0;
            int growingCells = 0;
            int stockpileCells = 0;

            if (map.zoneManager?.AllZones != null)
            {
                foreach (var z in map.zoneManager.AllZones)
                {
                    if (z is Zone_Growing)
                    {
                        growing++;
                        growingCells += z.CellCount;
                    }
                    else if (z is Zone_Stockpile)
                    {
                        stockpiles++;
                        stockpileCells += z.CellCount;
                    }
                }
            }

            return new AiZonesSummaryDto
            {
                GrowingZones = growing,
                StockpileZones = stockpiles,
                TotalGrowingCells = growingCells,
                TotalStockpileCells = stockpileCells
            };
        }

        private static List<AiGameConditionDto> BuildGameConditions(Map map)
        {
            var list = new List<AiGameConditionDto>();
            if (map.gameConditionManager?.ActiveConditions == null) return list;
            foreach (var cond in map.gameConditionManager.ActiveConditions)
            {
                list.Add(new AiGameConditionDto
                {
                    DefName = cond.def?.defName,
                    Label = cond.LabelCap.ToString(),
                    TicksLeft = cond.TicksLeft,
                    Permanent = cond.Permanent
                });
            }
            return list;
        }

        private static List<AiDesignationSummaryDto> BuildDesignationsList(Map map, bool omniscient, int? maxEntries)
        {
            var list = new List<AiDesignationSummaryDto>();
            if (map.designationManager?.AllDesignations == null) return list;

            foreach (var des in map.designationManager.AllDesignations)
            {
                IntVec3 pos = des.target.Cell;
                if (!omniscient && pos.IsValid && pos.InBounds(map) && pos.Fogged(map)) continue;

                list.Add(new AiDesignationSummaryDto
                {
                    DefName = des.def?.defName,
                    TargetType = des.target.HasThing ? "thing" : "cell",
                    TargetId = des.target.HasThing ? des.target.Thing?.thingIDNumber : (int?)null,
                    TargetLabel = des.target.HasThing ? des.target.Thing?.LabelShort : null,
                    Position = pos.IsValid ? new PositionDto { X = pos.x, Y = pos.y, Z = pos.z } : null
                });

                if (maxEntries.HasValue && list.Count >= maxEntries.Value) break;
            }
            return list;
        }

        private static List<AiPrisonerSummaryDto> BuildPrisoners(Map map)
        {
            var list = new List<AiPrisonerSummaryDto>();
            foreach (var p in map.mapPawns.PrisonersOfColonySpawned)
            {
                list.Add(new AiPrisonerSummaryDto
                {
                    Id = p.thingIDNumber,
                    Name = p.LabelShort,
                    Position = new PositionDto { X = p.Position.x, Y = 0, Z = p.Position.z },
                    HealthPct = Mathf.RoundToInt((p.health?.summaryHealth?.SummaryHealthPercent ?? 1f) * 100f),
                    MoodPct = Mathf.RoundToInt((p.needs?.mood?.CurLevelPercentage ?? 1f) * 100f),
                    Resistance = (float)Math.Round(p.guest?.resistance ?? 0f, 2),
                    InteractionMode = p.guest?.ExclusiveInteractionMode?.defName,
                    IsWildMan = p.IsWildMan(),
                    IsDowned = p.Downed
                });
            }
            return list;
        }

        private static List<AiGuestSummaryDto> BuildGuests(Map map, bool omniscient)
        {
            Type guestUtilityType = GenTypes.GetTypeInAnyAssembly("Hospitality.Utilities.GuestUtility", null);
            int? friendsRequired = null;
            MethodInfo friendPctMethod = null;
            if (guestUtilityType != null)
            {
                try
                {
                    MethodInfo friendsReqMethod = guestUtilityType.GetMethod(
                        "FriendsRequired",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(Map) },
                        null);
                    if (friendsReqMethod != null && friendsReqMethod.Invoke(null, new object[] { map }) is int req)
                    {
                        friendsRequired = req;
                    }
                    friendPctMethod = guestUtilityType.GetMethod(
                        "GetFriendPercentage",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(Pawn) },
                        null);
                }
                catch
                {
                    // Ignore Hospitality version mismatches cleanly
                }
            }

            var list = new List<AiGuestSummaryDto>();
            foreach (var p in map.mapPawns.AllPawnsSpawned)
            {
                if (!p.RaceProps.Humanlike || p.Dead || p.Faction == Faction.OfPlayer || p.HostileTo(Faction.OfPlayer) || p.IsPrisonerOfColony)
                    continue;
                if (!omniscient && p.Position.Fogged(map))
                    continue;

                var dto = new AiGuestSummaryDto
                {
                    Id = p.thingIDNumber,
                    Name = p.LabelShort,
                    Faction = p.Faction?.Name,
                    HostFaction = p.guest?.HostFaction?.Name,
                    GuestStatus = p.guest?.GuestStatus.ToString(),
                    TraderKind = p.TraderKind?.defName,
                    Position = new PositionDto { X = p.Position.x, Y = 0, Z = p.Position.z },
                    HealthPct = Mathf.RoundToInt((p.health?.summaryHealth?.SummaryHealthPercent ?? 1f) * 100f),
                    IsDowned = p.Downed
                };

                ThingComp compGuest = FindCompByFullName(p, "Hospitality.CompGuest");
                if (compGuest != null)
                {
                    try
                    {
                        bool hasArrived = TryGetFieldValue(compGuest, "arrived", out bool arrivedVal);
                        dto.HospitalityEnabled = hasArrived ? arrivedVal : true;
                        dto.Entertain = TryGetFieldValue(compGuest, "entertain", out bool ent) ? ent : (bool?)null;
                        dto.MakeFriends = TryGetFieldValue(compGuest, "makeFriends", out bool mf) ? mf : (bool?)null;
                        dto.GuestArea = TryGetPropertyValue(compGuest, "GuestArea", out Area ga) ? ga?.Label : null;
                        dto.ShoppingArea = TryGetPropertyValue(compGuest, "ShoppingArea", out Area sa) ? sa?.Label : null;
                        dto.HasBed = TryGetPropertyValue(compGuest, "HasBed", out bool hb) ? hb : (bool?)null;
                        dto.BedId = TryGetFieldValue(compGuest, "bed", out Thing bed) ? bed?.thingIDNumber : (int?)null;
                        dto.FriendsRequired = friendsRequired;
                        if (friendPctMethod != null && friendPctMethod.Invoke(null, new object[] { p }) is float fp)
                        {
                            dto.FriendPercentage = (float)Math.Round(fp, 1);
                        }
                    }
                    catch
                    {
                        // Degrade gracefully if CompGuest internals differ
                    }
                }

                list.Add(dto);
            }
            return list;
        }

        private static List<AiAnimalSummaryDto> BuildAnimals(Map map, bool omniscient)
        {
            var list = new List<AiAnimalSummaryDto>();
            foreach (var p in map.mapPawns.AllPawnsSpawned)
            {
                if (!p.RaceProps.Animal || p.Dead) continue;
                bool isColony = p.Faction == Faction.OfPlayer;
                if (!omniscient && !isColony && p.Position.Fogged(map)) continue;

                list.Add(new AiAnimalSummaryDto
                {
                    Id = p.thingIDNumber,
                    Name = p.LabelShort,
                    DefName = p.def?.defName,
                    Tamed = isColony,
                    Position = new PositionDto { X = p.Position.x, Y = 0, Z = p.Position.z },
                    HealthPct = Mathf.RoundToInt((p.health?.summaryHealth?.SummaryHealthPercent ?? 1f) * 100f),
                    IsDowned = p.Downed,
                    Manhunter = p.InAggroMentalState
                });
            }
            return list;
        }

        private static List<AiMechSummaryDto> BuildMechs(Map map)
        {
            var mechGroupMap = new Dictionary<Pawn, (Pawn overseer, MechanitorControlGroup group)>();
            foreach (var colonist in map.mapPawns.FreeColonistsSpawned)
            {
                if (colonist.mechanitor?.controlGroups == null) continue;
                foreach (var cg in colonist.mechanitor.controlGroups)
                {
                    if (cg?.MechsForReading == null) continue;
                    foreach (var m in cg.MechsForReading)
                    {
                        mechGroupMap[m] = (colonist, cg);
                    }
                }
            }

            var list = new List<AiMechSummaryDto>();
            var seenIds = new HashSet<int>();
            foreach (var p in map.mapPawns.SpawnedColonyMechs)
            {
                if (p.Dead) continue;
                mechGroupMap.TryGetValue(p, out var info);
                seenIds.Add(p.thingIDNumber);

                list.Add(new AiMechSummaryDto
                {
                    Id = p.thingIDNumber,
                    Name = p.LabelShort,
                    DefName = p.def?.defName,
                    Position = new PositionDto { X = p.Position.x, Y = 0, Z = p.Position.z },
                    HealthPct = Mathf.RoundToInt((p.health?.summaryHealth?.SummaryHealthPercent ?? 1f) * 100f),
                    EnergyPct = p.needs?.energy != null ? Mathf.RoundToInt(p.needs.energy.CurLevelPercentage * 100f) : (int?)null,
                    OverseerId = info.overseer?.thingIDNumber,
                    ControlGroupIndex = info.group?.Index,
                    WorkMode = info.group?.WorkMode?.defName
                });
            }

            // Soft bridge for Misc. Robots (AIRobot.X2_AIRobot & AIRobot.X2_Building_AIRobotRechargeStation)
            var stationByRobotId = new Dictionary<int, Thing>();
            var candidateRobots = new List<Pawn>();

            if (map.listerBuildings?.allBuildingsColonist != null)
            {
                foreach (var b in map.listerBuildings.allBuildingsColonist)
                {
                    if (b == null || b.GetType().FullName != "AIRobot.X2_Building_AIRobotRechargeStation") continue;
                    if (TryGetPropertyValue(b, "GetRobot", out Pawn linkedBot) && linkedBot != null)
                    {
                        stationByRobotId[linkedBot.thingIDNumber] = b;
                        candidateRobots.Add(linkedBot);
                    }
                }
            }

            foreach (var p in map.mapPawns.AllPawnsSpawned)
            {
                if (p == null || p.Dead || p.Faction != Faction.OfPlayer) continue;
                if (p.GetType().FullName == "AIRobot.X2_AIRobot")
                {
                    candidateRobots.Add(p);
                }
            }

            foreach (var bot in candidateRobots)
            {
                if (bot == null || bot.Dead || !seenIds.Add(bot.thingIDNumber)) continue;
                if (bot.Faction != null && bot.Faction != Faction.OfPlayer) continue;

                stationByRobotId.TryGetValue(bot.thingIDNumber, out Thing station);
                if (station == null)
                {
                    TryGetFieldValue(bot, "rechargeStation", out station);
                }

                IntVec3 pos = bot.Spawned
                    ? bot.Position
                    : (station != null && station.Spawned ? station.Position : IntVec3.Invalid);

                int? energyPct = null;
                if (bot.needs?.rest != null)
                {
                    energyPct = Mathf.RoundToInt(bot.needs.rest.CurLevelPercentage * 100f);
                }
                else if (bot.needs?.AllNeeds != null && bot.needs.AllNeeds.Count > 0)
                {
                    energyPct = Mathf.RoundToInt(bot.needs.AllNeeds[0].CurLevelPercentage * 100f);
                }

                string workMode = !bot.Spawned
                    ? "Recharging"
                    : (TryInvokeMethod(bot, "HasActiveJobs", out bool hasJobs) && hasJobs ? "Working" : "Idle");

                list.Add(new AiMechSummaryDto
                {
                    Id = bot.thingIDNumber,
                    Name = bot.LabelShort,
                    DefName = bot.def?.defName,
                    Position = pos.IsValid ? new PositionDto { X = pos.x, Y = pos.y, Z = pos.z } : null,
                    HealthPct = Mathf.RoundToInt((bot.health?.summaryHealth?.SummaryHealthPercent ?? 1f) * 100f),
                    EnergyPct = energyPct,
                    WorkMode = workMode,
                    IsModRobot = true,
                    RechargeStationId = station?.thingIDNumber
                });
            }

            return list;
        }

        internal static ThingComp FindCompByFullName(ThingWithComps thing, string fullName)
        {
            if (thing?.AllComps == null || string.IsNullOrEmpty(fullName)) return null;
            foreach (var comp in thing.AllComps)
            {
                if (comp != null && comp.GetType().FullName == fullName)
                {
                    return comp;
                }
            }
            return null;
        }

        internal static bool TryGetFieldValue<T>(object target, string fieldName, out T value)
        {
            value = default;
            FieldInfo fi = FindField(target?.GetType(), fieldName);
            if (fi == null) return false;
            object raw = fi.GetValue(target);
            if (raw is T typed)
            {
                value = typed;
                return true;
            }
            return raw == null && !typeof(T).IsValueType;
        }

        internal static bool SetFieldValue(object target, string fieldName, object value)
        {
            FieldInfo fi = FindField(target?.GetType(), fieldName);
            if (fi == null) return false;
            fi.SetValue(target, value);
            return true;
        }

        internal static bool TryGetPropertyValue<T>(object target, string propName, out T value)
        {
            value = default;
            PropertyInfo pi = FindProperty(target?.GetType(), propName);
            if (pi == null || !pi.CanRead) return false;
            object raw = pi.GetValue(target, null);
            if (raw is T typed)
            {
                value = typed;
                return true;
            }
            return raw == null && !typeof(T).IsValueType;
        }

        internal static bool SetPropertyValue(object target, string propName, object value)
        {
            PropertyInfo pi = FindProperty(target?.GetType(), propName);
            if (pi == null || !pi.CanWrite) return false;
            pi.SetValue(target, value, null);
            return true;
        }

        internal static bool TryInvokeMethod<T>(object target, string methodName, out T value, params object[] args)
        {
            value = default;
            if (target == null) return false;
            MethodInfo mi = FindInstanceMethod(target.GetType(), methodName, args);
            if (mi == null) return false;
            object raw = mi.Invoke(target, args);
            if (raw is T typed)
            {
                value = typed;
                return true;
            }
            return raw == null && !typeof(T).IsValueType;
        }

        internal static bool InvokeVoidMethod(object target, string methodName, params object[] args)
        {
            if (target == null) return false;
            MethodInfo mi = FindInstanceMethod(target.GetType(), methodName, args);
            if (mi == null) return false;
            mi.Invoke(target, args);
            return true;
        }

        private static FieldInfo FindField(Type type, string fieldName)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo fi = t.GetField(fieldName, flags);
                if (fi != null) return fi;
            }
            return null;
        }

        private static PropertyInfo FindProperty(Type type, string propName)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            for (Type t = type; t != null; t = t.BaseType)
            {
                PropertyInfo pi = t.GetProperty(propName, flags);
                if (pi != null) return pi;
            }
            return null;
        }

        private static MethodInfo FindInstanceMethod(Type type, string methodName, object[] args)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            int argCount = args?.Length ?? 0;
            foreach (MethodInfo m in type.GetMethods(flags))
            {
                if (!string.Equals(m.Name, methodName, StringComparison.Ordinal)) continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length != argCount) continue;
                bool match = true;
                for (int i = 0; i < argCount; i++)
                {
                    if (args[i] == null)
                    {
                        if (ps[i].ParameterType.IsValueType && Nullable.GetUnderlyingType(ps[i].ParameterType) == null)
                        {
                            match = false;
                            break;
                        }
                    }
                    else if (!ps[i].ParameterType.IsInstanceOfType(args[i]))
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return m;
            }
            return null;
        }
    }
}
