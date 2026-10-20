using System.Runtime.CompilerServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using StoreApi;

namespace CourseAchievements;

/// <summary>
/// API de cs2-store (IStoreApi, en shared/StoreApi). En su propia clase por lo mismo que RpMenuBridge: si la store no está
/// instalada, la falta de su DLL solo se nota al crearla.
/// </summary>
public class StoreBridge
{
    readonly IStoreApi _api;
    StoreBridge(IStoreApi api) => _api = api;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static StoreBridge? TryCreate() => IStoreApi.Capability.Get() is { } api ? new StoreBridge(api) : null;

    /// <summary>La store carga a cada jugador de su base de datos al entrar; hasta entonces GetPlayerCredits devuelve -1.</summary>
    public bool Loaded(CCSPlayerController p) => _api.GetPlayerCredits(p) >= 0;

    public bool GiveCredits(CCSPlayerController p, int credits) => _api.GivePlayerCredits(p, credits) >= 0;

    public Dictionary<string, string>? Item(string uniqueId) => _api.GetItem(uniqueId);

    public bool Has(CCSPlayerController p, Dictionary<string, string> item) => _api.Item_PlayerHas(p, item["type"], item["uniqueid"], true);

    public bool Give(CCSPlayerController p, Dictionary<string, string> item) => _api.Item_Give(p, item);
}

public partial class CourseAchievements
{
    StoreBridge? _store;

    void InitStore()
    {
        try { _store = StoreBridge.TryCreate(); }
        catch (Exception ex) { _store = null; Info($"cs2-store API not available: {ex.GetType().Name}"); }
        Info(_store is null ? "cs2-store: not found, rewards disabled" : "cs2-store: found");
        // Reintento de lo que no se pudo entregar (la store aún no había cargado al jugador, por ejemplo).
        AddTimer(15f, () => { foreach (var slot in _players.Keys.ToList()) DeliverRewards(slot); }, TimerFlags.REPEAT);
    }

    static IEnumerable<string> ItemIds(string items) => items.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Texto de la recompensa para el menú y el chat: "500 créditos + Phanton Wings".</summary>
    string RewardText(AchievementDef a)
    {
        var parts = new List<string>();
        if (a.RewardCredits > 0) parts.Add(T("reward.credits", a.RewardCredits));
        foreach (var id in ItemIds(a.RewardItems))
        {
            string? name = null;
            try { name = _store?.Item(id)?.GetValueOrDefault("name"); } catch { }
            parts.Add(name ?? id);
        }
        return string.Join(" + ", parts);
    }

    /// <summary>Al desbloquear: se apunta en la base de datos y se intenta entregar enseguida.</summary>
    void QueueReward(int slot, PlayerData d, AchievementDef a)
    {
        if (!Config.StoreRewards || !a.HasReward || _db is not { } db) return;
        var sid = d.SteamId;
        Task.Run(async () =>
        {
            try
            {
                var reward = await db.AddReward(sid, a);
                if (reward is null) return; // ya se le había dado en otro momento
                Server.NextFrame(() =>
                {
                    if (!_players.TryGetValue(slot, out var cur) || cur.SteamId != sid) return; // se entregará cuando vuelva
                    cur.Rewards.Add(reward);
                    DeliverRewards(slot);
                });
            }
            catch (Exception ex) { Info($"reward {sid} {a.Id}: {ex.Message}"); }
        });
    }

    void DeliverRewards(int slot)
    {
        if (!Config.StoreRewards || _store is not { } store || _db is not { } db) return;
        if (!_players.TryGetValue(slot, out var d) || !d.Loaded || d.Rewards.Count == 0) return;
        if (Utilities.GetPlayerFromSlot(slot) is not { IsValid: true, IsBot: false } p || p.SteamID != d.SteamId) return;
        try { if (!store.Loaded(p)) return; } // aún no: el temporizador lo reintenta
        catch (Exception ex) { Info($"store: {ex.Message}"); return; }

        foreach (var r in d.Rewards.ToList())
        {
            var given = new List<string>();
            var extra = 0;
            try
            {
                // Primero los créditos: si fallan no se ha dado nada todavía y el reintento no duplica productos.
                if (r.Credits > 0)
                {
                    if (!store.GiveCredits(p, r.Credits)) { Info($"reward {r.AchievementId}: credits not given, will retry"); continue; }
                    Chat(p, "reward.credits_given", r.Credits);
                    given.Add($"{r.Credits} credits");
                }
                foreach (var id in ItemIds(r.Items))
                {
                    var item = store.Item(id);
                    if (item is null) { Info($"reward {r.AchievementId}: store item '{id}' does not exist"); given.Add($"{id}:missing"); continue; }
                    var name = item.GetValueOrDefault("name") ?? id;
                    if (store.Has(p, item))
                    {
                        // Ya lo tenía: su precio en créditos (si está activado).
                        if (Config.DuplicateItemAsCredits && int.TryParse(item.GetValueOrDefault("price"), out var price) && price > 0)
                        {
                            extra += price;
                            Chat(p, "reward.duplicate", name, price);
                        }
                        given.Add($"{id}:owned");
                        continue;
                    }
                    if (store.Give(p, item))
                    {
                        Chat(p, "reward.item", name);
                        given.Add(id);
                    }
                    else given.Add($"{id}:failed");
                }
                if (extra > 0 && store.GiveCredits(p, extra)) given.Add($"{extra} credits for owned items");
            }
            catch (Exception ex)
            {
                Info($"reward {r.AchievementId}: {ex.Message}");
                continue;
            }

            d.Rewards.Remove(r);
            var result = string.Join(", ", given);
            Task.Run(async () =>
            {
                try { await db.RewardDelivered(r.Id, result); }
                catch (Exception ex) { Info($"reward delivered {r.Id}: {ex.Message}"); }
            });
        }
    }
}
