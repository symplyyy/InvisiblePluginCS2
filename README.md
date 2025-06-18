# 🔍 Plugin Invisible CS2

Un plugin avancé d'invisibilité pour Counter-Strike 2 utilisant CounterStrikeSharp, offrant une expérience d'invisibilité avec détection de bruit et interface utilisateur intuitive.

## ✨ Fonctionnalités

### 🎮 Invisibilité Intelligente
- **Invisibilité complète** : Les joueurs deviennent totalement invisibles (corps, armes, ombres)
- **Détection de bruit** : Révélation temporaire lors d'actions bruyantes
- **Persistance après mort** : Conservation du statut d'invisibilité au respawn
- **Masquage réseau optimisé** : Utilise `CheckTransmit` pour des performances maximales

### 🔊 Détection de Sons
- **Tirs d'armes** : Visibilité temporaire lors des tirs
- **Pas et course** : Détection des mouvements rapides
- **Atterrissages** : Sauts depuis une hauteur suffisante
- **Rechargement** : Courte visibilité lors du rechargement
- **Zoom de sniper** : Visibilité lors de l'utilisation du scope

### 💣 Objets Masqués
- **Bombe C4** : Modèle et lumière clignotante invisibles
- **Kit de désamorçage** : Fils et multimètre masqués pendant l'utilisation
- **Toutes les armes** : Invisibilité complète de l'arsenal

### 📊 Interface Utilisateur
- **Barre de progression ASCII** : Indicateur visuel du temps restant
- **Zone de texte verrouillée** : Affichage exclusif pendant la progression
- **Caractères esthétiques** : `[■■■■■·····]` pour un rendu moderne

## 🚀 Installation

### Prérequis
- **Counter-Strike 2** avec serveur dédié
- **[CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp)** v1.0.0+
- **Permissions d'administration** sur le serveur

### Étapes d'installation

1. **Télécharger le plugin**
   ```bash
   git clone https://github.com/votrecompte/InvisiblePluginCS2.git
   ```

2. **Compiler le projet**
   ```bash
   cd InvisiblePluginCS2
   dotnet build --configuration Release
   ```

3. **Installer sur le serveur**
   ```bash
   # Copier les fichiers compilés vers votre serveur CS2
   cp bin/Release/net8.0/* /path/to/cs2/game/csgo/addons/counterstrikesharp/plugins/PluginInvisible/
   ```

4. **Redémarrer le serveur**
   ```bash
   # Redémarrer votre serveur CS2 ou utiliser css_plugins reload
   ```

## ⚙️ Configuration

### Permissions
Ajoutez ces permissions dans votre fichier `admin_groups.cfg` :

```json
{
  "Moderators": {
    "flags": ["@css/invisible"]
  },
  "Admins": {
    "flags": ["@css/root"]
  }
}
```

### Paramètres ajustables
Dans `PluginInvisible.cs`, vous pouvez modifier :

```csharp
private const float RUN_SPEED_THRESHOLD = 200.0f;     // Seuil de vitesse pour la course
private const float FALL_DAMAGE_THRESHOLD = 44f;      // Hauteur de chute pour faire du bruit
```

## 🎮 Utilisation

### Commandes Principales

| Commande | Description | Permissions |
|----------|-------------|-------------|
| `css_inv` | Rendre invisible/visible soi-même | `@css/invisible` |
| `css_inv <joueur>` | Rendre invisible/visible un joueur | `@css/invisible` |
| `invisible <joueur>` | Commande alternative | `@css/invisible` |
| `listinvisible` | Lister les joueurs invisibles | `@css/invisible` |

### Exemples d'utilisation

```bash
# Se rendre invisible
css_inv

# Rendre invisible un joueur spécifique
css_inv PlayerName

# Voir tous les joueurs invisibles
listinvisible
```

## 🔧 Fonctionnement Technique

### Système CheckTransmit
Le plugin utilise la méthode `CheckTransmit` pour masquer les entités au niveau réseau :
- Performance optimale
- Masquage complet (joueur + armes + objets)
- Compatible avec tous les modes de jeu

### Détection de Bruit
```csharp
// Détection par vélocité d'impact
if (verticalVelocity > 200f) {
    HandlePlayerSound(player, "atterrissage");
}

// Détection par distance de chute
if (fallDistance > FALL_DAMAGE_THRESHOLD) {
    HandlePlayerSound(player, "atterrissage");
}
```

### Barre de Progression
```csharp
private string CreateProgressBar(float timeRemaining, float totalDuration, int barLength = 10)
{
    float progress = Math.Clamp(timeRemaining / totalDuration, 0f, 1f);
    int filled = (int)(progress * barLength);
    int empty = barLength - filled;

    string filledBar = new string('■', filled);
    string emptyBar = new string('·', empty);
    
    return $"[{filledBar}{emptyBar}]";
}
```

## 📋 API Publique

### Méthodes Disponibles

```csharp
// Vérifier si on peut afficher dans la zone centrale
public bool CanPrintToCenter(CCSPlayerController player)

// Afficher en respectant le verrouillage
public void TryPrintToCenter(CCSPlayerController player, string message)
```

### Événements
Le plugin gère automatiquement :
- `EventPlayerFootstep` : Détection des pas
- `EventWeaponFire` : Tirs d'armes
- `EventWeaponReload` : Rechargement
- `EventBombBegindefuse` : Début de désamorçage
- `EventPlayerDeath` : Gestion des morts
- `EventPlayerSpawn` : Restauration au respawn

## 🐛 Dépannage

### Problèmes Courants

**Le joueur reste visible après un son**
```bash
# Vérifier les logs du serveur
tail -f logs/counterstrikesharp.log | grep "Son détecté"
```

**La barre de progression ne s'affiche pas**
```bash
# Vérifier que PrintToCenter fonctionne
css_say test
```

**Permissions non reconnues**
```bash
# Recharger les permissions
css_admins_reload
```

### Logs de Debug
Le plugin affiche des informations détaillées dans la console serveur :
```
Son détecté pour PlayerName: atterrissage
Atterrissage avec impact détecté pour PlayerName (vélocité: 250.3)
```

**Développé avec ❤️ pour la communauté Counter-Strike 2**

*Version 1.2.2 - Dernière mise à jour : Juin 2025* 
