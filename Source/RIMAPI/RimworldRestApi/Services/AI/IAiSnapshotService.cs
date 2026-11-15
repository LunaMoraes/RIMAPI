using System.Collections.Generic;
using RIMAPI.Core;
using RIMAPI.Models;

namespace RIMAPI.Services
{
    public interface IAiSnapshotService
    {
        ApiResult<AiSnapshotDto> GetSnapshot(int? mapId, bool omniscient = false, string include = null, int? sinceTick = null);
        ApiResult<List<AiDesignationSummaryDto>> GetDesignations(int? mapId, bool omniscient = false);
    }
}
