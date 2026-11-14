using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RIMAPI.Core;
using RIMAPI.Helpers;
using RIMAPI.Models;
using RimWorld;
using Verse;

namespace RIMAPI.Services
{
    public class AiColonyExtrasService : IAiColonyExtrasService
    {
        public ApiResult<List<AiLetterDetailDto>> GetLetters()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.LetterStack == null)
            {
                return ApiResult<List<AiLetterDetailDto>>.Fail("Game is not currently playing.");
            }

            var list = new List<AiLetterDetailDto>();
            foreach (var letter in Find.LetterStack.LettersListForReading)
            {
                var dto = new AiLetterDetailDto
                {
                    Id = letter.ID,
                    Label = letter.Label.ToString(),
                    Type = letter.def?.defName ?? "Letter",
                    ArrivalTick = letter.arrivalTick,
                    CanDismiss = letter.CanDismissWithRightClick
                };

                if (letter is ChoiceLetter cl)
                {
                    dto.Text = cl.Text.Resolve();
                    if (cl.Choices != null)
                    {
                        var textField = typeof(DiaOption).GetField("text", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        dto.Choices = cl.Choices
                            .Select(c => textField?.GetValue(c)?.ToString() ?? c.ToString())
                            .ToList();
                    }
                }

                list.Add(dto);
            }

            return ApiResult<List<AiLetterDetailDto>>.Ok(list);
        }

        public ApiResult DismissLetter(AiDismissLetterRequestDto request)
        {
            if (Current.ProgramState != ProgramState.Playing || Find.LetterStack == null)
            {
                return ApiResult.Fail("Game is not currently playing.");
            }

            if (request == null || request.LetterId <= 0)
            {
                return ApiResult.ValidationFail("letter_id is required");
            }

            Letter letter = Find.LetterStack.LettersListForReading.FirstOrDefault(l => l.ID == request.LetterId);
            if (letter == null)
            {
                return ApiResult.Fail($"Letter not found: {request.LetterId}");
            }

            if (request.ChoiceIndex.HasValue)
            {
                if (!(letter is ChoiceLetter cl))
                {
                    return ApiResult.ValidationFail($"Letter {request.LetterId} is not a choice letter");
                }

                var choices = cl.Choices?.ToList() ?? new List<DiaOption>();
                int idx = request.ChoiceIndex.Value;
                if (idx < 0 || idx >= choices.Count)
                {
                    return ApiResult.ValidationFail($"choice_index {idx} out of range (0..{choices.Count - 1})");
                }

                var chosen = choices[idx];
                chosen.action?.Invoke();
                if (Find.LetterStack.LettersListForReading.Contains(letter))
                {
                    Find.LetterStack.RemoveLetter(letter);
                }
                return ApiResult.Ok();
            }

            Find.LetterStack.RemoveLetter(letter);
            return ApiResult.Ok();
        }

        public ApiResult<List<AiStockpileSummaryDto>> GetStockpiles(int? mapId)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return ApiResult<List<AiStockpileSummaryDto>>.Fail("Game is not currently playing.");
            }

            Map map = mapId.HasValue ? MapHelper.GetMapByID(mapId.Value) : Find.CurrentMap;
            if (map == null)
            {
                return ApiResult<List<AiStockpileSummaryDto>>.Fail($"Map not found: {mapId?.ToString() ?? "current"}");
            }

            var list = map.zoneManager.AllZones
                .OfType<Zone_Stockpile>()
                .Select(ToStockpileDto)
                .ToList();

            return ApiResult<List<AiStockpileSummaryDto>>.Ok(list);
        }

        public ApiResult<AiStockpileSummaryDto> UpdateStockpileFilter(AiStockpileFilterRequestDto request)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return ApiResult<AiStockpileSummaryDto>.Fail("Game is not currently playing.");
            }

            if (request == null || (!request.ZoneId.HasValue && string.IsNullOrWhiteSpace(request.ZoneLabel)))
            {
                return ApiResult<AiStockpileSummaryDto>.ValidationFail("zone_id or zone_label is required");
            }

            Map map = MapHelper.GetMapByID(request.MapId);
            if (map == null)
            {
                return ApiResult<AiStockpileSummaryDto>.Fail($"Map not found: {request.MapId}");
            }

            Zone_Stockpile zone = request.ZoneId.HasValue
                ? map.zoneManager.AllZones.OfType<Zone_Stockpile>().FirstOrDefault(z => z.ID == request.ZoneId.Value)
                : map.zoneManager.AllZones.OfType<Zone_Stockpile>().FirstOrDefault(z => z.label.Equals(request.ZoneLabel.Trim(), StringComparison.OrdinalIgnoreCase));

            if (zone == null)
            {
                return ApiResult<AiStockpileSummaryDto>.Fail("Stockpile zone not found");
            }

            if (!string.IsNullOrWhiteSpace(request.Priority))
            {
                if (!Enum.TryParse(request.Priority.Trim(), true, out StoragePriority prio))
                {
                    return ApiResult<AiStockpileSummaryDto>.ValidationFail("Invalid priority. Valid: Low, Normal, Preferred, Important, Critical");
                }
                zone.settings.Priority = prio;
            }

            var filter = zone.settings.filter;
            if (request.ClearAll == true) filter.SetDisallowAll();
            if (request.AllowAll == true) filter.SetAllowAll(null);

            var warnings = new List<string>();
            ApplyCategoryChanges(filter, request.AllowCategories, true, warnings);
            ApplyCategoryChanges(filter, request.DisallowCategories, false, warnings);
            ApplyDefChanges(filter, request.AllowDefs, true, warnings);
            ApplyDefChanges(filter, request.DisallowDefs, false, warnings);

            var result = ApiResult<AiStockpileSummaryDto>.Ok(ToStockpileDto(zone));
            if (warnings.Count > 0)
            {
                result.Warnings.AddRange(warnings);
            }
            return result;
        }

        public ApiResult<List<AiTraderDto>> GetTraders(int? mapId)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return ApiResult<List<AiTraderDto>>.Fail("Game is not currently playing.");
            }

            Map map = mapId.HasValue ? MapHelper.GetMapByID(mapId.Value) : Find.CurrentMap;
            if (map == null)
            {
                return ApiResult<List<AiTraderDto>>.Fail($"Map not found: {mapId?.ToString() ?? "current"}");
            }

            var traders = new List<AiTraderDto>();

            if (map.passingShipManager?.passingShips != null)
            {
                foreach (var ship in map.passingShipManager.passingShips.OfType<TradeShip>())
                {
                    traders.Add(BuildTraderDto(ship, GetShipId(ship), ship.name, ship.def?.defName, "orbital"));
                }
            }

            foreach (var pawn in map.mapPawns.AllPawnsSpawned.Where(p => p.CanTradeNow))
            {
                traders.Add(BuildTraderDto(pawn, pawn.thingIDNumber, pawn.LabelShort, pawn.TraderKind?.defName, "caravan"));
            }

            return ApiResult<List<AiTraderDto>>.Ok(traders);
        }

        public ApiResult<AiTradeExecuteResultDto> ExecuteTrade(AiTradeExecuteRequestDto request)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return ApiResult<AiTradeExecuteResultDto>.Fail("Game is not currently playing.");
            }

            if (request == null || request.TraderId <= 0 || request.Items == null || request.Items.Count == 0)
            {
                return ApiResult<AiTradeExecuteResultDto>.ValidationFail("trader_id and non-empty items list are required");
            }

            Map map = MapHelper.GetMapByID(request.MapId);
            if (map == null)
            {
                return ApiResult<AiTradeExecuteResultDto>.Fail($"Map not found: {request.MapId}");
            }

            ITrader trader = FindTrader(map, request.TraderId);
            if (trader == null)
            {
                return ApiResult<AiTradeExecuteResultDto>.Fail($"Trader not found: {request.TraderId}");
            }

            Pawn negotiator = request.NegotiatorPawnId.HasValue
                ? PawnHelper.FindPawnById(request.NegotiatorPawnId.Value)
                : map.mapPawns.FreeColonistsSpawned
                    .Where(p => !p.Downed && !p.Dead)
                    .OrderByDescending(p => p.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0)
                    .FirstOrDefault();

            if (negotiator == null)
            {
                return ApiResult<AiTradeExecuteResultDto>.Fail("No available negotiator colonist found");
            }

            try
            {
                TradeSession.SetupWith(trader, negotiator, false);
                int processed = 0;

                foreach (var order in request.Items)
                {
                    if (string.IsNullOrWhiteSpace(order.DefName) || order.CountDelta == 0) continue;
                    var tradeable = TradeSession.deal.AllTradeables.FirstOrDefault(
                        t => t.ThingDef != null && t.ThingDef.defName.Equals(order.DefName.Trim(), StringComparison.OrdinalIgnoreCase)
                    );
                    if (tradeable == null) continue;

                    tradeable.AdjustTo(order.CountDelta);
                    processed++;
                }

                bool executed = TradeSession.deal.TryExecute(out bool actuallyTraded);
                return ApiResult<AiTradeExecuteResultDto>.Ok(new AiTradeExecuteResultDto
                {
                    TraderId = request.TraderId,
                    TraderName = trader.TraderName,
                    Executed = executed && actuallyTraded,
                    ItemsProcessed = processed
                });
            }
            finally
            {
                TradeSession.Close();
            }
        }

        private static int GetShipId(TradeShip ship)
        {
            return Math.Abs(ship.GetUniqueLoadID()?.GetHashCode() ?? ship.GetHashCode());
        }

        private static ITrader FindTrader(Map map, int traderId)
        {
            var ship = map.passingShipManager?.passingShips?.OfType<TradeShip>().FirstOrDefault(s => GetShipId(s) == traderId);
            if (ship != null) return ship;

            return map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.CanTradeNow && p.thingIDNumber == traderId);
        }

        private static AiTraderDto BuildTraderDto(ITrader trader, int id, string name, string kind, string type)
        {
            int silver = 0;
            var goods = new List<AiTraderGoodDto>();

            foreach (var g in trader.Goods)
            {
                if (g.def == ThingDefOf.Silver)
                {
                    silver += g.stackCount;
                    continue;
                }
                goods.Add(new AiTraderGoodDto
                {
                    DefName = g.def.defName,
                    Label = g.LabelCap,
                    Count = g.stackCount,
                    MarketValue = (float)Math.Round(g.MarketValue, 1)
                });
            }

            return new AiTraderDto
            {
                TraderId = id,
                Name = name,
                TraderKind = kind,
                Type = type,
                Silver = silver,
                Goods = goods.Take(40).ToList()
            };
        }

        private static void ApplyCategoryChanges(ThingFilter filter, List<string> names, bool allow, List<string> warnings)
        {
            if (names == null) return;
            foreach (var name in names)
            {
                var cat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail(name?.Trim());
                if (cat == null) warnings.Add($"ThingCategoryDef not found: {name}");
                else filter.SetAllow(cat, allow);
            }
        }

        private static void ApplyDefChanges(ThingFilter filter, List<string> names, bool allow, List<string> warnings)
        {
            if (names == null) return;
            foreach (var name in names)
            {
                var def = DefDatabase<ThingDef>.GetNamedSilentFail(name?.Trim());
                if (def == null) warnings.Add($"ThingDef not found: {name}");
                else filter.SetAllow(def, allow);
            }
        }

        private static AiStockpileSummaryDto ToStockpileDto(Zone_Stockpile zone)
        {
            return new AiStockpileSummaryDto
            {
                Id = zone.ID,
                Label = zone.label,
                Priority = zone.settings.Priority.ToString(),
                CellCount = zone.CellCount,
                AllowedDefsCount = zone.settings.filter.AllowedDefCount
            };
        }
    }
}
