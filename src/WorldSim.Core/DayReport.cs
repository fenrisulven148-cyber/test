namespace WorldSim.Core;

/// <summary>Краткая сводка по городу на конец дня.</summary>
public sealed record DayReport(
    int Day,
    int Population,
    double FoodPrice,
    double WoodPrice,
    double ToolsPrice,
    double FoodStock,
    double WoodStock,
    double ToolStock,
    double MoneyInCirculation,
    double EmittedTotal,
    double BurnedTotal,
    int DeathsToday);
