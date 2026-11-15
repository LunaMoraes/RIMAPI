namespace RIMAPI.Models
{
    public class DesignateRequestDto
    {
        public int MapId { get; set; }
        public string Type { get; set; } // "mine", "deconstruct", "harvest", "chop", "cut", "haul", "hunt", "tame", "slaughter", "smooth", "claim", "uninstall", "forbid", "unforbid", "cancel", "remove-all"
        public PositionDto PointA { get; set; }
        public PositionDto PointB { get; set; }
        public bool Omniscient { get; set; } = false;
    }

    public class DesignateResultDto
    {
        public int MapId { get; set; }
        public string Type { get; set; }
        public int DesignatedCount { get; set; }
        public int AlreadyDesignatedCount { get; set; }
        public int AffectedCount { get; set; }
        public int SkippedFoggedCount { get; set; }
        public int CellsScanned { get; set; }
    }
}
