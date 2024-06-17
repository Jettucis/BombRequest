using System.Linq;
using System.Diagnostics.CodeAnalysis;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace BombRequest;

public class BombRequest : BasePlugin
{
  public override string ModuleName => "Bomb Request";
  public override string ModuleAuthor => "Jetta";
  public override string ModuleDescription => "Allows players to be the bomb carrier on the next round";
  public override string ModuleVersion => "0.0.2a";

  private List<CCSPlayerController> _potentialPlayers = new List<CCSPlayerController>();
  private bool _canUseRB = false;
  private bool _isWarmup = false;

  public override void Load(bool hotReload)
  {
    RegisterHooks();
    RegisterListener<Listeners.OnMapStart>(x =>
    {
      Server.NextFrame(() => {
        _isWarmup = true;
        _potentialPlayers.Clear();
      });
    });
    Console.WriteLine("[BombRequest] Plugin has been loaded!");
  }

  void RegisterHooks()
  {
    RegisterEventHandler<EventWarmupEnd>(OnWarmupEnd);
    RegisterEventHandler<EventRoundFreezeEnd>(OnFreezeEnd);
    RegisterEventHandler<EventRoundPrestart>(OnRoundPrestart);
    RegisterEventHandler<EventRoundStart>(OnRoundStart);
    RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
  }
  // ------------------- Hooks ------------------- //
  public HookResult OnWarmupEnd(EventWarmupEnd @event, GameEventInfo info)
  {
    _isWarmup = false;
    return HookResult.Continue;
  }
  public HookResult OnFreezeEnd(EventRoundFreezeEnd @event, GameEventInfo info)
  {
    if (_isWarmup) return HookResult.Continue;

    _canUseRB = true;
    return HookResult.Continue;
  }
  public HookResult OnRoundPrestart(EventRoundPrestart @event, GameEventInfo info)
  {
    if (_isWarmup) return HookResult.Continue;

    _canUseRB = false;
    return HookResult.Continue;
  }
  public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
  {
    if (_isWarmup) return HookResult.Continue;

    AddTimer(1.0f, () => {
      RBOnRoundStart();
    });
    return HookResult.Continue;
  }
  public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
  {
    if (_isWarmup) return HookResult.Continue;
    if (@event.Userid == null || @event.Userid.IsBot || @event.Userid.IsHLTV || @event.Userid.SteamID!.ToString().Length != 17) return HookResult.Continue;

    _potentialPlayers.RemoveAll(gamer => !gamer.IsConnected());
    return HookResult.Continue;
  }
  // ------------------- Functionality ------------------- //
  private void RBOnRoundStart()
  {
    if (_potentialPlayers.Count == 0) return;
    // Did check already, just give weapon_c4 won't work, there will be two bomb's then (sadly) :D
    CCSPlayerController? targetPlayer = GetRandomPlayer();
    if (targetPlayer == null) return;

    CCSPlayerController? bombCarrier = CheckWhoHasBomb();
    if (bombCarrier == targetPlayer)
    {
      TextToChat(targetPlayer, $"{ChatColors.Lime} Now that's a coincidence. You got the bomb already. Removing you from the RB list.");
      RBOnRoundStart_Cleanup(targetPlayer);
      return;
    }

    if (bombCarrier == null)
    {
      RemoveBombFromGround();
    }
    else
    {
      RemoveBombFromPlayer(bombCarrier);
    }

    GiveBombToPlayer(targetPlayer);
    // 0.0.1 - Don't want that the same player participating in the next round if I just keep the list, so just clear it, so everyone can write !rb again for next round
    // I know, I know, I can just store rb winners in temporary list that will auto-remove them after X amount of rounds, but I just can't CBA to do it atm. :D
    RBOnRoundStart_Cleanup(targetPlayer);
  }
  private void RBOnRoundStart_Cleanup(CCSPlayerController player)
  {
    _potentialPlayers.Remove(player);
    InformRBLoosers();
    _potentialPlayers.Clear();
  }
  private CCSPlayerController? CheckWhoHasBomb()
  {
    try
    {
      foreach (var player in _potentialPlayers)
      {
        foreach (var weapon in player?.PlayerPawn?.Value?.WeaponServices?.MyWeapons!)
        {
          if (weapon is { IsValid: true, Value.IsValid: true })
          {
            if (weapon.Value.DesignerName.Contains("c4"))
            {
              // Assuming there is only one player with the bomb
              return player;
            }
          }
        }
      }
      return null;
    }
    catch (Exception ex)
    {
      Console.WriteLine("[BombRequest] Error in CheckWhoHasBomb: " + ex.Message);
      return null;
    }
  }
  private void RemoveBombFromGround()
  {
    try
    {
      var bombs = Utilities.FindAllEntitiesByDesignerName<CCSWeaponBase>("weapon_c4");
      
      if (bombs == null) return;
      
      foreach (var entity in bombs)
      {
        if (!entity.IsValid) continue;
        if (entity.DesignerName.Contains("c4") == false) continue;
        if (entity.State == CSWeaponState_t.WEAPON_NOT_CARRIED)
        {
          entity.Remove();
        }
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine("[BombRequest] Error in RemoveBombFromGround: " + ex.Message);
    }
  }
  private void RemoveBombFromPlayer(CCSPlayerController player)
  {
    try
    {
      foreach (var weapon in player?.PlayerPawn?.Value?.WeaponServices?.MyWeapons!)
      {
        if (weapon is { IsValid: true, Value.IsValid: true })
        {
          if (weapon.Value.DesignerName.Contains("c4"))
          {
            Utilities.RemoveItemByDesignerName(player, weapon.Value.DesignerName);
            player.ExecuteClientCommand("slot3");
          }
        }
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine("[BombRequest] Error in RemoveBombFromPlayer: " + ex.Message);
    }
  }
  private void GiveBombToPlayer(CCSPlayerController player)
  {
    try
    {
      if (!player.IsLegalAliveT()) return;
      player.GiveNamedItem("weapon_c4");
      TextToChat(player, $"{ChatColors.Lime} You got the bomb. Removing you from the RB list.");
    }
    catch (Exception ex)
    {
      Console.WriteLine("[BombRequest] Error in GiveBombToPlayer: " + ex.Message);
    }
  }
  private CCSPlayerController? GetRandomPlayer()
  {
    try
    {
      if (_potentialPlayers.Count == 0) return null;
      if (_potentialPlayers.Count == 1) return _potentialPlayers[0];

      var vipPlayers = _potentialPlayers.Where(player => AdminManager.PlayerHasPermissions(player, "@css/vip")).ToList();
      bool prioritizeVip = false;

      if (vipPlayers.Count > 0) 
      {
        Random random = new Random();
        // Whether to prioritize vip's
        prioritizeVip = random.Next(2) == 0;
      }
      
      if (prioritizeVip)
      {
        if (vipPlayers.Count == 1) return vipPlayers[0];

        Random randomVip = new Random();
        int vipIndex = randomVip.Next(vipPlayers.Count);
        return vipPlayers[vipIndex];
      }
      
      Random randomGeneral = new Random();
      int generalIndex = randomGeneral.Next(_potentialPlayers.Count);
      return _potentialPlayers[generalIndex];
    }
    catch (Exception ex)
    {
      Console.WriteLine("[BombRequest] Error in GetRandomPlayer: " + ex.Message);
      return null;
    }
  }
  private void InformRBLoosers()
  {
    if (_potentialPlayers.Count == 0) return;
    foreach (var gamer in _potentialPlayers)
    {
      if (gamer.IsConnected())
      {
        TextToChat(gamer, $"{ChatColors.Gold} No luck this round. Write{ChatColors.White} !rb{ChatColors.Gold} to try again.");
      }
    }
  }
  // ------------------- Utils/Statics ------------------- //
  public void TextToChat(CCSPlayerController player, string text) {
    if (player.IsConnected())
      player.PrintToChat($" {Lib.ChatPrefix} {text}");
  }
  // ------------------- Komandas ------------------- //
  [ConsoleCommand("css_rbhelp", "Request Bomb - Info.")]
  [ConsoleCommand("css_helprb", "Request Bomb - Info.")]
  [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_ONLY)]
  public void OnRBHelpCommand(CCSPlayerController player, CommandInfo command)
  {
    if (!player.IsConnected()) return;
    TextToChat(player, $"{ChatColors.Lime} 1. Write{ChatColors.White} !rb{ChatColors.Lime} to get a chance to get the bomb on next round{ChatColors.White} (T's only)");
    TextToChat(player, $"{ChatColors.Lime} 2. Plugin will randomly (dice roll) select a player from{ChatColors.White} rb list{ChatColors.Lime} who'll receive the bomb");
    TextToChat(player, $"{ChatColors.Lime} *{ChatColors.White} VIP{ChatColors.Lime} players have 2 rolls to be selected, others have 1 roll");
    TextToChat(player, $"{ChatColors.Lime} 3. After receiving the bomb, the{ChatColors.White} rb list{ChatColors.Lime} will reset");
  }
  [ConsoleCommand("css_rb", "Request Bomb - be first to get the bomb in the next round.")]
  [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_ONLY)]
  public void OnRBCommand(CCSPlayerController player, CommandInfo command)
  {
    if (!player.IsConnected()) return;
    if (!player.IsT())
    {
      TextToChat(player, $"{ChatColors.Red} You are not terrorist.");
      return;
    }
    if (_isWarmup == true || _canUseRB == false)
    {
      TextToChat(player, $"{ChatColors.Red} Can't use{ChatColors.White} !rb{ChatColors.Red} at this moment.");
      return;
    }

    // At the end inform player about the inclusion to the list
    if (!_potentialPlayers.Contains(player))
    {
      _potentialPlayers.Add(player);
      TextToChat(player, $"{ChatColors.Lime} You have added yourself to the Request Bomb list.");
    }
    else
    {
      TextToChat(player, $"{ChatColors.LightRed} You are in the Request Bomb list already.");
    }
  }

  [ConsoleCommand("css_resetrb", "Reset RB list")]
  [ConsoleCommand("css_rbreset", "Reset RB list")]
  [RequiresPermissions("@css/ban")]
  [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_ONLY)]
  public void OnRBResetCommand(CCSPlayerController player, CommandInfo command)
  {
    if (!player.IsConnected()) return;
    if (_isWarmup)
    {
      TextToChat(player, $"{ChatColors.Red} Can't reset RB list during warmup.");
      return;
    }

    if (_potentialPlayers.Count == 0)
    {
      TextToChat(player, $"{ChatColors.Red} There are no players in Request Bomb list.");
      return;
    }
    // Inform in _potentialPlayers list that list has been reset by admin
    foreach(var gamer in _potentialPlayers)
    {
      if (gamer.IsConnected())
      {
        TextToChat(gamer, $"{ChatColors.Gold} Admin performed Request Bomb list reset, you have been removed from the list.");
      }
    }
    _potentialPlayers.Clear();
    TextToChat(player, $"{ChatColors.Lime} Request Bomb list has been reset.");
  }

  [ConsoleCommand("css_checkrb", "Check RB list")]
  [ConsoleCommand("css_rblist", "Check RB list")]
  [RequiresPermissionsOr("@css/ban", "@css/vip")]
  [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_ONLY)]
  public void OnCheckRBCommand(CCSPlayerController player, CommandInfo command)
  {
    if (!player.IsConnected()) return;
    if (_isWarmup)
    {
      TextToChat(player, $"{ChatColors.Red} RB list during warmup is always empty.");
      return;
    }
    if (_potentialPlayers.Count == 0)
    {
      TextToChat(player, $"{ChatColors.Red} There are no players in RB list.");
      return;
    }

    TextToChat(player, $"{ChatColors.Gold} Request Bomb list:");
    foreach (var gamer in _potentialPlayers)
    {
      if (gamer.IsConnected())
      {
        TextToChat(player, $"{ChatColors.Lime} [*] {(AdminManager.PlayerHasPermissions(gamer, "@css/vip") ? "VIP | " : "")}{gamer.PlayerName}");
      }
    }
  }
}
// Credits to https://github.com/destoer/Cs2Jailbreak
public static class Lib
{
  static public bool IsT([NotNullWhen(true)] this CCSPlayerController? player)
  {
    return IsLegal(player) && player.TeamNum == 2;
  }
  static public bool IsLegal([NotNullWhen(true)] this CCSPlayerController? player)
  {
    return player != null && player.IsValid && player.PlayerPawn.IsValid && player.PlayerPawn.Value?.IsValid == true; 
  }
  static public bool IsConnected([NotNullWhen(true)] this CCSPlayerController? player)
  {
    return player.IsLegal() && player.Connected == PlayerConnectedState.PlayerConnected;
  }
  static public bool IsLegalAlive([NotNullWhen(true)] this CCSPlayerController? player)
  {
    return player.IsConnected() && player.PawnIsAlive && player.PlayerPawn.Value?.LifeState == (byte)LifeState_t.LIFE_ALIVE;
  }
  static public bool IsLegalAliveT([NotNullWhen(true)] this CCSPlayerController? player)
  {
    return player.IsLegalAlive() && player.IsT();
  }
  static public List<CCSPlayerController> GetAlivePlayersT()
  {
    List<CCSPlayerController> players = Utilities.GetPlayers();
    return players.FindAll(player => player.IsLegalAliveT());
  }
  // Readonly's, prob to remove to Config if I am not gonna be too lazy
  public static readonly string ChatPrefix = $"{ChatColors.DarkBlue}[BombRequest] \u2740{ChatColors.White}";
}