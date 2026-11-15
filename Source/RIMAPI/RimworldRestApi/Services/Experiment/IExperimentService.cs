using RIMAPI.Core;
using RIMAPI.Models;

namespace RIMAPI.Services
{
    public interface IExperimentService
    {
        ApiResult<ExperimentIncidentResultDto> TriggerIncident(ExperimentIncidentRequestDto request);
        ApiResult<ExperimentSpeedResultDto> SetSpeed(ExperimentSpeedRequestDto request);
        ApiResult Annotate(ExperimentAnnotationRequestDto request, string actor = null);
        ApiResult<ExperimentStatusDto> GetStatus(int? mapId);
    }
}
