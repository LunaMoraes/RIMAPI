using System.Collections.Generic;

namespace RIMAPI.Models
{
    public class AiLetterDetailDto
    {
        public int Id { get; set; }
        public string Label { get; set; }
        public string Text { get; set; }
        public string Type { get; set; }
        public int ArrivalTick { get; set; }
        public bool CanDismiss { get; set; }
        public List<string> Choices { get; set; } = new List<string>();
        public string GrowthMomentPawn { get; set; }
        public List<string> PassionChoices { get; set; }
        public List<string> TraitChoices { get; set; }
    }

    public class AiDismissLetterRequestDto
    {
        public int LetterId { get; set; }
        public int? ChoiceIndex { get; set; }
        public List<string> SelectedPassions { get; set; }
        public string SelectedTrait { get; set; }
    }

    public class AiStockpileSummaryDto
    {
        public int Id { get; set; }
        public string StorageType { get; set; } // "zone" or "building"
        public string DefName { get; set; }
        public string Label { get; set; }
        public string Priority { get; set; }
        public int CellCount { get; set; }
        public int AllowedDefsCount { get; set; }
        public PositionDto Position { get; set; }
    }

    public class AiStockpileFilterRequestDto
    {
        public int MapId { get; set; }
        public int? ZoneId { get; set; }
        public int? BuildingId { get; set; }
        public string ZoneLabel { get; set; }
        public string Priority { get; set; }
        public bool? ClearAll { get; set; }
        public bool? AllowAll { get; set; }
        public List<string> AllowCategories { get; set; }
        public List<string> DisallowCategories { get; set; }
        public List<string> AllowDefs { get; set; }
        public List<string> DisallowDefs { get; set; }
    }

    public class AiTraderDto
    {
        public int TraderId { get; set; }
        public string Name { get; set; }
        public string TraderKind { get; set; }
        public string Type { get; set; } // "orbital" or "caravan"
        public int Silver { get; set; }
        public List<AiTraderGoodDto> Goods { get; set; } = new List<AiTraderGoodDto>();
    }

    public class AiTraderGoodDto
    {
        public string DefName { get; set; }
        public string Label { get; set; }
        public int Count { get; set; }
        public float MarketValue { get; set; }
    }

    public class AiTradeDealQuoteDto
    {
        public int MapId { get; set; }
        public int TraderId { get; set; }
        public string TraderName { get; set; }
        public int NegotiatorPawnId { get; set; }
        public string NegotiatorName { get; set; }
        public int ColonySilver { get; set; }
        public int TraderSilver { get; set; }
        public List<AiTradeDealLineDto> Lines { get; set; } = new List<AiTradeDealLineDto>();
    }

    public class AiTradeDealLineDto
    {
        public int Index { get; set; }
        public string DefName { get; set; }
        public string Label { get; set; }
        public int ColonyCount { get; set; }
        public int TraderCount { get; set; }
        public float BuyPrice { get; set; }
        public float SellPrice { get; set; }
        public int MinToTransfer { get; set; }
        public int MaxToTransfer { get; set; }
        public bool IsCurrency { get; set; }
        public bool IsFavor { get; set; }
    }

    public class AiTradeExecuteRequestDto
    {
        public int MapId { get; set; }
        public int TraderId { get; set; }
        public int? NegotiatorPawnId { get; set; }
        public bool DryRun { get; set; }
        public List<AiTradeItemOrderDto> Items { get; set; } = new List<AiTradeItemOrderDto>();
    }

    public class AiTradeItemOrderDto
    {
        public int? Index { get; set; }
        public string DefName { get; set; }
        public int CountDelta { get; set; } // >0 buy from trader, <0 sell to trader
    }

    public class AiTradeExecuteResultDto
    {
        public int TraderId { get; set; }
        public string TraderName { get; set; }
        public bool DryRun { get; set; }
        public bool Executed { get; set; }
        public int ItemsProcessed { get; set; }
        public int NetSilverChange { get; set; }
    }
}
