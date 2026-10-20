using System.Text.Json;
using CounterStrikeSharp.API;
using MySqlConnector;

namespace CourseAchievements;

public record TopRow(string Name, int Unlocked, int Score);

/// <summary>
/// Tablas propias ({prefijo}players, {prefijo}unlocks y {prefijo}achievements) en la base de datos de SharpTimer, cuya conexión se lee de
/// cfg/SharpTimer/mysqlConfig.json. De SharpTimer solo se lee: PlayerRecords y PlayerStats.
/// </summary>
public class Database
{
    readonly string _conn;
    readonly string _players, _unlocks, _defs, _rewards, _stStats;

    public Database(string tablePrefix)
    {
        var path = Path.Combine(Server.GameDirectory, "csgo", "cfg", "SharpTimer", "mysqlConfig.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var r = doc.RootElement;
        string Str(string key, string def) => r.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : def;
        _conn = new MySqlConnectionStringBuilder
        {
            Server = Str("Host", "localhost"),
            Port = r.TryGetProperty("Port", out var port) ? (uint)port.GetInt32() : 3306,
            Database = Str("Database", ""),
            UserID = Str("Username", "root"),
            Password = Str("Password", ""),
            SslMode = Enum.TryParse<MySqlSslMode>(Str("SslMode", "None"), true, out var ssl) ? ssl : MySqlSslMode.None,
        }.ConnectionString;
        // Misma regla que SharpTimer: PlayerStats lleva el prefijo detrás ("PlayerStats_prefijo"), PlayerRecords nunca.
        var stPrefix = Str("TablePrefix", "");
        _stStats = stPrefix.Length > 0 ? $"PlayerStats_{stPrefix}" : "PlayerStats";
        _players = $"{tablePrefix}players";
        _unlocks = $"{tablePrefix}unlocks";
        _defs = $"{tablePrefix}achievements";
        _rewards = $"{tablePrefix}rewards";
    }

    async Task<MySqlConnection> Open()
    {
        var db = new MySqlConnection(_conn);
        await db.OpenAsync();
        return db;
    }

    static MySqlCommand Cmd(MySqlConnection db, string sql, params (string Name, object? Value)[] args)
    {
        var cmd = new MySqlCommand(sql, db);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }

    public async Task Init()
    {
        await using var db = await Open();
        await Cmd(db, $@"CREATE TABLE IF NOT EXISTS {_players} (
            SteamID VARCHAR(20) NOT NULL PRIMARY KEY,
            PlayerName VARCHAR(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
            Jumps BIGINT NOT NULL DEFAULT 0,
            PlaytimeSec BIGINT NOT NULL DEFAULT 0,
            Connections BIGINT NOT NULL DEFAULT 0,
            TimerStarts BIGINT NOT NULL DEFAULT 0,
            ServerRecords BIGINT NOT NULL DEFAULT 0,
            PersonalBests BIGINT NOT NULL DEFAULT 0,
            MaxSpeed BIGINT NOT NULL DEFAULT 0,
            Unlocked INT NOT NULL DEFAULT 0,
            Score INT NOT NULL DEFAULT 0,
            LastSeen INT NOT NULL DEFAULT 0)").ExecuteNonQueryAsync();
        await Cmd(db, $@"CREATE TABLE IF NOT EXISTS {_unlocks} (
            SteamID VARCHAR(20) NOT NULL,
            AchievementId VARCHAR(64) NOT NULL,
            UnlockedAt INT NOT NULL,
            Points INT NOT NULL DEFAULT 0,
            PRIMARY KEY (SteamID, AchievementId))").ExecuteNonQueryAsync();
        await Cmd(db, $@"CREATE TABLE IF NOT EXISTS {_defs} (
            Id VARCHAR(64) NOT NULL PRIMARY KEY,
            SortOrder INT NOT NULL DEFAULT 0,
            Enabled BOOL NOT NULL DEFAULT TRUE,
            Name VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
            Description VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
            Type VARCHAR(32) NOT NULL,
            Target DOUBLE NOT NULL DEFAULT 1,
            Map VARCHAR(255) NOT NULL DEFAULT '',
            Style INT NULL,
            Points INT NOT NULL DEFAULT 10,
            Hidden BOOL NOT NULL DEFAULT FALSE,
            Command VARCHAR(255) NOT NULL DEFAULT '',
            RewardCredits INT NOT NULL DEFAULT 0,
            RewardItems VARCHAR(255) NOT NULL DEFAULT '')").ExecuteNonQueryAsync();
        // Recompensas de cs2-store: una fila por logro desbloqueado con recompensa; DeliveredAt se rellena al entregarla.
        await Cmd(db, $@"CREATE TABLE IF NOT EXISTS {_rewards} (
            Id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            SteamID VARCHAR(20) NOT NULL,
            AchievementId VARCHAR(64) NOT NULL,
            Credits INT NOT NULL DEFAULT 0,
            Items VARCHAR(255) NOT NULL DEFAULT '',
            CreatedAt INT NOT NULL,
            DeliveredAt INT NULL,
            Result VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
            UNIQUE KEY (SteamID, AchievementId),
            KEY (SteamID, DeliveredAt))").ExecuteNonQueryAsync();
        // Columnas que no existían en tablas creadas por versiones anteriores.
        await AddColumnIfMissing(db, _unlocks, "Points", "Points INT NOT NULL DEFAULT 0");
        await AddColumnIfMissing(db, _defs, "RewardCredits", "RewardCredits INT NOT NULL DEFAULT 0");
        await AddColumnIfMissing(db, _defs, "RewardItems", "RewardItems VARCHAR(255) NOT NULL DEFAULT ''");
    }

    static async Task AddColumnIfMissing(MySqlConnection db, string table, string column, string definition)
    {
        await using var has = Cmd(db, "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@t AND COLUMN_NAME=@c",
            ("@t", table), ("@c", column));
        if (Convert.ToInt32(await has.ExecuteScalarAsync()) == 0)
            await Cmd(db, $"ALTER TABLE {table} ADD COLUMN {definition}").ExecuteNonQueryAsync();
    }

    // ---- Recompensas pendientes ----

    /// <summary>Apunta la recompensa de un logro (una sola vez por jugador y logro). Devuelve su Id, o null si ya existía.</summary>
    public async Task<PendingReward?> AddReward(ulong steamId, AchievementDef a)
    {
        await using var db = await Open();
        var n = await Cmd(db, $"INSERT IGNORE INTO {_rewards} (SteamID,AchievementId,Credits,Items,CreatedAt) VALUES (@s,@a,@c,@i,UNIX_TIMESTAMP())",
            ("@s", steamId.ToString()), ("@a", a.Id), ("@c", a.RewardCredits), ("@i", a.RewardItems)).ExecuteNonQueryAsync();
        if (n == 0) return null;
        await using var id = Cmd(db, "SELECT LAST_INSERT_ID()");
        return new PendingReward(Convert.ToInt32(await id.ExecuteScalarAsync()), a.Id, a.RewardCredits, a.RewardItems);
    }

    public async Task<List<PendingReward>> PendingRewards(ulong steamId)
    {
        await using var db = await Open();
        await using var cmd = Cmd(db, $"SELECT Id,AchievementId,Credits,Items FROM {_rewards} WHERE SteamID=@s AND DeliveredAt IS NULL ORDER BY Id", ("@s", steamId.ToString()));
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<PendingReward>();
        while (await r.ReadAsync()) list.Add(new PendingReward(r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetString(3)));
        return list;
    }

    public async Task RewardDelivered(int id, string result)
    {
        await using var db = await Open();
        await Cmd(db, $"UPDATE {_rewards} SET DeliveredAt=UNIX_TIMESTAMP(), Result=@r WHERE Id=@id",
            ("@id", id), ("@r", result.Length > 255 ? result[..255] : result)).ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Logros de la tabla {prefijo}achievements, en su SortOrder. Si está vacía se rellena con <paramref name="seed"/> (los
    /// del JSON). Las filas con un Type desconocido se saltan y se avisa en la consola.
    /// </summary>
    public async Task<List<AchievementDef>> LoadDefinitions(IReadOnlyList<AchievementDef> seed)
    {
        await using var db = await Open();
        await using (var count = Cmd(db, $"SELECT COUNT(*) FROM {_defs}"))
        {
            if (Convert.ToInt32(await count.ExecuteScalarAsync()) == 0)
            {
                for (var i = 0; i < seed.Count; i++)
                {
                    var a = seed[i];
                    await Cmd(db, $@"INSERT IGNORE INTO {_defs} (Id,SortOrder,Enabled,Name,Description,Type,Target,Map,Style,Points,Hidden,Command,RewardCredits,RewardItems)
                        VALUES (@id,@o,TRUE,@n,@d,@ty,@t,@m,@s,@p,@h,@c,@rc,@ri)",
                        ("@id", a.Id), ("@o", (i + 1) * 10), ("@n", a.Name), ("@d", a.Description), ("@ty", a.Type.ToString()), ("@t", a.Target),
                        ("@m", a.Map), ("@s", a.Style), ("@p", a.Points), ("@h", a.Hidden), ("@c", a.Command),
                        ("@rc", a.RewardCredits), ("@ri", a.RewardItems)).ExecuteNonQueryAsync();
                }
                CourseAchievements.Info($"{_defs} was empty: filled with {seed.Count} achievements from the config");
            }
        }
        var list = new List<AchievementDef>();
        await using var cmd = Cmd(db, $"SELECT Id,Name,Description,Type,Target,Map,Style,Points,Hidden,Command,RewardCredits,RewardItems FROM {_defs} WHERE Enabled ORDER BY SortOrder, Id");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var id = r.GetString(0);
            if (!Enum.TryParse<AchievementType>(r.GetString(3), true, out var type))
            {
                CourseAchievements.Info($"achievement '{id}' has an unknown Type '{r.GetString(3)}', skipped");
                continue;
            }
            list.Add(new AchievementDef
            {
                Id = id, Name = r.GetString(1), Description = r.GetString(2), Type = type, Target = r.GetDouble(4), Map = r.GetString(5),
                Style = r.IsDBNull(6) ? null : r.GetInt32(6), Points = r.GetInt32(7), Hidden = r.GetBoolean(8), Command = r.GetString(9),
                RewardCredits = r.GetInt32(10), RewardItems = r.GetString(11),
            });
        }
        return list;
    }

    /// <summary>Carga todo lo del jugador en un PlayerData nuevo (se rellena fuera del hilo principal y se entrega después).</summary>
    public async Task<PlayerData> LoadPlayer(ulong steamId)
    {
        var d = new PlayerData { SteamId = steamId };
        var sid = steamId.ToString();
        await using var db = await Open();
        await using (var cmd = Cmd(db, $"SELECT Jumps,PlaytimeSec,Connections,TimerStarts,ServerRecords,PersonalBests,MaxSpeed FROM {_players} WHERE SteamID=@s", ("@s", sid)))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                d.Jumps = r.GetInt64(0); d.PlaytimeSec = r.GetInt64(1); d.Connections = r.GetInt64(2); d.TimerStarts = r.GetInt64(3);
                d.ServerRecords = r.GetInt64(4); d.PersonalBests = r.GetInt64(5); d.MaxSpeed = r.GetInt64(6);
            }
        }
        await using (var cmd = Cmd(db, $"SELECT AchievementId,UnlockedAt FROM {_unlocks} WHERE SteamID=@s", ("@s", sid)))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) d.Unlocked[r.GetString(0)] = r.GetInt32(1);
        }
        (d.Records, d.StPoints) = await LoadSharpTimer(db, sid);
        return d;
    }

    public async Task<(List<StRecord> Records, long Points)> LoadSharpTimer(ulong steamId)
    {
        await using var db = await Open();
        return await LoadSharpTimer(db, steamId.ToString());
    }

    async Task<(List<StRecord>, long)> LoadSharpTimer(MySqlConnection db, string sid)
    {
        var list = new List<StRecord>();
        await using (var cmd = Cmd(db, "SELECT MapName,Style,COALESCE(Mode,''),TimerTicks,TimesFinished FROM PlayerRecords WHERE SteamID=@s", ("@s", sid)))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
                list.Add(new StRecord(r.GetString(0), r.IsDBNull(1) ? 0 : r.GetInt32(1), r.GetString(2), r.IsDBNull(3) ? 0 : r.GetInt32(3), r.IsDBNull(4) ? 0 : r.GetInt32(4)));
        }
        long points = 0;
        try
        {
            await using var cmd = Cmd(db, $"SELECT GlobalPoints FROM {_stStats} WHERE SteamID=@s", ("@s", sid));
            if (await cmd.ExecuteScalarAsync() is { } v and not DBNull) points = Convert.ToInt64(v);
        }
        catch (MySqlException) { /* sin rangos globales en SharpTimer: la tabla o la columna pueden no existir */ }
        return (list, points);
    }

    /// <summary>Puesto del tiempo en su mapa/estilo/modo: 1 + cuántos tiempos mejores hay.</summary>
    public async Task<int> Rank(StRecord rec)
    {
        await using var db = await Open();
        await using var cmd = Cmd(db, "SELECT COUNT(*)+1 FROM PlayerRecords WHERE MapName=@m AND Style=@st AND COALESCE(Mode,'')=@mo AND TimerTicks<@t",
            ("@m", rec.MapName), ("@st", rec.Style), ("@mo", rec.Mode), ("@t", rec.Ticks));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task SavePlayer(ulong steamId, string name, long[] counters, int unlocked, int score)
    {
        await using var db = await Open();
        await Cmd(db, $@"INSERT INTO {_players} (SteamID,PlayerName,Jumps,PlaytimeSec,Connections,TimerStarts,ServerRecords,PersonalBests,MaxSpeed,Unlocked,Score,LastSeen)
            VALUES (@s,@n,@c0,@c1,@c2,@c3,@c4,@c5,@c6,@u,@sc,UNIX_TIMESTAMP())
            ON DUPLICATE KEY UPDATE PlayerName=@n,Jumps=@c0,PlaytimeSec=@c1,Connections=@c2,TimerStarts=@c3,ServerRecords=@c4,PersonalBests=@c5,
            MaxSpeed=@c6,Unlocked=@u,Score=@sc,LastSeen=UNIX_TIMESTAMP()",
            ("@s", steamId.ToString()), ("@n", name), ("@c0", counters[0]), ("@c1", counters[1]), ("@c2", counters[2]), ("@c3", counters[3]),
            ("@c4", counters[4]), ("@c5", counters[5]), ("@c6", counters[6]), ("@u", unlocked), ("@sc", score)).ExecuteNonQueryAsync();
    }

    public async Task Unlock(ulong steamId, string id, long at, int points)
    {
        await using var db = await Open();
        await Cmd(db, $"INSERT IGNORE INTO {_unlocks} (SteamID,AchievementId,UnlockedAt,Points) VALUES (@s,@a,@t,@p)",
            ("@s", steamId.ToString()), ("@a", id), ("@t", at), ("@p", points)).ExecuteNonQueryAsync();
    }

    public async Task Revoke(ulong steamId, string? id)
    {
        await using var db = await Open();
        if (id is null) await Cmd(db, $"DELETE FROM {_unlocks} WHERE SteamID=@s", ("@s", steamId.ToString())).ExecuteNonQueryAsync();
        else await Cmd(db, $"DELETE FROM {_unlocks} WHERE SteamID=@s AND AchievementId=@a", ("@s", steamId.ToString()), ("@a", id)).ExecuteNonQueryAsync();
    }

    public async Task<List<TopRow>> Top(int limit)
    {
        await using var db = await Open();
        await using var cmd = Cmd(db, $"SELECT PlayerName,Unlocked,Score FROM {_players} WHERE Unlocked>0 ORDER BY Score DESC, Unlocked DESC LIMIT @l", ("@l", limit));
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<TopRow>();
        while (await r.ReadAsync()) list.Add(new TopRow(r.GetString(0), r.GetInt32(1), r.GetInt32(2)));
        return list;
    }
}
