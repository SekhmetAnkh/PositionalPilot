using System.Numerics;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using LuminaSupplemental.Excel.Model;
using LuminaSupplemental.Excel.Services;

namespace DropListAutomator.Planning;

internal sealed class DropLocationProvider(PluginServices services)
{
    private Dictionary<uint, DropItemInfo>? dropsByItemId;

    public DropItemInfo GetDropInfo(uint itemId)
    {
        EnsureBuilt();
        return dropsByItemId!.TryGetValue(itemId, out var info) ? info : DropItemInfo.Empty;
    }

    public bool IsKnownDrop(uint itemId) => GetDropInfo(itemId).HasData;

    private void EnsureBuilt()
    {
        if (dropsByItemId != null)
            return;

        try
        {
            dropsByItemId = Build();
        }
        catch (Exception ex)
        {
            services.Log.Warning(ex, "Failed to build drop location cache.");
            dropsByItemId = [];
        }
    }

    private Dictionary<uint, DropItemInfo> Build()
    {
        var gameData = services.Data.GameData;
        var language = gameData.Options.DefaultExcelLanguage;
        var mobDrops = CsvLoader.LoadResource<MobDrop>(CsvLoader.MobDropResourceName, true, out _, out _, gameData, language) ?? [];
        var mobSpawns = CsvLoader.LoadResource<MobSpawnPosition>(CsvLoader.MobSpawnResourceName, true, out _, out _, gameData, language) ?? [];

        var bNpcNames = services.Data.GetExcelSheet<BNpcName>();
        var territories = services.Data.GetExcelSheet<TerritoryType>();
        var maps = services.Data.GetExcelSheet<Map>();
        var spawnsByMob = mobSpawns
            .Where(spawn => spawn.BNpcNameId != 0 && spawn.TerritoryTypeId != 0)
            .GroupBy(spawn => spawn.BNpcNameId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var byItemAndMob = mobDrops
            .Where(drop => drop.ItemId != 0 && drop.BNpcNameId != 0)
            .GroupBy(drop => (drop.ItemId, drop.BNpcNameId));

        var mutable = new Dictionary<uint, List<DropMobInfo>>();
        foreach (var group in byItemAndMob)
        {
            if (!bNpcNames.TryGetRow(group.Key.BNpcNameId, out var bNpcName))
                continue;

            var mobName = bNpcName.Singular.ExtractText();
            if (string.IsNullOrWhiteSpace(mobName))
                continue;

            spawnsByMob.TryGetValue(group.Key.BNpcNameId, out var spawns);
            var zones = BuildZones(spawns ?? [], territories, maps);
            if (zones.Count == 0)
                continue;

            if (!mutable.TryGetValue(group.Key.ItemId, out var mobs))
                mutable[group.Key.ItemId] = mobs = [];
            mobs.Add(new DropMobInfo(group.Key.BNpcNameId, mobName, zones));
        }

        return mutable.ToDictionary(
            pair => pair.Key,
            pair => new DropItemInfo(pair.Value
                .OrderBy(mob => mob.MobName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(mob => mob.BNpcNameId)
                .ToList()));
    }

    private static List<DropZoneInfo> BuildZones(
        IReadOnlyList<MobSpawnPosition> spawns,
        ExcelSheet<TerritoryType> territories,
        ExcelSheet<Map> maps)
    {
        var zones = new List<DropZoneInfo>();
        foreach (var group in spawns.GroupBy(spawn => spawn.TerritoryTypeId))
        {
            if (!territories.TryGetRow(group.Key, out var territory))
                continue;
            if (territory.ContentFinderCondition.RowId != 0 || territory.QuestBattle.RowId != 0)
                continue;

            var map = territory.Map.RowId != 0 && maps.TryGetRow(territory.Map.RowId, out var foundMap)
                ? foundMap
                : (Map?)null;
            var zoneName = territory.PlaceName.ValueNullable?.Name.ExtractText() ?? $"Territory {group.Key}";
            var clusters = group
                .Select(spawn => NormalizePosition(spawn.Position, group.Key, map))
                .Where(cluster => cluster != null)
                .Cast<DropClusterInfo>()
                .GroupBy(cluster => (X: MathF.Round(cluster.MapX, 1), Y: MathF.Round(cluster.MapY, 1)))
                .Select(cluster => new DropClusterInfo(
                    group.Key,
                    map?.RowId ?? 0,
                    cluster.Average(point => point.MapX),
                    cluster.Average(point => point.MapY),
                    cluster.Sum(point => point.SpawnPointCount)))
                .OrderByDescending(cluster => cluster.SpawnPointCount)
                .ThenBy(cluster => cluster.MapX)
                .ThenBy(cluster => cluster.MapY)
                .ToList();

            if (clusters.Count > 0)
                zones.Add(new DropZoneInfo(zoneName, group.Key, clusters));
        }

        return zones
            .OrderBy(zone => zone.ZoneName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static DropClusterInfo? NormalizePosition(Vector3 position, uint territoryTypeId, Map? map)
    {
        if (IsMapCoordinate(position.X) && IsMapCoordinate(position.Y))
            return new DropClusterInfo(territoryTypeId, map?.RowId ?? 0, position.X, position.Y, 1);

        if (!map.HasValue)
            return null;

        var mapX = ConvertWorldCoordToMapCoord(position.X, map.Value.SizeFactor, map.Value.OffsetX);
        var mapY = ConvertWorldCoordToMapCoord(position.Z, map.Value.SizeFactor, map.Value.OffsetY);
        return IsMapCoordinate(mapX) && IsMapCoordinate(mapY)
            ? new DropClusterInfo(territoryTypeId, map.Value.RowId, mapX, mapY, 1)
            : null;
    }

    private static bool IsMapCoordinate(float value) => value is > 0f and < 50f;

    private static float ConvertWorldCoordToMapCoord(float worldCoord, uint sizeFactor, int offset)
    {
        const double factor = 0.019999999552965164d;
        return sizeFactor == 0
            ? 0f
            : (float)((factor * offset) + (2048.0d / sizeFactor) + (factor * worldCoord) + 1.0d);
    }
}
