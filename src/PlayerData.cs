using System.Text.RegularExpressions;

namespace CourseAchievements;

/// <summary>Una fila de PlayerRecords de SharpTimer (los bonus se guardan como "mapa_bonusN").</summary>
public record StRecord(string MapName, int Style, string Mode, int Ticks, int Finished)
{
    static readonly Regex BonusSuffix = new(@"_bonus(\d+)$", RegexOptions.Compiled);

    public int Bonus => BonusSuffix.Match(MapName) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;
    public string BaseMap => BonusSuffix.Replace(MapName, "");
}

/// <summary>Lo que se sabe de un recorrido completado: sale del cronómetro de SharpTimer y se confirma con su base de datos.</summary>
public record FinishInfo(string Map, int Bonus, int Ticks, double Sync, int Style, int Tier, int Jumps, int Rank, bool IsPb, bool IsSr);

public class PlayerData
{
    public ulong SteamId;
    public string Name = "";
    public bool Loaded;

    // Guardado en {prefijo}players
    public long Jumps, PlaytimeSec, Connections, TimerStarts, ServerRecords, PersonalBests, MaxSpeed;
    public bool Dirty;

    // Guardado en {prefijo}unlocks: id del logro -> fecha (unix)
    public Dictionary<string, long> Unlocked = new();

    // Leído de SharpTimer
    public List<StRecord> Records = new();
    public long StPoints;

    // Recompensas de cs2-store pendientes de entregar ({prefijo}rewards)
    public List<PendingReward> Rewards = new();

    // Solo mientras sigue conectado
    public int SessionFinishes;
    public int RunJumps;
    public bool WasRunning;
    public int LastTicks, RunBonus, RunStyle;
    public string RunMode = "";
    public double RunSync;
    public bool HudOff;
}

public static class Progress
{
    /// <summary>Valor actual y objetivo de un logro para un jugador. Los de "al completar" van de 0 a 1.</summary>
    public static (double Value, double Target) Of(AchievementDef a, PlayerData d, IReadOnlyList<AchievementDef> defs)
    {
        double target = a.Target;
        double value = a.Type switch
        {
            AchievementType.Finishes => Main(a, d).Sum(r => (double)r.Finished),
            AchievementType.UniqueMaps => Main(a, d).Select(r => r.BaseMap.ToLowerInvariant()).Distinct().Count(),
            AchievementType.Bonuses => d.Records.Where(r => r.Bonus > 0 && MapMatches(a.Map, r.BaseMap) && StyleMatches(a.Style, r.Style))
                                                .Select(r => r.MapName.ToLowerInvariant()).Distinct().Count(),
            AchievementType.SharpTimerPoints => d.StPoints,
            AchievementType.ServerRecords => d.ServerRecords,
            AchievementType.PersonalBests => d.PersonalBests,
            AchievementType.TimerStarts => d.TimerStarts,
            AchievementType.Jumps => d.Jumps,
            AchievementType.MaxSpeed => d.MaxSpeed,
            AchievementType.PlaytimeMinutes => d.PlaytimeSec / 60,
            AchievementType.Connections => d.Connections,
            AchievementType.SessionFinishes => d.SessionFinishes,
            AchievementType.Achievements => defs.Count(x => x.Id != a.Id && !x.IsSeasonal && d.Unlocked.ContainsKey(x.Id)),
            _ => d.Unlocked.ContainsKey(a.Id) ? 1 : 0,
        };
        // Los de temporada no cuentan: si no, quien llegue después nunca podría completar "todos".
        if (a.Type == AchievementType.Achievements && target <= 0) target = Math.Max(1, defs.Count(x => x.Id != a.Id && !x.IsSeasonal));
        if (IsFinishCondition(a.Type)) target = 1;
        return (value, target);
    }

    public static bool IsFinishCondition(AchievementType t) => t is AchievementType.FinishUnder or AchievementType.FinishTop
        or AchievementType.FinishTier or AchievementType.FinishSync or AchievementType.FinishStyle or AchievementType.FinishMaxJumps;

    /// <summary>¿Cumple este recorrido la condición? Solo cuenta el mapa principal.</summary>
    public static bool FinishMeets(AchievementDef a, FinishInfo f)
    {
        if (f.Bonus != 0 || !MapMatches(a.Map, f.Map)) return false;
        if (a.Type != AchievementType.FinishStyle && !StyleMatches(a.Style, f.Style)) return false;
        return a.Type switch
        {
            AchievementType.FinishUnder => f.Ticks / 64.0 < a.Target,
            AchievementType.FinishTop => f.Rank > 0 && f.Rank <= a.Target,
            AchievementType.FinishTier => f.Tier >= a.Target,
            AchievementType.FinishSync => f.Sync >= a.Target,
            AchievementType.FinishStyle => a.Style is { } s ? f.Style == s : f.Style != 0,
            AchievementType.FinishMaxJumps => f.Jumps <= a.Target,
            _ => false,
        };
    }

    static IEnumerable<StRecord> Main(AchievementDef a, PlayerData d) =>
        d.Records.Where(r => r.Bonus == 0 && MapMatches(a.Map, r.BaseMap) && StyleMatches(a.Style, r.Style));

    static bool StyleMatches(int? style, int actual) => style is null || style == actual;

    static readonly Dictionary<string, Regex> Patterns = new();

    public static bool MapMatches(string pattern, string map)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return true;
        if (!Patterns.TryGetValue(pattern, out var rx))
        {
            var parts = pattern.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                               .Select(p => "^" + Regex.Escape(p).Replace(@"\*", ".*") + "$");
            Patterns[pattern] = rx = new Regex(string.Join("|", parts), RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
        return rx.IsMatch(map);
    }
}

/// <summary>Recompensa de cs2-store apuntada en {prefijo}rewards y aún sin entregar.</summary>
public record PendingReward(int Id, string AchievementId, int Credits, string Items);
