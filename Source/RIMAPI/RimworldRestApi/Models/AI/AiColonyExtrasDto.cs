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
    }

    public class AiDismissLetterRequestDto
    {
        public int LetterId { get; set; }
        public int? ChoiceIndex { get; set; }
    }

    public class AiStockpileSummaryDto
    {
        public int Id { get; set; }
        public string Label { get; set; }
        public string Priority { get; set; }
        public int CellCount { get; set; }
        public int AllowedDefsCount { get; set; }
    }

    public class AiStockpileFilterRequestDto
    {
        public int MapId { get; set; }
        public int? ZoneId { get; set; }
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

    public class AiTradeExecuteRequestDto
    {
        public int MapId { get; set; }
        public int TraderId { get; set; }
        public int? NegotiatorPawnId { get; set; }
        public List<AiTradeItemOrderDto> Items { get; set; } = new List<AiTradeItemOrderDto>();
    }

    public class AiTradeItemOrderDto
    {
        public string DefName { get; set; }
        public int CountDelta { get; set; } // >0 buy from trader, <0 sell to trader
    }

    public class AiTradeExecuteResultDto
    {
        public int TraderId { get; set; }
        public string TraderName { get; set; }
        public bool Executed { get; set; }
        public int ItemsProcessed { get; set; }
    }
}
