using System.Threading.Tasks;
using RIMAPI.Core;
using RIMAPI.Http;
using RIMAPI.Models;
using RIMAPI.Services;

namespace RIMAPI.Controllers
{
    public class ExperimentController
    {
        private readonly IExperimentService _experimentService;

        public ExperimentController(IExperimentService experimentService)
        {
            _experimentService = experimentService;
        }

        [Get("/api/v1/experiment/status")]
        [EndpointMetadata("[Human-only] Get experiment harness status (storyteller, difficulty, colony wealth, colonist & hostile counts).")]
        public async Task GetStatus(RimApiContext context)
        {
            if (!RequestParser.TryGetOptionalInt(context, "map_id", out int? mapId, out string mapErr))
            {
                await context.SendJsonResponse(ApiResult.ValidationFail(mapErr));
                return;
            }

            var result = _experimentService.GetStatus(mapId);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/experiment/incident")]
        [EndpointMetadata("[Human-only] Force-trigger a RimWorld IncidentDef with optional points override.")]
        public async Task TriggerIncident(RimApiContext context)
        {
            var body = await context.Request.ReadBodyAsync<ExperimentIncidentRequestDto>();
            var result = _experimentService.TriggerIncident(body);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/experiment/speed")]
        [EndpointMetadata("[Human-only] Override game speed (Paused/0, Normal/1, Fast/2, Superfast/3, Ultrafast/4).")]
        public async Task SetSpeed(RimApiContext context)
        {
            var body = await context.Request.ReadBodyAsync<ExperimentSpeedRequestDto>();
            var result = _experimentService.SetSpeed(body);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/experiment/annotation")]
        [EndpointMetadata("[Human-only] Add a timestamped marker note to the dashboard action log and SSE stream.")]
        public async Task Annotate(RimApiContext context)
        {
            var body = await context.Request.ReadBodyAsync<ExperimentAnnotationRequestDto>();
            string actor = context.Request.Headers?["X-RimApi-Actor"];
            var result = _experimentService.Annotate(body, actor);
            await context.SendJsonResponse(result);
        }
    }
}
