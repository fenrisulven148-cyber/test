namespace WorldSim.Core;

/// <summary>Три товара, доступные в мире.</summary>
public enum GoodsType
{
    Food = 0,
    Wood = 1,
    Tools = 2,
}

public static class Goods
{
    public static readonly GoodsType[] All = { GoodsType.Food, GoodsType.Wood, GoodsType.Tools };

    public static string Name(this GoodsType g) => g switch
    {
        GoodsType.Food => "еда",
        GoodsType.Wood => "дерево",
        GoodsType.Tools => "инструменты",
        _ => g.ToString(),
    };
}

/// <summary>Профессии жителей.</summary>
public enum Profession
{
    Farmer = 0,
    Lumberjack = 1,
    Blacksmith = 2,
    Merchant = 3,
}
