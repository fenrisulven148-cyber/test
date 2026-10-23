namespace WorldSim.Core;

/// <summary>
/// Единая точка настройки всей симуляции. Все «магические числа» живут здесь.
/// </summary>
public sealed class GameConfig
{
    // ── Время ────────────────────────────────────────────────────────────
    public int TicksPerDay { get; init; } = 24;              // 1 тик = 1 час

    // ── Население и профессии ────────────────────────────────────────────
    public int InitialPopulation { get; init; } = 20;        // всего жителей в начале
    public int Farmers { get; init; } = 8;                   // производят еду
    public int Lumberjacks { get; init; } = 6;               // добывают дерево
    public int Blacksmiths { get; init; } = 4;               // инструменты из дерева
    public int Merchants { get; init; } = 2;                 // торгуют на рынке

    // ── Голод и жизнь ────────────────────────────────────────────────────
    public double HungerPerTick { get; init; } = 0.35;       // прирост голода за тик
    public double HungerMax { get; init; } = 100.0;          // верхняя граница голода
    public double FullMealHungerRestore { get; init; } = 60; // один приём пищи у рынка
    public int MaxStarvingDays { get; init; } = 3;           // сколько дней можно голодать до смерти
    public double StartingMoneyMin { get; init; } = 80;      // стартовые деньги (мин)
    public double StartingMoneyMax { get; init; } = 120;     // стартовые деньги (макс)
    public double EatHungerThreshold { get; init; } = 50;    // порог голода «пора поесть»

    // ── Потребление жителями (спрос на все три товара) ───────────────────
    public int WoodConsumptionPeriodDays { get; init; } = 4; // дрова нужны всем раз в N дней
    public double WoodPerHousehold { get; init; } = 1;       // единиц дерева на растопку
    public double ToolWearPerProducingDay { get; init; } = 0.2; // инструментов снашивается за рабочий день

    // ── Производство (за тик, только если есть сырьё) ────────────────────
    public double FarmerFoodOutput { get; init; } = 0.6;         // еды за тик
    public double LumberjackWoodOutput { get; init; } = 0.5;     // дерева за тик
    public double BlacksmithToolOutput { get; init; } = 0.4;     // инструментов за тик
    public double WoodPerTool { get; init; } = 0.5;              // дерева на один инструмент

    // ── Рынок: цены зависят от спроса и предложения ──────────────────────
    public double MinPrice { get; init; } = 1.0;             // нижняя граница цены
    public double MaxPrice { get; init; } = 100.0;           // верхняя граница цены
    public double BasePrice { get; init; } = 10.0;           // цена при «нормальном» запасе
    public double TargetStock { get; init; } = 50.0;         // эталонный запас товара на рынке
    public double PriceSensitivity { get; init; } = 0.7;     // сила реакции цены на нехватку/избыток
    public double MaxStockFactor { get; init; } = 2.0;       // рынок не скупает товар сверх TargetStock*фактор

    // ── Торговцы ─────────────────────────────────────────────────────────
    public double MerchantBuyMargin { get; init; } = 0.9;    // покупают у жителей по цене*маржа
    public double SellPriceFactor { get; init; } = 1.1;      // жители покупают любую еду/товар по цене*фактор
    public double MerchantCommissionShare { get; init; } = 1.0; // доля разницы цен, уходящая торговцам (комиссия)

    // ── Стартовые запасы на рынке ──────────────────────────────────────────
    public double InitialFoodStock { get; init; } = 30;
    public double InitialWoodStock { get; init; } = 10;
    public double InitialToolStock { get; init; } = 5;
    public double MarketStartingCash { get; init; } = 400;   // касса рынка (эмиссия под стартовые запасы)

    /// <summary>Общая сумма денег, выдаваемая жителям при создании.</summary>
    public double TotalStartingMoney => InitialPopulation * (StartingMoneyMin + StartingMoneyMax) / 2.0;

    public static GameConfig Default { get; } = new();
}
