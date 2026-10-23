namespace WorldSim.Core;

/// <summary>
/// Рынок: запасы товаров и цены (спрос/предложение).
/// Деньги через рынок только перетекают: покупатель кладёт в кассу, продавец берёт из кассы.
/// Сам рынок деньги не эмитирует и не сжигает (см. AGENTS.md).
/// Товар на складе учитывается FIFO-очередью с указанием продавца — чтобы при продаже
/// деньги покупателя шли прежнему владельцу товара, а наценка оставалась торговцам.
/// </summary>
public sealed class Market
{
    private readonly GameConfig _cfg;
    private readonly Dictionary<GoodsType, double> _stock = new();
    private readonly Dictionary<GoodsType, double> _price = new();

    /// <summary>Партия товара на складе рынка; SellerId = null — начальные запасы.</summary>
    private sealed record StockUnit(double Amount, int? SellerId);

    private readonly Dictionary<GoodsType, Queue<StockUnit>> _goods = new();

    public Market(GameConfig cfg)
    {
        _cfg = cfg;
        foreach (var g in Goods.All)
        {
            _goods[g] = new Queue<StockUnit>();
            _stock[g] = InitialStock(g);
            _price[g] = PriceFor(_stock[g]);
        }
    }

    private double InitialStock(GoodsType g) => g switch
    {
        GoodsType.Food => _cfg.InitialFoodStock,
        GoodsType.Wood => _cfg.InitialWoodStock,
        GoodsType.Tools => _cfg.InitialToolStock,
        _ => 0,
    };

    /// <summary>Касса рынка: деньги в обороте рынка. Никогда не уходит в минус.</summary>
    public double Cash { get; private set; }

    public double GetStock(GoodsType g) => _stock[g];
    public double GetPrice(GoodsType g) => _price[g];

    /// <summary>Пополнить кассу рынка (эмиссию делает только World при старте мира).</summary>
    public void Deposit(double amount)
    {
        if (amount > 0) Cash += amount;
    }

    /// <summary>Выплатить из кассы. Возвращает false, если денег не хватает (касса не уходит в минус).</summary>
    public bool PayOut(double amount)
    {
        if (amount <= 0) return true;
        if (Cash + 1e-9 < amount) return false;
        Cash -= amount;
        return true;
    }

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

    /// <summary>Цена, по которой продавец получает деньги за сданный товар (комиссия торговцев).</summary>
    public double UnitBuyPrice(GoodsType g) => _price[g] * _cfg.MerchantBuyMargin;

    /// <summary>Цена, по которой житель покупает товар на рынке.</summary>
    public double PriceForBuyer(GoodsType g) => _price[g] * _cfg.SellPriceFactor;

    /// <summary>Наценка рынка над закупочной ценой за единицу — будущая комиссия торговцам.</summary>
    public double UnitCommission(GoodsType g) => PriceForBuyer(g) - UnitBuyPrice(g);

    /// <summary>
    /// Рынок скупает товар только пока запас не превышает разумный уровень:
    /// при stock >= MaxStockFactor*TargetStock покупка прекращается (касса не опустошается).
    /// </summary>
    public bool ShouldRestock(GoodsType g) =>
        _stock[g] < _cfg.MaxStockFactor * _cfg.TargetStock;

    /// <summary>
    /// Покупка жителем порции товара у рынка, по одной ЦЕЛОЙ единице за раз (FIFO со склада).
    /// Сделка: склад >= 1 ед., покупателю хватает денег, касса выдержит выплату прежнему владельцу.
    /// Покупатель вносит полную цену в кассу; оттуда сразу выплачивается закупочная цена
    /// продавцу партии. Если продавца уже нет среди живых или денег не хватает — сумма
    /// остаётся в кассе (эмиссии нет). Наценка остаётся в кассе как будущая комиссия торговцам.
    /// Возвращает число купленных единиц; товар переходит покупателю напрямую (не через инвентарь).
    /// </summary>
    public int BuyFromMarket(GoodsType g, int units, Resident buyer)
    {
        int bought = 0;
        for (int i = 0; i < units; i++)
        {
            double sellPrice = PriceForBuyer(g);
            if (_stock[g] < 1.0 - 1e-9) break;
            if (buyer.Money + 1e-9 < sellPrice) break;

            var head = _goods[g].Peek();
            double producerPrice = UnitBuyPrice(g);
            var seller = head.SellerId.HasValue ? FindResidentById(head.SellerId.Value) : null;
            bool paySeller = seller != null && Cash + sellPrice + 1e-9 >= producerPrice;

            buyer.Money -= sellPrice;
            Cash += sellPrice;                 // деньги покупателя пришли в кассу
            if (paySeller)
            {
                Cash -= producerPrice;         // выплата прежнему владельцу товара
                seller!.Money += producerPrice;
            }
            TakeStock(g, 1.0);
            bought++;
        }
        return bought;
    }

    /// <summary>Поиск живого жителя по Id (назначает World).</summary>
    internal Func<int, Resident?>? FindResidentById { get; set; }

    /// <summary>
    /// Житель сдаёт излишки товара рынку по одной целой единице за раз.
    /// Сделки нет, если: склад рынка уже переполнен (ShouldRestock), в кассе нечего платить,
    /// или у жителя меньше целой единицы. Выплата продавцу идёт сразу из кассы.
    /// </summary>
    public int SellToMarket(GoodsType g, Resident seller, int maxUnits)
    {
        int sold = 0;
        for (int i = 0; i < maxUnits; i++)
        {
            double producerPrice = UnitBuyPrice(g);
            if (!ShouldRestock(g)) break;
            if (seller.Get(g) < 1.0 - 1e-9) break;
            if (Cash + 1e-9 < producerPrice) break; // касса пуста — сделок больше не будет
            Cash -= producerPrice;
            seller.Money += producerPrice;
            seller.Add(g, -1.0);                   // единица уходит от жителя...
            AddStock(g, 1.0, seller.Id);           // ...и приходит на склад рынка (с владельцем)
            sold++;
        }
        return sold;
    }

    private void AddStock(GoodsType g, double amount, int sellerId)
    {
        _goods[g].Enqueue(new StockUnit(amount, sellerId));
        _stock[g] += amount;
    }

    private void TakeStock(GoodsType g, double amount)
    {
        double remaining = amount;
        while (remaining > 1e-9 && _goods[g].Count > 0)
        {
            var head = _goods[g].Peek();
            if (head.Amount <= remaining + 1e-9)
            {
                _goods[g].Dequeue();
                remaining -= head.Amount;
            }
            else
            {
                _goods[g].Dequeue();
                _goods[g].Enqueue(head with { Amount = head.Amount - remaining });
                remaining = 0;
            }
        }
        _stock[g] = Math.Max(0, _stock[g] - amount);
    }
}
