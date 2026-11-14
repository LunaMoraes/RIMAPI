using System.Collections.Generic;

namespace RIMAPI.Models
{
    public class AiSnapshotDto
    {
        public int MapId { get; set; }
        public string ProgramState { get; set; }
        public AiTimeDto Time { get; set; }
        public AiWeatherDto Weather { get; set; }
        public AiPowerDto Power { get; set; }
        public AiResourceSummaryDto Resources { get; set; }
        public List<AiColonistSummaryDto> Colonists { get; set; } = new List<AiColonistSummaryDto>();
        public AiThreatsSummaryDto Threats { get; set; }
        public List<AiAlertDto> Alerts { get; set; } = new List<AiAlertDto>();
        public List<AiLetterDto> Letters { get; set; } = new List<AiLetterDto>();
        public AiResearchSummaryDto Research { get; set; }
        public AiZonesSummaryDto Zones { get; set; }
    }

    public class AiTimeDto
    {
        public int Tick { get; set; }
        public int DayOfQuadrum { get; set; }
        public string Quadrum { get; set; }
        public int Year { get; set; }
        public float Hour { get; set; }
        public string GameSpeed { get; set; }
        public bool IsPaused { get; set; }
    }

    public class AiWeatherDto
    {
        public string Weather { get; set; }
        public float TemperatureC { get; set; }
        public float WindSpeed { get; set; }
    }

    public class AiPowerDto
    {
        public float GeneratedW { get; set; }
        public float ConsumedW { get; set; }
        public float NetW { get; set; }
        public float StoredWd { get; set; }
    }

    public class AiResourceSummaryDto
    {
        public int TotalNutrition { get; set; }
        public int Meals { get; set; }
        public int Wood { get; set; }
        public int Steel { get; set; }
        public int Components { get; set; }
        public int Medicine { get; set; }
        public int Silver { get; set; }
    }

    public class AiColonistSummaryDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public PositionDto Position { get; set; }
        public int HealthPct { get; set; }
        public int MoodPct { get; set; }
        public int FoodPct { get; set; }
        public int RestPct { get; set; }
        public float BleedingRate { get; set; }
        public bool IsDrafted { get; set; }
        public bool IsDowned { get; set; }
        public bool InMentalBreak { get; set; }
        public string CurrentJob { get; set; }
        public string AllowedArea { get; set; }
        public string HostilityResponse { get; set; }
        public List<string> TopSkills { get; set; } = new List<string>();
    }

    public class AiThreatsSummaryDto
    {
        public int HostileCount { get; set; }
        public List<AiHostileDto> Hostiles { get; set; } = new List<AiHostileDto>();
    }

    public class AiHostileDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Faction { get; set; }
        public PositionDto Position { get; set; }
        public bool IsDowned { get; set; }
        public string Weapon { get; set; }
    }

    public class AiAlertDto
    {
        public string Label { get; set; }
        public string Priority { get; set; }
    }

    public class AiLetterDto
    {
        public int Id { get; set; }
        public string Label { get; set; }
        public string Type { get; set; }
    }

    public class AiResearchSummaryDto
    {
        public string CurrentProject { get; set; }
        public int ProgressPct { get; set; }
        public float PointsRemaining { get; set; }
    }

    public class AiZonesSummaryDto
    {
        public int GrowingZones { get; set; }
        public int StockpileZones { get; set; }
        public int TotalGrowingCells { get; set; }
        public int TotalStockpileCells { get; set; }
    }
}
