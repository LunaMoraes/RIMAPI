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
        [EndpointMetadata("Get recent API actions executed by the AI agent or human experiment harness (writes/errors by default, pass ?include_reads=true for GETs).")]
        public async Task GetActions(RimApiContext context)
        {
            if (!RequestParser.TryGetOptionalInt(context, "limit", out int? limitOpt, out string limitErr))
            {
                await context.SendJsonResponse(ApiResult.ValidationFail(limitErr));
                return;
            }

            int limit = limitOpt ?? 50;
            bool includeReads = RequestParser.GetBooleanParameter(context, "include_reads", false);

            var result = _dashboardService.GetRecentActions(limit, includeReads);
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
        [EndpointMetadata("Combined colony snapshot (null at main menu), recent action log, and SSE client count for the human monitoring dashboard.")]
        public async Task GetOverview(RimApiContext context)
        {
            if (!RequestParser.TryGetOptionalInt(context, "map_id", out int? mapId, out string mapErr))
            {
                await context.SendJsonResponse(ApiResult.ValidationFail(mapErr));
                return;
            }

            if (!RequestParser.TryGetOptionalInt(context, "limit", out int? limitOpt, out string limitErr))
            {
                await context.SendJsonResponse(ApiResult.ValidationFail(limitErr));
                return;
            }

            int actionLimit = limitOpt ?? 20;
            bool includeReads = RequestParser.GetBooleanParameter(context, "include_reads", false);

            var result = _dashboardService.GetOverview(mapId, actionLimit, includeReads);
            await context.SendJsonResponse(result);
        }
    }
}
