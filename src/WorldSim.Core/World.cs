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
    /// <summary>Эмиссия: сколько денег банк выдал жителям и рынку при старте мира.</summary>
    public double EmittedTotal { get; private set; }
    /// <summary>Сжигание: деньги умерших жителей изымаются из обращения.</summary>
    public double BurnedTotal { get; private set; }

    /// <summary>Все деньги в экономике: у живых жителей + касса рынка.</summary>
    public double MoneyInEconomy() => MoneyInCirculation() + Market.Cash;

    // Комиссии (наценка рынка сверх закупочной цены) копятся в кассе и в конце дня
    // распределяются торговцам. Это перераспределение, а не эмиссия.
    private double _commissionDue;

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
        bool isEndOfDay = (Tick + 1) % Config.TicksPerDay == 0;

        foreach (var r in _residents.Where(r => r.IsAlive))
        {
            AdvanceHunger(r);
            Produce(r);
            SellProduce(r);
            TryBuyFood(r);
            if (isEndOfDay)
            {
                ConsumeWood(r);      // дрова нужны всем раз в N дней
                WearTools(r);        // инструменты изнашиваются у производящих профессий
                BuyToolsIfBroken(r); // спрос на инструменты
            }
        }

        if (isEndOfDay) PayMerchantCommissions();
        EndOfDayIfDue(isEndOfDay);
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
                break; // торговцы не производят — живут с комиссии (PayMerchantCommissions)
        }
    }

    /// <summary>
    /// Излишки товара житель сдаёт на рынок по одной целой единице за раз.
    /// Ограничения внутри Market.SellToMarket: склад не больше MaxStockFactor*TargetStock,
    /// выплаты только из кассы — эмиссия во время тика запрещена.
    /// </summary>
    private void SellProduce(Resident r)
    {
        foreach (var g in Goods.All)
        {
            double keep = KeepAmount(r, g);
            int surplusUnits = (int)Math.Floor(r.Get(g) - keep + 1e-9);
            if (surplusUnits <= 0) continue;
            Market.SellToMarket(g, r, surplusUnits);
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
        return 0;
    }

    /// <summary>
    /// Житель покупает еду на рынке, если голоден и есть деньги.
    /// Покупает и съедает РОВНО ОДНУ порцию за тик (не всё доступное).
    /// </summary>
    private void TryBuyFood(Resident r)
    {
        if (r.Hunger < Config.EatHungerThreshold) return;
        int bought = Market.BuyFromMarket(GoodsType.Food, 1, r);
        if (bought > 0)
        {
            Eat(r, GoodsType.Food, bought);
            _commissionDue += CommissionFor(GoodsType.Food, bought);
        }
    }

    /// <summary>Житель съедает/сжигает купленный товар (он уже в его инвентаре).</summary>
    private void Eat(Resident r, GoodsType g, int units)
    {
        r.Add(g, -units);
        if (g == GoodsType.Food)
            r.Hunger = Math.Max(0, r.Hunger - units * Config.FullMealHungerRestore);
    }

    /// <summary>Наценка рынка над закупочной ценой — будущая комиссия торговцам.</summary>
    private double CommissionFor(GoodsType g, int units) =>
        (Market.PriceForBuyer(g) - Market.UnitBuyPrice(g)) * units;

    /// <summary>Комиссия торговцам выплачивается в конце дня из кассы (перераспределение, не эмиссия).</summary>
    private void PayMerchantCommissions()
    {
        if (_commissionDue <= 1e-12) return;
        var merchants = _residents.Where(r => r.IsAlive && r.Profession == Profession.Merchant).ToList();
        if (merchants.Count == 0) return; // без торговцев сумма остаётся в кассе рынка
        double take = Math.Min(_commissionDue, Market.Cash);
        if (take <= 1e-12) return;
        Market.PayOut(take);
        double share = take / merchants.Count;
        foreach (var m in merchants) m.Money += share;
        _commissionDue = 0;
    }

    /// <summary>Раз в WoodConsumptionPeriodDays дней каждый житель топит печь: 1 ед. дерева.</summary>
    private void ConsumeWood(Resident r)
    {
        int period = Config.WoodConsumptionPeriodDays;
        if (period <= 0 || Day % period != 0) return;
        // Сначала берём из собственного инвентаря (дерево сожжено).
        double need = Config.WoodPerHousehold;
        double fromInventory = Math.Min(r.Get(GoodsType.Wood), need);
        if (fromInventory > 1e-9) r.Add(GoodsType.Wood, -fromInventory);
        need -= fromInventory;
        // Недостающее докупаем на рынке по одной единице; купленное сразу идёт в печь.
        while (need > 1e-9)
        {
            int bought = Market.BuyFromMarket(GoodsType.Wood, 1, r);
            if (bought == 0) break; // нет денег/склада/кассы — мёрзнем, но выживаем
            Eat(r, GoodsType.Wood, bought);
            _commissionDue += CommissionFor(GoodsType.Wood, bought);
            need -= bought;
        }
    }

    private static bool IsProducingProfession(Profession p) =>
        p == Profession.Farmer || p == Profession.Lumberjack || p == Profession.Blacksmith;

    /// <summary>Инструменты изнашиваются у производящих профессий (фермеры, дровосеки, кузнецы).</summary>
    private void WearTools(Resident r)
    {
        if (!IsProducingProfession(r.Profession)) return;
        double wear = Math.Min(r.Get(GoodsType.Tools), Config.ToolWearPerProducingDay);
        if (wear > 1e-9) r.Add(GoodsType.Tools, -wear);
        // Сломанный инструмент списан и в оборот не возвращается.
    }

    /// <summary>Если рабочий инструмент изношен полностью — житель покупает новый на рынке.</summary>
    private void BuyToolsIfBroken(Resident r)
    {
        if (!IsProducingProfession(r.Profession)) return;
        if (r.Get(GoodsType.Tools) >= 1.0 - 1e-9) return;
        int bought = Market.BuyFromMarket(GoodsType.Tools, 1, r);
        if (bought > 0)
        {
            r.Add(GoodsType.Tools, -bought); // тут же ставится вместо сломанного
            _commissionDue += CommissionFor(GoodsType.Tools, bought);
        }
    }

    /// <summary>Раз в конце дня: учёт голодавших и смертей.</summary>
    private void EndOfDayIfDue(bool isEndOfDay)
    {
        if (!isEndOfDay) return;
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
