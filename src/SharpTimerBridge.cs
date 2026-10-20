using System.Collections;
using System.Reflection;

namespace CourseAchievements;

public record struct TimerState(bool Running, bool BonusRunning, int Ticks, int BonusTicks, int BonusStage, int Style, string Mode, double Sync, bool Noclip);

/// <summary>
/// Lee el estado del cronómetro de SharpTimer (SharpTimer.Instance.playerTimers) por reflexión, como RandomPicksHud, para no
/// depender de su DLL. Su API de eventos no sirve aquí: registra la capability con "() => new SharpTimerAPI_EventSender()",
/// y como CounterStrikeSharp llama a esa función en cada Get(), el evento se lanza sobre una instancia sin suscriptores.
/// </summary>
public class SharpTimerBridge
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    object? _instance;
    IDictionary? _timers;
    DateTime _nextLookup;
    readonly Dictionary<string, PropertyInfo?> _props = new();

    public bool Found => _timers is not null;

    public void Reset() { _instance = null; _timers = null; _nextLookup = default; }

    bool Lookup()
    {
        if (_timers is not null) return true;
        if (DateTime.UtcNow < _nextLookup) return false;
        _nextLookup = DateTime.UtcNow.AddSeconds(5); // por si SharpTimer carga después que este plugin
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "SharpTimer");
            var type = asm?.GetTypes().FirstOrDefault(t => t.Name == "SharpTimer" && t.GetMember("playerTimers", Any).Length > 0);
            var instM = type?.GetProperty("Instance", Any) as MemberInfo ?? type?.GetField("Instance", Any);
            _instance = instM is PropertyInfo pi ? pi.GetValue(null) : (instM as FieldInfo)?.GetValue(null);
            var ptM = _instance?.GetType().GetField("playerTimers", Any) as MemberInfo ?? _instance?.GetType().GetProperty("playerTimers", Any);
            _timers = (ptM is FieldInfo fi ? fi.GetValue(_instance) : (ptM as PropertyInfo)?.GetValue(_instance)) as IDictionary;
            CourseAchievements.Info($"SharpTimer timers {(_timers is null ? "NOT found" : "found")}");
        }
        catch (Exception ex) { CourseAchievements.Info($"SharpTimer lookup: {ex.Message}"); }
        return _timers is not null;
    }

    object? Get(object o, string name)
    {
        var key = name;
        if (!_props.TryGetValue(key, out var p)) _props[key] = p = o.GetType().GetProperty(name, Any);
        return p?.GetValue(o);
    }

    public TimerState? Read(int slot)
    {
        try
        {
            if (!Lookup() || !_timers!.Contains(slot) || _timers[slot] is not { } t) return null;
            return new TimerState(
                (bool)(Get(t, "IsTimerRunning") ?? false),
                (bool)(Get(t, "IsBonusTimerRunning") ?? false),
                (int)(Get(t, "TimerTicks") ?? 0),
                (int)(Get(t, "BonusTimerTicks") ?? 0),
                (int)(Get(t, "BonusStage") ?? 0),
                (int)(Get(t, "currentStyle") ?? 0),
                Get(t, "Mode") as string ?? "",
                Convert.ToDouble(Get(t, "Sync") ?? 0.0),
                (bool)(Get(t, "IsNoclip") ?? false));
        }
        catch { return null; }
    }

    /// <summary>Tier del mapa actual según SharpTimer (currentMapTier); 1 si no lo tiene.</summary>
    public int MapTier()
    {
        try
        {
            if (!Lookup()) return 1;
            var f = _instance!.GetType().GetField("currentMapTier", Any);
            return f?.GetValue(_instance) is int tier ? tier : 1;
        }
        catch { return 1; }
    }
}
