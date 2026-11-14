using System;
using System.Linq;
using RIMAPI.Core;
using RIMAPI.Helpers;
using RIMAPI.Models;
using RimWorld;
using Verse;

namespace RIMAPI.Services
{
    public class ExperimentService : IExperimentService
    {
        private readonly IDashboardService _dashboardService;

        public ExperimentService(IDashboardService dashboardService)
        {
            _dashboardService = dashboardService;
        }

        public ApiResult<ExperimentIncidentResultDto> TriggerIncident(ExperimentIncidentRequestDto request)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return ApiResult<ExperimentIncidentResultDto>.Fail("Game is not currently playing.");
            }

            if (request == null || string.IsNullOrWhiteSpace(request.IncidentDef))
            {
                return ApiResult<ExperimentIncidentResultDto>.ValidationFail("incident_def is required");
            }

            Map map = request.MapId.HasValue ? MapHelper.GetMapByID(request.MapId.Value) : Find.CurrentMap;
            if (map == null)
            {
                return ApiResult<ExperimentIncidentResultDto>.Fail($"Map not found: {request.MapId?.ToString() ?? "current"}");
            }

            IncidentDef def = DefDatabase<IncidentDef>.AllDefsListForReading
                .FirstOrDefault(d => d.defName.Equals(request.IncidentDef.Trim(), StringComparison.OrdinalIgnoreCase));
            if (def == null)
            {
                return ApiResult<ExperimentIncidentResultDto>.Fail($"IncidentDef not found: {request.IncidentDef}");
            }

            IncidentParms parms = StorytellerUtility.DefaultParmsNow(def.category, map);
            if (request.Points.HasValue)
            {
                parms.points = request.Points.Value;
            }
            parms.forced = true;

            bool fired = def.Worker.TryExecute(parms);
            return ApiResult<ExperimentIncidentResultDto>.Ok(new ExperimentIncidentResultDto
            {
                IncidentDef = def.defName,
                MapId = map.uniqueID,
                PointsUsed = parms.points,
                Fired = fired
            });
        }

        public ApiResult<ExperimentSpeedResultDto> SetSpeed(ExperimentSpeedRequestDto request)
        {
            if (Current.ProgramState != ProgramState.Playing || Find.TickManager == null)
            {
                return ApiResult<ExperimentSpeedResultDto>.Fail("Game is not currently playing.");
            }

            if (request == null || string.IsNullOrWhiteSpace(request.Speed))
            {
                return ApiResult<ExperimentSpeedResultDto>.ValidationFail("speed is required");
            }

            TimeSpeed speed;
            if (int.TryParse(request.Speed.Trim(), out int numericSpeed) && Enum.IsDefined(typeof(TimeSpeed), (byte)numericSpeed))
            {
                speed = (TimeSpeed)(byte)numericSpeed;
            }
            else if (!Enum.TryParse(request.Speed.Trim(), true, out speed))
            {
                return ApiResult<ExperimentSpeedResultDto>.ValidationFail("Invalid speed. Valid: Paused (0), Normal (1), Fast (2), Superfast (3), Ultrafast (4)");
            }

            Find.TickManager.CurTimeSpeed = speed;

            return ApiResult<ExperimentSpeedResultDto>.Ok(new ExperimentSpeedResultDto
            {
                GameSpeed = Find.TickManager.CurTimeSpeed.ToString(),
                IsPaused = Find.TickManager.Paused
            });
        }

        public ApiResult Annotate(ExperimentAnnotationRequestDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Note))
            {
                return ApiResult.ValidationFail("note is required");
            }

            _dashboardService.RecordAnnotation(request.Note.Trim());

            if (request.ShowInGameBanner && Current.ProgramState == ProgramState.Playing)
            {
                Messages.Message($"[Experiment] {request.Note.Trim()}", MessageTypeDefOf.NeutralEvent, historical: false);
            }

            return ApiResult.Ok();
        }

        public ApiResult<ExperimentStatusDto> GetStatus(int? mapId)
        {
            if (Current.ProgramState != ProgramState.Playing || Current.Game == null)
            {
                return ApiResult<ExperimentStatusDto>.Fail("Game is not currently playing.");
            }

            Map map = mapId.HasValue ? MapHelper.GetMapByID(mapId.Value) : Find.CurrentMap;
            if (map == null)
            {
                return ApiResult<ExperimentStatusDto>.Fail($"Map not found: {mapId?.ToString() ?? "current"}");
            }

            int hostiles = map.mapPawns.AllPawnsSpawned.Count(p => !p.Dead && p.HostileTo(Faction.OfPlayer));

            return ApiResult<ExperimentStatusDto>.Ok(new ExperimentStatusDto
            {
                MapId = map.uniqueID,
                Tick = Find.TickManager.TicksGame,
                Storyteller = Current.Game.storyteller?.def?.defName,
                Difficulty = Current.Game.storyteller?.difficultyDef?.defName,
                ColonyWealth = map.wealthWatcher?.WealthTotal ?? 0f,
                ColonistCount = map.mapPawns.FreeColonistsSpawnedCount,
                HostileCount = hostiles,
                GameSpeed = Find.TickManager.CurTimeSpeed.ToString(),
                IsPaused = Find.TickManager.Paused
            });
        }
    }
}
