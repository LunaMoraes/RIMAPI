using System.Threading.Tasks;
using RIMAPI.Core;
using RIMAPI.Http;
using RIMAPI.Models;
using RIMAPI.Services;

namespace RIMAPI.Controllers
{
    public class AiCommandSurfaceController
    {
        private readonly IAiCommandSurfaceService _commandSurfaceService;

        public AiCommandSurfaceController(IAiCommandSurfaceService commandSurfaceService)
        {
            _commandSurfaceService = commandSurfaceService;
        }

        [Get("/api/v1/ai/designators")]
        [EndpointMetadata("List live resolved Designator tools from DesignationCategoryDef (Architect tabs), including mod buildings and custom orders.")]
        public async Task GetDesignators(RimApiContext context)
        {
            string category = RequestParser.GetStringParameter(context, "category", required: false);
            bool includeHidden = RequestParser.GetBooleanParameter(context, "include_hidden", defaultValue: false);
            var result = _commandSurfaceService.GetDesignators(category, includeHidden);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/ai/designate")]
        [EndpointMetadata("Invoke a live Designator (by build_def_name, class_name, or label) over a cell list, rectangle, or target thing.")]
        public async Task InvokeDesignator(RimApiContext context)
        {
            var body = await context.Request.ReadBodyAsync<AiDesignatorInvokeRequestDto>();
            var result = _commandSurfaceService.InvokeDesignator(body);
            await context.SendJsonResponse(result);
        }

        [Get("/api/v1/ai/pawn/options")]
        [EndpointMetadata("Inspect live context right-click FloatMenu options for a colonist at a target cell (x, z).")]
        public async Task GetFloatMenuOptions(RimApiContext context)
        {
            if (!RequestParser.TryGetOptionalInt(context, "pawn_id", out int? pawnId, out string pawnErr) || !pawnId.HasValue)
            {
                await context.SendJsonResponse(ApiResult.ValidationFail(pawnErr ?? "pawn_id query parameter is required"));
                return;
            }
            if (!RequestParser.TryGetOptionalInt(context, "x", out int? x, out string xErr) || !x.HasValue)
            {
                await context.SendJsonResponse(ApiResult.ValidationFail(xErr ?? "x query parameter is required"));
                return;
            }
            if (!RequestParser.TryGetOptionalInt(context, "z", out int? z, out string zErr) || !z.HasValue)
            {
                await context.SendJsonResponse(ApiResult.ValidationFail(zErr ?? "z query parameter is required"));
                return;
            }

            bool omniscient = RequestParser.GetBooleanParameter(context, "omniscient", defaultValue: false);
            var result = _commandSurfaceService.GetFloatMenuOptions(pawnId.Value, x.Value, z.Value, omniscient);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/ai/pawn/options/invoke")]
        [EndpointMetadata("Invoke a right-click FloatMenu option (by option_index or option_label) for a colonist at cell (x, z).")]
        public async Task InvokeFloatMenuOption(RimApiContext context)
        {
            var body = await context.Request.ReadBodyAsync<AiFloatMenuInvokeRequestDto>();
            var result = _commandSurfaceService.InvokeFloatMenuOption(body);
            await context.SendJsonResponse(result);
        }

        [Get("/api/v1/ai/gizmos")]
        [EndpointMetadata("Inspect live command gizmos on a pawn, building/thing, or zone.")]
        public async Task GetGizmos(RimApiContext context)
        {
            if (!RequestParser.TryGetOptionalInt(context, "map_id", out int? mapId, out string mapErr))
            {
                await context.SendJsonResponse(ApiResult.ValidationFail(mapErr));
                return;
            }
            if (!RequestParser.TryGetOptionalInt(context, "pawn_id", out int? pawnId, out string pawnErr))
            {
                await context.SendJsonResponse(ApiResult.ValidationFail(pawnErr));
                return;
            }
            if (!RequestParser.TryGetOptionalInt(context, "thing_id", out int? thingId, out string thingErr))
            {
                await context.SendJsonResponse(ApiResult.ValidationFail(thingErr));
                return;
            }
            if (!RequestParser.TryGetOptionalInt(context, "zone_id", out int? zoneId, out string zoneErr))
            {
                await context.SendJsonResponse(ApiResult.ValidationFail(zoneErr));
                return;
            }

            bool omniscient = RequestParser.GetBooleanParameter(context, "omniscient", defaultValue: false);
            var result = _commandSurfaceService.GetGizmos(mapId, pawnId, thingId, zoneId, omniscient);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/ai/gizmos/invoke")]
        [EndpointMetadata("Invoke a command gizmo (toggle or action) on a pawn, building/thing, or zone.")]
        public async Task InvokeGizmo(RimApiContext context)
        {
            var body = await context.Request.ReadBodyAsync<AiGizmoInvokeRequestDto>();
            var result = _commandSurfaceService.InvokeGizmo(body);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/ai/mech/work-mode")]
        [EndpointMetadata("Change a Biotech mechanitor control group's MechWorkModeDef or recall/activate a Misc. Robots AIRobot.")]
        public async Task SetMechWorkMode(RimApiContext context)
        {
            var body = await context.Request.ReadBodyAsync<AiMechWorkModeRequestDto>();
            var result = _commandSurfaceService.SetMechWorkMode(body);
            await context.SendJsonResponse(result);
        }

        [Get("/api/v1/ai/pawn/abilities")]
        [EndpointMetadata("List all vanilla/Royalty and Vanilla Expanded Framework (VEF.Abilities / VPE) abilities learned by a pawn.")]
        public async Task GetPawnAbilities(RimApiContext context)
        {
            if (!RequestParser.TryGetOptionalInt(context, "pawn_id", out int? pawnId, out string pawnErr) || !pawnId.HasValue)
            {
                await context.SendJsonResponse(ApiResult.ValidationFail(pawnErr ?? "pawn_id query parameter is required"));
                return;
            }
            var result = _commandSurfaceService.GetPawnAbilities(pawnId.Value);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/ai/pawn/ability")]
        [EndpointMetadata("Queue casting a vanilla/Royalty Ability or VEF.Abilities (VPE psycast) ability onto a target pawn or map cell.")]
        public async Task CastAbility(RimApiContext context)
        {
            var body = await context.Request.ReadBodyAsync<AiPawnAbilityRequestDto>();
            var result = _commandSurfaceService.CastAbility(body);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/ai/hospitality/guest")]
        [EndpointMetadata("Update Hospitality mod settings for a guest (entertain, make_friends, guest_area, shopping_area, send_home).")]
        public async Task UpdateHospitalityGuest(RimApiContext context)
        {
            var body = await context.Request.ReadBodyAsync<AiHospitalityGuestRequestDto>();
            var result = _commandSurfaceService.UpdateHospitalityGuest(body);
            await context.SendJsonResponse(result);
        }

        [Get("/api/v1/ai/hospitality/beds")]
        [EndpointMetadata("List Hospitality Building_GuestBed instances on a map with rental fees and owners.")]
        public async Task GetHospitalityBeds(RimApiContext context)
        {
            if (!RequestParser.TryGetOptionalInt(context, "map_id", out int? mapId, out string mapErr))
            {
                await context.SendJsonResponse(ApiResult.ValidationFail(mapErr));
                return;
            }
            var result = _commandSurfaceService.GetHospitalityBeds(mapId);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/ai/hospitality/bed")]
        [EndpointMetadata("Convert a bed to/from a Hospitality Building_GuestBed or update its rental_fee.")]
        public async Task UpdateHospitalityBed(RimApiContext context)
        {
            var body = await context.Request.ReadBodyAsync<AiHospitalityBedRequestDto>();
            var result = _commandSurfaceService.UpdateHospitalityBed(body);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/ai/time/advance")]
        [EndpointMetadata("Deterministically advance simulation by N ticks (max 2500, 400ms budget), pausing on completion, new letter, or new hostile.")]
        public async Task AdvanceTime(RimApiContext context)
        {
            var body = await context.Request.ReadBodyAsync<AiTimeAdvanceRequestDto>();
            var result = _commandSurfaceService.AdvanceTime(body);
            await context.SendJsonResponse(result);
        }
    }
}
