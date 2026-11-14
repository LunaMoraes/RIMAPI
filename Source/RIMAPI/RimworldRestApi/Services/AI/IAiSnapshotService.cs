using RIMAPI.Core;
using RIMAPI.Models;

namespace RIMAPI.Services
{
    public interface IAiSnapshotService
    {
        ApiResult<AiSnapshotDto> GetSnapshot(int? mapId);
    }
}
