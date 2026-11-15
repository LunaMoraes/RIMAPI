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
        private readonly LinkedList<DashboardActionDto> _writeActions = new LinkedList<DashboardActionDto>();
        private readonly LinkedList<DashboardActionDto> _allActions = new LinkedList<DashboardActionDto>();
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

        public void RecordHttpAction(string method, string path, string query, int statusCode, long durationMs, string actor)
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

            string tier = ClassifyTier(method, path);
            bool isReadOnly = string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
                || string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase);
            bool isSignificant = !isReadOnly || statusCode >= 400;

            var entry = CreateEntry(category, tier, actor, method, path, query, statusCode, durationMs, null);
            AddAndPublish(entry, isSignificant, publishSse: isSignificant);
        }

        public void RecordAnnotation(string note, string actor = null)
        {
            var entry = CreateEntry("annotation", "experiment", actor, "NOTE", "/api/v1/experiment/annotation", null, 200, 0, note);
            AddAndPublish(entry, isSignificant: true, publishSse: true);
        }

        public ApiResult<List<DashboardActionDto>> GetRecentActions(int limit, bool includeReads = false)
        {
            int clamped = Math.Max(1, Math.Min(limit, MaxActions));
            lock (_lock)
            {
                var source = includeReads ? _allActions : _writeActions;
                var list = source.Take(clamped).ToList();
                return ApiResult<List<DashboardActionDto>>.Ok(list);
            }
        }

        public ApiResult ClearActions()
        {
            lock (_lock)
            {
                _writeActions.Clear();
                _allActions.Clear();
            }
            return ApiResult.Ok();
        }

        public ApiResult<DashboardOverviewDto> GetOverview(int? mapId, int actionLimit, bool includeReads = false)
        {
            bool isPlaying = Current.ProgramState == ProgramState.Playing && Current.Game != null && Find.Maps != null;
            AiSnapshotDto snapshot = null;
            List<string> warnings = null;

            if (isPlaying)
            {
                var snapshotResult = _aiSnapshotService.GetSnapshot(mapId);
                if (snapshotResult.Success)
                {
                    snapshot = snapshotResult.Data;
                }
                else if (snapshotResult.Errors?.Count > 0)
                {
                    warnings = new List<string>(snapshotResult.Errors);
                }
            }

            var actions = GetRecentActions(actionLimit, includeReads).Data ?? new List<DashboardActionDto>();
            var overview = new DashboardOverviewDto
            {
                ProgramState = Current.ProgramState.ToString(),
                IsPlaying = isPlaying,
                Snapshot = snapshot,
                RecentActions = actions,
                SseClientCount = _sseService?.GetClientCount() ?? 0
            };

            return warnings != null && warnings.Count > 0
                ? ApiResult<DashboardOverviewDto>.Partial(overview, warnings)
                : ApiResult<DashboardOverviewDto>.Ok(overview);
        }

        private static string ClassifyTier(string method, string path)
        {
            if (path.StartsWith("/api/v1/experiment", StringComparison.OrdinalIgnoreCase))
                return "experiment";

            if (path.StartsWith("/api/v1/pawn/edit", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/v1/pawn/spawn", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/v1/item/spawn", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/v1/map/droppod", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/v1/map/destroy", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/v1/incident/trigger", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/v1/faction/goodwill", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/v1/dev/", StringComparison.OrdinalIgnoreCase))
            {
                return "cheat";
            }

            if (path.StartsWith("/api/v1/game/quit", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/v1/game/main-menu", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/v1/game/load", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/v1/game/start", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/v1/mods/configure", StringComparison.OrdinalIgnoreCase))
            {
                return "admin";
            }

            if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
                || string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                return "observe";
            }

            return "act";
        }

        private static DashboardActionDto CreateEntry(
            string category, string tier, string actor, string method, string path, string query,
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
                Tier = tier,
                Actor = string.IsNullOrWhiteSpace(actor) ? null : actor.Trim(),
                Note = note
            };
        }

        private void AddAndPublish(DashboardActionDto entry, bool isSignificant, bool publishSse)
        {
            lock (_lock)
            {
                entry.Id = _nextId++;
                _allActions.AddFirst(entry);
                while (_allActions.Count > MaxActions)
                {
                    _allActions.RemoveLast();
                }

                if (isSignificant)
                {
                    _writeActions.AddFirst(entry);
                    while (_writeActions.Count > MaxActions)
                    {
                        _writeActions.RemoveLast();
                    }
                }
            }

            if (!publishSse) return;

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
