using System.Threading.Tasks;
using RIMAPI.Core;
using RIMAPI.Http;
using RIMAPI.Services;

namespace RIMAPI.Controllers
{
    public class AiController
    {
        private readonly IAiSnapshotService _aiSnapshotService;

        public AiController(IAiSnapshotService aiSnapshotService)
        {
            _aiSnapshotService = aiSnapshotService;
        }

        [Get("/api/v1/ai/snapshot")]
        [EndpointMetadata("Compact colony state snapshot for autonomous AI agents (time, weather, power, resources, colonists, threats, alerts, letters, research, zones).")]
        public async Task GetSnapshot(RimApiContext context)
        {
            int? mapId = null;
            string mapIdRaw = RequestParser.GetStringParameter(context, "map_id", required: false);
            if (!string.IsNullOrEmpty(mapIdRaw))
            {
                if (!int.TryParse(mapIdRaw, out int parsedMapId))
                {
                    await context.SendJsonResponse(
                        ApiResult<Models.AiSnapshotDto>.ValidationFail("Invalid map_id query parameter: must be an integer")
                    );
                    return;
                }
                mapId = parsedMapId;
            }

            var result = _aiSnapshotService.GetSnapshot(mapId);
            await context.SendJsonResponse(result);
        }
    }
}
