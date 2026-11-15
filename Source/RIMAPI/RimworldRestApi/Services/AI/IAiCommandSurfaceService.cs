using System.Collections.Generic;
using RIMAPI.Core;
using RIMAPI.Models;

namespace RIMAPI.Services
{
    public interface IAiCommandSurfaceService
    {
        ApiResult<List<AiDesignatorDto>> GetDesignators(string category, bool includeHidden);
        ApiResult<AiDesignatorInvokeResultDto> InvokeDesignator(AiDesignatorInvokeRequestDto request);
        ApiResult<List<AiFloatMenuOptionDto>> GetFloatMenuOptions(int pawnId, int x, int z, bool omniscient);
        ApiResult<AiFloatMenuInvokeResultDto> InvokeFloatMenuOption(AiFloatMenuInvokeRequestDto request);
        ApiResult<List<AiGizmoDto>> GetGizmos(int? mapId, int? pawnId, int? thingId, int? zoneId, bool omniscient);
        ApiResult<AiGizmoInvokeResultDto> InvokeGizmo(AiGizmoInvokeRequestDto request);
        ApiResult<AiMechWorkModeResultDto> SetMechWorkMode(AiMechWorkModeRequestDto request);
        ApiResult<AiPawnAbilityResultDto> CastAbility(AiPawnAbilityRequestDto request);
        ApiResult<AiTimeAdvanceResultDto> AdvanceTime(AiTimeAdvanceRequestDto request);
    }
}
