using WorldSim.Core;
using Xunit;

namespace WorldSim.Tests;

public class SimulationTests
{
    private const double Epsilon = 1e-6;

    // ── Тест 1: за 10 000 тиков население не вымирает полностью ───────────
    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(777)]
    public void Population_Survives_10000_Ticks(int seed)
    {
        var world = new World(GameConfig.Default, seed);
        world.RunTicks(10_000);

        Assert.True(world.Day >= 416, $"Должно пройти ~416 дней, прошло {world.Day}");
        Assert.True(world.Population() > 0,
            $"Население вымерло за 10 000 тиков (seed={seed})");
        // Кроме того, большинство должно выжить — город устойчив.
        Assert.True(world.Population() >= GameConfig.Default.InitialPopulation / 2,
            $"Население упало ниже половины: {world.Population()} (seed={seed})");
    }

    // ── Тест 2: цены всегда в заданных границах ───────────────────────────
    [Fact]
    public void Prices_Always_Within_Config_Bounds()
    {
        var cfg = GameConfig.Default;
        var world = new World(cfg, seed: 5);

        for (int tick = 0; tick < 5_000; tick++)
        {
            world.TickOnce();
            foreach (var g in Goods.All)
            {
                double price = world.Market.GetPrice(g);
                Assert.True(price >= cfg.MinPrice - Epsilon && price <= cfg.MaxPrice + Epsilon,
                    $"{g} на тике {tick}: цена {price} вне границ [{cfg.MinPrice}, {cfg.MaxPrice}]");
                // Цена для покупателя отличается не более чем на SellPriceFactor.
                double buyPrice = world.Market.PriceForBuyer(g);
                Assert.True(buyPrice <= cfg.MaxPrice * cfg.SellPriceFactor + Epsilon);
            }
        }
    }

    // ── Тест 3: деньги не создаются из воздуха ─────────────────────────────
    [Fact]
    public void Money_Is_Conserved_After_Emission_And_Burn()
    {
        var world = new World(GameConfig.Default, seed: 9);

        // В самом начале: вся экономика = эмиссия банка.
        Assert.Equal(world.EmittedTotal, world.MoneyInEconomy(), 6);

        for (int tick = 0; tick < 5_000; tick++)
        {
            world.TickOnce();
            // Инвариант: деньги у живых + касса рынка = эмиссия − сожжённое.
            Assert.True(
                Math.Abs(world.EmittedTotal - world.BurnedTotal - world.MoneyInEconomy()) < 1e-6,
                $"Нарушение баланса денег на тике {tick}");
        }
    }

    // ── Тест 4: детерминированность при одинаковом seed ────────────────────
    [Fact]
    public void Simulation_Is_Deterministic_For_Same_Seed()
    {
        string Run(int seed)
        {
            var w = new World(GameConfig.Default, seed);
            w.RunTicks(2_000);
            var r = w.BuildDayReport();
            return $"{r.Population}|{r.FoodPrice:F6}|{r.WoodPrice:F6}|{r.ToolsPrice:F6}|" +
                   $"{r.FoodStock:F6}|{r.WoodStock:F6}|{r.ToolStock:F6}|{r.MoneyInCirculation:F6}";
        }

        Assert.Equal(Run(7), Run(7));
        Assert.NotEqual(Run(7), Run(8)); // разные seed → разный мир
    }

    // ── Дополнительные проверки базовых правил ────────────────────────────

    [Fact]
    public void Hunger_Grows_Every_Tick_And_Resident_With_No_Money_Eventually_Dies()
    {
        var cfg = GameConfig.Default;
        var world = new World(cfg, seed: 3);

        // Голод растёт каждый тик.
        var r0 = world.Residents[0];
        double before = r0.Hunger;
        world.TickOnce();
        Assert.True(r0.Hunger > before);
        Assert.Equal(Math.Min(cfg.HungerMax, before + cfg.HungerPerTick), r0.Hunger, 9);

        // Посадим жителя в изоляцию без денег — он обязан умереть через MaxStarvingDays+ день.
        var victim = world.Residents[1];
        victim.Money = 0;
        int startDay = world.Day;
        while (victim.IsAlive && world.Day - startDay < cfg.MaxStarvingDays + 5)
            world.TickOnce();
        Assert.False(victim.IsAlive, "Житель без денег должен умереть с голода.");
    }

    [Fact]
    public void Profession_Counts_Match_Config()
    {
        var cfg = GameConfig.Default;
        var world = new World(cfg, seed: 1);

        Assert.Equal(cfg.Farmers, world.Residents.Count(r => r.Profession == Profession.Farmer));
        Assert.Equal(cfg.Lumberjacks, world.Residents.Count(r => r.Profession == Profession.Lumberjack));
        Assert.Equal(cfg.Blacksmiths, world.Residents.Count(r => r.Profession == Profession.Blacksmith));
        Assert.Equal(cfg.Merchants, world.Residents.Count(r => r.Profession == Profession.Merchant));
        Assert.Equal(cfg.InitialPopulation, world.Residents.Count);
    }

    [Fact]
    public void Day_Length_Is_24_Ticks()
    {
        var world = new World(GameConfig.Default, seed: 1);
        Assert.Equal(24, GameConfig.Default.TicksPerDay);
        world.RunTicks(24);
        Assert.Equal(1, world.Day);
        world.RunTicks(48);
        Assert.Equal(3, world.Day);
    }

    [Fact]
    public void Blacksmith_Consumes_Wood_To_Produce_Tools()
    {
        var cfg = GameConfig.Default;
        var world = new World(cfg, seed: 2);
        var smith = world.Residents.First(r => r.Profession == Profession.Blacksmith);

        // Дадим кузнецу дерево и прогоним тик — часть дерева превратится в инструменты.
        smith.Add(GoodsType.Wood, 10);
        double woodBefore = smith.Get(GoodsType.Wood);
        world.TickOnce();
        // Дерево либо ушло в инструменты, либо продано на рынок; инструменты появились или проданы.
        double woodSpentOrSold = woodBefore - smith.Get(GoodsType.Wood);
        Assert.True(woodSpentOrSold > 0, "Кузнец должен расходовать/продавать дерево.");
    }
}
