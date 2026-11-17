# CourseAchievements

Logros para servidores de **minigames course** de CS2 con **SharpTimer**, con una HUD Panorama hecha con la misma técnica
que [RandomPicksHud](https://github.com/josesilvaruiz/RandomPicksHud) (`custom_hud_layout`): la dibuja la propia interfaz
del juego, sin parpadeos y sin tapar el chat.

## Qué hace

| Parte de la HUD | Muestra |
|---|---|
| **Aviso** (arriba, centrado) | «LOGRO DESBLOQUEADO», nombre, descripción y puntos. Aparece y se va con una transición. Si se desbloquean varios a la vez, salen uno detrás de otro. |
| **Seguimiento** (lateral) | Logros conseguidos (`12/38`), puntos y el siguiente logro más cercano con su barra de progreso. Por defecto va a la derecha, debajo del top 5 de RandomPicksHud. |
| **Lista** (centro, con `!logros`) | Todos los logros por páginas de 8, con su progreso. Los secretos salen como `???` hasta conseguirlos. |

- **Retroactivo**: los logros de recorridos se calculan con los récords que SharpTimer ya tiene en su base de datos.
  Quien entra por primera vez recibe de golpe los que ya se había ganado (un resumen en el chat y como mucho tres avisos).
- **Configurable**: los logros están en el JSON de configuración. Se pueden cambiar, quitar o añadir logros propios,
  incluidos logros de un mapa concreto (`"Map": "mg_course*"`).
- **Recompensas** opcionales: cada logro puede ejecutar un comando de servidor (créditos de una tienda, un rango…).
- Anuncio en el chat, sonido, top de jugadores por puntos de logros y textos en español e inglés.

## Requisitos

- CounterStrikeSharp **1.0.374 o superior** (la API de `custom_hud_layout`).
- [SharpTimer](https://github.com/Letaryat/poor-SharpTimer) con **MySQL**. El plugin usa la misma base de datos: lee la
  conexión de `cfg/SharpTimer/mysqlConfig.json` y crea dos tablas propias (`ca_achievements`, `ca_unlocks` y `ca_players`).
- [MultiAddonManager](https://github.com/Source2ZE/MultiAddonManager), para que los clientes descarguen el addon con el layout.
  Sin addon el plugin funciona igual, pero solo por chat (`"UseHud": false`).

## Instalación

### 1. Layout de la HUD (addon de Workshop)

El cliente de CS2 solo enseña **una** `custom_hud_layout`. Con RandomPicksHud en el servidor (versión con menús, que
trae `shared/RandomPicksHudApi`), la lista de `!logros` es un menú de RandomPicksHud (se mueve con sus teclas, y al
elegir un logro se ve su detalle). El panel de seguimiento lateral no se usa.

Para el aviso centrado con la copa, usa `"HudMode": "shared"`: el plugin escribe en la entidad de RandomPicksHud, cuyo
layout tiene que llevar el aviso. En tu addon de Workshop:

1. Copia `addon-randompicks/panorama/layout/custom_game/randompicks_hud.xml` y
   `addon-randompicks/panorama/styles/custom_game/randompicks_hud.css` a `content\csgo_addons\<tu_addon>\panorama\…`.
   Son el layout y el CSS de RandomPicksHud **con menús** (descompilados del addon publicado) **sin cambios**, más el
   panel `ca_toast` y sus estilos `.CaToast*` al final.
2. Compila el layout desde `game\bin\win64` de CS2 (compila también el CSS que incluye) y republica el addon:

```
resourcecompiler.exe -nop4 -i "<CS2>\content\csgo_addons\<tu_addon>\panorama\layout\custom_game\randompicks_hud.xml"
```

No hay que tocar la configuración de RandomPicksHud: su `Layout` sigue siendo `randompicks_hud.xml`. Si actualizas
RandomPicksHud y cambia su layout, vuelve a añadirle el bloque `ca_toast` y los estilos `.CaToast*`.

Sin tocar el addon, `"HudMode": "auto"` con `"Toast": "menu"` dibuja el aviso en el panel de menús de RandomPicksHud
(abajo en el centro, sin copa).

**Sin RandomPicksHud**: publica `addon/panorama/` (layout `course_achievements.xml` y su CSS) en un addon, compila
`course_achievements.xml` y añade el ID del addon a `mm_extra_addons` y `mm_client_extra_addons` en
`cfg/multiaddonmanager/multiaddonmanager.cfg`. El plugin creará su propia entidad.

Todas las clases del CSS de logros llevan el prefijo `Ca` para no mezclarse con las de RandomPicksHud.

### 2. Plugin

```
dotnet build -c Release
```

Copia el contenido de `bin/Release/net10.0/` a `game/csgo/addons/counterstrikesharp/plugins/CourseAchievements/`:

```
CourseAchievements.dll
CourseAchievements.deps.json
MySqlConnector.dll
lang/es.json
lang/en.json
```

Reinicia o cambia de mapa. La configuración se crea sola en
`addons/counterstrikesharp/configs/plugins/CourseAchievements/CourseAchievements.json`.
Los textos salen en el idioma del servidor (`ServerLanguage` en `configs/core.json`, por ejemplo `"es"`).

## Comandos

| Comando | Quién | Qué hace |
|---|---|---|
| `!logros` / `!ach` | Todos | Escribe en el chat cuántos llevas, el siguiente y la primera página (8 logros), y la abre en la HUD. Si la HUD ya está abierta, la cierra. |
| `!logros <página>` | Todos | Esa página, en el chat y en la HUD |
| `!logros todos` | Todos | La lista entera, con progreso, en la consola del jugador |
| `!logros cerrar` | Todos | Cierra la lista de la HUD |
| `!toplogros` / `!topach` | Todos | Top 10 por puntos de logros |
| `!logroshud` / `!achhud` | Todos | Oculta o muestra el panel de seguimiento |
| `css_logro_give <objetivo> <id>` | `@css/root` | Da un logro (para probar) |
| `css_logro_revoke <objetivo> <id\|all>` | `@css/root` | Quita un logro o todos. Los que el jugador siga cumpliendo se le vuelven a dar al reconectar. |
| `css_logros_reload` | `@css/root` | Vuelve a leer los logros de la tabla `ca_achievements` (tras editarla) |
| `css_logros_debug` | `@css/root` | Estado de la HUD (qué entidad usa, cuántos paneles tiene) y de SharpTimer |

## Configuración

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Layout` | `panorama/layout/custom_game/course_achievements.xml` | Ruta del layout dentro del addon. Nombre **fuente** (`.xml`), no `.vxml_c`. |
| `UseHud` | `true` | `false` = sin HUD: avisos y lista solo por chat. |
| `HudMode` | `auto` | `auto`: con RandomPicksHud, la lista va en **sus menús** y se crea una HUD propia solo para el aviso (que el cliente no llega a enseñar junto a la suya); sin él, HUD propia completa. `shared`: el aviso se escribe en la entidad de RandomPicksHud (su layout tiene que llevar `ca_toast`, ver arriba). `own`: siempre HUD propia con todos sus paneles. |
| `Toast` | `hud` | Aviso al desbloquear. `hud`: copa y nombre del logro, centrado algo por encima del medio, con una barra que se vacía en 6 s (layout propio, también con RandomPicksHud). `menu`: en el panel de menús de RandomPicksHud. `chat`: solo el chat. |
| `DefinitionsSource` | `database` | `database`: los logros se leen de la tabla `ca_achievements`. `config`: de `Achievements` en este JSON. |
| `TrackerPosition` | `right` | Panel de seguimiento: `right`, `left` u `off`. |
| `RefreshSeconds` | `5` | Cada cuánto se reenvía la HUD entera (CS2 la borra a quien ya estaba cuando entra otro). |
| `ToastSeconds` | `6` | Segundos que se ve cada aviso. |
| `ListSeconds` | `20` | Segundos que se ve la lista. |
| `TablePrefix` | `ca_` | Prefijo de las tablas propias. |
| `AnnounceToAll` | `true` | Anunciar en el chat de todos quién desbloquea cada logro. |
| `UnlockSound` | `sounds/ui/panorama/round_report_round_won_01.vsnd` | Sonido al desbloquear. Vacío = sin sonido. |
| `FinishCheckDelay` | `2` | Segundos que se espera a que SharpTimer guarde el tiempo antes de comprobarlo. |
| `UtcOffsetHours` | `1` | Desfase de la hora local respecto a UTC para las ventanas de temporada (`ActiveFrom`/`ActiveTo`). |
| `Achievements` | 48 logros | La lista de logros (ver abajo). Con `DefinitionsSource: database` solo sirve para rellenar la tabla la primera vez. |

## Recompensas de cs2-store

Si está [cs2-store](https://github.com/schwarper/cs2-store), cada logro puede dar créditos (`RewardCredits`) y
productos (`RewardItems`: `uniqueid` de `cs2-store.json`, varios separados por comas). Se usa su API (`IStoreApi`).

- Al desbloquear, la recompensa se apunta en `ca_rewards` y se entrega en cuanto la store tiene cargado al jugador
  (se reintenta cada 15 s y al volver a entrar). Ninguna se pierde ni se entrega dos veces.
- Primero se dan los créditos y después los productos. Si ya tenía el producto, recibe su precio en créditos
  (`DuplicateItemAsCredits`).
- Los logros retroactivos (los que se ganan al entrar por primera vez gracias a los récords) también dan recompensa.
- Las recompensas por defecto están en `sql/rewards.sql`. Para cambiarlas, edita `ca_achievements` y ejecuta `css_logros_reload`.

```sql
UPDATE ca_achievements SET RewardCredits = 3000, RewardItems = 'kakashi_hatake' WHERE Id = 'top_3';
-- Entregas: pendientes (DeliveredAt NULL) y hechas, con lo que se dio en Result
SELECT SteamID, AchievementId, Credits, Items, FROM_UNIXTIME(DeliveredAt), Result FROM ca_rewards ORDER BY Id DESC LIMIT 20;
```

## Base de datos

Todo va en la base de datos MySQL de SharpTimer, con el prefijo `TablePrefix` (`ca_`):

| Tabla | Contenido |
|---|---|
| `ca_achievements` | **Los logros**: `Id`, `SortOrder` (orden en la lista), `Enabled`, `Name`, `Description`, `Type`, `Target`, `Map`, `Style`, `Points`, `Hidden`, `Command`. Se rellena sola con los 38 de la config la primera vez que está vacía. |
| `ca_unlocks` | **Quién tiene cada logro**: `SteamID`, `AchievementId`, `UnlockedAt` (unix) y `Points` (los puntos que recibió). |
| `ca_rewards` | Recompensas de la store: qué se debía, cuándo se entregó y qué se dio (`Result`). |
| `ca_players` | Por jugador: nombre, contadores (saltos, tiempo jugado, entradas, inicios, récords, PB, velocidad máxima), `Unlocked` (nº de logros), `Score` (puntos totales) y `LastSeen`. |

Para añadir o cambiar logros, edita `ca_achievements` y ejecuta `css_logros_reload` (o cambia de mapa). Para quitar uno
sin perder quién lo tenía, pon `Enabled = 0`. Ejemplos:

```sql
-- Logro nuevo: completar 50 veces cualquier mapa mg_course
INSERT INTO ca_achievements (Id, SortOrder, Name, Description, Type, Target, Map, Points)
VALUES ('course_50', 75, 'Adicto al course', 'Completa 50 veces un mapa mg_course', 'Finishes', 50, 'mg_course*', 40);

-- Ranking por puntos de logros
SELECT PlayerName, Unlocked, Score FROM ca_players ORDER BY Score DESC LIMIT 10;

-- Logros de un jugador, con fecha y puntos
SELECT u.AchievementId, a.Name, FROM_UNIXTIME(u.UnlockedAt) AS Fecha, u.Points
FROM ca_unlocks u LEFT JOIN ca_achievements a ON a.Id = u.AchievementId
WHERE u.SteamID = '7656119XXXXXXXXXX' ORDER BY u.UnlockedAt;
```

### Formato de un logro

```json
{
  "Id": "course_master",
  "Name": "Maestro del course",
  "Description": "Completa 25 veces un mapa mg_course",
  "Type": "Finishes",
  "Target": 25,
  "Map": "mg_course*",
  "Style": null,
  "Points": 30,
  "Hidden": false,
  "Command": ""
}
```

- `Id`: único y fijo. Es lo que se guarda en la base de datos; si lo cambias, el logro cuenta como nuevo.
- `Map`: patrón de mapas con `*`, varios separados por comas (`"mg_course*,mg_parkour*"`). Vacío = cualquier mapa.
  Se aplica a `Finishes`, `UniqueMaps`, `Bonuses` y a todos los `Finish…`.
- `Style`: estilo de SharpTimer (`0` = normal). `null` = cualquiera.
- `Hidden`: secreto, se ve como `???` hasta conseguirlo.
- `Command`: comando de servidor al desbloquearlo. Admite `{steamid}`, `{userid}`, `{slot}` y `{name}`.
  Ejemplo: `"css_givecredits {userid} 500"`.
- El orden de la lista es el orden en que salen en `!logros`.

### Tipos (`Type`)

**Acumulados** (con barra de progreso hasta `Target`):

| Tipo | Cuenta | Origen |
|---|---|---|
| `Finishes` | Recorridos completados (suma de `TimesFinished`) | Récords de SharpTimer, retroactivo |
| `UniqueMaps` | Mapas distintos completados | Récords de SharpTimer, retroactivo |
| `Bonuses` | Bonus distintos completados | Récords de SharpTimer, retroactivo |
| `SharpTimerPoints` | Puntos globales de SharpTimer | `PlayerStats` de SharpTimer |
| `ServerRecords` | Récords del servidor conseguidos | Este plugin, desde que se instala |
| `PersonalBests` | Veces que se mejora la marca personal | Este plugin |
| `TimerStarts` | Veces que se inicia el cronómetro | Este plugin |
| `Jumps` | Saltos | Este plugin |
| `MaxSpeed` | Velocidad horizontal máxima con el cronómetro en marcha (u/s, sin noclip) | Este plugin |
| `PlaytimeMinutes` | Minutos jugados | Este plugin |
| `Connections` | Veces que se entra al servidor (los cambios de mapa no cuentan) | Este plugin |
| `SessionFinishes` | Recorridos completados sin salir del servidor | Este plugin |
| `Achievements` | Otros logros desbloqueados. `Target: 0` = todos los demás. **No cuentan los de temporada** | Este plugin |
| `Event` | Nunca se cumple solo: se da con `css_logro_give` desde otro plugin (p. ej. AdventCalendar) | Externo |

**Al completar un recorrido** (solo el mapa principal, no los bonus):

| Tipo | Se cumple si… |
|---|---|
| `FinishUnder` | el tiempo es menor que `Target` segundos |
| `FinishTop` | tras el recorrido tu marca está entre los `Target` mejores del mapa |
| `FinishTier` | el mapa es de tier `Target` o más (tier de SharpTimer; los mapas sin tier cuentan como 1) |
| `FinishSync` | el sync del recorrido es de `Target` % o más |
| `FinishStyle` | se usa un estilo distinto al normal, o el de `Style` si está puesto |
| `FinishMaxJumps` | se salta `Target` veces o menos (no cuenta el salto para salir de la zona de inicio) |

### Logros de temporada

Cualquier logro puede llevar `ActiveFrom` / `ActiveTo` (columnas DATETIME de `ca_achievements`, hora local según
`UtcOffsetHours`, por defecto 1 = España en invierno). Fuera de esa ventana:

- no se puede conseguir (no se evalúa);
- no sale en la lista ni en el total `x/y`, salvo a quien ya lo tenga (sus puntos siguen contando);
- nunca cuenta para los de tipo `Achievements` (Coleccionista / Completista), así quien llega después puede completarlos.

`sql/navidad_2026.sql` añade 10 logros de Navidad (1 dic – 6 ene). Los de tipo `Event` (casillas del calendario,
Nochebuena, Navidad) los da el plugin AdventCalendar con sus `AchievementHooks`.

## Cómo funciona por dentro

- **Recorridos**: el estado del cronómetro se lee de SharpTimer por reflexión (`SharpTimer.Instance.playerTimers`), como
  hace RandomPicksHud, sin depender de su DLL. Cuando el cronómetro se para con tiempo, el plugin espera
  `FinishCheckDelay` segundos y mira en `PlayerRecords` si `TimesFinished` ha subido: así sabe que fue un final válido
  (y no un reinicio o una zona de fin con stages pendientes), si fue marca personal y en qué puesto ha quedado.
- **¿Por qué no la API de SharpTimer?** Su `ISharpTimerEventSender` se registra con
  `() => new SharpTimerAPI_EventSender()` y CounterStrikeSharp llama a esa función en cada `Capability.Get()`: SharpTimer
  lanza cada evento sobre una instancia nueva, sin suscriptores, y nunca llega a otros plugins.
- **HUD**: una entidad `custom_hud_layout` propia. Como en RandomPicksHud, se crea cuando entra o aparece un jugador
  (nunca en `OnMapStart`, que tumba el servidor), se recrea al entrar alguien y se reenvía entera cada `RefreshSeconds`.
  Las barras son clases `w0`…`w10` (de 10 % en 10 %), porque Panorama solo recibe textos y clases desde el servidor.

## Problemas conocidos y avisos

- **No probado en un servidor real.** Compila, pero la HUD y la integración con SharpTimer hay que verlas en partida.
  Mira la consola del servidor: al cargar sale `[CourseAchievements] SharpTimer timers found` y al crear la HUD
  `[CourseAchievements] custom_hud_layout #N layout=…`.
- **Convivencia con RandomPicksHud**: con dos entidades `custom_hud_layout` solo se veía la de RandomPicksHud. Desde la
  1.1.0 los logros usan su entidad y el layout combinado. Si actualizas RandomPicksHud y cambia su layout, hay que volver a
  combinarlo (el bloque `CaHud` va al final, antes de `</root>`, y el `<include>` del CSS de logros en `<styles>`).
- Las posiciones del CSS (`margin-top` del seguimiento) están pensadas para no pisar los paneles de RandomPicksHud;
  si no lo usas, puedes subir el panel cambiando `.Tracker` en el CSS.
- Solo MySQL (como RandomPicksHud). SQLite y PostgreSQL de SharpTimer no están soportados.
