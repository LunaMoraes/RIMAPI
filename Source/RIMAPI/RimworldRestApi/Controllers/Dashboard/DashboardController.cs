using System.Threading.Tasks;
using RIMAPI.Core;
using RIMAPI.Http;
using RIMAPI.Services;

namespace RIMAPI.Controllers
{
    public class DashboardController
    {
        private readonly IDashboardService _dashboardService;

        public DashboardController(IDashboardService dashboardService)
        {
            _dashboardService = dashboardService;
        }

        [Get("/api/v1/dashboard/actions")]
        [EndpointMetadata("Get recent API actions executed by the AI agent or human experiment harness.")]
        public async Task GetActions(RimApiContext context)
        {
            int limit = 50;
            string limitRaw = RequestParser.GetStringParameter(context, "limit", required: false);
            if (!string.IsNullOrEmpty(limitRaw) && int.TryParse(limitRaw, out int parsed))
            {
                limit = parsed;
            }

            var result = _dashboardService.GetRecentActions(limit);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/dashboard/actions/clear")]
        [EndpointMetadata("Clear the in-memory dashboard action log.")]
        public async Task ClearActions(RimApiContext context)
        {
            var result = _dashboardService.ClearActions();
            await context.SendJsonResponse(result);
        }

        [Get("/api/v1/dashboard/overview")]
        [EndpointMetadata("Combined colony snapshot, recent action log, and SSE client count for the human monitoring dashboard.")]
        public async Task GetOverview(RimApiContext context)
        {
            int? mapId = null;
            string mapIdRaw = RequestParser.GetStringParameter(context, "map_id", required: false);
            if (!string.IsNullOrEmpty(mapIdRaw))
            {
                if (!int.TryParse(mapIdRaw, out int parsedMapId))
                {
                    await context.SendJsonResponse(
                        ApiResult.ValidationFail("Invalid map_id query parameter: must be an integer")
                    );
                    return;
                }
                mapId = parsedMapId;
            }

            int actionLimit = 20;
            string limitRaw = RequestParser.GetStringParameter(context, "limit", required: false);
            if (!string.IsNullOrEmpty(limitRaw) && int.TryParse(limitRaw, out int parsedLimit))
            {
                actionLimit = parsedLimit;
            }

            var result = _dashboardService.GetOverview(mapId, actionLimit);
            await context.SendJsonResponse(result);
        }
    }
}
