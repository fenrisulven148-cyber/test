using System.Globalization;
using WorldSim.Core;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

// Аргументы: [ticks] [seed]
int totalTicks = args.Length > 0 && int.TryParse(args[0], out var t) ? t : 24 * 30; // по умолчанию 30 дней
int seed = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 1234;

var config = GameConfig.Default;
var world = new World(config, seed);

Console.WriteLine("=== Симуляция маленького города (WorldSim) ===");
Console.WriteLine($"Тиков: {totalTicks} ({totalTicks / config.TicksPerDay} дней), seed: {seed}, жителей: {config.InitialPopulation}");
Console.WriteLine();
Console.WriteLine($"{"День",4} | {"Нас.",4} | {"Цена еда",9} | {"Дерево",8} | {"Инстр.",8} | {"Еда скл",8} | {"Дер скл",8} | {"Инс скл",8} | {"$ в эк.",9} | Смерти");
Console.WriteLine(new string('-', 100));

int ticksPerDay = config.TicksPerDay;
for (int done = 0; done < totalTicks;)
{
    int batch = Math.Min(ticksPerDay, totalTicks - done);
    world.RunTicks(batch);
    done += batch;

    var r = world.BuildDayReport();
    Console.WriteLine(
        $"{r.Day,4} | {r.Population,4} | {r.FoodPrice,9:F2} | {r.WoodPrice,8:F2} | {r.ToolsPrice,8:F2} | " +
        $"{r.FoodStock,8:F1} | {r.WoodStock,8:F1} | {r.ToolStock,8:F1} | {r.MoneyInCirculation,9:F1} | {r.DeathsToday}");
}

var report = world.BuildDayReport();
Console.WriteLine();
Console.WriteLine("=== Итог ===");
Console.WriteLine($"Дней пройдено:      {report.Day}");
Console.WriteLine($"Население:          {report.Population} из {config.InitialPopulation}");
Console.WriteLine($"Цены (еда/дерево/инструменты): {report.FoodPrice:F2} / {report.WoodPrice:F2} / {report.ToolsPrice:F2}");
Console.WriteLine($"Запасы рынка:       еда {report.FoodStock:F1}, дерево {report.WoodStock:F1}, инструменты {report.ToolStock:F1}");
Console.WriteLine($"Эмиссия (банк):     {world.EmittedTotal:F1}");
Console.WriteLine($"Сожжено (смерти):   {world.BurnedTotal:F1}");
Console.WriteLine($"Деньги в экономике: {world.MoneyInEconomy():F1} (ожидается {world.EmittedTotal - world.BurnedTotal:F1})");
