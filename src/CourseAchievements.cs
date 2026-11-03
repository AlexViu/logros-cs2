using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace CourseAchievements;

/// <summary>
/// Logros para servidores de minigames course con SharpTimer. Los récords salen de la base de datos de SharpTimer (así los
/// logros de recorridos son retroactivos), el resto lo cuenta el plugin en sus propias tablas. HUD Panorama en Hud.cs.
/// </summary>
[MinimumApiVersion(300)]
public partial class CourseAchievements : BasePlugin, IPluginConfig<AchievementsConfig>
{
    public override string ModuleName => "CourseAchievements";
    public override string ModuleVersion => "1.2.0";
    public override string ModuleAuthor => "Lonza";
    public override string ModuleDescription => "Achievements for SharpTimer minigames course servers, with a Panorama HUD";
    public AchievementsConfig Config { get; set; } = new();

    Database? _db;
    Task _dbReady = Task.CompletedTask; // las tablas se crean al cargar; la primera carga de jugadores espera a que existan
    readonly SharpTimerBridge _st = new();
    readonly Dictionary<int, PlayerData> _players = new();
    string _map = "";
    bool _hudReady; // false mientras carga el mapa: hasta que entra o aparece alguien no se crea la entidad de la HUD

    public void OnConfigParsed(AchievementsConfig config)
    {
        var dupes = config.Achievements.GroupBy(a => a.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0) CourseAchievements.Info($"duplicated achievement ids: {string.Join(", ", dupes)}");
        Config = config;
        Defs = config.Achievements;
    }

    // Al log de CounterStrikeSharp (lo que sale en la consola del servidor y en los logs del pod); Console.WriteLine no llega ahí.
    static ILogger? _log;
    internal static void Info(string msg)
    {
        if (_log is not null) _log.LogInformation("{Message}", msg);
        else Console.WriteLine($"[CourseAchievements] {msg}");
    }

    // Los logros en uso: los del JSON hasta que se leen de la tabla {prefijo}achievements (DefinitionsSource = "database").
    List<AchievementDef> Defs = new();

    /// <summary>Lee los logros de la base de datos y vuelve a evaluar y pintar a todos. <paramref name="reply"/> recibe el resultado.</summary>
    async Task ReloadDefinitions(Database db, Action<string>? reply = null)
    {
        try
        {
            var defs = await db.LoadDefinitions(Config.Achievements);
            Server.NextFrame(() =>
            {
                Defs = defs;
                foreach (var slot in _players.Keys.ToList()) Evaluate(slot, null);
                _sent.Clear();
                PushAll();
                reply?.Invoke($"{defs.Count} achievements loaded from the database");
            });
        }
        catch (Exception ex)
        {
            CourseAchievements.Info($"achievements table: {ex.Message}");
            Server.NextFrame(() => reply?.Invoke($"Error: {ex.Message}"));
        }
    }

    public override void Load(bool hotReload)
    {
        _log = Logger;
        try
        {
            var db = new Database(Config.TablePrefix);
            _db = db;
            _dbReady = Task.Run(async () =>
            {
                try { await db.Init(); }
                catch (Exception ex) { CourseAchievements.Info($"database init: {ex.Message}"); return; }
                // Antes de cargar a nadie: los jugadores esperan a _dbReady y así se evalúan ya con la lista de la base de datos.
                if (Config.DefinitionsSource.Equals("database", StringComparison.OrdinalIgnoreCase)) await ReloadDefinitions(db);
            });
        }
        catch (Exception ex) { CourseAchievements.Info($"cannot read cfg/SharpTimer/mysqlConfig.json: {ex.Message}"); }

        LoadHud();
        RegisterListener<Listeners.OnMapStart>(map =>
        {
            _map = map; _hudReady = false; _st.Reset();
            ResetHudForMap();
        });
        RegisterListener<Listeners.OnTick>(OnTick);
        AddTimer(60f, OnMinute, TimerFlags.REPEAT);
        _map = Server.MapName;

        if (hotReload)
        {
            _hudReady = true;
            foreach (var p in Utilities.GetPlayers().Where(p => p is { IsValid: true, IsBot: false })) LoadPlayer(p, countConnection: false);
        }
    }

    public override void Unload(bool hotReload)
    {
        foreach (var d in _players.Values) Save(d);
        UnloadHud();
    }

    string T(string key, params object[] args) => Localizer[key, args].Value; // el localizador de CSS ya sustituye {green}, {gold}...

    void Chat(CCSPlayerController p, string key, params object[] args) => p.PrintToChat($" {T("prefix")} {T(key, args)}");

    // ---- Entrada y salida ----

    [GameEventHandler(HookMode.Post)]
    public HookResult OnConnectFull(EventPlayerConnectFull e, GameEventInfo i)
    {
        if (e.Userid is not { IsValid: true, IsBot: false } p) return HookResult.Continue;
        _hudReady = true;
        // connect_full se repite en cada cambio de mapa sin que el jugador haya salido: solo es una entrada nueva si no estaba.
        if (!_players.TryGetValue(p.Slot, out var d) || d.SteamId != p.SteamID) LoadPlayer(p, countConnection: true);
        HudOnConnectFull(p);
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnDisconnect(EventPlayerDisconnect e, GameEventInfo i)
    {
        if (e.Userid is not { } p) return HookResult.Continue;
        if (_players.Remove(p.Slot, out var d)) Save(d);
        HudOnDisconnect(p.Slot);
        return HookResult.Continue;
    }

    void LoadPlayer(CCSPlayerController p, bool countConnection)
    {
        if (_db is null || p.SteamID == 0) return;
        var slot = p.Slot;
        var sid = p.SteamID;
        var name = p.PlayerName;
        _players[slot] = new PlayerData { SteamId = sid, Name = name };
        var (db, ready) = (_db, _dbReady);
        Task.Run(async () =>
        {
            try
            {
                await ready;
                var d = await db.LoadPlayer(sid);
                d.Rewards = await db.PendingRewards(sid);
                Server.NextFrame(() =>
                {
                    // Puede haberse ido (o haber entrado otro en su hueco) mientras se cargaba.
                    if (!_players.TryGetValue(slot, out var cur) || cur.SteamId != sid) return;
                    d.Name = name;
                    d.Loaded = true;
                    // Lo contado mientras cargaba se suma a lo guardado.
                    d.Jumps += cur.Jumps; d.TimerStarts += cur.TimerStarts; d.MaxSpeed = Math.Max(d.MaxSpeed, cur.MaxSpeed);
                    (d.WasRunning, d.LastTicks, d.RunBonus, d.RunStyle, d.RunMode, d.RunSync, d.RunJumps) =
                        (cur.WasRunning, cur.LastTicks, cur.RunBonus, cur.RunStyle, cur.RunMode, cur.RunSync, cur.RunJumps);
                    if (countConnection) d.Connections++;
                    d.Dirty = true;
                    _players[slot] = d;
                    // Primera evaluación: aquí salen de golpe los logros que ya se tenían por los récords de SharpTimer.
                    Evaluate(slot, null, quiet: true);
                    if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } pl) Push(pl);
                });
            }
            catch (Exception ex) { CourseAchievements.Info($"load {sid}: {ex.Message}"); }
        });
    }

    void Save(PlayerData d)
    {
        if (_db is null || !d.Loaded || !d.Dirty) return;
        d.Dirty = false;
        var counters = new[] { d.Jumps, d.PlaytimeSec, d.Connections, d.TimerStarts, d.ServerRecords, d.PersonalBests, d.MaxSpeed };
        var (count, _, score) = Summary(d);
        var (sid, name, db) = (d.SteamId, d.Name, _db);
        Task.Run(async () =>
        {
            try { await db.SavePlayer(sid, name, counters, count, score); }
            catch (Exception ex) { CourseAchievements.Info($"save {sid}: {ex.Message}"); }
        });
    }

    void OnMinute()
    {
        foreach (var (slot, d) in _players)
        {
            if (!d.Loaded || Utilities.GetPlayerFromSlot(slot) is not { IsValid: true, IsBot: false }) continue;
            d.PlaytimeSec += 60;
            d.Dirty = true;
            Evaluate(slot, null, only: AchievementType.PlaytimeMinutes);
            Save(d);
        }
    }

    [GameEventHandler]
    public HookResult OnJump(EventPlayerJump e, GameEventInfo i)
    {
        if (e.Userid is { IsBot: false } p && _players.TryGetValue(p.Slot, out var d))
        {
            d.Jumps++;
            d.RunJumps++;
            d.Dirty = true;
            if (d.Jumps % 50 == 0) Evaluate(p.Slot, null, only: AchievementType.Jumps);
        }
        return HookResult.Continue;
    }

    // ---- Cronómetro de SharpTimer: inicio, final y velocidad ----

    void OnTick()
    {
        if (Server.TickCount % 2 != 0 || _players.Count == 0) return;
        var checkSpeed = Server.TickCount % 8 == 0;
        foreach (var (slot, d) in _players)
        {
            if (_st.Read(slot) is not { } t) continue;
            var running = t.Running || t.BonusRunning;
            var ticks = t.BonusRunning ? t.BonusTicks : t.Ticks;

            // Arranca (o vuelve a empezar sin haberse parado: SharpTimer pone los ticks a 0 al salir otra vez de la zona de inicio).
            if (running && (!d.WasRunning || ticks < d.LastTicks))
            {
                d.TimerStarts++;
                d.RunJumps = 0;
                d.Dirty = true;
                if (d.Loaded) Evaluate(slot, null, only: AchievementType.TimerStarts);
            }

            if (running)
            {
                d.RunBonus = t.BonusRunning ? t.BonusStage : 0;
                d.RunStyle = t.Style; d.RunMode = t.Mode; d.RunSync = t.Sync;
                if (checkSpeed && !t.Noclip && Utilities.GetPlayerFromSlot(slot)?.PlayerPawn.Value is { } pawn)
                {
                    var v = pawn.AbsVelocity;
                    var speed = (long)Math.Sqrt(v.X * v.X + v.Y * v.Y);
                    if (speed > d.MaxSpeed)
                    {
                        d.MaxSpeed = speed;
                        d.Dirty = true;
                        if (d.Loaded) Evaluate(slot, null, only: AchievementType.MaxSpeed);
                    }
                }
            }
            else if (d.WasRunning)
            {
                // Al parar, SharpTimer conserva los ticks finales. Que sea un final de verdad (y no un reinicio o una zona de
                // fin con stages pendientes) se sabe al ver en su base de datos que TimesFinished ha subido.
                var final = d.RunBonus > 0 ? t.BonusTicks : t.Ticks;
                if (final > 0 && d.Loaded) StartFinishCheck(slot, d, final);
            }

            d.WasRunning = running;
            d.LastTicks = ticks;
        }
    }

    void StartFinishCheck(int slot, PlayerData d, int ticks)
    {
        var recordName = d.RunBonus > 0 ? $"{_map}_bonus{d.RunBonus}" : _map;
        var before = d.Records.FirstOrDefault(r => r.MapName.Equals(recordName, StringComparison.OrdinalIgnoreCase) && r.Style == d.RunStyle && r.Mode == d.RunMode);
        var run = new FinishInfo(_map, d.RunBonus, ticks, d.RunSync, d.RunStyle, _st.MapTier(), d.RunJumps, 0, false, false);
        var mode = d.RunMode;
        AddTimer(Config.FinishCheckDelay, () => CheckFinish(slot, d.SteamId, recordName, mode, before, run, retry: true));
    }

    void CheckFinish(int slot, ulong sid, string recordName, string mode, StRecord? before, FinishInfo run, bool retry)
    {
        if (_db is not { } db) return;
        Task.Run(async () =>
        {
            try
            {
                var (records, points) = await db.LoadSharpTimer(sid);
                var after = records.FirstOrDefault(r => r.MapName.Equals(recordName, StringComparison.OrdinalIgnoreCase) && r.Style == run.Style && r.Mode == mode);
                var finished = after is not null && after.Finished > (before?.Finished ?? 0);
                var rank = finished ? await db.Rank(after!) : 0;
                Server.NextFrame(() =>
                {
                    if (!_players.TryGetValue(slot, out var d) || d.SteamId != sid || !d.Loaded) return;
                    d.Records = records;
                    d.StPoints = points;
                    if (!finished)
                    {
                        // SharpTimer aún no lo había guardado: un segundo intento más tarde. Si tampoco, no era un final.
                        if (retry) AddTimer(4f, () => CheckFinish(slot, sid, recordName, mode, before, run, retry: false));
                        return;
                    }
                    // Igual que SharpTimer: PB si mejora la marca anterior; récord si además (o en su primera vez) queda primero.
                    var pb = before is not null && after!.Ticks < before.Ticks;
                    var sr = rank == 1 && (pb || before is null);
                    if (pb) d.PersonalBests++;
                    if (sr) d.ServerRecords++;
                    if (run.Bonus == 0) d.SessionFinishes++;
                    d.Dirty = true;
                    Evaluate(slot, run with { Rank = rank, IsPb = pb, IsSr = sr });
                });
            }
            catch (Exception ex) { CourseAchievements.Info($"finish check {sid}: {ex.Message}"); }
        });
    }

    // ---- Logros ----

    /// <summary>
    /// Desbloquea lo que se cumpla. <paramref name="finish"/> es el recorrido recién completado (para los de "al completar");
    /// <paramref name="only"/> limita la comprobación a un tipo (contadores que cambian a menudo); <paramref name="quiet"/>
    /// es la carga inicial: muchos logros a la vez, así que va un resumen por chat y como mucho tres avisos en la HUD.
    /// </summary>
    void Evaluate(int slot, FinishInfo? finish, AchievementType? only = null, bool quiet = false)
    {
        if (!_players.TryGetValue(slot, out var d) || !d.Loaded) return;
        var unlocked = new List<AchievementDef>();
        bool changed;
        do
        {
            changed = false;
            foreach (var a in Defs)
            {
                if (d.Unlocked.ContainsKey(a.Id)) continue;
                if (only is { } o && a.Type != o && a.Type != AchievementType.Achievements) continue;
                bool met;
                if (Progress.IsFinishCondition(a.Type)) met = finish is not null && Progress.FinishMeets(a, finish);
                else
                {
                    var (v, t) = Progress.Of(a, d, Defs);
                    met = v >= t;
                }
                if (!met) continue;
                d.Unlocked[a.Id] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                unlocked.Add(a);
                changed = true; // un logro desbloqueado puede completar uno de tipo Achievements
            }
        } while (changed);

        if (unlocked.Count == 0) return;
        d.Dirty = true;
        var p = Utilities.GetPlayerFromSlot(slot);
        foreach (var a in unlocked) { Unlocked(p, d, a, announce: !quiet); QueueReward(slot, d, a); }
        if (quiet && p is { IsValid: true })
        {
            Chat(p, "chat.unlocked_offline", unlocked.Count, unlocked.Sum(a => a.Points));
            foreach (var a in unlocked.Take(3)) ShowToast(p, a);
        }
        Save(d);
        if (p is { IsValid: true }) Push(p);
    }

    void Unlocked(CCSPlayerController? p, PlayerData d, AchievementDef a, bool announce)
    {
        var at = d.Unlocked[a.Id];
        if (_db is { } db)
        {
            var sid = d.SteamId;
            Task.Run(async () =>
            {
                try { await db.Unlock(sid, a.Id, at, a.Points); }
                catch (Exception ex) { CourseAchievements.Info($"unlock {sid} {a.Id}: {ex.Message}"); }
            });
        }
        if (p is not { IsValid: true }) return;

        if (announce)
        {
            if (Config.AnnounceToAll) Server.PrintToChatAll($" {T("prefix")} {T("chat.unlocked_all", p.PlayerName, a.Name, a.Points)}");
            else Chat(p, "chat.unlocked", a.Name, a.Points);
            if (!MenuToast(p)) Chat(p, "chat.unlocked_desc", a.Description); // el aviso con copa solo lleva el nombre
            ShowToast(p, a);
            if (Config.UnlockSound.Length > 0) p.ExecuteClientCommand($"play {Config.UnlockSound}");
        }

        if (a.Command.Length > 0)
        {
            // El nombre del jugador va dentro de un comando de servidor: fuera todo lo que podría cortarlo o encadenar otro.
            var safeName = new string(p.PlayerName.Where(c => c is not (';' or '"' or '\'' or '\n' or '\r')).ToArray());
            Server.ExecuteCommand(a.Command
                .Replace("{steamid}", p.SteamID.ToString())
                .Replace("{userid}", p.UserId?.ToString() ?? "")
                .Replace("{slot}", p.Slot.ToString())
                .Replace("{name}", safeName));
        }
    }

    (int Count, int Total, int Score) Summary(PlayerData d)
    {
        var defs = Defs;
        var done = defs.Where(a => d.Unlocked.ContainsKey(a.Id)).ToList();
        return (done.Count, defs.Count, done.Sum(a => a.Points));
    }

    /// <summary>El logro visible y pendiente al que más le falta poco (por porcentaje).</summary>
    AchievementDef? NextFor(PlayerData d) => Defs
        .Where(a => !a.Hidden && !d.Unlocked.ContainsKey(a.Id))
        .Select(a => (a, p: Progress.Of(a, d, Defs)))
        .OrderByDescending(x => x.p.Value / x.p.Target)
        .ThenBy(x => x.a.Target)
        .Select(x => x.a)
        .FirstOrDefault();

    string ProgressText(AchievementDef a, double v, double t)
    {
        if (Progress.IsFinishCondition(a.Type)) return T("hud.pending");
        static string N(double x) => x >= 10000 ? $"{x / 1000:0.#}k" : $"{x:0}";
        return $"{N(Math.Min(v, t))}/{N(t)}";
    }
}
