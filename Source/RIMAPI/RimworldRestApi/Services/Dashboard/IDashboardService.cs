using System.Collections.Generic;
using RIMAPI.Core;
using RIMAPI.Models;

namespace RIMAPI.Services
{
    public interface IDashboardService
    {
        void RecordHttpAction(string method, string path, string query, int statusCode, long durationMs);
        void RecordAnnotation(string note);
        ApiResult<List<DashboardActionDto>> GetRecentActions(int limit);
        ApiResult ClearActions();
        ApiResult<DashboardOverviewDto> GetOverview(int? mapId, int actionLimit);
    }
}
