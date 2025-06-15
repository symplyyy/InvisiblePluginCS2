using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using System.Drawing;
using static CounterStrikeSharp.API.Core.Listeners;

namespace PluginInvisible;

public class PluginInvisible : BasePlugin
{
    public override string ModuleName => "PluginInvisible";
    public override string ModuleVersion => "1.2.2";

    private List<CCSPlayerController> InvisiblePlayers = new List<CCSPlayerController>();
    private Dictionary<CCSPlayerController, bool> PlayerMakingSound = new Dictionary<CCSPlayerController, bool>();
    private Dictionary<CCSPlayerController, bool> PlayerThirdPerson = new Dictionary<CCSPlayerController, bool>();
    private const float RUN_SPEED_THRESHOLD = 200.0f; // Vitesse de course ajustée

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnTick>(() => OnTick());
        RegisterEventHandler<EventPlayerFootstep>(OnPlayerFootstep);
        RegisterEventHandler<EventWeaponFire>(OnWeaponFire);
        RegisterEventHandler<EventPlayerJump>(OnPlayerJump);
        RegisterEventHandler<EventWeaponReload>(OnWeaponReload);
        RegisterEventHandler<EventItemPickup>(OnItemPickup);
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventWeaponZoom>(OnWeaponZoom);

        // Désactiver le radar au chargement du plugin
        Server.ExecuteCommand("mp_radar_showall 0");
        Server.ExecuteCommand("sv_disable_radar 1");
    }

    private void OnTick()
    {
        foreach (var player in InvisiblePlayers.ToList())
        {
            if (player == null || !player.IsValid || !player.PawnIsAlive) continue;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null) continue;

            // Vérifier les mouvements rapides (course uniquement)
            float velocity = (float)Math.Sqrt(
                pawn.AbsVelocity.X * pawn.AbsVelocity.X +
                pawn.AbsVelocity.Y * pawn.AbsVelocity.Y
            ); // On ignore la composante Z pour ne pas compter les sauts

            if (velocity > RUN_SPEED_THRESHOLD)
            {
                HandlePlayerSound(player, "course");
            }
        }
    }

    private HookResult OnPlayerFootstep(EventPlayerFootstep @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && InvisiblePlayers.Contains(player))
        {
            HandlePlayerSound(player, "pas");
        }
        return HookResult.Continue;
    }

    private HookResult OnWeaponFire(EventWeaponFire @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && InvisiblePlayers.Contains(player))
        {
            HandlePlayerSound(player, "tir");
        }
        return HookResult.Continue;
    }

    private HookResult OnPlayerJump(EventPlayerJump @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && InvisiblePlayers.Contains(player))
        {
            HandlePlayerSound(player, "saut");
        }
        return HookResult.Continue;
    }

    private HookResult OnWeaponReload(EventWeaponReload @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && InvisiblePlayers.Contains(player))
        {
            // Durée réduite à 0.2 secondes pour le rechargement
            HandlePlayerSoundWithDuration(player, "rechargement", 0.2f);
        }
        return HookResult.Continue;
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        // S'assurer que le radar reste désactivé au début de chaque round
        Server.ExecuteCommand("mp_radar_showall 0");
        Server.ExecuteCommand("sv_disable_radar 1");
        return HookResult.Continue;
    }

    private HookResult OnWeaponZoom(EventWeaponZoom @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && InvisiblePlayers.Contains(player))
        {
            // Durée réduite à 0.2 secondes pour le zoom
            HandlePlayerSoundWithDuration(player, "zoom", 0.2f);
        }
        return HookResult.Continue;
    }

    private bool HasInvisiblePermission(CCSPlayerController? player)
    {
        if (player == null) return false;
        return AdminManager.PlayerHasPermissions(player, "@css/invisible") || AdminManager.PlayerHasPermissions(player, "@css/root");
    }

    [ConsoleCommand("css_inv", "Rend un joueur invisible")]
    public void OnInvisibleCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (caller == null || !caller.IsValid || !caller.PawnIsAlive)
        {
            return;
        }

        string targetName = command.ArgString.Trim();
        CCSPlayerController targetPlayer;

        if (string.IsNullOrEmpty(targetName))
        {
            // Si pas de nom spécifié, on cible le joueur qui a tapé la commande
            targetPlayer = caller;
        }
        else
        {
            // Chercher le joueur ciblé
            targetPlayer = null;
            foreach (var player in Utilities.GetPlayers())
            {
                if (player != null && player.PlayerName.Contains(targetName, StringComparison.OrdinalIgnoreCase))
                {
                    targetPlayer = player;
                    break;
                }
            }

            if (targetPlayer == null)
            {
                caller.PrintToChat($" {ChatColors.Red}[Invisible]{ChatColors.Default} Joueur non trouvé : {targetName}");
                return;
            }
        }

        if (!targetPlayer.PawnIsAlive)
        {
            caller.PrintToChat($" {ChatColors.Red}[Invisible]{ChatColors.Default} Le joueur doit être vivant.");
            return;
        }

        if (InvisiblePlayers.Contains(targetPlayer))
        {
            SetPlayerVisible(targetPlayer);
            InvisiblePlayers.Remove(targetPlayer);
            Server.PrintToChatAll($" {ChatColors.Red}[Invisible]{ChatColors.Default} {targetPlayer.PlayerName} est maintenant visible.");
        }
        else
        {
            SetPlayerInvisible(targetPlayer);
            InvisiblePlayers.Add(targetPlayer);
            Server.PrintToChatAll($" {ChatColors.Red}[Invisible]{ChatColors.Default} {targetPlayer.PlayerName} est maintenant invisible.");
        }
    }

    private void HandlePlayerSoundWithDuration(CCSPlayerController player, string soundType, float duration)
    {
        if (!PlayerMakingSound.ContainsKey(player) || !PlayerMakingSound[player])
        {
            PlayerMakingSound[player] = true;

            // Debug: Afficher le type de son (uniquement visible dans la console serveur)
            Server.PrintToConsole($"Son détecté pour {player.PlayerName}: {soundType}");

            // Rendre le joueur visible temporairement
            SetPlayerVisible(player);

            // Afficher le timer initial
            player.PrintToCenter($"{duration:0.00}");

            // Mettre à jour le timer toutes les 0.25 secondes
            float remainingTime = duration;
            while (remainingTime > 0)
            {
                float currentTime = remainingTime;
                AddTimer(duration - remainingTime, () =>
                {
                    if (player != null && player.IsValid && player.PawnIsAlive)
                    {
                        player.PrintToCenter($"{currentTime:0.00}");
                    }
                });
                remainingTime -= 0.25f;
            }

            // Programmer le retour à l'invisibilité
            AddTimer(duration, () =>
            {
                if (InvisiblePlayers.Contains(player))
                {
                    SetPlayerInvisible(player);
                    PlayerMakingSound[player] = false;
                    player.PrintToCenter("");
                }
            });
        }
    }

    private void HandlePlayerSound(CCSPlayerController player, string soundType)
    {
        // Utiliser la durée par défaut de 0.5 secondes pour les autres sons
        HandlePlayerSoundWithDuration(player, soundType, 0.5f);
    }

    private void SetPlayerVisible(CCSPlayerController player)
    {
        if (player == null || !player.IsValid || !player.PlayerPawn.IsValid) return;

        var pawn = player.PlayerPawn.Value;
        if (pawn == null) return;

        // Rendre le joueur visible
        pawn.Render = Color.FromArgb(255, 255, 255, 255);
        Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");

        // Réactiver la visibilité du modèle
        pawn.RenderMode = RenderMode_t.kRenderNormal;
        Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_nRenderMode");

        // Rendre les armes visibles
        var activeWeapon = pawn.WeaponServices?.ActiveWeapon.Value;
        if (activeWeapon != null && activeWeapon.IsValid)
        {
            activeWeapon.Render = Color.FromArgb(255, 255, 255, 255);
            activeWeapon.ShadowStrength = 1.0f;
            Utilities.SetStateChanged(activeWeapon, "CBaseModelEntity", "m_clrRender");
        }

        // Rendre toutes les armes visibles
        var myWeapons = pawn.WeaponServices?.MyWeapons;
        if (myWeapons != null)
        {
            foreach (var gun in myWeapons)
            {
                var weapon = gun.Value;
                if (weapon != null)
                {
                    weapon.Render = Color.FromArgb(255, 255, 255, 255);
                    weapon.ShadowStrength = 1.0f;
                    Utilities.SetStateChanged(weapon, "CBaseModelEntity", "m_clrRender");
                }
            }
        }
    }

    private void SetPlayerInvisible(CCSPlayerController player)
    {
        if (player == null || !player.IsValid || !player.PlayerPawn.IsValid) return;

        var pawn = player.PlayerPawn.Value;
        if (pawn == null) return;

        // Rendre le joueur invisible
        pawn.Render = Color.FromArgb(0, 255, 255, 255);
        Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");

        // Désactiver la visibilité du modèle
        pawn.RenderMode = RenderMode_t.kRenderTransColor;
        Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_nRenderMode");

        // Rendre l'arme active invisible
        var activeWeapon = pawn.WeaponServices?.ActiveWeapon.Value;
        if (activeWeapon != null && activeWeapon.IsValid)
        {
            activeWeapon.Render = Color.FromArgb(0, 255, 255, 255);
            activeWeapon.ShadowStrength = 0.0f;
            Utilities.SetStateChanged(activeWeapon, "CBaseModelEntity", "m_clrRender");
        }

        // Rendre toutes les armes invisibles
        var myWeapons = pawn.WeaponServices?.MyWeapons;
        if (myWeapons != null)
        {
            foreach (var gun in myWeapons)
            {
                var weapon = gun.Value;
                if (weapon != null)
                {
                    weapon.Render = Color.FromArgb(0, 255, 255, 255);
                    weapon.ShadowStrength = 0.0f;
                    Utilities.SetStateChanged(weapon, "CBaseModelEntity", "m_clrRender");
                }
            }
        }
    }

    private HookResult OnItemPickup(EventItemPickup @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && InvisiblePlayers.Contains(player))
        {
            SetPlayerInvisible(player);
        }
        return HookResult.Continue;
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

        var invisibleList = InvisiblePlayers.Select(p => p.PlayerName).ToList();

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

        if (InvisiblePlayers.Contains(target))
        {
            SetPlayerVisible(target);
            InvisiblePlayers.Remove(target);
            Server.PrintToChatAll($"[PluginInvisible] {target.PlayerName} est maintenant visible.");
        }
        else
        {
            SetPlayerInvisible(target);
            InvisiblePlayers.Add(target);
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
} 