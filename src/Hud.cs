using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace CourseAchievements;

/// <summary>
/// HUD Panorama (custom_hud_layout) con la técnica de RandomPicksHud: el layout viene en el addon de Workshop del servidor y
/// el plugin lo rellena por jugador con variables de diálogo (una por Label, mismo nombre que su id) y clases.
/// Tres partes: aviso al desbloquear (arriba), seguimiento del siguiente logro (lateral) y lista por páginas (centro).
/// </summary>
public partial class CourseAchievements
{
    public const int PageSize = 8;

    CCSCustomHudLayout? _hud;
    bool _recreatePending;
    readonly HashSet<int> _spawned = new();
    // Lo último enviado a cada jugador, para mandar solo lo que cambia. Se vacía en cada refresco porque CS2 borra los textos
    // de la HUD a quien ya estaba dentro cuando entra otro jugador o vuelve de espectador.
    readonly Dictionary<(int Slot, string Key), string> _sent = new();
    readonly Dictionary<int, Queue<AchievementDef>> _toastQueue = new();
    readonly HashSet<int> _toastBusy = new();
    readonly Dictionary<int, (int Page, Timer Timer)> _lists = new();

    void LoadHud()
    {
        if (!Config.UseHud) return;
        // El manifest quiere el recurso compilado (.vxml); la entidad, el nombre fuente (.xml).
        RegisterListener<Listeners.OnServerPrecacheResources>(m => m.AddResource(Path.ChangeExtension(Config.Layout, ".vxml")));
        AddTimer(Config.RefreshSeconds, () => { _sent.Clear(); PushAll(); }, TimerFlags.REPEAT);
        AddTimer(0.5f, WatchHud, TimerFlags.REPEAT);
    }

    void UnloadHud()
    {
        if (_ownsHud && _hud is { IsValid: true }) _hud.Remove();
        _hud = null;
    }

    // ---- Entidad compartida ----
    // El cliente solo enseña una custom_hud_layout: con RandomPicksHud en el servidor, su HUD se veía y la de logros no.
    // En HudMode "auto" se escribe en la entidad de RandomPicksHud (cuyo layout combinado lleva también los paneles de
    // logros) y solo se crea una propia si no hay ninguna. RandomPicksHud recrea la suya cada vez que entra alguien, así que
    // se vigila y, al cambiar, se pasa a la nueva y se reenvía todo.

    bool _ownsHud;  // la entidad la creó este plugin (y la borra al descargarse)
    nint _hudPtr;   // para saber si RandomPicksHud la ha cambiado por otra
    bool Shared => Config.HudMode.Equals("shared", StringComparison.OrdinalIgnoreCase);
    // HUD propia (sus paneles: aviso, seguimiento y lista). En "auto" con RandomPicksHud se usan sus menús en su lugar.
    // Paneles propios de seguimiento y lista: no con RandomPicksHud en "auto" (la lista va en sus menús).
    bool OwnPanels => Config.UseHud && !(_menu is not null && Config.HudMode.Equals("auto", StringComparison.OrdinalIgnoreCase));
    // Aviso con copa en el layout propio (Toast = "hud"), también con RandomPicksHud.
    bool HudToast => Config.UseHud && Config.Toast.Equals("hud", StringComparison.OrdinalIgnoreCase);
    bool MenuToast(CCSPlayerController p) => Config.Toast.Equals("menu", StringComparison.OrdinalIgnoreCase) && MenuFor(p);
    // Hace falta la entidad propia si se usa cualquiera de las dos cosas.
    bool OwnHud => OwnPanels || HudToast;

    CCSCustomHudLayout? ForeignHud() => Utilities.FindAllEntitiesByDesignerName<CCSCustomHudLayout>("custom_hud_layout")
        .FirstOrDefault(e => e.IsValid && e.StrLayout != Config.Layout);

    void Adopt(CCSCustomHudLayout hud)
    {
        _hud = hud; _hudPtr = hud.Handle; _ownsHud = false;
        _sent.Clear();
        CourseAchievements.Info($"using custom_hud_layout #{hud.Index} layout={hud.StrLayout} from another plugin (its layout must include the achievement panels)");
    }

    void WatchHud()
    {
        if (!OwnHud || !_hudReady || _hud is null) return;
        if (Shared && (_ownsHud || !_hud.IsValid) && ForeignHud() is { } foreign)
        {
            if (_ownsHud && _hud.IsValid) _hud.Remove();
            Adopt(foreign);
            PushAll();
            return;
        }
        if (_hud.IsValid && _hud.Handle == _hudPtr) return;
        _hud = null; // la que usábamos ya no existe: EnsureHud busca la nueva (o crea una)
        PushAll();
    }

    // Nunca desde OnMapStart: con el mapa a medio cargar crear la entidad tumba el servidor. La crean la entrada y la aparición.
    void ResetHudForMap()
    {
        _hud = null; _ownsHud = false; _hudPtr = 0; _sent.Clear(); _toastQueue.Clear(); _toastBusy.Clear(); _spawned.Clear();
        foreach (var (_, l) in _lists) l.Timer.Kill();
        _lists.Clear();
    }

    // El cliente solo construye la HUD de una entidad que aparece estando él ya en el juego: al entrar alguien
    // se recrea (con retardo, agrupando entradas simultáneas) y se reenvía todo a todos.
    void HudOnConnectFull(CCSPlayerController p)
    {
        if (!OwnHud || _recreatePending) return;
        _recreatePending = true;
        AddTimer(1.5f, () => { _recreatePending = false; Recreate(); });
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnSpawn(EventPlayerSpawn e, GameEventInfo i)
    {
        if (!OwnHud || e.Userid is not { IsBot: false } p) return HookResult.Continue;
        _hudReady = true;
        // Por si connect_full llega antes de que el cliente esté del todo dentro: también tras su primera aparición.
        if (_spawned.Add(p.Slot) && !_recreatePending)
        {
            _recreatePending = true;
            AddTimer(2f, () => { _recreatePending = false; Recreate(); });
            return HookResult.Continue;
        }
        // En cada aparición el cliente puede haber perdido la HUD: se le reenvía todo.
        var slot = p.Slot;
        AddTimer(0.5f, () =>
        {
            ForgetSent(slot);
            if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } again) Push(again);
        });
        return HookResult.Continue;
    }

    void HudOnDisconnect(int slot)
    {
        _spawned.Remove(slot); _toastQueue.Remove(slot); _toastBusy.Remove(slot);
        if (_lists.Remove(slot, out var l)) l.Timer.Kill();
        ForgetSent(slot);
    }

    void ForgetSent(int slot)
    {
        foreach (var k in _sent.Keys.Where(k => k.Slot == slot).ToList()) _sent.Remove(k);
    }

    bool EnsureHud()
    {
        if (_hud is { IsValid: true }) return true;
        if (!_hudReady) return false;
        if (Shared && ForeignHud() is { } foreign)
        {
            Adopt(foreign);
            return true;
        }
        foreach (var stale in Utilities.FindAllEntitiesByDesignerName<CCSCustomHudLayout>("custom_hud_layout"))
            if (stale.IsValid && stale.StrLayout == Config.Layout) stale.Remove();
        var hud = Utilities.CreateEntityByName<CCSCustomHudLayout>("custom_hud_layout");
        if (hud is null || !hud.IsValid) { Info("could not create custom_hud_layout"); return false; }
        var kv = new CEntityKeyValues();
        kv.SetString("layout", Config.Layout);
        hud.StrLayout = Config.Layout;
        hud.DispatchSpawn(kv);
        _hud = hud; _hudPtr = hud.Handle; _ownsHud = true;
        CourseAchievements.Info($"created own custom_hud_layout #{hud.Index} layout={Config.Layout}");
        return true;
    }

    void Recreate()
    {
        // La entidad de otro plugin la recrea él; aquí basta con reenviar.
        if (!_ownsHud && _hud is { IsValid: true })
        {
            _sent.Clear();
            PushAll();
            return;
        }
        var old = _hud;
        _hud = null;
        _sent.Clear(); // entidad nueva: el cliente parte de cero, hay que reenviarlo todo
        if (!EnsureHud()) return;
        if (old is { IsValid: true }) old.Remove();
        PushAll();
    }

    void PushAll()
    {
        if (!OwnHud) return;
        var players = Utilities.GetPlayers().Where(p => p is { IsValid: true, IsBot: false }).ToList();
        if (players.Count == 0 || !EnsureHud()) return;
        foreach (var p in players) Push(p);
    }

    void Text(CCSPlayerController p, string id, string value)
    {
        if (_hud is not { IsValid: true } hud) return;
        var k = (p.Slot, id);
        if (_sent.TryGetValue(k, out var old) && old == value) return;
        _sent[k] = value;
        hud.SetDialogVariableStringForPlayer(p, id, id, value);
    }

    void Class(CCSPlayerController p, string id, string cls, bool on)
    {
        if (_hud is not { IsValid: true } hud) return;
        var k = (p.Slot, $"{id}.{cls}");
        var v = on ? "1" : "0";
        if (_sent.TryGetValue(k, out var old) && old == v) return;
        _sent[k] = v;
        hud.SetHasClassForPlayer(p, id, cls, on);
    }

    // Barra de progreso: el relleno lleva una de las clases w0..w10 (0 %, 10 %... 100 % de ancho).
    void Bar(CCSPlayerController p, string id, double fraction)
    {
        var step = (int)Math.Clamp(Math.Floor(fraction * 10), 0, 10);
        for (var i = 0; i <= 10; i++) Class(p, id, $"w{i}", i == step);
    }

    void Push(CCSPlayerController p)
    {
        if (!OwnHud || p is not { IsValid: true, IsBot: false } || _hud is not { IsValid: true }) return;
        _players.TryGetValue(p.Slot, out var d);
        if (OwnPanels) { PushTracker(p, d); PushList(p, d); }
        if (!_toastBusy.Contains(p.Slot)) Class(p, "ca_toast", "show", false);
    }

    void PushTracker(CCSPlayerController p, PlayerData? d)
    {
        var pos = Config.TrackerPosition.ToLowerInvariant();
        var hidden = d is not { Loaded: true } || d.HudOff || pos is not ("left" or "right");
        Class(p, "ca_tracker", "hidden", hidden);
        if (hidden) return;
        Class(p, "ca_tracker", "left", pos == "left");
        var (count, total, score) = Summary(d!);
        Text(p, "ca_trk_title", T("hud.title"));
        Text(p, "ca_trk_count", $"{count}/{total}");
        Text(p, "ca_trk_score", $"{score} {T("hud.pts")}");
        Text(p, "ca_trk_next_label", T("hud.next"));
        if (NextFor(d!) is { } next)
        {
            var (v, t) = Progress.Of(next, d!, Defs);
            Text(p, "ca_trk_next", next.Name);
            Text(p, "ca_trk_prog", ProgressText(next, v, t));
            Bar(p, "ca_trk_bar", v / t);
        }
        else
        {
            Text(p, "ca_trk_next", T("hud.all_done"));
            Text(p, "ca_trk_prog", "");
            Bar(p, "ca_trk_bar", 1);
        }
    }

    // ---- Aviso al desbloquear: uno detrás de otro, ToastSeconds cada uno ----

    // Con HUD propia, en su panel; si no, en el panel de menús de RandomPicksHud (cuando el jugador no tiene un menú abierto).
    void ShowToast(CCSPlayerController p, AchievementDef a)
    {
        if (!HudToast && !MenuToast(p)) return;
        if (!_toastQueue.TryGetValue(p.Slot, out var q)) _toastQueue[p.Slot] = q = new();
        q.Enqueue(a);
        if (!_toastBusy.Contains(p.Slot)) NextToast(p.Slot);
    }

    void NextToast(int slot)
    {
        if (Utilities.GetPlayerFromSlot(slot) is not { IsValid: true } p || !_toastQueue.TryGetValue(slot, out var q) || q.Count == 0)
        {
            _toastBusy.Remove(slot);
            return;
        }
        if (!HudToast)
        {
            var next = q.Dequeue();
            // Con un menú abierto no se le tapa: ese aviso se pierde (el chat ya lo ha anunciado) y se pasa al siguiente.
            if (!RpToast(p, next)) { _toastBusy.Remove(slot); NextToast(slot); return; }
            _toastBusy.Add(slot);
            AddTimer(Config.ToastSeconds, () =>
            {
                RpToastEnd(slot);
                AddTimer(0.3f, () => { _toastBusy.Remove(slot); NextToast(slot); });
            });
            return;
        }
        if (!EnsureHud())
        {
            _toastBusy.Remove(slot);
            return;
        }
        var a = q.Dequeue();
        _toastBusy.Add(slot);
        Text(p, "ca_toast_name", a.Name);
        Class(p, "ca_toast", "show", true);
        AddTimer(Config.ToastSeconds, () =>
        {
            if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } again) Class(again, "ca_toast", "show", false);
            // Medio segundo para que termine la transición de salida antes del siguiente.
            AddTimer(0.6f, () => { _toastBusy.Remove(slot); NextToast(slot); });
        });
    }

    // ---- Lista de logros por páginas, ListSeconds en pantalla ----

    void OpenList(CCSPlayerController p, int page)
    {
        if (_lists.Remove(p.Slot, out var old)) old.Timer.Kill();
        var slot = p.Slot;
        var timer = AddTimer(Config.ListSeconds, () =>
        {
            _lists.Remove(slot);
            if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } again) Class(again, "ca_list", "hidden", true);
        });
        _lists[slot] = (page, timer);
        if (EnsureHud()) PushList(p, _players.GetValueOrDefault(slot));
    }

    void CloseList(CCSPlayerController p)
    {
        if (_lists.Remove(p.Slot, out var l)) l.Timer.Kill();
        Class(p, "ca_list", "hidden", true);
    }

    int Pages => Math.Max(1, (Defs.Count + PageSize - 1) / PageSize);

    void PushList(CCSPlayerController p, PlayerData? d)
    {
        if (d is not { Loaded: true } || !_lists.TryGetValue(p.Slot, out var l))
        {
            Class(p, "ca_list", "hidden", true);
            return;
        }
        var page = Math.Clamp(l.Page, 1, Pages);
        var (count, total, score) = Summary(d);
        Text(p, "ca_list_title", T("hud.title"));
        Text(p, "ca_list_sub", $"{count}/{total}  ·  {score} {T("hud.pts")}");
        Text(p, "ca_list_page", T("hud.page", page, Pages));
        Text(p, "ca_list_hint", page < Pages ? T("hud.hint_next", page + 1) : T("hud.hint_close"));
        var items = Defs.Skip((page - 1) * PageSize).Take(PageSize).ToList();
        for (var i = 0; i < PageSize; i++)
        {
            var row = $"ca_row_{i}";
            Class(p, row, "hidden", i >= items.Count);
            if (i >= items.Count) continue;
            var a = items[i];
            var done = d.Unlocked.ContainsKey(a.Id);
            var secret = a.Hidden && !done;
            var (v, t) = Progress.Of(a, d, Defs);
            Class(p, row, "done", done);
            Text(p, $"{row}_name", secret ? T("hud.secret_name") : a.Name);
            Text(p, $"{row}_desc", secret ? T("hud.secret_desc") : a.Description);
            Text(p, $"{row}_pts", $"{a.Points}");
            Text(p, $"{row}_prog", done ? T("hud.done") : secret ? "" : ProgressText(a, v, t));
            Bar(p, $"{row}_bar", done ? 1 : secret ? 0 : v / t);
        }
        Class(p, "ca_list", "hidden", false);
    }
}
