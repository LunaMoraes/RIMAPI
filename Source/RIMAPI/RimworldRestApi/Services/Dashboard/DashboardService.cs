using System;
using System.Collections.Generic;
using System.Linq;
using RIMAPI.Core;
using RIMAPI.Models;
using Verse;

namespace RIMAPI.Services
{
    public class DashboardService : IDashboardService
    {
        private const int MaxActions = 200;
        private readonly LinkedList<DashboardActionDto> _actions = new LinkedList<DashboardActionDto>();
        private readonly object _lock = new object();
        private long _nextId = 1;

        private readonly IEventPublisher _eventPublisher;
        private readonly IAiSnapshotService _aiSnapshotService;
        private readonly SseService _sseService;

        public DashboardService(
            IEventPublisher eventPublisher,
            IAiSnapshotService aiSnapshotService,
            SseService sseService)
        {
            _eventPublisher = eventPublisher;
            _aiSnapshotService = aiSnapshotService;
            _sseService = sseService;
        }

        public void RecordHttpAction(string method, string path, string query, int statusCode, long durationMs)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (path.StartsWith("/api/v1/dashboard", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/v1/events", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string category = path.StartsWith("/api/v1/experiment", StringComparison.OrdinalIgnoreCase)
                ? "experiment"
                : path.StartsWith("/api/v1/ai", StringComparison.OrdinalIgnoreCase)
                    ? "ai"
                    : "core";

            var entry = CreateEntry(category, method, path, query, statusCode, durationMs, null);
            AddAndPublish(entry);
        }

        public void RecordAnnotation(string note)
        {
            var entry = CreateEntry("annotation", "NOTE", "/api/v1/experiment/annotation", null, 200, 0, note);
            AddAndPublish(entry);
        }

        public ApiResult<List<DashboardActionDto>> GetRecentActions(int limit)
        {
            int clamped = Math.Max(1, Math.Min(limit, MaxActions));
            lock (_lock)
            {
                var list = _actions.Take(clamped).ToList();
                return ApiResult<List<DashboardActionDto>>.Ok(list);
            }
        }

        public ApiResult ClearActions()
        {
            lock (_lock)
            {
                _actions.Clear();
            }
            return ApiResult.Ok();
        }

        public ApiResult<DashboardOverviewDto> GetOverview(int? mapId, int actionLimit)
        {
            var snapshotResult = _aiSnapshotService.GetSnapshot(mapId);
            if (!snapshotResult.Success)
            {
                return ApiResult<DashboardOverviewDto>.Fail(snapshotResult.Errors.FirstOrDefault() ?? "Failed to get snapshot");
            }

            var actions = GetRecentActions(actionLimit).Data ?? new List<DashboardActionDto>();
            return ApiResult<DashboardOverviewDto>.Ok(new DashboardOverviewDto
            {
                Snapshot = snapshotResult.Data,
                RecentActions = actions,
                SseClientCount = _sseService?.GetClientCount() ?? 0
            });
        }

        private DashboardActionDto CreateEntry(
            string category, string method, string path, string query,
            int statusCode, long durationMs, string note)
        {
            int tick = Current.ProgramState == ProgramState.Playing && Find.TickManager != null
                ? Find.TickManager.TicksGame
                : 0;

            return new DashboardActionDto
            {
                TimestampUtc = DateTime.UtcNow,
                GameTick = tick,
                Method = method,
                Path = path,
                Query = string.IsNullOrEmpty(query) ? null : query,
                StatusCode = statusCode,
                DurationMs = durationMs,
                Category = category,
                Note = note
            };
        }

        private void AddAndPublish(DashboardActionDto entry)
        {
            lock (_lock)
            {
                entry.Id = _nextId++;
                _actions.AddFirst(entry);
                while (_actions.Count > MaxActions)
                {
                    _actions.RemoveLast();
                }
            }

            try
            {
                _eventPublisher?.Publish("ai_action", entry);
            }
            catch (Exception ex)
            {
                LogApi.Warning($"Failed to publish ai_action SSE event: {ex.Message}");
            }
        }
    }
}
