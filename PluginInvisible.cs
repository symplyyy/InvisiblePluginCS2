using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;

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

    [ConsoleCommand("css_invisible")]
    [CommandHelper(minArgs: 1, usage: "!invisible <pseudo>")]
    [RequiresPermissions("@css/invisible")]
    public void OnInvisibleCommand(CCSPlayerController? caller, CommandInfo info)
    {
        HandleInvisibleCommand(caller, info.ArgString);
    }

    [ChatCommand("invisible")]
    public void OnInvisibleChatCommand(CCSPlayerController? caller, CommandInfo info)
    {
        if (caller == null) return;
        
        if (!HasInvisiblePermission(caller))
        {
            info.ReplyToCommand($" {ChatColors.Red}[PluginInvisible] Vous n'avez pas la permission d'utiliser cette commande.");
            return;
        }

        if (info.ArgCount < 2)
        {
            info.ReplyToCommand($" {ChatColors.Red}[PluginInvisible] Usage: !invisible <pseudo>");
            return;
        }

        HandleInvisibleCommand(caller, info.ArgString);
    }

    [ChatCommand("listinvisible")]
    [RequiresPermissions("@css/invisible")]
    public void OnListInvisibleCommand(CCSPlayerController? caller, CommandInfo info)
    {
        if (caller == null) return;

        if (!HasInvisiblePermission(caller))
        {
            info.ReplyToCommand($" {ChatColors.Red}[PluginInvisible] Vous n'avez pas la permission d'utiliser cette commande.");
            return;
        }

        var invisibleList = invisiblePlayers.Where(kv => kv.Value)
            .Select(kv => Utilities.GetPlayerFromSteamId(kv.Key)?.PlayerName ?? "Inconnu")
            .ToList();

        if (invisibleList.Count == 0)
        {
            info.ReplyToCommand($" {ChatColors.Blue}[PluginInvisible] Aucun joueur invisible actuellement.");
            return;
        }

        info.ReplyToCommand($" {ChatColors.Blue}[PluginInvisible] Joueurs invisibles :");
        foreach (var playerName in invisibleList)
        {
            info.ReplyToCommand($" {ChatColors.Green}- {playerName}");
        }
    }

    private void HandleInvisibleCommand(CCSPlayerController? caller, string targetName)
    {
        var target = Utilities.FindPlayers(targetName).FirstOrDefault();

        if (target == null)
        {
            ReplyToCommand(caller, $" {ChatColors.Red}[PluginInvisible] Joueur introuvable : {targetName}");
            return;
        }

        if (!target.PawnIsAlive)
        {
            ReplyToCommand(caller, $" {ChatColors.Red}[PluginInvisible] Le joueur doit être vivant pour utiliser cette commande.");
            return;
        }

        ulong steamId = target.SteamID;

        if (invisiblePlayers.ContainsKey(steamId) && invisiblePlayers[steamId])
        {
            // Désactiver l'invisibilité
            SetPlayerInvisible(target, false);
            invisiblePlayers[steamId] = false;
            Server.PrintToChatAll($" {ChatColors.Blue}[PluginInvisible] {ChatColors.Green}{target.PlayerName} {ChatColors.Default}est maintenant visible.");
        }
        else
        {
            // Activer l'invisibilité
            SetPlayerInvisible(target, true);
            invisiblePlayers[steamId] = true;
            Server.PrintToChatAll($" {ChatColors.Blue}[PluginInvisible] {ChatColors.Green}{target.PlayerName} {ChatColors.Default}est maintenant invisible.");
        }
    }

    private void SetPlayerInvisible(CCSPlayerController player, bool invisible)
    {
        if (player.PlayerPawn.Value == null) return;

        // On rend l'entité invisible ou visible
        player.PlayerPawn.Value.Render = invisible ? 0 : 1;
    }

    private void ReplyToCommand(CCSPlayerController? caller, string message)
    {
        if (caller == null)
        {
            Console.WriteLine(message.Replace(ChatColors.Red, "").Replace(ChatColors.Blue, "").Replace(ChatColors.Green, "").Replace(ChatColors.Default, ""));
        }
        else
        {
            caller.PrintToChat(message);
        }
    }
}
