using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;

namespace CourseAchievements;

public partial class CourseAchievements
{
    // !logros            abre la lista (o la cierra si ya está abierta)
    // !logros <página>   abre esa página
    // !logros cerrar     la cierra
    [ConsoleCommand("css_logros", "Achievements list: !logros [page|close]")]
    [ConsoleCommand("css_ach", "Achievements list: !ach [page|close]")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnList(CCSPlayerController? p, CommandInfo c)
    {
        if (p is null || !_players.TryGetValue(p.Slot, out var d) || !d.Loaded)
        {
            if (p is not null) Chat(p, "chat.loading");
            return;
        }
        var arg = c.ArgCount > 1 ? c.GetArg(1).ToLowerInvariant() : "";
        var (count, total, score) = Summary(d);

        if (arg is "cerrar" or "close" or "0" || (arg == "" && _lists.ContainsKey(p.Slot)))
        {
            CloseList(p);
            return;
        }

        // !logros todos: la lista entera en la consola del jugador.
        if (arg is "todos" or "all")
        {
            p.PrintToConsole($"---- {T("hud.title")}  {count}/{total}  ·  {score} {T("hud.pts")} ----");
            foreach (var a in ListFor(d)) p.PrintToConsole(ConsoleRow(a, d));
            Chat(p, "chat.console");
            return;
        }

        // Con RandomPicksHud: su menú (se mueve con sus teclas y él pagina) y al elegir un logro, su detalle.
        if (MenuFor(p))
        {
            try
            {
                OpenRpList(p, d);
                Chat(p, "chat.summary", count, total, score);
                return;
            }
            catch (Exception ex) { Info($"menu: {ex.Message}"); } // si falla, sigue por el chat
        }

        var page = int.TryParse(arg, out var n) ? Math.Clamp(n, 1, PagesFor(d)) : 1;
        Chat(p, "chat.summary", count, total, score);
        if (NextFor(d) is { } next)
        {
            var (v, t) = Progress.Of(next, d, Defs);
            Chat(p, "chat.next", next.Name, ProgressText(next, v, t));
        }

        // La página va siempre al chat (no depende de que el cliente tenga la HUD) y, si hay HUD, también a la HUD.
        foreach (var a in ListFor(d).Skip((page - 1) * PageSize).Take(PageSize))
        {
            var done = d.Unlocked.ContainsKey(a.Id);
            var (v, t) = Progress.Of(a, d, Defs);
            var secret = a.Hidden && !done;
            p.PrintToChat($" {T(done ? "chat.row_done" : "chat.row", secret ? T("hud.secret_name") : a.Name,
                done ? T("hud.done") : secret ? "" : ProgressText(a, v, t), a.Points, secret ? T("hud.secret_desc") : a.Description)}");
        }
        Chat(p, page < PagesFor(d) ? "chat.more" : "chat.last", page + 1, page, PagesFor(d));
        if (OwnPanels) OpenList(p, page);
    }

    string ConsoleRow(AchievementDef a, PlayerData d)
    {
        var done = d.Unlocked.ContainsKey(a.Id);
        var secret = a.Hidden && !done;
        var (v, t) = Progress.Of(a, d, Defs);
        var state = done ? T("hud.done") : secret ? "" : ProgressText(a, v, t);
        var reward = Config.StoreRewards && a.HasReward && !secret ? $"  [{T("menu.reward")}: {RewardText(a)}]" : "";
        return $"{(done ? "[x]" : "[ ]")} {(secret ? T("hud.secret_name") : a.Name),-28} {state,-10} {a.Points,4} {T("hud.pts")}  {(secret ? T("hud.secret_desc") : a.Description)}{reward}";
    }

    [ConsoleCommand("css_logros_reload", "Reload the achievements from the database table")]
    [RequiresPermissions("@css/root")]
    public void OnReloadDefs(CCSPlayerController? p, CommandInfo c)
    {
        if (_db is not { } db) { c.ReplyToCommand("No database"); return; }
        if (!Config.DefinitionsSource.Equals("database", StringComparison.OrdinalIgnoreCase))
        {
            c.ReplyToCommand("DefinitionsSource is \"config\": edit the JSON and reload the plugin instead");
            return;
        }
        var slot = p?.Slot;
        Task.Run(() => ReloadDefinitions(db, msg =>
        {
            if (slot is { } s && Utilities.GetPlayerFromSlot(s) is { IsValid: true } pl) pl.PrintToChat($" {T("prefix")} {msg}");
            else Server.PrintToConsole($"[CourseAchievements] {msg}");
        }));
    }

    // Estado de la HUD y de SharpTimer, para saber por qué no se ve algo.
    [ConsoleCommand("css_logros_debug", "Show HUD and SharpTimer status")]
    [RequiresPermissions("@css/root")]
    public void OnDebug(CCSPlayerController? p, CommandInfo c)
    {
        c.ReplyToCommand($"[CourseAchievements] UseHud={Config.UseHud} HudMode={Config.HudMode} RandomPicksHud menus={(_menu is null ? "no" : "yes")} ownHud={OwnHud} ownPanels={OwnPanels} Toast={Config.Toast} Layout={Config.Layout} ready={_hudReady}");
        c.ReplyToCommand($"[CourseAchievements] using: {(_hud is { IsValid: true } h ? $"#{h.Index} layout={h.StrLayout} own={_ownsHud} panels={h.PanelIds.Count} vars={h.DialogVariableNames.Count}" : "none")}");
        foreach (var e in Utilities.FindAllEntitiesByDesignerName<CCSCustomHudLayout>("custom_hud_layout").Where(e => e.IsValid))
            c.ReplyToCommand($"[CourseAchievements]   custom_hud_layout #{e.Index} layout={e.StrLayout}");
        _st.MapTier(); // fuerza la búsqueda de SharpTimer aunque no haya nadie jugando
        c.ReplyToCommand($"[CourseAchievements] cs2-store: {(_store is null ? "NOT found" : "found")}  StoreRewards={Config.StoreRewards}  pending rewards: {_players.Values.Sum(x => x.Rewards.Count)}");
        c.ReplyToCommand($"[CourseAchievements] SharpTimer timers: {(_st.Found ? "found" : "NOT found")}  achievements: {Defs.Count} ({Config.DefinitionsSource})  players loaded: {_players.Values.Count(x => x.Loaded)}");
    }

    [ConsoleCommand("css_toplogros", "Top players by achievement points")]
    [ConsoleCommand("css_topach", "Top players by achievement points")]
    public void OnTop(CCSPlayerController? p, CommandInfo c)
    {
        if (_db is not { } db) return;
        var slot = p?.Slot;
        Task.Run(async () =>
        {
            try
            {
                var top = await db.Top(10);
                Server.NextFrame(() =>
                {
                    var target = slot is { } s ? Utilities.GetPlayerFromSlot(s) : null;
                    void Reply(string msg) { if (target is { IsValid: true }) target.PrintToChat(msg); else if (slot is null) Server.PrintToConsole(msg); }
                    Reply($" {T("prefix")} {T("chat.top_title")}");
                    if (top.Count == 0) Reply($" {T("chat.top_empty")}");
                    for (var i = 0; i < top.Count; i++) Reply($" {T("chat.top_row", i + 1, top[i].Name, top[i].Score, top[i].Unlocked)}");
                });
            }
            catch (Exception ex) { CourseAchievements.Info($"top: {ex.Message}"); }
        });
    }

    // Muestra u oculta el panel de seguimiento (solo para quien lo usa y hasta que se vaya).
    [ConsoleCommand("css_logroshud", "Show or hide the achievement tracker")]
    [ConsoleCommand("css_achhud", "Show or hide the achievement tracker")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnToggleHud(CCSPlayerController? p, CommandInfo c)
    {
        if (p is null || !_players.TryGetValue(p.Slot, out var d)) return;
        d.HudOff = !d.HudOff;
        Chat(p, d.HudOff ? "chat.tracker_off" : "chat.tracker_on");
        Push(p);
    }

    [ConsoleCommand("css_logro_give", "Unlock an achievement: css_logro_give <target> <id>")]
    [CommandHelper(minArgs: 2, usage: "<target> <id>")]
    [RequiresPermissions("@css/root")]
    public void OnGive(CCSPlayerController? p, CommandInfo c)
    {
        var def = Defs.FirstOrDefault(a => a.Id.Equals(c.GetArg(2), StringComparison.OrdinalIgnoreCase));
        if (def is null) { c.ReplyToCommand($"Unknown achievement id: {c.GetArg(2)}"); return; }
        foreach (var target in c.GetArgTargetResult(1).Players.Where(t => !t.IsBot))
        {
            if (!_players.TryGetValue(target.Slot, out var d) || !d.Loaded || d.Unlocked.ContainsKey(def.Id)) continue;
            d.Unlocked[def.Id] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            d.Dirty = true;
            Unlocked(target, d, def, announce: true);
            QueueReward(target.Slot, d, def);
            Evaluate(target.Slot, null, only: AchievementType.Achievements);
            Save(d);
            Push(target);
            c.ReplyToCommand($"{def.Id} -> {target.PlayerName}");
        }
    }

    [ConsoleCommand("css_logro_revoke", "Remove an achievement (or all): css_logro_revoke <target> <id|all>")]
    [CommandHelper(minArgs: 2, usage: "<target> <id|all>")]
    [RequiresPermissions("@css/root")]
    public void OnRevoke(CCSPlayerController? p, CommandInfo c)
    {
        if (_db is not { } db) return;
        var arg = c.GetArg(2);
        string? id = arg.Equals("all", StringComparison.OrdinalIgnoreCase) ? null : arg;
        foreach (var target in c.GetArgTargetResult(1).Players.Where(t => !t.IsBot))
        {
            if (!_players.TryGetValue(target.Slot, out var d) || !d.Loaded) continue;
            if (id is null) d.Unlocked.Clear(); else d.Unlocked.Remove(id);
            d.Dirty = true;
            var sid = d.SteamId;
            Task.Run(async () =>
            {
                try { await db.Revoke(sid, id); }
                catch (Exception ex) { CourseAchievements.Info($"revoke {sid}: {ex.Message}"); }
            });
            Save(d);
            Push(target);
            c.ReplyToCommand($"{arg} revoked from {target.PlayerName}");
        }
    }
}
