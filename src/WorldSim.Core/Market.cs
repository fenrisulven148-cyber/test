namespace WorldSim.Core;

/// <summary>
/// Рынок: запасы товаров и цены (спрос/предложение).
/// Деньги через рынок только перетекают: покупатель кладёт в кассу, продавец берёт из кассы.
/// Сам рынок деньги не эмитирует и не сжигает (см. AGENTS.md).
/// </summary>
public sealed class Market
{
    private readonly GameConfig _cfg;
    private readonly Dictionary<GoodsType, double> _stock = new();
    private readonly Dictionary<GoodsType, double> _price = new();

    public Market(GameConfig cfg)
    {
        _cfg = cfg;
        _stock[GoodsType.Food] = cfg.InitialFoodStock;
        _stock[GoodsType.Wood] = cfg.InitialWoodStock;
        _stock[GoodsType.Tools] = cfg.InitialToolStock;
        foreach (var g in Goods.All)
            _price[g] = PriceFor(_stock[g]);
    }

    /// <summary>Касса рынка: деньги покупателей, ещё не выплаченные продавцам.</summary>
    public double Cash { get; set; }

    public double GetStock(GoodsType g) => _stock[g];
    public double GetPrice(GoodsType g) => _price[g];

    /// <summary>Цена по запасу: мало товара — дороже. Жёстко ограничена MinPrice..MaxPrice.</summary>
    private double PriceFor(double stock)
    {
        double ratio = (_cfg.TargetStock + 1.0) / (stock + 1.0); // >1 при дефиците
        double p = _cfg.BasePrice * Math.Pow(ratio, _cfg.PriceSensitivity);
        return Math.Clamp(p, _cfg.MinPrice, _cfg.MaxPrice);
    }

    /// <summary>Пересчёт цен в конце тика.</summary>
    public void UpdatePrices()
    {
        foreach (var g in Goods.All)
            _price[g] = PriceFor(_stock[g]);
    }

    /// <summary>Цена, по которой рынок закупает товар у жителя (комиссия торговцев).</summary>
    public double UnitBuyPrice(GoodsType g) => _price[g] * _cfg.MerchantBuyMargin;

    /// <summary>Цена, по которой житель покупает товар на рынке (наценка на еду).</summary>
    public double PriceForBuyer(GoodsType g) =>
        _price[g] * (g == GoodsType.Food ? _cfg.SellPriceFactor : 1.0);

    /// <summary>Складывается ли сделка «житель продаёт рынку»: есть ли деньги в кассе.</summary>
    public bool CanBuyFrom(GoodsType g, double amount) =>
        amount > 0 && Cash >= UnitBuyPrice(g) * amount - 1e-9;

    /// <summary>Рынок принимает товар на склад (выплату денег делает World из кассы).</summary>
    public void AcceptGoods(GoodsType g, double amount)
    {
        if (amount > 0) _stock[g] += amount;
    }

    /// <summary>Рынок отдаёт товар жителю (полную стоимость перечисляет в кассу World).</summary>
    public bool ReleaseGoods(GoodsType g, double amount, out double cost)
    {
        cost = 0;
        if (amount <= 0 || _stock[g] < amount - 1e-9) return false;
        cost = PriceForBuyer(g) * amount;
        _stock[g] -= amount;
        return true;
    }
}
