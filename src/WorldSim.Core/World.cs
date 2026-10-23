namespace WorldSim.Core;

/// <summary>
/// Ядро симуляции. Детерминирована: все случайные решения идут через один
/// System.Random(seed), порядок обитателей фиксирован.
/// </summary>
public sealed class World
{
    public GameConfig Config { get; }
    public IReadOnlyList<Resident> Residents => _residents;
    public Market Market { get; }
    public long Tick { get; private set; }
    public int Day => (int)(Tick / Config.TicksPerDay);

    private readonly List<Resident> _residents = new();
    private readonly Random _rng;

    // ── Денежный учёт (для теста «деньги не из воздуха») ──────────────────
    /// <summary>Эмиссия: сколько денег банк выдал жителям при старте мира.</summary>
    public double EmittedTotal { get; private set; }
    /// <summary>Сжигание: деньги умерших жителей изымаются из обращения.</summary>
    public double BurnedTotal { get; private set; }

    /// <summary>Все деньги в экономике: у живых жителей + касса рынка.</summary>
    public double MoneyInEconomy() => MoneyInCirculation() + Market.Cash;

    private static readonly string[] Names =
    {
        "Аня", "Борис", "Вера", "Глеб", "Даша", "Егор", "Женя", "Зоя",
        "Илья", "Катя", "Лев", "Маша", "Никита", "Оля", "Пётр", "Рита",
        "Саша", "Тимур", "Ульяна", "Фёдор", "Хионя", "Цвета", "Честник", "Шурa",
    };

    public World(GameConfig? config = null, int seed = 1)
    {
        Config = config ?? GameConfig.Default;
        _rng = new Random(seed);
        Market = new Market(Config);

        var professions = BuildProfessionList(Config);
        for (int i = 0; i < Config.InitialPopulation; i++)
        {
            double money = NextDouble(Config.StartingMoneyMin, Config.StartingMoneyMax);
            _residents.Add(new Resident(i, Names[i % Names.Length], professions[i], money));
            EmittedTotal += money; // стартовый капитал эмитирован банком
        }

        // Стартовый капитал рынка: эмиссия под стартовые запасы товаров.
        Market.Deposit(Config.MarketStartingCash);
        EmittedTotal += Config.MarketStartingCash;
    }

    private static List<Profession> BuildProfessionList(GameConfig cfg)
    {
        var list = new List<Profession>(cfg.InitialPopulation);
        AddProfession(list, Profession.Farmer, cfg.Farmers);
        AddProfession(list, Profession.Lumberjack, cfg.Lumberjacks);
        AddProfession(list, Profession.Blacksmith, cfg.Blacksmiths);
        AddProfession(list, Profession.Merchant, cfg.Merchants);
        if (list.Count != cfg.InitialPopulation)
            throw new ArgumentException(
                $"Сумма профессий ({list.Count}) не равна населению ({cfg.InitialPopulation}).");
        return list;
    }

    private static void AddProfession(List<Profession> list, Profession p, int count)
    {
        for (int i = 0; i < count; i++) list.Add(p);
    }

    private double NextDouble(double min, double max) => min + _rng.NextDouble() * (max - min);

    /// <summary>Сумма денег у живых жителей.</summary>
    public double MoneyInCirculation()
    {
        double sum = 0;
        foreach (var r in _residents)
            if (r.IsAlive) sum += r.Money;
        return sum;
    }

    /// <summary>Продвинуть мир на один тик (час).</summary>
    public void TickOnce()
    {
        foreach (var r in _residents.Where(r => r.IsAlive))
        {
            AdvanceHunger(r);
            Produce(r);
            SellProduce(r);
            TryBuyFood(r);
        }

        EndOfDayIfDue();
        Market.UpdatePrices();
        Tick++;
    }

    public void RunTicks(int ticks)
    {
        for (int i = 0; i < ticks; i++) TickOnce();
    }

    private void AdvanceHunger(Resident r)
    {
        r.Hunger = Math.Min(Config.HungerMax, r.Hunger + Config.HungerPerTick);
    }

    /// <summary>Производство по профессии. Кузнец тратит дерево из инвентаря.</summary>
    private void Produce(Resident r)
    {
        switch (r.Profession)
        {
            case Profession.Farmer:
                r.Add(GoodsType.Food, Config.FarmerFoodOutput);
                break;
            case Profession.Lumberjack:
                r.Add(GoodsType.Wood, Config.LumberjackWoodOutput);
                break;
            case Profession.Blacksmith:
                double need = Config.BlacksmithToolOutput * Config.WoodPerTool;
                if (r.Get(GoodsType.Wood) >= need)
                {
                    r.Add(GoodsType.Wood, -need);
                    r.Add(GoodsType.Tools, Config.BlacksmithToolOutput);
                }
                break;
            case Profession.Merchant:
                break; // торговцы не производят — они обслуживают рынок
        }
    }

    /// <summary>Излишки товара житель сдаёт на рынок за деньги из кассы рынка.</summary>
    private void SellProduce(Resident r)
    {
        foreach (var g in Goods.All)
        {
            double keep = KeepAmount(r, g);
            double surplus = r.Get(g) - keep;
            if (surplus > 1e-9)
            {
                double price = Market.UnitBuyPrice(g);
                // Выплачиваем ровно столько, сколько есть в кассе (эмиссия запрещена).
                double affordableUnits = Market.Cash / price;
                double units = Math.Min(surplus, affordableUnits);
                if (units <= 1e-9) continue; // касса пуста — сделка не состоялась
                double payout = price * units;
                Market.PayOut(payout);
                Market.AcceptGoods(g, units);
                r.Add(g, -units);
                r.Money += payout;
            }
        }
    }

    /// <summary>Сколько товара житель оставляет себе (еда — на ближайшую еду).</summary>
    private double KeepAmount(Resident r, GoodsType g)
    {
        if (g == GoodsType.Food)
        {
            // Оставляем столько еды, чтобы хватило примерно на сутки.
            double mealsPerDay = Config.HungerPerTick * Config.TicksPerDay / Config.FullMealHungerRestore;
            return Math.Ceiling(mealsPerDay);
        }
        if (g == GoodsType.Wood && r.Profession == Profession.Blacksmith)
            return 0; // всё дерево идёт в переработку/продажу
        return 0;
    }

    /// <summary>Житель покупает еду на рынке, если голоден и есть деньги.</summary>
    private void TryBuyFood(Resident r)
    {
        if (r.Hunger < 50) return;
        double price = Market.PriceForBuyer(GoodsType.Food);
        double afford = Math.Floor(r.Money / price);
        if (afford <= 0) return; // денег нет — остаётся голодать
        double amount = Math.Min(afford, Math.Max(1, Market.GetStock(GoodsType.Food)));
        if (amount <= 0) return;
        if (Market.BuyFromMarket(GoodsType.Food, amount, r, out double cost))
        {
            // Деньги за покупку остаются в экономике: переводим их с личного счёта
            // покупателя в кассу рынка.
            Market.Deposit(cost);
            double meals = Math.Floor(amount);
            if (meals > 0)
            {
                r.Add(GoodsType.Food, -meals);
                r.Hunger = Math.Max(0, r.Hunger - meals * Config.FullMealHungerRestore);
            }
        }
    }

    /// <summary>Раз в конце дня: учёт голодавших и смертей.</summary>
    private void EndOfDayIfDue()
    {
        if ((Tick + 1) % Config.TicksPerDay != 0) return;
        DeathsToday = 0;
        foreach (var r in _residents.Where(r => r.IsAlive))
        {
            if (r.Hunger >= Config.HungerMax)
                r.StarvingDays++;
            else
                r.StarvingDays = 0;

            if (r.StarvingDays > Config.MaxStarvingDays)
            {
                r.IsAlive = false;
                // Деньги умершего изымаются из обращения («сгорают»).
                BurnedTotal += r.Money;
                r.Money = 0;
                DeathsToday++;
            }
        }
    }

    public int DeathsToday { get; private set; }

    public int Population() => _residents.Count(r => r.IsAlive);

    public DayReport BuildDayReport() => new(
        Day: Day,
        Population: Population(),
        FoodPrice: Market.GetPrice(GoodsType.Food),
        WoodPrice: Market.GetPrice(GoodsType.Wood),
        ToolsPrice: Market.GetPrice(GoodsType.Tools),
        FoodStock: Market.GetStock(GoodsType.Food),
        WoodStock: Market.GetStock(GoodsType.Wood),
        ToolStock: Market.GetStock(GoodsType.Tools),
        MoneyInCirculation: MoneyInCirculation(),
        EmittedTotal: EmittedTotal,
        BurnedTotal: BurnedTotal,
        DeathsToday: DeathsToday);
}
