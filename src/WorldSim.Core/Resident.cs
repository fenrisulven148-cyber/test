namespace WorldSim.Core;

/// <summary>
/// Житель города: голод, деньги, инвентарь, профессия.
/// </summary>
public sealed class Resident
{
    public int Id { get; }
    public string Name { get; }
    public Profession Profession { get; }

    /// <summary>Голод 0..100, растёт каждый тик.</summary>
    public double Hunger { get; set; }

    public double Money { get; set; }

    /// <summary>Инвентарь: количество каждого товара у жителя.</summary>
    public Dictionary<GoodsType, double> Inventory { get; } = new()
    {
        [GoodsType.Food] = 0,
        [GoodsType.Wood] = 0,
        [GoodsType.Tools] = 0,
    };

    /// <summary>Сколько полных дней подряд житель провёл с голодом >= HungerMax.</summary>
    public int StarvingDays { get; set; }

    public bool IsAlive { get; set; } = true;

    public Resident(int id, string name, Profession profession, double money)
    {
        Id = id;
        Name = name;
        Profession = profession;
        Money = money;
    }

    public double Get(GoodsType g) => Inventory.TryGetValue(g, out var v) ? v : 0;
    public void Add(GoodsType g, double amount) => Inventory[g] = Get(g) + amount;
}
