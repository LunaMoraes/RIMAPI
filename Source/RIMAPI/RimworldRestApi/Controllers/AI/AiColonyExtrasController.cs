using System.Threading.Tasks;
using RIMAPI.Core;
using RIMAPI.Http;
using RIMAPI.Models;
using RIMAPI.Services;

namespace RIMAPI.Controllers
{
    public class AiColonyExtrasController
    {
        private readonly IAiColonyExtrasService _extrasService;

        public AiColonyExtrasController(IAiColonyExtrasService extrasService)
        {
            _extrasService = extrasService;
        }

        [Get("/api/v1/ai/letters")]
        [EndpointMetadata("List active letters with text and available choices.")]
        public async Task GetLetters(RimApiContext context)
        {
            var result = _extrasService.GetLetters();
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/ai/letters/dismiss")]
        [EndpointMetadata("Dismiss a letter or select a choice_index on a ChoiceLetter.")]
        public async Task DismissLetter(RimApiContext context)
        {
            var body = await context.Request.ReadBodyAsync<AiDismissLetterRequestDto>();
            var result = _extrasService.DismissLetter(body);
            await context.SendJsonResponse(result);
        }

        [Get("/api/v1/ai/zones/stockpile")]
        [EndpointMetadata("List all stockpile zones with priority and allowed def counts.")]
        public async Task GetStockpiles(RimApiContext context)
        {
            int? mapId = null;
            string mapIdRaw = RequestParser.GetStringParameter(context, "map_id", required: false);
            if (!string.IsNullOrEmpty(mapIdRaw))
            {
                if (!int.TryParse(mapIdRaw, out int parsed))
                {
                    await context.SendJsonResponse(ApiResult.ValidationFail("Invalid map_id query parameter: must be an integer"));
                    return;
                }
                mapId = parsed;
            }

            var result = _extrasService.GetStockpiles(mapId);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/ai/zones/stockpile/filter")]
        [EndpointMetadata("Update a stockpile zone's priority and storage filter (allow/disallow categories or ThingDefs).")]
        public async Task UpdateStockpileFilter(RimApiContext context)
        {
            var body = await context.Request.ReadBodyAsync<AiStockpileFilterRequestDto>();
            var result = _extrasService.UpdateStockpileFilter(body);
            await context.SendJsonResponse(result);
        }

        [Get("/api/v1/ai/trade/traders")]
        [EndpointMetadata("List active orbital trade ships and visiting trade caravans on the map.")]
        public async Task GetTraders(RimApiContext context)
        {
            int? mapId = null;
            string mapIdRaw = RequestParser.GetStringParameter(context, "map_id", required: false);
            if (!string.IsNullOrEmpty(mapIdRaw))
            {
                if (!int.TryParse(mapIdRaw, out int parsed))
                {
                    await context.SendJsonResponse(ApiResult.ValidationFail("Invalid map_id query parameter: must be an integer"));
                    return;
                }
                mapId = parsed;
            }

            var result = _extrasService.GetTraders(mapId);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/ai/trade/execute")]
        [EndpointMetadata("Execute a trade deal with an active orbital ship or caravan trader.")]
        public async Task ExecuteTrade(RimApiContext context)
        {
            var body = await context.Request.ReadBodyAsync<AiTradeExecuteRequestDto>();
            var result = _extrasService.ExecuteTrade(body);
            await context.SendJsonResponse(result);
        }
    }
}
