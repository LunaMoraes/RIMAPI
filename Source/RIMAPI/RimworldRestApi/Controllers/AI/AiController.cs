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
        [EndpointMetadata("Compact colony state snapshot for autonomous AI agents. Supports ?map_id=, ?omniscient=true|false, ?include=game_conditions,designations,prisoners,guests,animals,mechs (or all), and ?since_tick=.")]
        public async Task GetSnapshot(RimApiContext context)
        {
            if (!RequestParser.TryGetOptionalInt(context, "map_id", out int? mapId, out string mapErr))
            {
                await context.SendJsonResponse(ApiResult.ValidationFail(mapErr));
                return;
            }
            if (!RequestParser.TryGetOptionalInt(context, "since_tick", out int? sinceTick, out string tickErr))
            {
                await context.SendJsonResponse(ApiResult.ValidationFail(tickErr));
                return;
            }

            bool omniscient = RequestParser.GetBooleanParameter(context, "omniscient", defaultValue: false);
            string include = RequestParser.GetStringParameter(context, "include", required: false);

            var result = _aiSnapshotService.GetSnapshot(mapId, omniscient, include, sinceTick);
            await context.SendJsonResponse(result);
        }

        [Get("/api/v1/ai/designations")]
        [EndpointMetadata("List active map designations (mining, construct/deconstruct, harvest, haul, hunt, etc.), respecting fog of war unless ?omniscient=true.")]
        public async Task GetDesignations(RimApiContext context)
        {
            if (!RequestParser.TryGetOptionalInt(context, "map_id", out int? mapId, out string mapErr))
            {
                await context.SendJsonResponse(ApiResult.ValidationFail(mapErr));
                return;
            }

            bool omniscient = RequestParser.GetBooleanParameter(context, "omniscient", defaultValue: false);
            var result = _aiSnapshotService.GetDesignations(mapId, omniscient);
            await context.SendJsonResponse(result);
        }
    }
}
