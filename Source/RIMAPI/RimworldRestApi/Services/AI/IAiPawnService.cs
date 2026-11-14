using System.Collections.Generic;
using RIMAPI.Core;
using RIMAPI.Models;

namespace RIMAPI.Services
{
    public interface IAiPawnService
    {
        ApiResult<AiDraftResultDto> SetDraft(AiDraftRequestDto request);
        ApiResult<AiPawnCommandResultDto> CommandPawn(AiPawnCommandRequestDto request);
        ApiResult<AiPawnPolicyResultDto> UpdatePolicy(AiPawnPolicyRequestDto request);
        ApiResult<List<AiAreaDto>> GetAreas(int? mapId);
        ApiResult<AiAreaDto> CreateArea(AiAreaCreateRequestDto request);
        ApiResult<AiAreaDto> UpdateAreaCells(AiAreaCellsRequestDto request);
        ApiResult<AiPrisonerInteractionResultDto> SetPrisonerInteraction(AiPrisonerInteractionRequestDto request);
    }
}
