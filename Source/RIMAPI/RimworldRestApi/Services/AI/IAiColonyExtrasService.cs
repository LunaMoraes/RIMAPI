using System.Collections.Generic;
using RIMAPI.Core;
using RIMAPI.Models;

namespace RIMAPI.Services
{
    public interface IAiColonyExtrasService
    {
        ApiResult<List<AiLetterDetailDto>> GetLetters();
        ApiResult DismissLetter(AiDismissLetterRequestDto request);
        ApiResult<List<AiStockpileSummaryDto>> GetStockpiles(int? mapId);
        ApiResult<AiStockpileSummaryDto> UpdateStockpileFilter(AiStockpileFilterRequestDto request);
        ApiResult<List<AiTraderDto>> GetTraders(int? mapId);
        ApiResult<AiTradeExecuteResultDto> ExecuteTrade(AiTradeExecuteRequestDto request);
    }
}
