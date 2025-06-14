using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Memory;

namespace PluginInvisible;

public class PluginInvisible : BasePlugin
{
    public override string ModuleName => "PluginInvisible";
    public override string ModuleVersion => "1.0.0";

    private Dictionary<ulong, bool> invisiblePlayers = new();

    public override void Load(bool hotReload)
    {
        Console.WriteLine("[PluginInvisible] Plugin chargé !");
    }

    private bool HasInvisiblePermission(CCSPlayerController? player)
    {
        if (player == null) return false;
        return AdminManager.PlayerHasPermissions(player, "@css/invisible") || AdminManager.PlayerHasPermissions(player, "@css/root");
    }

    [ConsoleCommand("css_invisible", "Rend un joueur invisible")]
    public void OnInvisibleCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (caller == null) return;

        // Obtenir le nom du joueur cible (prendre tout le reste de la commande comme nom)
        string targetName = command.ArgString.Trim();

        if (string.IsNullOrEmpty(targetName))
        {
            caller.PrintToChat($" {ChatColors.Red}[Invisible]{ChatColors.Default} Usage: !invisible <pseudo>");
            return;
        }

        // Chercher le joueur
        CCSPlayerController? targetPlayer = null;
        
        // Afficher tous les joueurs en debug
        Server.PrintToConsole("Joueurs connectés :");
        foreach (var player in Utilities.GetPlayers())
        {
            if (player != null)
            {
                Server.PrintToConsole($"- {player.PlayerName}");
            }
        }

        // Recherche exacte d'abord
        foreach (var player in Utilities.GetPlayers())
        {
            if (player != null && player.PlayerName.Equals(targetName, StringComparison.OrdinalIgnoreCase))
            {
                targetPlayer = player;
                break;
            }
        }

        // Si pas trouvé, recherche partielle
        if (targetPlayer == null)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player != null && player.PlayerName.Contains(targetName, StringComparison.OrdinalIgnoreCase))
                {
                    targetPlayer = player;
                    break;
                }
            }
        }

        if (targetPlayer == null)
        {
            caller.PrintToChat($" {ChatColors.Red}[Invisible]{ChatColors.Default} Joueur non trouvé. Nom recherché : {targetName}");
            return;
        }

        // Vérifier si le joueur est vivant
        if (!targetPlayer.PawnIsAlive)
        {
            caller.PrintToChat($" {ChatColors.Red}[Invisible]{ChatColors.Default} Le joueur doit être vivant.");
            return;
        }

        // Inverser l'état d'invisibilité
        bool isCurrentlyInvisible = IsPlayerInvisible(targetPlayer);
        SetPlayerInvisible(targetPlayer, !isCurrentlyInvisible);

        // Notifier les joueurs
        string status = !isCurrentlyInvisible ? "invisible" : "visible";
        Server.PrintToChatAll($" {ChatColors.Red}[Invisible]{ChatColors.Default} {targetPlayer.PlayerName} est maintenant {status}.");
    }

    private void SetPlayerInvisible(CCSPlayerController player, bool invisible)
    {
        if (player.PlayerPawn.Value == null) return;

        var pawn = player.PlayerPawn.Value;
        
        if (invisible)
        {
            // Rendre complètement invisible
            Server.ExecuteCommand($"ent_fire {player.PlayerName} addoutput \"rendermode 1\"");
            Server.ExecuteCommand($"ent_fire {player.PlayerName} addoutput \"renderamt 0\"");
            Server.ExecuteCommand($"ent_fire {player.PlayerName} addoutput \"renderfx 0\"");
            Server.ExecuteCommand($"ent_fire {player.PlayerName} alpha 0");
            
            // Forcer l'invisibilité avec plusieurs méthodes
            pawn.RenderMode = RenderMode_t.kRenderTransColor;
            Server.ExecuteCommand($"sm_drug #{player.UserId}"); // Effet visuel qui aide à l'invisibilité
            Server.ExecuteCommand($"ent_fire {player.PlayerName} color \"0 0 0 0\"");
        }
        else
        {
            // Rendre visible
            Server.ExecuteCommand($"ent_fire {player.PlayerName} addoutput \"rendermode 0\"");
            Server.ExecuteCommand($"ent_fire {player.PlayerName} addoutput \"renderamt 255\"");
            Server.ExecuteCommand($"ent_fire {player.PlayerName} addoutput \"renderfx 0\"");
            Server.ExecuteCommand($"ent_fire {player.PlayerName} alpha 255");
            
            // Restaurer la visibilité
            pawn.RenderMode = RenderMode_t.kRenderNormal;
            Server.ExecuteCommand($"sm_drug #{player.UserId} 0"); // Arrêter l'effet
            Server.ExecuteCommand($"ent_fire {player.PlayerName} color \"255 255 255 255\"");
        }
    }

    private bool IsPlayerInvisible(CCSPlayerController player)
    {
        if (player.PlayerPawn.Value == null) return false;
        return player.PlayerPawn.Value.RenderMode != RenderMode_t.kRenderNormal;
    }

    [ConsoleCommand("invisible")]
    public void OnInvisibleChatCommand(CCSPlayerController? caller, CommandInfo info)
    {
        if (caller == null) return;
        
        if (!HasInvisiblePermission(caller))
        {
            info.ReplyToCommand("[PluginInvisible] Vous n'avez pas la permission d'utiliser cette commande.");
            return;
        }

        if (info.ArgCount < 2)
        {
            info.ReplyToCommand("[PluginInvisible] Usage: !invisible <pseudo>");
            return;
        }

        HandleInvisibleCommand(caller, info.ArgString);
    }

    [ConsoleCommand("listinvisible")]
    [RequiresPermissions("@css/invisible")]
    public void OnListInvisibleCommand(CCSPlayerController? caller, CommandInfo info)
    {
        if (caller == null) return;

        if (!HasInvisiblePermission(caller))
        {
            info.ReplyToCommand("[PluginInvisible] Vous n'avez pas la permission d'utiliser cette commande.");
            return;
        }

        var invisibleList = invisiblePlayers.Where(kv => kv.Value)
            .Select(kv => GetPlayerByUserId(kv.Key)?.PlayerName ?? "Inconnu")
            .ToList();

        if (invisibleList.Count == 0)
        {
            info.ReplyToCommand("[PluginInvisible] Aucun joueur invisible actuellement.");
            return;
        }

        info.ReplyToCommand("[PluginInvisible] Joueurs invisibles :");
        foreach (var playerName in invisibleList)
        {
            info.ReplyToCommand($"- {playerName}");
        }
    }

    private void HandleInvisibleCommand(CCSPlayerController? caller, string targetName)
    {
        var players = Utilities.GetPlayers();
        var target = players.FirstOrDefault(p => p.PlayerName.Contains(targetName, StringComparison.OrdinalIgnoreCase));

        if (target == null)
        {
            ReplyToCommand(caller, $"[PluginInvisible] Joueur introuvable : {targetName}");
            return;
        }

        if (!target.PawnIsAlive)
        {
            ReplyToCommand(caller, "[PluginInvisible] Le joueur doit être vivant pour utiliser cette commande.");
            return;
        }

        ulong steamId = target.SteamID;

        if (invisiblePlayers.ContainsKey(steamId) && invisiblePlayers[steamId])
        {
            // Désactiver l'invisibilité
            SetPlayerInvisible(target, false);
            invisiblePlayers[steamId] = false;
            Server.PrintToChatAll($"[PluginInvisible] {target.PlayerName} est maintenant visible.");
        }
        else
        {
            // Activer l'invisibilité
            SetPlayerInvisible(target, true);
            invisiblePlayers[steamId] = true;
            Server.PrintToChatAll($"[PluginInvisible] {target.PlayerName} est maintenant invisible.");
        }
    }

    private void ReplyToCommand(CCSPlayerController? caller, string message)
    {
        if (caller == null)
        {
            Console.WriteLine(message);
        }
        else
        {
            caller.PrintToChat(message);
        }
    }

    private CCSPlayerController? GetPlayerByUserId(ulong steamId)
    {
        return Utilities.GetPlayers().FirstOrDefault(p => p.SteamID == steamId);
    }
} 