using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace CourseAchievements;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AchievementType
{
    // Acumulados, salen de los récords de SharpTimer (PlayerRecords), así que cuentan también lo hecho antes de instalar el plugin
    Finishes,         // veces que se ha completado un mapa (suma de TimesFinished)
    UniqueMaps,       // mapas distintos completados
    Bonuses,          // bonus distintos completados
    SharpTimerPoints, // puntos globales de SharpTimer (PlayerStats.GlobalPoints)

    // Acumulados que lleva este plugin
    ServerRecords,    // récords del servidor conseguidos
    PersonalBests,    // veces que se ha mejorado la marca personal
    TimerStarts,      // veces que se ha iniciado el cronómetro
    Jumps,            // saltos totales
    MaxSpeed,         // velocidad horizontal máxima con el cronómetro en marcha (u/s)
    PlaytimeMinutes,  // minutos jugados en el servidor
    Connections,      // veces que se ha entrado al servidor
    SessionFinishes,  // recorridos completados sin salir del servidor
    Achievements,     // logros desbloqueados (Target 0 = todos los demás)

    // Al completar un recorrido (solo el mapa principal, no los bonus)
    FinishUnder,      // en menos de Target segundos
    FinishTop,        // quedando entre los Target mejores tiempos del mapa
    FinishTier,       // en un mapa de tier Target o más
    FinishSync,       // con un sync de Target % o más
    FinishStyle,      // con un estilo distinto al normal (o con el de Style)
    FinishMaxJumps,   // saltando como mucho Target veces

    // Externos: solo se dan con css_logro_give (p. ej. desde el plugin AdventCalendar)
    Event,
}

public class AchievementDef
{
    [JsonPropertyName("Id")] public string Id { get; set; } = "";
    [JsonPropertyName("Name")] public string Name { get; set; } = "";
    [JsonPropertyName("Description")] public string Description { get; set; } = "";
    [JsonPropertyName("Type")] public AchievementType Type { get; set; }
    [JsonPropertyName("Target")] public double Target { get; set; } = 1;
    // Patrón de mapas, admite * y varios separados por comas ("mg_course*,mg_obstacle*"). Vacío = cualquiera.
    [JsonPropertyName("Map")] public string Map { get; set; } = "";
    // Estilo de SharpTimer (0 = normal). null = cualquiera; en FinishStyle, null = cualquiera menos el normal.
    [JsonPropertyName("Style")] public int? Style { get; set; }
    [JsonPropertyName("Points")] public int Points { get; set; } = 10;
    // Oculto: en la lista sale como "???" hasta desbloquearlo.
    [JsonPropertyName("Hidden")] public bool Hidden { get; set; }
    // Comando de servidor al desbloquearlo (recompensas). Admite {steamid}, {userid}, {slot} y {name}.
    [JsonPropertyName("Command")] public string Command { get; set; } = "";
    // Recompensas de cs2-store: créditos y productos (uniqueid de cs2-store.json, varios separados por comas).
    [JsonPropertyName("RewardCredits")] public int RewardCredits { get; set; }
    [JsonPropertyName("RewardItems")] public string RewardItems { get; set; } = "";

    // Temporada (hora local del servidor, ver UtcOffsetHours): fuera de [ActiveFrom, ActiveTo] no se puede conseguir y solo
    // sale en la lista a quien ya lo tenga. Los logros de temporada no cuentan para los de tipo Achievements.
    [JsonPropertyName("ActiveFrom")] public DateTime? ActiveFrom { get; set; }
    [JsonPropertyName("ActiveTo")] public DateTime? ActiveTo { get; set; }

    public bool HasReward => RewardCredits > 0 || !string.IsNullOrWhiteSpace(RewardItems);
    public bool IsSeasonal => ActiveFrom is not null || ActiveTo is not null;
    public bool IsActive(DateTime localNow) => (ActiveFrom is null || localNow >= ActiveFrom) && (ActiveTo is null || localNow <= ActiveTo);
}

public class AchievementsConfig : BasePluginConfig
{
    // Nombre FUENTE (.xml) del layout dentro del addon de Workshop; el cliente rechaza ".vxml" aquí.
    [JsonPropertyName("Layout")] public string Layout { get; set; } = "panorama/layout/custom_game/course_achievements.xml";
    // false = sin HUD Panorama: los avisos y la lista van solo al chat.
    [JsonPropertyName("UseHud")] public bool UseHud { get; set; } = true;
    // "auto": con RandomPicksHud se usan sus menús (lista y avisos) y no se crea HUD propia; sin él, HUD propia con Layout.
    // "shared": se escribe en la custom_hud_layout de RandomPicksHud en vez de crear otra, porque el
    // cliente solo enseña una; su layout tiene que llevar los paneles de logros (randompicks_hud.xml combinado, ver README).
    // Si no hay ninguna, se crea una propia. "own": siempre una propia y sin menús de RandomPicksHud.
    [JsonPropertyName("HudMode")] public string HudMode { get; set; } = "auto";
    // Aviso al desbloquear: "hud" = copa y nombre, centrado, en el layout propio (aunque esté RandomPicksHud);
    // "menu" = en el panel de menús de RandomPicksHud; "chat" = solo el anuncio del chat.
    [JsonPropertyName("Toast")] public string Toast { get; set; } = "hud";
    // "database": los logros se leen de la tabla {prefijo}achievements, que se rellena con Achievements la primera vez.
    // "config": se leen de Achievements, en este JSON.
    [JsonPropertyName("DefinitionsSource")] public string DefinitionsSource { get; set; } = "database";
    // Panel con el progreso del siguiente logro: "right", "left" u "off".
    [JsonPropertyName("TrackerPosition")] public string TrackerPosition { get; set; } = "right";
    [JsonPropertyName("RefreshSeconds")] public float RefreshSeconds { get; set; } = 5f;
    [JsonPropertyName("ToastSeconds")] public float ToastSeconds { get; set; } = 6f;
    [JsonPropertyName("ListSeconds")] public float ListSeconds { get; set; } = 20f;
    // Las tablas propias van en la misma base de datos que SharpTimer, con este prefijo.
    [JsonPropertyName("TablePrefix")] public string TablePrefix { get; set; } = "ca_";
    // true = se anuncia a todo el servidor quién desbloquea cada logro; false = solo a quien lo consigue.
    [JsonPropertyName("AnnounceToAll")] public bool AnnounceToAll { get; set; } = true;
    // Sonido al desbloquear (se reproduce con "play" en el cliente). Vacío = sin sonido.
    [JsonPropertyName("UnlockSound")] public string UnlockSound { get; set; } = "sounds/ui/panorama/round_report_round_won_01.vsnd";
    // Segundos que se espera, tras parar el cronómetro, a que SharpTimer guarde el tiempo antes de comprobarlo.
    [JsonPropertyName("FinishCheckDelay")] public float FinishCheckDelay { get; set; } = 2f;
    // Recompensas de cs2-store (RewardCredits / RewardItems de cada logro). false = no se da nada.
    [JsonPropertyName("StoreRewards")] public bool StoreRewards { get; set; } = true;
    // Si ya tiene el producto de la recompensa, se le da su precio en créditos. false = no se le da nada.
    [JsonPropertyName("DuplicateItemAsCredits")] public bool DuplicateItemAsCredits { get; set; } = true;
    // Desfase de la hora local respecto a UTC para las ventanas de temporada (el contenedor no trae zonas horarias). España en invierno = 1.
    [JsonPropertyName("UtcOffsetHours")] public double UtcOffsetHours { get; set; } = 1;

    [JsonPropertyName("Achievements")] public List<AchievementDef> Achievements { get; set; } = Defaults();

    static AchievementDef A(string id, string name, string desc, AchievementType type, double target, int points, bool hidden = false, string map = "",
        int credits = 0, string items = "", string? from = null, string? to = null) =>
        new() { Id = id, Name = name, Description = desc, Type = type, Target = target, Points = points, Hidden = hidden, Map = map,
                RewardCredits = credits, RewardItems = items,
                ActiveFrom = from is null ? null : DateTime.Parse(from), ActiveTo = to is null ? null : DateTime.Parse(to) };

    // Navidad 2026: del 1 de diciembre a Reyes. Los "Event" los da el plugin AdventCalendar al abrir casillas.
    const string XmasFrom = "2026-12-01 00:00", XmasTo = "2027-01-06 23:59:59";

    public static List<AchievementDef> Defaults() => new()
    {
        A("first_finish", "Primer recorrido", "Completa un mapa por primera vez", AchievementType.Finishes, 1, 5, credits: 100),
        A("finishes_10", "Corredor", "Completa 10 recorridos", AchievementType.Finishes, 10, 10, credits: 250),
        A("finishes_50", "Veterano del circuito", "Completa 50 recorridos", AchievementType.Finishes, 50, 25, credits: 750),
        A("finishes_250", "Máquina de recorridos", "Completa 250 recorridos", AchievementType.Finishes, 250, 50, credits: 2000, items: "trailgreencolor"),
        A("finishes_1000", "Leyenda del course", "Completa 1000 recorridos", AchievementType.Finishes, 1000, 100, credits: 5000, items: "vampire_wings"),
        A("course_master", "Maestro del course", "Completa 25 veces un mapa mg_course", AchievementType.Finishes, 25, 30, map: "mg_course*", items: "trailrandomcolor"),

        A("maps_3", "Explorador", "Completa 3 mapas distintos", AchievementType.UniqueMaps, 3, 10, credits: 200),
        A("maps_10", "Trotamundos", "Completa 10 mapas distintos", AchievementType.UniqueMaps, 10, 25, credits: 1000),
        A("maps_25", "Cartógrafo", "Completa 25 mapas distintos", AchievementType.UniqueMaps, 25, 50, items: "phanton_wings"),
        A("bonus_1", "Ruta alternativa", "Completa un bonus", AchievementType.Bonuses, 1, 10, credits: 200),
        A("bonus_10", "Cazador de bonus", "Completa 10 bonus distintos", AchievementType.Bonuses, 10, 40, credits: 1500),

        A("under_60", "Contrarreloj", "Completa un recorrido en menos de 1 minuto", AchievementType.FinishUnder, 60, 15, credits: 300),
        A("under_30", "Relámpago", "Completa un recorrido en menos de 30 segundos", AchievementType.FinishUnder, 30, 30, credits: 1000),
        A("speed_1000", "Supersónico", "Alcanza 1000 u/s con el cronómetro en marcha", AchievementType.MaxSpeed, 1000, 10, credits: 200),
        A("speed_2500", "Velocidad de escape", "Alcanza 2500 u/s con el cronómetro en marcha", AchievementType.MaxSpeed, 2500, 30, items: "energycircltrail"),

        A("pb_1", "Superación", "Mejora tu marca personal en un mapa", AchievementType.PersonalBests, 1, 5, credits: 100),
        A("pb_25", "Siempre a más", "Mejora tu marca personal 25 veces", AchievementType.PersonalBests, 25, 25, credits: 1000),
        A("top_10", "Top 10", "Termina un mapa entre los 10 mejores tiempos", AchievementType.FinishTop, 10, 15, credits: 300),
        A("top_3", "Podio", "Termina un mapa entre los 3 mejores tiempos", AchievementType.FinishTop, 3, 30, credits: 1000),
        A("sr_1", "Récord del servidor", "Consigue un récord del servidor", AchievementType.ServerRecords, 1, 50, credits: 2000, items: "stking"),
        A("sr_10", "Rey del circuito", "Consigue 10 récords del servidor", AchievementType.ServerRecords, 10, 150, credits: 5000, items: "homer_simpson"),

        A("tier_3", "Cuesta arriba", "Completa un mapa de tier 3 o más", AchievementType.FinishTier, 3, 20, credits: 500),
        A("tier_5", "Sin miedo", "Completa un mapa de tier 5 o más", AchievementType.FinishTier, 5, 50, credits: 1500),
        A("sync_90", "Sincronizado", "Completa un recorrido con un sync del 90% o más", AchievementType.FinishSync, 90, 25, credits: 500),
        A("style", "Con estilo", "Completa un recorrido con un estilo distinto al normal", AchievementType.FinishStyle, 1, 15, credits: 300),
        A("no_jump", "Pies de plomo", "Completa un recorrido sin saltar ni una vez", AchievementType.FinishMaxJumps, 0, 40, hidden: true, credits: 1500, items: "ctqueen"),

        A("starts_100", "Otra vez", "Inicia el cronómetro 100 veces", AchievementType.TimerStarts, 100, 5, credits: 100),
        A("starts_1000", "Persistencia", "Inicia el cronómetro 1000 veces", AchievementType.TimerStarts, 1000, 25, credits: 1000),
        A("session_10", "Maratón", "Completa 10 recorridos sin salir del servidor", AchievementType.SessionFinishes, 10, 20, credits: 500),
        A("jumps_1000", "Saltarín", "Salta 1.000 veces", AchievementType.Jumps, 1000, 5, credits: 100),
        A("jumps_25000", "Canguro", "Salta 25.000 veces", AchievementType.Jumps, 25000, 25, credits: 1000),

        A("play_60", "Recién llegado", "Juega 1 hora en el servidor", AchievementType.PlaytimeMinutes, 60, 5, credits: 300),
        A("play_600", "Habitual", "Juega 10 horas en el servidor", AchievementType.PlaytimeMinutes, 600, 20, credits: 2000),
        A("play_3000", "Residente", "Juega 50 horas en el servidor", AchievementType.PlaytimeMinutes, 3000, 50, items: "sapphire_angel_wings_pink"),
        A("connect_25", "Cliente fiel", "Entra 25 veces al servidor", AchievementType.Connections, 25, 10, credits: 500),
        A("points_1000", "Puntuador", "Alcanza 1000 puntos de SharpTimer", AchievementType.SharpTimerPoints, 1000, 20, credits: 1000),

        A("ach_10", "Coleccionista", "Desbloquea 10 logros", AchievementType.Achievements, 10, 20, credits: 1000),
        A("ach_all", "Completista", "Desbloquea todos los demás logros", AchievementType.Achievements, 0, 200, hidden: true, credits: 10000, items: "Goku"),

        A("xmas_advent_1", "Primera casilla", "Abre tu primera casilla del calendario de adviento", AchievementType.Event, 1, 10, credits: 200, from: XmasFrom, to: XmasTo),
        A("xmas_advent_10", "Espíritu navideño", "Abre 10 casillas del calendario de adviento", AchievementType.Event, 1, 30, credits: 1000, from: XmasFrom, to: XmasTo),
        A("xmas_advent_25", "Calendario completo", "Abre las 25 casillas del calendario de adviento", AchievementType.Event, 1, 100, credits: 5000, from: XmasFrom, to: XmasTo),
        A("xmas_nochebuena", "Nochebuena", "Juega en el servidor el 24 de diciembre", AchievementType.Event, 1, 15, credits: 500, from: XmasFrom, to: XmasTo),
        A("xmas_navidad", "¡Feliz Navidad!", "Juega en el servidor el 25 de diciembre", AchievementType.Event, 1, 15, credits: 500, from: XmasFrom, to: XmasTo),
        A("xmas_course", "Recorrido navideño", "Completa un recorrido durante las fiestas", AchievementType.FinishUnder, 999999, 10, credits: 300, from: XmasFrom, to: XmasTo),
        A("xmas_marathon", "Atracón de turrón", "Completa 10 recorridos sin salir del servidor durante las fiestas", AchievementType.SessionFinishes, 10, 25, credits: 1000, from: XmasFrom, to: XmasTo),
        A("xmas_top3", "Regalo de podio", "Termina un mapa entre los 3 mejores tiempos durante las fiestas", AchievementType.FinishTop, 3, 40, credits: 1500, from: XmasFrom, to: XmasTo),
        A("xmas_nochevieja", "Último salto del año", "Completa un recorrido en Nochevieja", AchievementType.FinishUnder, 999999, 30, hidden: true, credits: 1000, from: "2026-12-31 00:00", to: "2026-12-31 23:59:59"),
        A("xmas_reyes", "Regalo de Reyes", "Completa un recorrido el día de Reyes", AchievementType.FinishUnder, 999999, 30, hidden: true, credits: 1000, from: "2027-01-06 00:00", to: "2027-01-06 23:59:59"),
    };
}
