namespace RIMAPI.Models
{
    public class ExperimentIncidentRequestDto
    {
        public int? MapId { get; set; }
        public string IncidentDef { get; set; }
        public float? Points { get; set; }
    }

    public class ExperimentIncidentResultDto
    {
        public string IncidentDef { get; set; }
        public int MapId { get; set; }
        public float PointsUsed { get; set; }
        public bool Fired { get; set; }
    }

    public class ExperimentSpeedRequestDto
    {
        public string Speed { get; set; } // "Paused", "Normal", "Fast", "Superfast", "Ultrafast" or "0"-"4"
    }

    public class ExperimentSpeedResultDto
    {
        public string GameSpeed { get; set; }
        public bool IsPaused { get; set; }
    }

    public class ExperimentAnnotationRequestDto
    {
        public string Note { get; set; }
        public bool ShowInGameBanner { get; set; }
    }

    public class ExperimentStatusDto
    {
        public int MapId { get; set; }
        public int Tick { get; set; }
        public string Storyteller { get; set; }
        public string Difficulty { get; set; }
        public float ColonyWealth { get; set; }
        public int ColonistCount { get; set; }
        public int HostileCount { get; set; }
        public string GameSpeed { get; set; }
        public bool IsPaused { get; set; }
    }
}
