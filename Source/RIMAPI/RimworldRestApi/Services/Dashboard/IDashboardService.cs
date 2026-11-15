using System.Collections.Generic;
using RIMAPI.Core;
using RIMAPI.Models;

namespace RIMAPI.Services
{
    public interface IDashboardService
    {
        void RecordHttpAction(string method, string path, string query, int statusCode, long durationMs, string actor);
        void RecordAnnotation(string note, string actor = null);
        ApiResult<List<DashboardActionDto>> GetRecentActions(int limit, bool includeReads = false);
        ApiResult ClearActions();
        ApiResult<DashboardOverviewDto> GetOverview(int? mapId, int actionLimit, bool includeReads = false);
    }
}
