using System.Threading.Tasks;
using RIMAPI.Core;
using RIMAPI.Http;
using RIMAPI.Models;
using RIMAPI.Services;

namespace RIMAPI.Controllers
{
    public class AiPawnController
    {
        private readonly IAiPawnService _aiPawnService;

        public AiPawnController(IAiPawnService aiPawnService)
        {
            _aiPawnService = aiPawnService;
        }

        [Post("/api/v1/ai/pawn/draft")]
        [EndpointMetadata("Draft or undraft a colonist for tactical combat control.")]
        public async Task SetDraft(RimApiContext context)
        {
            var body = await context.Request.ReadBodyAsync<AiDraftRequestDto>();
            var result = _aiPawnService.SetDraft(body);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/ai/pawn/command")]
        [EndpointMetadata("Command a drafted colonist to move, attack a target pawn/cell, or wait.")]
        public async Task CommandPawn(RimApiContext context)
        {
            var body = await context.Request.ReadBodyAsync<AiPawnCommandRequestDto>();
            var result = _aiPawnService.CommandPawn(body);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/ai/pawn/policy")]
        [EndpointMetadata("Update colonist gameplay policies: hostility_response, self_tend, medical_care, allowed_area, food_policy, drug_policy, apparel_policy.")]
        public async Task UpdatePolicy(RimApiContext context)
        {
            var body = await context.Request.ReadBodyAsync<AiPawnPolicyRequestDto>();
            var result = _aiPawnService.UpdatePolicy(body);
            await context.SendJsonResponse(result);
        }

        [Get("/api/v1/ai/areas")]
        [EndpointMetadata("List all map areas (Home, Allowed areas, etc.) with cell counts.")]
        public async Task GetAreas(RimApiContext context)
        {
            int? mapId = null;
            string mapIdRaw = RequestParser.GetStringParameter(context, "map_id", required: false);
            if (!string.IsNullOrEmpty(mapIdRaw))
            {
                if (!int.TryParse(mapIdRaw, out int parsedMapId))
                {
                    await context.SendJsonResponse(
                        ApiResult.ValidationFail("Invalid map_id query parameter: must be an integer")
                    );
                    return;
                }
                mapId = parsedMapId;
            }

            var result = _aiPawnService.GetAreas(mapId);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/ai/areas/create")]
        [EndpointMetadata("Create a new allowed area, optionally filling a rectangular region.")]
        public async Task CreateArea(RimApiContext context)
        {
            var body = await context.Request.ReadBodyAsync<AiAreaCreateRequestDto>();
            var result = _aiPawnService.CreateArea(body);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/ai/areas/cells")]
        [EndpointMetadata("Add, remove, or clear cells in a named map area.")]
        public async Task UpdateAreaCells(RimApiContext context)
        {
            var body = await context.Request.ReadBodyAsync<AiAreaCellsRequestDto>();
            var result = _aiPawnService.UpdateAreaCells(body);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/ai/prisoner/interaction")]
        [EndpointMetadata("Set a colony prisoner's interaction mode (e.g., Recruit, ReduceResistance, Convert, Release, Execution).")]
        public async Task SetPrisonerInteraction(RimApiContext context)
        {
            var body = await context.Request.ReadBodyAsync<AiPrisonerInteractionRequestDto>();
            var result = _aiPawnService.SetPrisonerInteraction(body);
            await context.SendJsonResponse(result);
        }
    }
}
