using System.Runtime.CompilerServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using RandomPicksHud.Api;

namespace CourseAchievements;

/// <summary>
/// Menús de RandomPicksHud (IRpMenuApi, en shared/RandomPicksHudApi). Va en su propia clase para que, si RandomPicksHud
/// no está instalado, la falta de su DLL solo se note al intentar crearla (y se capture), no al cargar el plugin.
/// </summary>
public class RpMenuBridge
{
    readonly IRpMenuApi _api;
    RpMenuBridge(IRpMenuApi api) => _api = api;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static RpMenuBridge? TryCreate() => IRpMenuApi.Capability.Get() is { } api ? new RpMenuBridge(api) : null;

    public bool Enabled(CCSPlayerController p, string plugin) => _api.IsEnabled(p, plugin);
    public bool IsOpen(CCSPlayerController p) => _api.IsOpen(p);
    public void Close(CCSPlayerController p) => _api.Close(p);
    public void Hide(CCSPlayerController p) => _api.Hide(p);

    public record Item(string Text, string Value, Action<CCSPlayerController>? OnSelect, bool Disabled = false);

    public void Open(CCSPlayerController p, string title, string subtitle, IEnumerable<Item> items, Action<CCSPlayerController>? onBack = null)
    {
        var menu = new RpMenu(title) { Subtitle = subtitle, ExitButton = true, OnBack = onBack };
        foreach (var it in items) menu.Add(it.Text, (pl, _) => it.OnSelect?.Invoke(pl), it.Value, it.Disabled);
        _api.Open(p, menu);
    }

    // Vista suelta en el panel de menús, sin teclas: para el aviso de logro desbloqueado.
    public void Draw(CCSPlayerController p, string title, string subtitle, IEnumerable<(string Text, string Value)> rows, float progress)
    {
        var view = new RpMenuView { Title = title, Subtitle = subtitle, Selected = 0, Progress = progress };
        foreach (var (text, value) in rows) view.Rows.Add(new RpMenuRow(text, value));
        _api.Draw(p, view);
    }
}

public partial class CourseAchievements
{
    RpMenuBridge? _menu;

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        try { _menu = RpMenuBridge.TryCreate(); }
        catch (Exception ex) { _menu = null; CourseAchievements.Info($"RandomPicksHud menu API not available: {ex.GetType().Name}"); }
        Info(_menu is null ? "RandomPicksHud menus: not found, using chat/own HUD" : "RandomPicksHud menus: found");
        InitStore();
    }

    bool MenuFor(CCSPlayerController p)
    {
        if (_menu is null || Config.HudMode.Equals("own", StringComparison.OrdinalIgnoreCase)) return false;
        try { return _menu.Enabled(p, ModuleName); }
        catch { return false; }
    }

    // ---- Lista: un menú de RandomPicksHud con todos los logros (él pagina) y un submenú de detalle por logro ----

    void OpenRpList(CCSPlayerController p, PlayerData d)
    {
        var (count, total, score) = Summary(d);
        var items = ListFor(d).Select(a =>
        {
            var done = d.Unlocked.ContainsKey(a.Id);
            var secret = a.Hidden && !done;
            var (v, t) = Progress.Of(a, d, Defs);
            var value = done ? T("hud.done") : secret ? "" : ProgressText(a, v, t);
            return new RpMenuBridge.Item($"{(done ? "✔ " : "")}{(secret ? T("hud.secret_name") : a.Name)}", value, pl => OpenRpDetail(pl, a));
        });
        _menu!.Open(p, T("hud.title"), $"{count}/{total}  ·  {score} {T("hud.pts")}", items);
    }

    void OpenRpDetail(CCSPlayerController p, AchievementDef a)
    {
        if (!_players.TryGetValue(p.Slot, out var d) || !d.Loaded) return;
        var done = d.Unlocked.TryGetValue(a.Id, out var at);
        var secret = a.Hidden && !done;
        var (v, t) = Progress.Of(a, d, Defs);
        var rows = new List<RpMenuBridge.Item>
        {
            new(secret ? T("hud.secret_desc") : a.Description, "", null, true),
            new(T("menu.progress"), done ? T("hud.done") : secret ? "?" : ProgressText(a, v, t), null, true),
            new(T("menu.points"), $"{a.Points}", null, true),
        };
        if (Config.StoreRewards && a.HasReward) rows.Add(new(T("menu.reward"), RewardText(a), null, true));
        if (done) rows.Add(new(T("menu.unlocked_at"), DateTimeOffset.FromUnixTimeSeconds(at).ToLocalTime().ToString("dd/MM/yyyy"), null, true));
        _menu!.Open(p, secret ? T("hud.secret_name") : a.Name, done ? T("hud.done") : T("hud.pending"), rows,
            onBack: pl => { if (_players.TryGetValue(pl.Slot, out var dd) && dd.Loaded) OpenRpList(pl, dd); });
    }

    // ---- Aviso: se dibuja en el panel de menús si el jugador no tiene ninguno abierto ----

    /// <summary>true si el aviso se ha mostrado en el panel de RandomPicksHud.</summary>
    bool RpToast(CCSPlayerController p, AchievementDef a)
    {
        if (!MenuFor(p)) return false;
        try
        {
            if (_menu!.IsOpen(p)) return false;
            var d = _players.GetValueOrDefault(p.Slot);
            var (count, total, _) = d is null ? (0, ListFor(null).Count, 0) : Summary(d);
            _menu.Draw(p, T("hud.unlocked"), a.Name, new[] { (a.Description, ""), (T("menu.points"), $"+{a.Points}"), (T("hud.title"), $"{count}/{total}") },
                total > 0 ? (float)count / total : 0f);
            return true;
        }
        catch (Exception ex)
        {
            CourseAchievements.Info($"menu toast: {ex.Message}");
            return false;
        }
    }

    void RpToastEnd(int slot)
    {
        if (Utilities.GetPlayerFromSlot(slot) is not { IsValid: true } p || _menu is null) return;
        try { if (!_menu.IsOpen(p)) _menu.Hide(p); } // si entretanto ha abierto un menú, no se le cierra
        catch { }
    }
}
