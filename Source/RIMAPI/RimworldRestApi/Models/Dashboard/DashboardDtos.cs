using System;
using System.Collections.Generic;

namespace RIMAPI.Models
{
    public class DashboardActionDto
    {
        public long Id { get; set; }
        public DateTime TimestampUtc { get; set; }
        public int GameTick { get; set; }
        public string Method { get; set; }
        public string Path { get; set; }
        public string Query { get; set; }
        public int StatusCode { get; set; }
        public long DurationMs { get; set; }
        public string Category { get; set; } // "ai", "experiment", "core", "annotation"
        public string Note { get; set; }
    }

    public class DashboardOverviewDto
    {
        public AiSnapshotDto Snapshot { get; set; }
        public List<DashboardActionDto> RecentActions { get; set; } = new List<DashboardActionDto>();
        public int SseClientCount { get; set; }
    }
}
