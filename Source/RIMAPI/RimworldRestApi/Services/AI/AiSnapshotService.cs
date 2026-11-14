using System;
using System.Collections.Generic;
using System.Linq;
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

        public ApiResult<AiSnapshotDto> GetSnapshot(int? mapId)
        {
            try
            {
                if (Current.ProgramState != ProgramState.Playing || Current.Game == null || Find.Maps == null)
                {
                    return ApiResult<AiSnapshotDto>.Fail("Game is not currently playing.");
                }

                Map map = mapId.HasValue ? MapHelper.GetMapByID(mapId.Value) : Find.CurrentMap;
                if (map == null)
                {
                    return ApiResult<AiSnapshotDto>.Fail($"Map not found: {mapId?.ToString() ?? "current"}");
                }

                var snapshot = new AiSnapshotDto
                {
                    MapId = map.uniqueID,
                    ProgramState = Current.ProgramState.ToString(),
                    Time = BuildTime(map),
                    Weather = BuildWeather(map),
                    Power = BuildPower(map),
                    Resources = BuildResources(map),
                    Colonists = BuildColonists(map),
                    Threats = BuildThreats(map),
                    Alerts = BuildAlerts(),
                    Letters = BuildLetters(),
                    Research = BuildResearch(),
                    Zones = BuildZones(map)
                };

                return ApiResult<AiSnapshotDto>.Ok(snapshot);
            }
            catch (Exception ex)
            {
                LogApi.Error($"Error building AI snapshot: {ex}");
                return ApiResult<AiSnapshotDto>.Fail($"Failed to build AI snapshot: {ex.Message}");
            }
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

        private static AiResourceSummaryDto BuildResources(Map map)
        {
            var counter = map.resourceCounter;
            if (counter == null) return new AiResourceSummaryDto();

            int meals = 0;
            foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree))
            {
                if (thing.def.IsIngestible && thing.def.ingestible?.preferability >= FoodPreferability.MealAwful)
                {
                    meals += thing.stackCount;
                }
            }

            int medicine = 0;
            foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Medicine))
            {
                medicine += thing.stackCount;
            }

            return new AiResourceSummaryDto
            {
                TotalNutrition = Mathf.RoundToInt(counter.TotalHumanEdibleNutrition),
                Meals = meals,
                Wood = counter.GetCount(ThingDefOf.WoodLog),
                Steel = counter.GetCount(ThingDefOf.Steel),
                Components = counter.GetCount(ThingDefOf.ComponentIndustrial),
                Medicine = medicine,
                Silver = counter.GetCount(ThingDefOf.Silver)
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

                result.Add(new AiColonistSummaryDto
                {
                    Id = p.thingIDNumber,
                    Name = p.LabelShort,
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
                    TopSkills = topSkills
                });
            }
            return result;
        }

        private static AiThreatsSummaryDto BuildThreats(Map map)
        {
            var hostiles = new List<AiHostileDto>();
            foreach (var p in map.mapPawns.AllPawnsSpawned)
            {
                if (p.Dead || !p.HostileTo(Faction.OfPlayer)) continue;
                hostiles.Add(new AiHostileDto
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
                HostileCount = hostiles.Count,
                Hostiles = hostiles
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
    }
}
