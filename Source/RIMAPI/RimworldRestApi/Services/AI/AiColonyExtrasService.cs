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

                if (letter is ChoiceLetter_GrowthMoment gm)
                {
                    dto.GrowthMomentPawn = gm.pawn?.LabelShort;
                    dto.PassionChoices = gm.passionChoices?.Select(s => s.defName).ToList() ?? new List<string>();
                    dto.TraitChoices = gm.traitChoices?.Select(t => t.LabelCap.ToString()).ToList() ?? new List<string>();
                }

                list.Add(dto);
            }

            return ApiResult<List<AiLetterDetailDto>>.Ok(list);
        }

        public ApiResult DismissLetter(AiDismissLetterRequestDto request)
        {
            if (Current.ProgramState != ProgramState.Playing || Find.LetterStack == null)
            {
                return ApiResult.ServiceUnavailable("Game is not currently playing.");
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

            if (letter is ChoiceLetter_GrowthMoment gm && (request.SelectedPassions != null || !string.IsNullOrWhiteSpace(request.SelectedTrait)))
            {
                var chosenPassions = new List<SkillDef>();
                if (request.SelectedPassions != null)
                {
                    var availablePassions = gm.passionChoices ?? new List<SkillDef>();
                    foreach (var sp in request.SelectedPassions)
                    {
                        if (string.IsNullOrWhiteSpace(sp)) continue;
                        string trimmed = sp.Trim();
                        var match = availablePassions.FirstOrDefault(s =>
                            s.defName.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
                            s.label.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
                        if (match == null)
                        {
                            return ApiResult.ValidationFail($"Passion choice '{sp}' is not among available growth moment choices");
                        }
                        if (!chosenPassions.Contains(match))
                        {
                            chosenPassions.Add(match);
                        }
                    }
                }

                Trait chosenTrait = null;
                if (!string.IsNullOrWhiteSpace(request.SelectedTrait))
                {
                    var availableTraits = gm.traitChoices ?? new List<Trait>();
                    string trimmed = request.SelectedTrait.Trim();
                    chosenTrait = availableTraits.FirstOrDefault(t =>
                        t.LabelCap.ToString().Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
                        t.Label.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
                        t.def.defName.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
                        $"{t.def.defName}:{t.Degree}".Equals(trimmed, StringComparison.OrdinalIgnoreCase));
                    if (chosenTrait == null)
                    {
                        return ApiResult.ValidationFail($"Trait choice '{request.SelectedTrait}' is not among available growth moment choices");
                    }
                }

                gm.MakeChoices(chosenPassions, chosenTrait);
                if (Find.LetterStack.LettersListForReading.Contains(letter))
                {
                    Find.LetterStack.RemoveLetter(letter);
                }
                return ApiResult.Ok();
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
                return ApiResult<List<AiStockpileSummaryDto>>.ServiceUnavailable("Game is not currently playing.");
            }

            Map map = MapHelper.ResolveMap(mapId);
            if (map == null)
            {
                return ApiResult<List<AiStockpileSummaryDto>>.Fail($"Map not found: {mapId?.ToString() ?? "current"}");
            }

            var list = new List<AiStockpileSummaryDto>();
            foreach (var zone in map.zoneManager.AllZones.OfType<Zone_Stockpile>())
            {
                list.Add(ToZoneSummaryDto(zone));
            }

            foreach (var building in map.listerBuildings.allBuildingsColonist)
            {
                if (building is IStoreSettingsParent storeParent && storeParent.StorageTabVisible && storeParent.GetStoreSettings() != null)
                {
                    list.Add(ToBuildingSummaryDto(building, storeParent.GetStoreSettings()));
                }
            }

            return ApiResult<List<AiStockpileSummaryDto>>.Ok(list);
        }

        public ApiResult<AiStockpileSummaryDto> UpdateStockpileFilter(AiStockpileFilterRequestDto request)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return ApiResult<AiStockpileSummaryDto>.ServiceUnavailable("Game is not currently playing.");
            }

            if (request == null || (!request.ZoneId.HasValue && !request.BuildingId.HasValue && string.IsNullOrWhiteSpace(request.ZoneLabel)))
            {
                return ApiResult<AiStockpileSummaryDto>.ValidationFail("zone_id, building_id, or zone_label is required");
            }

            Map map = MapHelper.ResolveMapOrDefault(request.MapId);
            if (map == null)
            {
                return ApiResult<AiStockpileSummaryDto>.Fail($"Map not found: {request.MapId}");
            }

            Zone_Stockpile zone = null;
            Building storageBuilding = null;
            IStoreSettingsParent settingsParent = null;

            if (request.BuildingId.HasValue)
            {
                storageBuilding = map.listerBuildings.allBuildingsColonist
                    .FirstOrDefault(b => b.thingIDNumber == request.BuildingId.Value && b is IStoreSettingsParent sp && sp.StorageTabVisible);
                settingsParent = storageBuilding as IStoreSettingsParent;
            }
            else if (request.ZoneId.HasValue)
            {
                zone = map.zoneManager.AllZones.OfType<Zone_Stockpile>().FirstOrDefault(z => z.ID == request.ZoneId.Value);
                if (zone != null)
                {
                    settingsParent = zone;
                }
                else
                {
                    storageBuilding = map.listerBuildings.allBuildingsColonist
                        .FirstOrDefault(b => b.thingIDNumber == request.ZoneId.Value && b is IStoreSettingsParent sp && sp.StorageTabVisible);
                    settingsParent = storageBuilding as IStoreSettingsParent;
                }
            }
            else
            {
                string targetLabel = request.ZoneLabel.Trim();
                zone = map.zoneManager.AllZones.OfType<Zone_Stockpile>()
                    .FirstOrDefault(z => z.label.Equals(targetLabel, StringComparison.OrdinalIgnoreCase));
                if (zone != null)
                {
                    settingsParent = zone;
                }
                else
                {
                    storageBuilding = map.listerBuildings.allBuildingsColonist
                        .FirstOrDefault(b => b is IStoreSettingsParent sp && sp.StorageTabVisible &&
                            (b.Label.Equals(targetLabel, StringComparison.OrdinalIgnoreCase) ||
                             b.LabelCap.ToString().Equals(targetLabel, StringComparison.OrdinalIgnoreCase)));
                    settingsParent = storageBuilding as IStoreSettingsParent;
                }
            }

            StorageSettings settings = settingsParent?.GetStoreSettings();
            if (settings == null)
            {
                return ApiResult<AiStockpileSummaryDto>.Fail("Stockpile zone or storage building not found");
            }

            if (!string.IsNullOrWhiteSpace(request.Priority))
            {
                if (!Enum.TryParse(request.Priority.Trim(), true, out StoragePriority prio))
                {
                    return ApiResult<AiStockpileSummaryDto>.ValidationFail("Invalid priority. Valid: Low, Normal, Preferred, Important, Critical");
                }
                settings.Priority = prio;
            }

            var filter = settings.filter;
            if (request.ClearAll == true) filter.SetDisallowAll();
            if (request.AllowAll == true) filter.SetAllowAll(null);

            var warnings = new List<string>();
            ApplyCategoryChanges(filter, request.AllowCategories, true, warnings);
            ApplyCategoryChanges(filter, request.DisallowCategories, false, warnings);
            ApplyDefChanges(filter, request.AllowDefs, true, warnings);
            ApplyDefChanges(filter, request.DisallowDefs, false, warnings);

            var summary = zone != null
                ? ToZoneSummaryDto(zone)
                : ToBuildingSummaryDto(storageBuilding, settings);

            var result = ApiResult<AiStockpileSummaryDto>.Ok(summary);
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
                return ApiResult<List<AiTraderDto>>.ServiceUnavailable("Game is not currently playing.");
            }

            Map map = MapHelper.ResolveMap(mapId);
            if (map == null)
            {
                return ApiResult<List<AiTraderDto>>.Fail($"Map not found: {mapId?.ToString() ?? "current"}");
            }

            var traders = new List<AiTraderDto>();

            if (map.passingShipManager?.passingShips != null)
            {
                foreach (var ship in map.passingShipManager.passingShips.OfType<TradeShip>().Where(s => s.CanTradeNow))
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

        public ApiResult<AiTradeDealQuoteDto> GetTradeDeal(int? mapId, int traderId, int? negotiatorPawnId)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return ApiResult<AiTradeDealQuoteDto>.ServiceUnavailable("Game is not currently playing.");
            }

            if (traderId <= 0)
            {
                return ApiResult<AiTradeDealQuoteDto>.ValidationFail("trader_id is required");
            }

            if (TradeSession.Active)
            {
                return ApiResult<AiTradeDealQuoteDto>.StateConflict("A trade session is already active in the UI");
            }

            Map map = MapHelper.ResolveMap(mapId);
            if (map == null)
            {
                return ApiResult<AiTradeDealQuoteDto>.Fail($"Map not found: {mapId?.ToString() ?? "current"}");
            }

            ITrader trader = FindTrader(map, traderId);
            if (trader == null)
            {
                return ApiResult<AiTradeDealQuoteDto>.Fail($"Trader not found or cannot currently trade: {traderId}");
            }

            Pawn negotiator = ResolveNegotiator(map, negotiatorPawnId);
            if (negotiator == null)
            {
                return ApiResult<AiTradeDealQuoteDto>.Fail("No available negotiator colonist found");
            }

            bool openedSession = false;
            try
            {
                TradeSession.SetupWith(trader, negotiator, false);
                openedSession = true;

                var allTradeables = TradeSession.deal?.AllTradeables ?? new List<Tradeable>();
                var lines = new List<AiTradeDealLineDto>(allTradeables.Count);

                for (int i = 0; i < allTradeables.Count; i++)
                {
                    var t = allTradeables[i];
                    lines.Add(new AiTradeDealLineDto
                    {
                        Index = i,
                        DefName = t.ThingDef?.defName,
                        Label = t.LabelCap.ToString(),
                        ColonyCount = t.CountHeldBy(Transactor.Colony),
                        TraderCount = t.CountHeldBy(Transactor.Trader),
                        BuyPrice = t.TraderWillTrade ? (float)Math.Round(t.GetPriceFor(TradeAction.PlayerBuys), 2) : 0f,
                        SellPrice = t.TraderWillTrade ? (float)Math.Round(t.GetPriceFor(TradeAction.PlayerSells), 2) : 0f,
                        MinToTransfer = t.GetMinimumToTransfer(),
                        MaxToTransfer = t.GetMaximumToTransfer(),
                        IsCurrency = t.IsCurrency,
                        IsFavor = t.IsFavor
                    });
                }

                var currency = TradeSession.deal?.CurrencyTradeable;
                return ApiResult<AiTradeDealQuoteDto>.Ok(new AiTradeDealQuoteDto
                {
                    MapId = map.uniqueID,
                    TraderId = traderId,
                    TraderName = trader.TraderName,
                    NegotiatorPawnId = negotiator.thingIDNumber,
                    NegotiatorName = negotiator.LabelShort,
                    ColonySilver = currency?.CountHeldBy(Transactor.Colony) ?? 0,
                    TraderSilver = currency?.CountHeldBy(Transactor.Trader) ?? 0,
                    Lines = lines
                });
            }
            finally
            {
                if (openedSession && TradeSession.Active)
                {
                    TradeSession.Close();
                }
            }
        }

        public ApiResult<AiTradeExecuteResultDto> ExecuteTrade(AiTradeExecuteRequestDto request)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return ApiResult<AiTradeExecuteResultDto>.ServiceUnavailable("Game is not currently playing.");
            }

            if (request == null || request.TraderId <= 0 || request.Items == null || request.Items.Count == 0)
            {
                return ApiResult<AiTradeExecuteResultDto>.ValidationFail("trader_id and non-empty items list are required");
            }

            if (TradeSession.Active)
            {
                return ApiResult<AiTradeExecuteResultDto>.StateConflict("A trade session is already active in the UI");
            }

            Map map = MapHelper.ResolveMapOrDefault(request.MapId);
            if (map == null)
            {
                return ApiResult<AiTradeExecuteResultDto>.Fail($"Map not found: {request.MapId}");
            }

            ITrader trader = FindTrader(map, request.TraderId);
            if (trader == null)
            {
                return ApiResult<AiTradeExecuteResultDto>.Fail($"Trader not found or cannot currently trade: {request.TraderId}");
            }

            Pawn negotiator = ResolveNegotiator(map, request.NegotiatorPawnId);
            if (negotiator == null)
            {
                return ApiResult<AiTradeExecuteResultDto>.Fail("No available negotiator colonist found");
            }

            bool openedSession = false;
            try
            {
                TradeSession.SetupWith(trader, negotiator, false);
                openedSession = true;

                var allTradeables = TradeSession.deal.AllTradeables;
                int processed = 0;
                var warnings = new List<string>();

                foreach (var order in request.Items)
                {
                    if (order == null || order.CountDelta == 0) continue;

                    Tradeable tradeable = null;
                    if (order.Index.HasValue)
                    {
                        int idx = order.Index.Value;
                        if (idx < 0 || idx >= allTradeables.Count)
                        {
                            warnings.Add($"Tradeable index out of range: {idx}");
                            continue;
                        }
                        tradeable = allTradeables[idx];
                    }
                    else if (!string.IsNullOrWhiteSpace(order.DefName))
                    {
                        string defName = order.DefName.Trim();
                        tradeable = allTradeables.FirstOrDefault(
                            t => t.ThingDef != null && t.ThingDef.defName.Equals(defName, StringComparison.OrdinalIgnoreCase)
                        );
                        if (tradeable == null)
                        {
                            warnings.Add($"Tradeable def_name not found in deal: {order.DefName}");
                            continue;
                        }
                    }
                    else
                    {
                        warnings.Add("Trade item order missing both index and def_name");
                        continue;
                    }

                    if (!tradeable.TraderWillTrade)
                    {
                        warnings.Add($"Trader will not trade '{tradeable.LabelCap}'");
                        continue;
                    }

                    int minTransfer = tradeable.GetMinimumToTransfer();
                    int maxTransfer = tradeable.GetMaximumToTransfer();
                    if (order.CountDelta < minTransfer || order.CountDelta > maxTransfer)
                    {
                        warnings.Add($"Order {order.CountDelta} for '{tradeable.LabelCap}' is outside allowed range [{minTransfer}..{maxTransfer}]");
                        continue;
                    }

                    tradeable.AdjustTo(order.CountDelta);
                    if (tradeable.CountToTransfer != order.CountDelta)
                    {
                        warnings.Add($"Adjusted '{tradeable.LabelCap}' to {tradeable.CountToTransfer} instead of requested {order.CountDelta}");
                    }

                    processed++;
                }

                int netSilverChange = TradeSession.deal.CurrencyTradeable?.CountToTransfer ?? 0;
                bool executed = false;

                if (!request.DryRun && processed > 0)
                {
                    executed = TradeSession.deal.TryExecute(out bool actuallyTraded) && actuallyTraded;
                    if (!executed)
                    {
                        warnings.Add("TradeDeal.TryExecute did not complete the transaction (check silver balance or trader capacity).");
                    }
                }

                var dto = new AiTradeExecuteResultDto
                {
                    TraderId = request.TraderId,
                    TraderName = trader.TraderName,
                    DryRun = request.DryRun,
                    Executed = executed,
                    ItemsProcessed = processed,
                    NetSilverChange = netSilverChange
                };

                var result = ApiResult<AiTradeExecuteResultDto>.Ok(dto);
                if (warnings.Count > 0)
                {
                    result.Warnings.AddRange(warnings);
                }
                return result;
            }
            finally
            {
                if (openedSession && TradeSession.Active)
                {
                    TradeSession.Close();
                }
            }
        }

        private static Pawn ResolveNegotiator(Map map, int? negotiatorPawnId)
        {
            if (negotiatorPawnId.HasValue)
            {
                return PawnHelper.FindPawnById(negotiatorPawnId.Value);
            }

            return map.mapPawns.FreeColonistsSpawned
                .Where(p => !p.Downed && !p.Dead)
                .OrderByDescending(p => p.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0)
                .FirstOrDefault();
        }

        private static int GetShipId(TradeShip ship)
        {
            return Math.Abs(ship.GetUniqueLoadID()?.GetHashCode() ?? ship.GetHashCode());
        }

        private static ITrader FindTrader(Map map, int traderId)
        {
            var ship = map.passingShipManager?.passingShips?
                .OfType<TradeShip>()
                .FirstOrDefault(s => s.CanTradeNow && GetShipId(s) == traderId);
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

        private static AiStockpileSummaryDto ToZoneSummaryDto(Zone_Stockpile zone)
        {
            IntVec3 pos = zone.cells.Count > 0 ? zone.cells[0] : IntVec3.Invalid;
            return new AiStockpileSummaryDto
            {
                Id = zone.ID,
                StorageType = "zone",
                DefName = null,
                Label = zone.label,
                Priority = zone.settings.Priority.ToString(),
                CellCount = zone.CellCount,
                AllowedDefsCount = zone.settings.filter.AllowedDefCount,
                Position = pos.IsValid ? new PositionDto { X = pos.x, Y = pos.y, Z = pos.z } : null
            };
        }

        private static AiStockpileSummaryDto ToBuildingSummaryDto(Building building, StorageSettings settings)
        {
            return new AiStockpileSummaryDto
            {
                Id = building.thingIDNumber,
                StorageType = "building",
                DefName = building.def?.defName,
                Label = building.LabelCap.ToString(),
                Priority = settings.Priority.ToString(),
                CellCount = building.OccupiedRect().Area,
                AllowedDefsCount = settings.filter.AllowedDefCount,
                Position = new PositionDto { X = building.Position.x, Y = building.Position.y, Z = building.Position.z }
            };
        }
    }
}
