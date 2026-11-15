using System.Collections.Generic;

namespace RIMAPI.Models
{
    public class AiDesignatorDto
    {
        public string Category { get; set; }
        public int Index { get; set; }
        public string ClassName { get; set; }
        public string Label { get; set; }
        public string Desc { get; set; }
        public bool Visible { get; set; }
        public bool Disabled { get; set; }
        public string DisabledReason { get; set; }
        public string BuildDefName { get; set; }
    }

    public class AiDesignatorInvokeRequestDto
    {
        public int MapId { get; set; }
        public string Category { get; set; }
        public string ClassName { get; set; }
        public string Label { get; set; }
        public string BuildDefName { get; set; }
        public string StuffDefName { get; set; }
        public int? TargetThingId { get; set; }
        public List<PositionDto> Cells { get; set; }
        public PositionDto PointA { get; set; }
        public PositionDto PointB { get; set; }
        public bool Omniscient { get; set; } = false;
    }

    public class AiDesignatorInvokeResultDto
    {
        public int MapId { get; set; }
        public string DesignatorLabel { get; set; }
        public string ClassName { get; set; }
        public int DesignatedCount { get; set; }
        public int RejectedCount { get; set; }
        public int SkippedFoggedCount { get; set; }
    }

    public class AiFloatMenuOptionDto
    {
        public int Index { get; set; }
        public string Label { get; set; }
        public bool Disabled { get; set; }
        public string Priority { get; set; }
    }

    public class AiFloatMenuInvokeRequestDto
    {
        public int PawnId { get; set; }
        public int X { get; set; }
        public int Z { get; set; }
        public int? OptionIndex { get; set; }
        public string OptionLabel { get; set; }
        public bool Omniscient { get; set; } = false;
    }

    public class AiFloatMenuInvokeResultDto
    {
        public int PawnId { get; set; }
        public string PawnName { get; set; }
        public int OptionIndex { get; set; }
        public string Label { get; set; }
        public bool Invoked { get; set; }
        public string AssignedJob { get; set; }
    }

    public class AiGizmoDto
    {
        public int Index { get; set; }
        public string Label { get; set; }
        public string Desc { get; set; }
        public string GizmoType { get; set; }
        public bool Visible { get; set; }
        public bool Disabled { get; set; }
        public string DisabledReason { get; set; }
        public bool? IsActive { get; set; }
    }

    public class AiGizmoInvokeRequestDto
    {
        public int MapId { get; set; }
        public int? PawnId { get; set; }
        public int? ThingId { get; set; }
        public int? ZoneId { get; set; }
        public int? GizmoIndex { get; set; }
        public string GizmoLabel { get; set; }
        public bool Omniscient { get; set; } = false;
    }

    public class AiGizmoInvokeResultDto
    {
        public int GizmoIndex { get; set; }
        public string Label { get; set; }
        public string GizmoType { get; set; }
        public bool Invoked { get; set; }
        public bool? IsActive { get; set; }
    }

    public class AiMechWorkModeRequestDto
    {
        public int? MechanitorPawnId { get; set; }
        public int? MechPawnId { get; set; }
        public int? ControlGroupIndex { get; set; }
        public string WorkMode { get; set; }
    }

    public class AiMechWorkModeResultDto
    {
        public int MechanitorPawnId { get; set; }
        public string MechanitorName { get; set; }
        public int ControlGroupIndex { get; set; }
        public string WorkMode { get; set; }
        public List<int> MechIds { get; set; } = new List<int>();
    }

    public class AiPawnAbilityDto
    {
        public string DefName { get; set; }
        public string Label { get; set; }
        public string Source { get; set; }
        public bool CanCast { get; set; }
        public string DisabledReason { get; set; }
        public float Range { get; set; }
        public float Radius { get; set; }
    }

    public class AiPawnAbilityRequestDto
    {
        public int PawnId { get; set; }
        public string AbilityDefName { get; set; }
        public int? TargetPawnId { get; set; }
        public PositionDto TargetPosition { get; set; }
        public PositionDto DestinationPosition { get; set; }
        public bool Omniscient { get; set; } = false;
    }

    public class AiPawnAbilityResultDto
    {
        public int PawnId { get; set; }
        public string PawnName { get; set; }
        public string AbilityDefName { get; set; }
        public string Source { get; set; }
        public bool Queued { get; set; }
    }

    public class AiHospitalityGuestRequestDto
    {
        public int PawnId { get; set; }
        public bool? Entertain { get; set; }
        public bool? MakeFriends { get; set; }
        public string GuestArea { get; set; }
        public int? GuestAreaId { get; set; }
        public string ShoppingArea { get; set; }
        public int? ShoppingAreaId { get; set; }
        public bool? SendHome { get; set; }
    }

    public class AiHospitalityGuestResultDto
    {
        public int PawnId { get; set; }
        public string PawnName { get; set; }
        public bool Entertain { get; set; }
        public bool MakeFriends { get; set; }
        public string GuestArea { get; set; }
        public string ShoppingArea { get; set; }
        public bool SentHome { get; set; }
    }

    public class AiHospitalityBedDto
    {
        public int BedId { get; set; }
        public string DefName { get; set; }
        public string Label { get; set; }
        public PositionDto Position { get; set; }
        public int RentalFee { get; set; }
        public List<string> Owners { get; set; } = new List<string>();
    }

    public class AiHospitalityBedRequestDto
    {
        public int? MapId { get; set; }
        public int BedId { get; set; }
        public bool? AsGuestBed { get; set; }
        public int? RentalFee { get; set; }
    }

    public class AiHospitalityBedResultDto
    {
        public int BedId { get; set; }
        public string DefName { get; set; }
        public bool IsGuestBed { get; set; }
        public int RentalFee { get; set; }
    }

    public class AiTimeAdvanceRequestDto
    {
        public int? MapId { get; set; }
        public int Ticks { get; set; }
        public bool StopOnLetter { get; set; } = true;
        public bool StopOnHostile { get; set; } = true;
        public bool PauseAfter { get; set; } = true;
        public bool Omniscient { get; set; } = false;
    }

    public class AiTimeAdvanceResultDto
    {
        public int StartTick { get; set; }
        public int EndTick { get; set; }
        public int TicksRequested { get; set; }
        public int TicksExecuted { get; set; }
        public string StopReason { get; set; }
        public long ElapsedMs { get; set; }
        public bool IsPaused { get; set; }
        public int NewLettersCount { get; set; }
    }
}
