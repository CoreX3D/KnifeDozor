using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using System.Drawing;
using System.Globalization;
using System.Text.Json.Serialization;

namespace KnifeDozor;

public class KnifeDozorConfig : BasePluginConfig
{
    [JsonPropertyName("kd_enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("kd_only_35hp_maps")]
    public bool Only35hpMaps { get; set; } = true;

    [JsonPropertyName("kd_spawn_protect_by_time")]
    public bool SpawnProtectByTime { get; set; } = false;

    [JsonPropertyName("kd_spawn_protect_time")]
    public float SpawnProtectTime { get; set; } = 1.5f;

    [JsonPropertyName("kd_anti_chain_time")]
    public float AntiChainTime { get; set; } = 2.0f;

    [JsonPropertyName("kd_protect_color")]
    public bool ProtectColor { get; set; } = true;

    [JsonPropertyName("kd_anti_chain_color")]
    public bool AntiChainColor { get; set; } = true;

    [JsonPropertyName("kd_loglevel")]
    public int LogLevel { get; set; } = 3;
}

[MinimumApiVersion(362)]
public class KnifeDozor : BasePlugin, IPluginConfig<KnifeDozorConfig>
{
    public override string ModuleName => "[Knife Dozor] CS2";
    public override string ModuleVersion => "3.0";
    public override string ModuleAuthor => "Core";

    public required KnifeDozorConfig Config { get; set; }

    private bool _isActiveOnCurrentMap = false;

    private readonly Dictionary<CCSPlayerController, bool> _spawnProtectActive = new();
    private readonly Dictionary<CCSPlayerController, bool> _antiChainActive = new();
    private readonly Dictionary<CCSPlayerController, bool> _testModeActive = new();

    private string ConfigPath => Path.Combine(Server.GameDirectory, "counterstrikesharp", "configs", "plugins", "KnifeDozor", "KnifeDozor.json");

    public void OnConfigParsed(KnifeDozorConfig config)
    {
        config.SpawnProtectTime = Math.Clamp(config.SpawnProtectTime, 0.5f, 5.0f);
        config.AntiChainTime = Math.Clamp(config.AntiChainTime, 1.0f, 5.0f);
        config.LogLevel = Math.Clamp(config.LogLevel, 0, 5);
        Config = config;
    }

    public override void Load(bool hotReload)
    {
        EnsureConfigFileExists();
        RegisterCommands();
        RegisterEvents();

        CheckMapActivation();
        PrintInfo();

        if (hotReload)
            Server.NextFrame(CheckMapActivation);
    }

    private void EnsureConfigFileExists()
    {
        string directory = Path.GetDirectoryName(ConfigPath)!;
        Directory.CreateDirectory(directory);

        if (!File.Exists(ConfigPath))
        {
            SaveConfig();
            Log(LogLevel.Information, "Создан новый конфигурационный файл KnifeDozor.json");
        }
    }

    private void SaveConfig()
    {
        try
        {
            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            string json = System.Text.Json.JsonSerializer.Serialize(Config, options);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"Ошибка сохранения конфига: {ex.Message}");
        }
    }

    private void RegisterCommands()
    {
        AddCommand("css_kd_help", "Справка", OnHelpCommand);
        AddCommand("css_kd_settings", "Настройки", OnSettingsCommand);
        AddCommand("css_kd_test", "Тест", OnTestCommand);
        AddCommand("css_kdt", "Открыть меню тестов", OnKdTestCommand);

        AddCommand("css_kd_reload", "Перезагрузить", OnReloadCommand);
        AddCommand("css_kd_enabled", "Вкл/выкл", OnSetEnabledCommand);
        AddCommand("css_kd_protecttime", "Время спавн-протекта", OnSetProtectTimeCommand);
        AddCommand("css_kd_antichaintime", "Время Anti-Chain", OnSetAntiChainTimeCommand);

        // Быстрые команды (только для тебя)
        AddCommand("!kd1", "Тест Anti-Chain", OnKdTest1);
        AddCommand("!kd2", "Тест Distance", OnKdTest2);
        AddCommand("!kd3", "Тест Spawn Protect", OnKdTest3);
        AddCommand("!kd4", "Тест Backstab", OnKdTest4);
        AddCommand("!kd5", "Тест всех модулей", OnKdTest5);
    }

    private void RegisterEvents()
    {
        RegisterEventHandler<EventPlayerSpawn>(OnSpawn);
        RegisterEventHandler<EventPlayerDeath>(OnDeath);
        RegisterEventHandler<EventPlayerHurt>(OnHurt, HookMode.Pre);
    }

    private void PrintInfo()
    {
        Log(LogLevel.Information, "===============================================");
        Log(LogLevel.Information, $"[Knife Dozor] v{ModuleVersion} успешно загружен!");
        Log(LogLevel.Information, $"Активен на карте: {Server.MapName}");
        Log(LogLevel.Information, "===============================================");
    }

    private void Log(LogLevel level, string message)
    {
        if ((int)level >= Config.LogLevel)
            Logger.Log(level, "[KnifeDozor] {Message}", message);
    }

    private void CheckMapActivation()
    {
        if (!Config.Enabled)
        {
            _isActiveOnCurrentMap = false;
            return;
        }

        var mapName = (Server.MapName ?? "").ToLowerInvariant().Trim();
        _isActiveOnCurrentMap = !Config.Only35hpMaps || mapName.StartsWith("35hp") || mapName.Contains("knife");
    }

    private bool IsRootAdmin(CCSPlayerController? player) =>
        player != null && AdminManager.PlayerHasPermissions(player, "@css/root");

    // ====================== КОМАНДЫ ======================
    private void OnHelpCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsRootAdmin(player)) { command.ReplyToCommand(" \x07[Knife Dozor] \x01У вас нет прав."); return; }
        command.ReplyToCommand(" \x07[Knife Dozor] \x01css_kd_help | css_kd_settings | css_kdt");
        command.ReplyToCommand(" \x07[Knife Dozor] \x01css_kd_reload | css_kd_enabled | css_kd_protecttime | css_kd_antichaintime");
    }

    private void OnSettingsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsRootAdmin(player)) { command.ReplyToCommand(" \x07[Knife Dozor] \x01У вас нет прав."); return; }
        command.ReplyToCommand($"[Knife Dozor] Enabled: {Config.Enabled} | Only35hp: {Config.Only35hpMaps}");
        command.ReplyToCommand($"SpawnProtectTime: {Config.SpawnProtectTime:F1}s | AntiChainTime: {Config.AntiChainTime:F1}s");
    }

    private void OnTestCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsRootAdmin(player)) { command.ReplyToCommand(" \x07[Knife Dozor] \x01У вас нет прав."); return; }
        command.ReplyToCommand("[Knife Dozor] Для теста модулей используйте !kdt");
    }

    private void OnKdTestCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsRootAdmin(player)) { command.ReplyToCommand(" \x07[Knife Dozor] \x01У вас нет прав."); return; }
        if (player == null || !player.IsValid) return;

        _testModeActive[player] = true;

        var menu = new ChatMenu("Knife Dozor - Тест модулей");
        menu.AddMenuOption("1 Anti-Chain", (p, _) => TestAntiChain(p));
        menu.AddMenuOption("2 Distance", (p, _) => TestDistance(p));
        menu.AddMenuOption("3 Spawn Protect", (p, _) => TestSpawnProtect(p));
        menu.AddMenuOption("4 Backstab Protection", (p, _) => TestBackstab(p));
        menu.AddMenuOption("5 Тест ВСЕХ модулей", (p, _) => TestAllModules(p));

        MenuManager.OpenChatMenu(player, menu);
    }

    // ====================== БЫСТРЫЕ КОМАНДЫ !kd1 !kd2 !kd3 !kd4 !kd5 ======================
    private void OnKdTest1(CCSPlayerController? player, CommandInfo command) => ExecuteQuickTest(player, TestAntiChain);
    private void OnKdTest2(CCSPlayerController? player, CommandInfo command) => ExecuteQuickTest(player, TestDistance);
    private void OnKdTest3(CCSPlayerController? player, CommandInfo command) => ExecuteQuickTest(player, TestSpawnProtect);
    private void OnKdTest4(CCSPlayerController? player, CommandInfo command) => ExecuteQuickTest(player, TestBackstab);
    private void OnKdTest5(CCSPlayerController? player, CommandInfo command) => ExecuteQuickTest(player, TestAllModules);

    private void ExecuteQuickTest(CCSPlayerController? player, Action<CCSPlayerController> testAction)
    {
        if (player == null || !player.IsValid || !IsRootAdmin(player)) return;

        if (!_testModeActive.TryGetValue(player, out bool active) || !active)
            return;

        _testModeActive.Remove(player);

        testAction(player);
    }

    private void OnReloadCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsRootAdmin(player)) { command.ReplyToCommand(" \x07[Knife Dozor] \x01У вас нет прав."); return; }
        command.ReplyToCommand("[Knife Dozor] Конфигурация перезагружена.");
    }

    private void OnSetEnabledCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsRootAdmin(player)) { command.ReplyToCommand(" \x07[Knife Dozor] \x01У вас нет прав."); return; }
        if (command.ArgCount < 2 || !int.TryParse(command.GetArg(1), out int value) || (value != 0 && value != 1))
        {
            command.ReplyToCommand("[Knife Dozor] Использование: css_kd_enabled <0/1>");
            return;
        }
        Config.Enabled = value == 1;
        SaveConfig();
        CheckMapActivation();
        command.ReplyToCommand($"[Knife Dozor] Плагин {(Config.Enabled ? "включён" : "выключен")}.");
    }

    private void OnSetProtectTimeCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsRootAdmin(player)) { command.ReplyToCommand(" \x07[Knife Dozor] \x01У вас нет прав."); return; }
        if (command.ArgCount < 2 || !float.TryParse(command.GetArg(1).Replace(',', '.'), out float value))
        {
            command.ReplyToCommand("[Knife Dozor] Использование: css_kd_protecttime <число>");
            return;
        }
        Config.SpawnProtectTime = Math.Clamp(value, 0.5f, 5.0f);
        SaveConfig();
        command.ReplyToCommand($"[Knife Dozor] Время Spawn Protect изменено на {Config.SpawnProtectTime:F1} сек.");
    }

    private void OnSetAntiChainTimeCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsRootAdmin(player)) { command.ReplyToCommand(" \x07[Knife Dozor] \x01У вас нет прав."); return; }
        if (command.ArgCount < 2 || !float.TryParse(command.GetArg(1).Replace(',', '.'), out float value))
        {
            command.ReplyToCommand("[Knife Dozor] Использование: css_kd_antichaintime <число>");
            return;
        }
        Config.AntiChainTime = Math.Clamp(value, 1.0f, 5.0f);
        SaveConfig();
        command.ReplyToCommand($"[Knife Dozor] Время Anti-Chain изменено на {Config.AntiChainTime:F1} сек.");
    }

    // ====================== ТЕСТЫ (только для тебя) ======================
    private void TestAntiChain(CCSPlayerController player)
    {
        _antiChainActive[player] = true;
        player.PrintToCenter(" \x02🔴 Anti-Chain активирован на 2 секунды");
        player.PrintToChat(" \x07[Knife Dozor] \x01[Тест] Anti-Chain запущен.");

        AddTimer(Config.AntiChainTime, () =>
        {
            if (player.IsValid)
            {
                _antiChainActive[player] = false;
                player.PrintToCenter("");
                player.PrintToChat(" \x07[Knife Dozor] \x01[Тест] Anti-Chain завершён.");
            }
        });
    }

    private void TestDistance(CCSPlayerController player)
    {
        player.PrintToChat(" \x07[Knife Dozor] \x01[Тест] Distance:");
        player.PrintToChat($" \x01Пример: Вы убили игрока на дистанции \x04{1.554:F3}\x01 м");
        player.PrintToChat($" \x01Пример: Вас убил игрок на дистанции \x04{2.873:F3}\x01 м");
    }

    private void TestSpawnProtect(CCSPlayerController player)
    {
        _spawnProtectActive[player] = true;
        player.PrintToChat(" \x07[Knife Dozor] \x01[Тест] Spawn Protect активирован на 5 секунд");

        if (Config.ProtectColor)
            SetPlayerColor(player, 0, 255, 100, 200);

        AddTimer(5.0f, () =>
        {
            if (player.IsValid)
            {
                _spawnProtectActive.Remove(player);
                if (Config.ProtectColor)
                    SetPlayerColor(player, 255, 255, 255, 255);
                player.PrintToChat(" \x07[Knife Dozor] \x01[Тест] Spawn Protect тест завершён.");
            }
        });
    }

    private void TestBackstab(CCSPlayerController player)
    {
        player.PrintToCenter(" \x02❌ Тест: Удар в спину");
        player.PrintToChat(" \x07[Knife Dozor] \x01[Тест] Backstab: вас телепортировало на спавн");
        player.Respawn();
    }

    private void TestAllModules(CCSPlayerController player)
    {
        player.PrintToChat(" \x07[Knife Dozor] \x04[Тест] === ЗАПУСК ТЕСТА ВСЕХ МОДУЛЕЙ ===");
        TestAntiChain(player);
        AddTimer(2.5f, () => TestDistance(player));
        AddTimer(5.0f, () => TestSpawnProtect(player));
        AddTimer(8.0f, () => TestBackstab(player));
    }

    // ====================== ЛОГИКА ПЛАГИНА ======================
    private HookResult OnSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        if (!_isActiveOnCurrentMap) return HookResult.Continue;
        var player = @event.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;

        AddTimer(0.12f, () =>
        {
            if (!player.IsValid || player.PlayerPawn.Value == null) return;
            var pawn = player.PlayerPawn.Value;
            pawn.Health = 35;
            pawn.ArmorValue = 0;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");

            _spawnProtectActive[player] = true;

            if (Config.ProtectColor)
                SetPlayerColor(player, Config.SpawnProtectByTime ? (byte)255 : (byte)0, 255, Config.SpawnProtectByTime ? (byte)100 : (byte)0, 200);
        });
        return HookResult.Continue;
    }

    private HookResult OnDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        if (!_isActiveOnCurrentMap) return HookResult.Continue;

        var attacker = @event.Attacker;
        var victim = @event.Userid;

        if (attacker == null || victim == null || attacker == victim || !attacker.IsValid || !victim.IsValid)
            return HookResult.Continue;

        var vPos = victim.PlayerPawn.Value?.AbsOrigin;
        var aPos = attacker.PlayerPawn.Value?.AbsOrigin;

        if (vPos != null && aPos != null)
        {
            float dist = (vPos - aPos).Length() * 0.01905f;
            string distStr = dist.ToString("F3", CultureInfo.InvariantCulture);

            attacker.PrintToChat($" \x07[Knife Dozor] \x01Вы убили \x0B{victim.PlayerName} \x01на дистанции \x04{distStr}\x01 м");
            victim.PrintToChat($" \x07[Knife Dozor] \x01Вас убил \x0B{attacker.PlayerName} \x01на дистанции \x04{distStr}\x01 м");
        }

        _antiChainActive[attacker] = true;
        if (Config.AntiChainColor)
            attacker.PrintToCenter(" \x02🔴 Anti-Chain! Следующий килл заблокирован");

        AddTimer(Config.AntiChainTime, () =>
        {
            if (attacker.IsValid)
            {
                _antiChainActive[attacker] = false;
                attacker.PrintToCenter("");
            }
        });

        return HookResult.Continue;
    }

    private HookResult OnHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        if (!_isActiveOnCurrentMap) return HookResult.Continue;

        var victim = @event.Userid;
        var attacker = @event.Attacker;

        if (victim == null || !victim.IsValid || victim.PlayerPawn.Value == null)
            return HookResult.Continue;

        var pawn = victim.PlayerPawn.Value;

        AddTimer(3.08f, () =>
        {
            if (!pawn.IsValid || pawn.Health <= 0 || pawn.Health >= 35) return;
            pawn.Health = 35;
            pawn.ArmorValue = 0;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_ArmorValue");
        });

        if (@event.Weapon != "knife" && @event.Weapon != "knifegg")
            return HookResult.Continue;

        float vAng = victim.PlayerPawn.Value?.EyeAngles.Y ?? 0f;
        float aAng = attacker?.PlayerPawn.Value?.EyeAngles.Y ?? 0f;
        float diff = (aAng - vAng + 180f) % 360f - 180f;

        if (diff > -90f && diff < 90f && attacker != null && attacker.IsValid)
        {
            attacker.PrintToCenter(" \x02❌ Нельзя бить в спину!");
            victim.Respawn();
            @event.DmgHealth = 0;
            @event.DmgArmor = 0;
            return HookResult.Stop;
        }

        if (_antiChainActive.TryGetValue(attacker, out bool active) && active && attacker != null)
        {
            attacker.PrintToCenter(" \x02🔴 Anti-Chain! Следующий килл заблокирован");
            @event.DmgHealth = 0;
            @event.DmgArmor = 0;
            return HookResult.Stop;
        }

        if (_spawnProtectActive.TryGetValue(victim, out bool sp) && sp)
        {
            pawn.Health += @event.DmgHealth;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
        }

        return HookResult.Continue;
    }

    private void SetPlayerColor(CCSPlayerController? player, byte r, byte g, byte b, byte a)
    {
        if (player == null || !player.IsValid || player.PlayerPawn.Value == null) return;
        player.PlayerPawn.Value.Render = Color.FromArgb(a, r, g, b);
    }

    public override void Unload(bool hotReload)
    {
        _spawnProtectActive.Clear();
        _antiChainActive.Clear();
        _testModeActive.Clear();
    }
}