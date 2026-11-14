using System.Collections.Generic;

namespace RIMAPI.Models
{
    public class AiDraftRequestDto
    {
        public int PawnId { get; set; }
        public bool Drafted { get; set; }
    }

    public class AiDraftResultDto
    {
        public int PawnId { get; set; }
        public string Name { get; set; }
        public bool IsDrafted { get; set; }
    }

    public class AiPawnCommandRequestDto
    {
        public int PawnId { get; set; }
        public string Action { get; set; } // "move", "attack", "wait"
        public PositionDto TargetPosition { get; set; }
        public int? TargetPawnId { get; set; }
        public bool AutoDraft { get; set; }
        public bool Queue { get; set; }
    }

    public class AiPawnCommandResultDto
    {
        public int PawnId { get; set; }
        public string Name { get; set; }
        public string Action { get; set; }
        public string AssignedJob { get; set; }
        public bool IsDrafted { get; set; }
    }

    public class AiPawnPolicyRequestDto
    {
        public int PawnId { get; set; }
        public string HostilityResponse { get; set; }
        public bool? SelfTend { get; set; }
        public string MedicalCare { get; set; }
        public string AllowedArea { get; set; }
        public string FoodPolicy { get; set; }
        public string DrugPolicy { get; set; }
        public string ApparelPolicy { get; set; }
    }

    public class AiPawnPolicyResultDto
    {
        public int PawnId { get; set; }
        public string Name { get; set; }
        public string HostilityResponse { get; set; }
        public bool SelfTend { get; set; }
        public string MedicalCare { get; set; }
        public string AllowedArea { get; set; }
        public string FoodPolicy { get; set; }
        public string DrugPolicy { get; set; }
        public string ApparelPolicy { get; set; }
    }

    public class AiAreaDto
    {
        public int Id { get; set; }
        public string Label { get; set; }
        public string Type { get; set; }
        public int CellCount { get; set; }
        public bool IsHome { get; set; }
    }

    public class AiAreaCreateRequestDto
    {
        public int MapId { get; set; }
        public string Label { get; set; }
        public PositionDto PointA { get; set; }
        public PositionDto PointB { get; set; }
    }

    public class AiAreaCellsRequestDto
    {
        public int MapId { get; set; }
        public string Label { get; set; }
        public string Mode { get; set; } // "add", "remove", "clear"
        public PositionDto PointA { get; set; }
        public PositionDto PointB { get; set; }
    }

    public class AiPrisonerInteractionRequestDto
    {
        public int PawnId { get; set; }
        public string Mode { get; set; }
    }

    public class AiPrisonerInteractionResultDto
    {
        public int PawnId { get; set; }
        public string Name { get; set; }
        public string InteractionMode { get; set; }
        public float Resistance { get; set; }
    }
}
