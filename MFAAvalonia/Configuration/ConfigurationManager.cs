using Avalonia.Collections;
using MFAAvalonia;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MFAAvalonia.Configuration;

public static class ConfigurationManager
{
    private static readonly string _configDir = Path.Combine(
        AppContext.BaseDirectory,
        "config");
    public static readonly MFAConfiguration Maa = new("Maa", "maa_option", new Dictionary<string, object>());
    public static MFAConfiguration Current = new("Default", "config", new Dictionary<string, object>());
    public static InstanceConfiguration CurrentInstance => MaaProcessorManager.Instance?.Current?.InstanceConfiguration ?? new InstanceConfiguration("Default");

    public static AvaloniaList<MFAConfiguration> Configs { get; } = LoadConfigurations();

    public static event Action<string, string>? ConfigurationSwitched;

    private static readonly object _switchLock = new();
    private static readonly Dictionary<string, string?> _pendingSwitchName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _switchingInstances = new(StringComparer.OrdinalIgnoreCase);

    private static readonly object _instanceConfigLock = new();
    private static readonly Dictionary<string, string> _instanceConfigMap = new(StringComparer.OrdinalIgnoreCase);

    public static string ConfigName { get; set; }

    public static string GetCurrentConfiguration()
    {
        if (MaaProcessorManager.IsInstanceCreated && MaaProcessorManager.Instance.Current != null)
        {
            return GetConfigNameForInstance(MaaProcessorManager.Instance.Current.InstanceId);
        }

        return ConfigName;
    }

    public static string GetActualConfiguration()
    {
        var current = GetCurrentConfiguration();
        if (current.Equals("Default", StringComparison.OrdinalIgnoreCase))
            return "config";
        return $"mfa_{current}";
    }

    public static void Initialize()
    {
        LoggerHelper.Info("Current Configuration: " + GetCurrentConfiguration());
    }

    public static MFAConfiguration GetConfigByName(string? name)
    {
        var target = string.IsNullOrWhiteSpace(name) ? "Default" : name;
        var config = Configs.FirstOrDefault(c => c.Name.Equals(target, StringComparison.OrdinalIgnoreCase));
        return config ?? Current;
    }

    public static MFAConfiguration GetConfigForInstance(string instanceId)
    {
        return GetConfigByName(GetConfigNameForInstance(instanceId));
    }

    public static InstanceConfiguration GetInstanceConfiguration(string instanceId)
    {
        if (MaaProcessorManager.IsInstanceCreated
            && MaaProcessorManager.Instance.TryGetInstance(instanceId, out var processor))
        {
            return processor.InstanceConfiguration;
        }

        return new InstanceConfiguration(instanceId);
    }

    public static string GetConfigNameForInstance(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return GetDefaultConfig();
        }

        lock (_instanceConfigLock)
        {
            if (_instanceConfigMap.TryGetValue(instanceId, out var existing))
            {
                return existing;
            }
        }

        var key = string.Format(ConfigurationKeys.InstanceConfigTemplate, instanceId);
        var stored = GlobalConfiguration.GetValue(key, GetDefaultConfig());
        var resolved = Configs.Any(c => c.Name.Equals(stored, StringComparison.OrdinalIgnoreCase))
            ? stored
            : GetDefaultConfig();

        SetConfigNameForInstance(instanceId, resolved, persist: true);
        return resolved;
    }

    public static void SetConfigNameForInstance(string instanceId, string? name, bool persist = true)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return;

        var resolved = string.IsNullOrWhiteSpace(name) ? GetDefaultConfig() : name!;
        if (!Configs.Any(c => c.Name.Equals(resolved, StringComparison.OrdinalIgnoreCase)))
        {
            resolved = GetDefaultConfig();
        }

        lock (_instanceConfigLock)
        {
            _instanceConfigMap[instanceId] = resolved;
        }

        if (persist)
        {
            var key = string.Format(ConfigurationKeys.InstanceConfigTemplate, instanceId);
            GlobalConfiguration.SetValue(key, resolved);
        }
    }

    public static void SetCurrentForInstance(string instanceId)
    {
        var configName = GetConfigNameForInstance(instanceId);
        var config = GetConfigByName(configName);
        ConfigName = config.Name;
        Current = config;
    }

    public static bool IsSwitching
    {
        get
        {
            if (!MaaProcessorManager.IsInstanceCreated || MaaProcessorManager.Instance.Current == null)
            {
                return false;
            }

            return IsSwitchingForInstance(MaaProcessorManager.Instance.Current.InstanceId);
        }
    }

    public static bool IsSwitchingForInstance(string instanceId)
    {
        lock (_switchLock)
        {
            return _switchingInstances.Contains(instanceId);
        }
    }

    public static bool IsConfigInUse(string configName)
    {
        if (!MaaProcessorManager.IsInstanceCreated)
        {
            return false;
        }

        return MaaProcessorManager.Instance.Instances
            .Any(p => string.Equals(GetConfigNameForInstance(p.InstanceId), configName, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsConfigLockedForInstance(string instanceId)
    {
        var configName = GetConfigNameForInstance(instanceId);
        return IsConfigLockedForConfigName(configName, instanceId);
    }

    public static bool IsConfigLockedForConfigName(string configName, string? excludeInstanceId = null)
    {
        if (!MaaProcessorManager.IsInstanceCreated)
        {
            return false;
        }

        return MaaProcessor.Processors.Any(p =>
            p.TaskQueue.Count > 0
            && !string.Equals(p.InstanceId, excludeInstanceId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(GetConfigNameForInstance(p.InstanceId), configName, StringComparison.OrdinalIgnoreCase));
    }

    public static bool TrySetConfigValueForInstance(string instanceId, string key, object? value)
    {
        if (ConfigurationKeys.IsInstanceScoped(key))
        {
            GetInstanceConfiguration(instanceId).SetValue(key, value);
            return true;
        }

        if (IsConfigLockedForInstance(instanceId))
        {
            return false;
        }

        GetConfigForInstance(instanceId).SetValue(key, value);
        return true;
    }

    public static bool TrySetActiveConfigValue(string key, object? value)
    {
        if (!MaaProcessorManager.IsInstanceCreated || MaaProcessorManager.Instance.Current == null)
        {
            return false;
        }

        return TrySetConfigValueForInstance(MaaProcessorManager.Instance.Current.InstanceId, key, value);
    }

    public static Dictionary<string, object?> GetInstanceData(string instanceId)
    {
        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return data;
        }

        var config = GetConfigForInstance(instanceId);
        var prefix = $"Instance.{instanceId}.";
        var legacyPrefix = $"instance.{instanceId}.";

        foreach (var kvp in config.Config)
        {
            if (kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var suffix = kvp.Key.Substring(prefix.Length);
                if (!data.ContainsKey(suffix))
                {
                    data[suffix] = kvp.Value;
                }
                continue;
            }

            if (kvp.Key.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var suffix = kvp.Key.Substring(legacyPrefix.Length);
                if (!data.ContainsKey(suffix))
                {
                    data[suffix] = kvp.Value;
                }
            }
        }

        return data;
    }

    public static void ApplyInstanceData(string instanceId, IDictionary<string, object?> data, bool clearExisting = true)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return;
        }

        var config = GetConfigForInstance(instanceId);
        var prefix = $"Instance.{instanceId}.";
        var legacyPrefix = $"instance.{instanceId}.";

        if (clearExisting)
        {
            var keysToRemove = config.Config.Keys
                .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                              || key.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var key in keysToRemove)
            {
                config.Config.Remove(key);
            }
        }

        foreach (var kvp in data)
        {
            if (string.IsNullOrWhiteSpace(kvp.Key))
            {
                continue;
            }

            if (kvp.Value == null)
            {
                continue;
            }

            config.Config[$"{prefix}{kvp.Key}"] = kvp.Value;
        }

        SaveConfigFile(config);
    }

    public static void RemoveInstanceKeys(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return;
        }

        var prefix = $"Instance.{instanceId}.";
        var legacyPrefix = $"instance.{instanceId}.";

        foreach (var config in Configs)
        {
            config.RemoveKeysByPrefix(prefix, StringComparison.Ordinal);
            config.RemoveKeysByPrefix(legacyPrefix, StringComparison.OrdinalIgnoreCase);
        }

        GlobalConfiguration.RemoveKey(string.Format(ConfigurationKeys.InstanceConfigTemplate, instanceId));
    }

    public static int CleanupInstanceKeys(IEnumerable<string> validInstanceIds)
    {
        var valid = new HashSet<string>(validInstanceIds, StringComparer.OrdinalIgnoreCase);
        var totalRemoved = 0;

        foreach (var config in Configs)
        {
            var keysToRemove = config.Config.Keys
                .Where(key => TryGetInstanceIdFromKey(key, out var instanceId)
                              && !valid.Contains(instanceId))
                .ToList();

            if (keysToRemove.Count == 0)
            {
                continue;
            }

            foreach (var key in keysToRemove)
            {
                config.Config.Remove(key);
            }

            SaveConfigFile(config);
            totalRemoved += keysToRemove.Count;
        }

        return totalRemoved;
    }

    private static bool TryGetInstanceIdFromKey(string key, out string instanceId)
    {
        instanceId = string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        string prefix;
        if (key.StartsWith("Instance.", StringComparison.Ordinal))
        {
            prefix = "Instance.";
        }
        else if (key.StartsWith("instance.", StringComparison.OrdinalIgnoreCase))
        {
            prefix = key.StartsWith("instance.", StringComparison.Ordinal)
                ? "instance."
                : "Instance.";
        }
        else
        {
            return false;
        }

        var rest = key.Substring(prefix.Length);
        var dotIndex = rest.IndexOf('.');
        if (dotIndex <= 0)
        {
            return false;
        }

        instanceId = rest.Substring(0, dotIndex);
        return !string.IsNullOrWhiteSpace(instanceId);
    }

    private static void SaveConfigFile(MFAConfiguration config)
    {
        JsonHelper.SaveConfig(config.FileName, config.Config,
            new MaaInterfaceSelectAdvancedConverter(false), new MaaInterfaceSelectOptionConverter(false));
    }

    public static void SwitchConfiguration(string? name)
    {
        if (!MaaProcessorManager.IsInstanceCreated || MaaProcessorManager.Instance.Current == null)
        {
            return;
        }

        SwitchConfigurationForInstance(MaaProcessorManager.Instance.Current.InstanceId, name);
    }

    public static void SwitchConfigurationForInstance(string instanceId, string? name)
    {
        _ = SwitchConfigurationForInstanceAsync(instanceId, name);
    }

    private static async Task SwitchConfigurationForInstanceAsync(string instanceId, string? name)
    {
        if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(name))
            return;

        var currentName = GetConfigNameForInstance(instanceId);
        if (currentName.Equals(name, StringComparison.OrdinalIgnoreCase))
            return;

        if (!Configs.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            LoggerHelper.Warning($"配置 {name} 不存在，切换已取消");
            return;
        }

        if (!TryBeginSwitch(instanceId, name))
        {
            return;
        }

        if (!MaaProcessorManager.IsInstanceCreated
            || !MaaProcessorManager.Instance.TryGetInstance(instanceId, out var instanceProcessor))
        {
            EndSwitch(instanceId);
            return;
        }

        var isActiveInstance = MaaProcessorManager.Instance.Current != null
            && MaaProcessorManager.Instance.Current.InstanceId.Equals(instanceId, StringComparison.OrdinalIgnoreCase);

        if (IsInstanceRunning(instanceId))
        {
            ToastHelper.Warn(LangKeys.SwitchConfiguration.ToLocalization());
            ResetConfigSwitchSelection(instanceId, currentName, isActiveInstance);
            EndSwitch(instanceId);
            return;
        }

        if (isActiveInstance)
        {
            await DispatcherHelper.RunOnMainThreadAsync(() =>
            {
                Instances.RootViewModel.SetConfigSwitchingState(true);
                Instances.RootViewModel.SetConfigSwitchProgress(5);
            });
        }

        await Task.Run(() => instanceProcessor.SetTasker());

        await Task.Delay(60);

        try
        {
            if (isActiveInstance)
            {
                DispatcherHelper.PostOnMainThread(() => Instances.RootViewModel.SetConfigSwitchProgress(25));
            }

            var config = Configs.First(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var configData = await Task.Run(() => JsonHelper.LoadConfig(config.FileName, new Dictionary<string, object>()));

            await DispatcherHelper.RunOnMainThreadAsync(() =>
            {
                if (isActiveInstance)
                {
                    SetDefaultConfig(name);
                }

                config.SetConfig(configData);
                SetConfigNameForInstance(instanceId, name, persist: true);

                if (isActiveInstance)
                {
                    SetCurrentForInstance(instanceId);
                    Instances.RootViewModel.SetConfigSwitchProgress(55);
                }
            });

            await DispatcherHelper.RunOnMainThreadAsync(() => ConfigurationSwitched?.Invoke(instanceId, name));

            if (isActiveInstance)
            {
                await Instances.ReloadConfigurationForSwitchAsync();
                DispatcherHelper.PostOnMainThread(() => Instances.RootViewModel.SetConfigSwitchProgress(98));
            }
            else
            {
                var taskVm = instanceProcessor.ViewModel ?? MaaProcessorManager.Instance.GetViewModel(instanceId);
                taskVm.CurrentConfiguration = name;
                taskVm.ReloadForConfigSwitch();
            }
        }
        finally
        {
            if (isActiveInstance)
            {
                await DispatcherHelper.RunOnMainThreadAsync(() => Instances.RootViewModel.SetConfigSwitchProgress(100));
                await Task.Delay(120);
                DispatcherHelper.PostOnMainThread(() => Instances.RootViewModel.SetConfigSwitchingState(false));
            }

            EndSwitch(instanceId);
        }

        string? pending;
        lock (_switchLock)
        {
            _pendingSwitchName.TryGetValue(instanceId, out pending);
            _pendingSwitchName.Remove(instanceId);
        }

        if (!string.IsNullOrWhiteSpace(pending)
            && !pending.Equals(GetConfigNameForInstance(instanceId), StringComparison.OrdinalIgnoreCase))
        {
            await SwitchConfigurationForInstanceAsync(instanceId, pending);
        }
    }

    private static bool TryBeginSwitch(string instanceId, string? pendingName)
    {
        lock (_switchLock)
        {
            if (_switchingInstances.Contains(instanceId))
            {
                _pendingSwitchName[instanceId] = pendingName;
                return false;
            }

            _switchingInstances.Add(instanceId);
            return true;
        }
    }

    private static void EndSwitch(string instanceId)
    {
        lock (_switchLock)
        {
            _switchingInstances.Remove(instanceId);
        }
    }

    private static bool IsInstanceRunning(string instanceId)
    {
        if (!MaaProcessorManager.IsInstanceCreated)
        {
            return false;
        }

        return MaaProcessorManager.Instance.TryGetInstance(instanceId, out var processor)
               && processor.TaskQueue.Count > 0;
    }

    private static void ResetConfigSwitchSelection(string instanceId, string currentName, bool isActiveInstance)
    {
        DispatcherHelper.PostOnMainThread(() =>
        {
            if (isActiveInstance && Instances.IsResolved<ViewModels.Pages.SettingsViewModel>())
            {
                Instances.SettingsViewModel.RefreshCurrentConfiguration();
            }

            var taskVm = MaaProcessorManager.Instance.GetViewModel(instanceId);
            taskVm.CurrentConfiguration = currentName;
        });
    }

    public static void SetDefaultConfig(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;
        GlobalConfiguration.SetValue(ConfigurationKeys.DefaultConfig, name);
    }

    public static string GetDefaultConfig()
    {
        return GlobalConfiguration.GetValue(ConfigurationKeys.DefaultConfig, "Default");
    }

    private static AvaloniaList<MFAConfiguration> LoadConfigurations()
    {
        LoggerHelper.Info("Loading Configurations...");
        ConfigName = GetDefaultConfig();

        var collection = new AvaloniaList<MFAConfiguration>();

        var defaultConfigPath = Path.Combine(_configDir, "config.json");
        if (!Directory.Exists(_configDir))
            Directory.CreateDirectory(_configDir);
        if (!File.Exists(defaultConfigPath))
            File.WriteAllText(defaultConfigPath, "{}");
        if (ConfigName != "Default" && !File.Exists(Path.Combine(_configDir, $"mfa_{ConfigName}.json")))
            ConfigName = "Default";
        collection.Add(Current.SetConfig(JsonHelper.LoadConfig("config", new Dictionary<string, object>())));
        foreach (var file in Directory.EnumerateFiles(_configDir, "mfa_*.json"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (fileName == "maa_option" || fileName == "config") continue;
            string nameWithoutPrefix = fileName.StartsWith("mfa_")
                ? fileName.Substring("mfa_".Length)
                : fileName;
            var configs = JsonHelper.LoadConfig(fileName, new Dictionary<string, object>());

            var config = new MFAConfiguration(nameWithoutPrefix, fileName, configs);

            collection.Add(config);
        }

        Maa.SetConfig(JsonHelper.LoadConfig("maa_option", new Dictionary<string, object>()));

        if (AppRuntime.Args.TryGetValue("c", out var param) && !string.IsNullOrEmpty(param))
        {
            if (collection.Any(c => c.Name == param))
                ConfigName = param;
        }
        Current = collection.FirstOrDefault(c
                => !string.IsNullOrWhiteSpace(c.Name)
                && c.Name.Equals(ConfigName, StringComparison.OrdinalIgnoreCase))
            ?? Current;

        return collection;
    }

    public static void SaveConfiguration(string configName)
    {
        var config = Configs.FirstOrDefault(c => c.Name == configName);
        if (config != null)
        {
            JsonHelper.SaveConfig(config.FileName, config.Config);
        }
    }

    public static MFAConfiguration Add(string name)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "config");
        var newConfigPath = Path.Combine(configPath, $"{name}.json");
        var newConfig = new MFAConfiguration(name.Equals("config", StringComparison.OrdinalIgnoreCase) ? "Default" : name, name.Equals("config", StringComparison.OrdinalIgnoreCase) ? name : $"mfa_{name}", new Dictionary<string, object>());
        Configs.Add(newConfig);
        return newConfig;
    }
}
