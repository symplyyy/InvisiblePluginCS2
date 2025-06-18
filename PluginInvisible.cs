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

    private readonly List<int> InvisiblePlayerSlots = new List<int>();
    private readonly Dictionary<int, bool> PlayerMakingSound = new Dictionary<int, bool>();
    private readonly Dictionary<int, bool> PlayerThirdPerson = new Dictionary<int, bool>();
    private readonly List<int> InvisiblePlayerSlotsPersistent = new List<int>(); // Garde le statut même après la mort
    private readonly Dictionary<int, CBaseEntity> DefuseKitsInUse = new Dictionary<int, CBaseEntity>(); // Tracking des kits de désamorçage
    private readonly Dictionary<int, bool> PlayerWasInAir = new Dictionary<int, bool>(); // Tracking des joueurs en l'air
    private readonly Dictionary<int, float> PlayerFallStartHeight = new Dictionary<int, float>(); // Hauteur de début de chute
    private readonly Dictionary<int, bool> PlayerCenterTextLocked = new Dictionary<int, bool>(); // Verrouillage de la zone de texte centrale
    private const float RUN_SPEED_THRESHOLD = 200.0f; // Vitesse de course ajustée
    private const float FALL_DAMAGE_THRESHOLD = 44f; // Hauteur de chute pour faire du bruit (en unités) - très sensible pour capturer les sauts sur place

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnTick>(() => OnTick());
        RegisterEventHandler<EventPlayerFootstep>(OnPlayerFootstep);
        RegisterEventHandler<EventWeaponFire>(OnWeaponFire);
        RegisterEventHandler<EventWeaponReload>(OnWeaponReload);
        RegisterEventHandler<EventItemPickup>(OnItemPickup);
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventWeaponZoom>(OnWeaponZoom);

        // CheckTransmit pour masquer complètement les joueurs invisibles et leurs objets
        RegisterListener<Listeners.CheckTransmit>(OnCheckTransmit);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath, HookMode.Pre);
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        RegisterEventHandler<EventBombBegindefuse>(OnBombBeginDefuse);
        RegisterEventHandler<EventBombAbortdefuse>(OnBombAbortDefuse);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);

        // Désactiver le radar au chargement du plugin
        Server.ExecuteCommand("mp_radar_showall 0");
        Server.ExecuteCommand("sv_disable_radar 1");
    }

    private void OnCheckTransmit(CCheckTransmitInfoList infoList)
    {
        var players = Utilities.GetPlayers();
        if (!players.Any()) return;

        foreach ((CCheckTransmitInfo info, CCSPlayerController? player) in infoList)
        {
            if (player == null || !player.IsValid) continue;

            // Masquer les joueurs invisibles qui ne font pas de bruit
            var hiddenPlayers = players.Where(p => 
                p.IsValid && 
                p.PawnIsAlive && 
                p.Slot != player.Slot && 
                InvisiblePlayerSlots.Contains(p.Slot) && 
                (!PlayerMakingSound.ContainsKey(p.Slot) || !PlayerMakingSound[p.Slot])
            );

            foreach (var hiddenPlayer in hiddenPlayers)
            {
                // Masquer le joueur
                info.TransmitEntities.Remove((int)hiddenPlayer.Pawn.Index);

                // Masquer toutes ses armes (y compris la bombe)
                var pawn = hiddenPlayer.PlayerPawn.Value;
                if (pawn?.WeaponServices?.MyWeapons != null)
                {
                    foreach (var weaponHandle in pawn.WeaponServices.MyWeapons)
                    {
                        var weapon = weaponHandle.Value;
                        if (weapon != null && weapon.IsValid)
                        {
                            info.TransmitEntities.Remove((int)weapon.Index);
                        }
                    }
                }

                // Masquer le kit de désamorçage si le joueur en utilise un
                if (DefuseKitsInUse.ContainsKey(hiddenPlayer.Slot))
                {
                    var defuseKit = DefuseKitsInUse[hiddenPlayer.Slot];
                    if (defuseKit != null && defuseKit.IsValid)
                    {
                        info.TransmitEntities.Remove((int)defuseKit.Index);
                    }
                }
            }
        }
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;

        // Ne plus supprimer le joueur de la liste des invisibles à la mort
        // Seulement nettoyer le son
        if (PlayerMakingSound.ContainsKey(player.Slot))
        {
            PlayerMakingSound[player.Slot] = false;
        }
        
        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;

        // Restaurer l'invisibilité si le joueur était invisible avant de mourir
        if (InvisiblePlayerSlotsPersistent.Contains(player.Slot) && 
            !InvisiblePlayerSlots.Contains(player.Slot))
        {
            InvisiblePlayerSlots.Add(player.Slot);
        }

        return HookResult.Continue;
    }

    private HookResult OnBombBeginDefuse(EventBombBegindefuse @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;

        // Approche alternative : chercher toutes les entités avec le modèle defuse_multimeter
        AddTimer(0.1f, () =>
        {
            var entities = Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("prop_dynamic");
            foreach (var entity in entities)
            {
                if (entity != null && entity.IsValid)
                {
                    // Vérifier si c'est un kit de désamorçage basé sur la position près du joueur
                    var playerPos = player.PlayerPawn?.Value?.AbsOrigin;
                    var entityPos = entity.AbsOrigin;
                    
                    if (playerPos != null && entityPos != null)
                    {
                        float distance = (float)Math.Sqrt(
                            Math.Pow(playerPos.X - entityPos.X, 2) +
                            Math.Pow(playerPos.Y - entityPos.Y, 2) +
                            Math.Pow(playerPos.Z - entityPos.Z, 2)
                        );
                        
                        // Si l'entité est proche du joueur (dans un rayon de 100 unités)
                        if (distance < 100f)
                        {
                            DefuseKitsInUse[player.Slot] = entity;
                            break;
                        }
                    }
                }
            }
        });

        return HookResult.Continue;
    }

    private HookResult OnBombAbortDefuse(EventBombAbortdefuse @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;

        // Retirer le kit de désamorçage de la liste
        if (DefuseKitsInUse.ContainsKey(player.Slot))
        {
            DefuseKitsInUse.Remove(player.Slot);
        }

        return HookResult.Continue;
    }

    private void OnClientDisconnect(int slot)
    {
        if (InvisiblePlayerSlots.Contains(slot))
        {
            InvisiblePlayerSlots.Remove(slot);
        }
        if (InvisiblePlayerSlotsPersistent.Contains(slot))
        {
            InvisiblePlayerSlotsPersistent.Remove(slot);
        }
        if (PlayerMakingSound.ContainsKey(slot))
        {
            PlayerMakingSound.Remove(slot);
        }
        if (PlayerThirdPerson.ContainsKey(slot))
        {
            PlayerThirdPerson.Remove(slot);
        }
        if (DefuseKitsInUse.ContainsKey(slot))
        {
            DefuseKitsInUse.Remove(slot);
        }
        if (PlayerWasInAir.ContainsKey(slot))
        {
            PlayerWasInAir.Remove(slot);
        }
        if (PlayerFallStartHeight.ContainsKey(slot))
        {
            PlayerFallStartHeight.Remove(slot);
        }
        if (PlayerCenterTextLocked.ContainsKey(slot))
        {
            PlayerCenterTextLocked.Remove(slot);
        }
    }

    private void UnhidePlayer(CCSPlayerController player)
    {
        if (InvisiblePlayerSlots.Contains(player.Slot))
        {
            InvisiblePlayerSlots.Remove(player.Slot);
        }
        if (PlayerMakingSound.ContainsKey(player.Slot))
        {
            PlayerMakingSound.Remove(player.Slot);
        }
    }

    private void OnTick()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid || !player.PawnIsAlive) continue;
            if (!InvisiblePlayerSlots.Contains(player.Slot)) continue;

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

            // Vérifier les atterrissages de saut
            CheckFallLanding(player, pawn);
        }
    }

    private void CheckFallLanding(CCSPlayerController player, CCSPlayerPawn pawn)
    {
        bool isOnGround = (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0;
        float currentHeight = pawn.AbsOrigin?.Z ?? 0f;
        bool wasInAir = PlayerWasInAir.ContainsKey(player.Slot) && PlayerWasInAir[player.Slot];
        float verticalVelocity = Math.Abs(pawn.AbsVelocity?.Z ?? 0f);

        if (!isOnGround && !wasInAir)
        {
            // Le joueur vient de quitter le sol (début de saut/chute)
            PlayerWasInAir[player.Slot] = true;
            PlayerFallStartHeight[player.Slot] = currentHeight;
            Server.PrintToConsole($"Joueur {player.PlayerName} a quitté le sol à la hauteur {currentHeight:F1}");
        }
        else if (isOnGround && wasInAir)
        {
            // Le joueur vient d'atterrir
            PlayerWasInAir[player.Slot] = false;
            
            // Détection basée sur la vélocité verticale lors de l'atterrissage
            if (verticalVelocity > 200f) // Vélocité d'impact significative
            {
                Server.PrintToConsole($"Atterrissage avec impact détecté pour {player.PlayerName} (vélocité: {verticalVelocity:F1})");
                HandlePlayerSound(player, "atterrissage");
            }
            else if (PlayerFallStartHeight.ContainsKey(player.Slot))
            {
                float fallDistance = PlayerFallStartHeight[player.Slot] - currentHeight;
                
                // Debug: Afficher la distance de chute
                Server.PrintToConsole($"Joueur {player.PlayerName} - Distance de chute: {fallDistance:F1} unités, vélocité: {verticalVelocity:F1}");
                
                // Si la chute est assez haute pour faire du bruit
                if (fallDistance > FALL_DAMAGE_THRESHOLD)
                {
                    Server.PrintToConsole($"Atterrissage bruyant détecté pour {player.PlayerName} ({fallDistance:F1} unités)");
                    HandlePlayerSound(player, "atterrissage");
                }
            }
            
            if (PlayerFallStartHeight.ContainsKey(player.Slot))
            {
                PlayerFallStartHeight.Remove(player.Slot);
            }
        }
        else if (!isOnGround && wasInAir)
        {
            // Le joueur est toujours en l'air, mettre à jour la hauteur max si nécessaire
            if (PlayerFallStartHeight.ContainsKey(player.Slot) && currentHeight > PlayerFallStartHeight[player.Slot])
            {
                PlayerFallStartHeight[player.Slot] = currentHeight;
            }
        }
    }

    private HookResult OnPlayerFootstep(EventPlayerFootstep @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && InvisiblePlayerSlots.Contains(player.Slot))
        {
            HandlePlayerSound(player, "pas");
        }
        return HookResult.Continue;
    }

    private HookResult OnWeaponFire(EventWeaponFire @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && InvisiblePlayerSlots.Contains(player.Slot))
        {
            HandlePlayerSound(player, "tir");
        }
        return HookResult.Continue;
    }

    private HookResult OnWeaponReload(EventWeaponReload @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && InvisiblePlayerSlots.Contains(player.Slot))
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
        if (player != null && InvisiblePlayerSlots.Contains(player.Slot))
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

        if (InvisiblePlayerSlots.Contains(targetPlayer.Slot))
        {
            InvisiblePlayerSlots.Remove(targetPlayer.Slot);
            InvisiblePlayerSlotsPersistent.Remove(targetPlayer.Slot);
            Server.PrintToChatAll($" {ChatColors.Red}[Invisible]{ChatColors.Default} {targetPlayer.PlayerName} est maintenant visible.");
        }
        else
        {
            InvisiblePlayerSlots.Add(targetPlayer.Slot);
            InvisiblePlayerSlotsPersistent.Add(targetPlayer.Slot);
            Server.PrintToChatAll($" {ChatColors.Red}[Invisible]{ChatColors.Default} {targetPlayer.PlayerName} est maintenant invisible.");
        }
    }

    private string CreateProgressBar(float timeRemaining, float totalDuration, int barLength = 10)
    {
        float progress = Math.Clamp(timeRemaining / totalDuration, 0f, 1f);
        int filled = (int)(progress * barLength);
        int empty = barLength - filled;

        string filledBar = new string('■', filled);   // ou '█'
        string emptyBar = new string('·', empty);     // ou '░'
        
        return $"[{filledBar}{emptyBar}]";
    }

        private void HandlePlayerSoundWithDuration(CCSPlayerController player, string soundType, float duration)
    {
        if (!PlayerMakingSound.ContainsKey(player.Slot) || !PlayerMakingSound[player.Slot])
        {
            PlayerMakingSound[player.Slot] = true;
            PlayerCenterTextLocked[player.Slot] = true; // Verrouiller la zone de texte

            // Debug: Afficher le type de son (uniquement visible dans la console serveur)
            Server.PrintToConsole($"Son détecté pour {player.PlayerName}: {soundType}");

            // Variables pour le timer
            float startTime = Server.CurrentTime;
            float updateInterval = 0.1f;

            void UpdateProgressBar()
            {
                if (player != null && player.IsValid && player.PawnIsAlive && 
                    PlayerMakingSound.ContainsKey(player.Slot) && PlayerMakingSound[player.Slot])
                {
                    float elapsed = Server.CurrentTime - startTime;
                    float timeRemaining = duration - elapsed;
                    
                    if (timeRemaining > 0)
                    {
                        string progressBar = CreateProgressBar(timeRemaining, duration);
                        
                        // S'assurer qu'on affiche seulement si le joueur est encore dans le processus
                        if (PlayerMakingSound.ContainsKey(player.Slot) && PlayerMakingSound[player.Slot])
                        {
                            SafePrintToCenter(player, progressBar);
                            // Programmer la prochaine mise à jour
                            AddTimer(updateInterval, UpdateProgressBar);
                        }
                    }
                    else
                    {
                        // Temps écoulé, nettoyer
                        if (PlayerMakingSound.ContainsKey(player.Slot))
                        {
                            PlayerMakingSound[player.Slot] = false;
                        }
                        // Déverrouiller et nettoyer l'affichage
                        UnlockAndClearCenterText(player);
                    }
                }
            }

            // Démarrer immédiatement avec une barre pleine
            string initialBar = CreateProgressBar(duration, duration);
            SafePrintToCenter(player, initialBar);
            
            // Démarrer les mises à jour
            AddTimer(updateInterval, UpdateProgressBar);

            // Timer de sécurité pour s'assurer que le joueur redevient invisible
            AddTimer(duration + 0.2f, () =>
            {
                if (PlayerMakingSound.ContainsKey(player.Slot))
                {
                    PlayerMakingSound[player.Slot] = false;
                }
                UnlockAndClearCenterText(player);
            });
        }
    }

    private void SafePrintToCenter(CCSPlayerController player, string message)
    {
        // N'affiche que si la zone de texte est verrouillée pour la barre de progression
        if (player != null && player.IsValid && player.PawnIsAlive && 
            PlayerCenterTextLocked.ContainsKey(player.Slot) && PlayerCenterTextLocked[player.Slot])
        {
            player.PrintToCenter(message);
        }
    }

    private void UnlockAndClearCenterText(CCSPlayerController player)
    {
        if (player != null && player.IsValid && player.PawnIsAlive)
        {
            PlayerCenterTextLocked[player.Slot] = false; // Déverrouiller
            AddTimer(0.1f, () => {
                if (player != null && player.IsValid && player.PawnIsAlive)
                {
                    player.PrintToCenter(" ");
                    AddTimer(0.1f, () => {
                        if (player != null && player.IsValid && player.PawnIsAlive)
                        {
                            player.PrintToCenter("");
                        }
                    });
                }
            });
        }
    }

    // Méthode publique pour vérifier si on peut afficher dans la zone centrale
    public bool CanPrintToCenter(CCSPlayerController player)
    {
        return player != null && player.IsValid && 
               (!PlayerCenterTextLocked.ContainsKey(player.Slot) || !PlayerCenterTextLocked[player.Slot]);
    }

    // Méthode publique pour afficher en respectant le verrouillage
    public void TryPrintToCenter(CCSPlayerController player, string message)
    {
        if (CanPrintToCenter(player))
        {
            player.PrintToCenter(message);
        }
    }

    private void HandlePlayerSound(CCSPlayerController player, string soundType)
    {
        // Utiliser la durée par défaut de 0.5 secondes pour les autres sons
        HandlePlayerSoundWithDuration(player, soundType, 0.5f);
    }



    private HookResult OnItemPickup(EventItemPickup @event, GameEventInfo info)
    {
        // Plus besoin de faire quoi que ce soit ici avec la nouvelle approche CheckTransmit
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

        var invisibleList = Utilities.GetPlayers()
            .Where(p => p != null && p.IsValid && InvisiblePlayerSlots.Contains(p.Slot))
            .Select(p => p.PlayerName)
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

        if (InvisiblePlayerSlots.Contains(target.Slot))
        {
            InvisiblePlayerSlots.Remove(target.Slot);
            InvisiblePlayerSlotsPersistent.Remove(target.Slot);
            Server.PrintToChatAll($"[PluginInvisible] {target.PlayerName} est maintenant visible.");
        }
        else
        {
            InvisiblePlayerSlots.Add(target.Slot);
            InvisiblePlayerSlotsPersistent.Add(target.Slot);
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