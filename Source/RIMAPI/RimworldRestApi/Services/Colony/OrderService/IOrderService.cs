using RIMAPI.Core;
using RIMAPI.Models;

namespace RIMAPI.Services
{
    public interface IOrderService
    {
        ApiResult<DesignateResultDto> DesignateArea(DesignateRequestDto request);
    }
}
