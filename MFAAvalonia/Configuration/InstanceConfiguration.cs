using MFAAvalonia.Helper.Converters;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace MFAAvalonia.Configuration;

public sealed class InstanceConfiguration
{
    private readonly string _instanceId;

    public InstanceConfiguration(string instanceId)
    {
        _instanceId = instanceId;
    }

    private MFAConfiguration Config => ConfigurationManager.GetConfigForInstance(_instanceId);

    private string ScopedKey(string key) => $"Instance.{_instanceId}.{key}";
    private string LegacyScopedKey(string key) => $"instance.{_instanceId}.{key}";
    private string LegacyScopedKeyLower(string key) => LegacyScopedKey(key).ToLowerInvariant();

    private bool TryGetLegacyValue<T>(string key, T defaultValue, out T value)
    {
        var legacyKey = LegacyScopedKey(key);
        if (Config.ContainsKey(legacyKey))
        {
            value = Config.GetValue(legacyKey, defaultValue);
            SetValue(key, value);
            return true;
        }

        var legacyLower = LegacyScopedKeyLower(key);
        if (Config.ContainsKey(legacyLower))
        {
            value = Config.GetValue(legacyLower, defaultValue);
            SetValue(key, value);
            return true;
        }

        value = defaultValue;
        return false;
    }

    private bool TryGetLegacyValue<T>(string key, T defaultValue, List<T> whitelist, out T value)
    {
        var legacyKey = LegacyScopedKey(key);
        if (Config.ContainsKey(legacyKey))
        {
            value = Config.GetValue(legacyKey, defaultValue, whitelist);
            SetValue(key, value);
            return true;
        }

        var legacyLower = LegacyScopedKeyLower(key);
        if (Config.ContainsKey(legacyLower))
        {
            value = Config.GetValue(legacyLower, defaultValue, whitelist);
            SetValue(key, value);
            return true;
        }

        value = defaultValue;
        return false;
    }

    private bool TryGetLegacyValue<T>(string key, T defaultValue, Dictionary<object, T> options, out T value)
    {
        var legacyKey = LegacyScopedKey(key);
        if (Config.ContainsKey(legacyKey))
        {
            value = Config.GetValue(legacyKey, defaultValue, options);
            SetValue(key, value);
            return true;
        }

        var legacyLower = LegacyScopedKeyLower(key);
        if (Config.ContainsKey(legacyLower))
        {
            value = Config.GetValue(legacyLower, defaultValue, options);
            SetValue(key, value);
            return true;
        }

        value = defaultValue;
        return false;
    }

    private bool TryGetLegacyValue<T>(string key, T defaultValue, T? noValue, JsonConverter[] valueConverters, out T value)
    {
        var legacyKey = LegacyScopedKey(key);
        if (Config.ContainsKey(legacyKey))
        {
            value = Config.GetValue(legacyKey, defaultValue, noValue, valueConverters);
            SetValue(key, value);
            return true;
        }

        var legacyLower = LegacyScopedKeyLower(key);
        if (Config.ContainsKey(legacyLower))
        {
            value = Config.GetValue(legacyLower, defaultValue, noValue, valueConverters);
            SetValue(key, value);
            return true;
        }

        value = defaultValue;
        return false;
    }

    private bool TryGetLegacyValue<T>(string key, T defaultValue, List<T>? noValue, JsonConverter[] valueConverters, out T value)
    {
        var legacyKey = LegacyScopedKey(key);
        if (Config.ContainsKey(legacyKey))
        {
            value = Config.GetValue(legacyKey, defaultValue, noValue, valueConverters);
            SetValue(key, value);
            return true;
        }

        var legacyLower = LegacyScopedKeyLower(key);
        if (Config.ContainsKey(legacyLower))
        {
            value = Config.GetValue(legacyLower, defaultValue, noValue, valueConverters);
            SetValue(key, value);
            return true;
        }

        value = defaultValue;
        return false;
    }

    public bool ContainsKey(string key)
        => Config.ContainsKey(ScopedKey(key))
           || Config.ContainsKey(LegacyScopedKey(key))
           || Config.ContainsKey(LegacyScopedKeyLower(key))
           || Config.ContainsKey(key);

        public void SetValue(string key, object? value)
        {
            // 移除配置切换时阻止 TaskItems 保存的逻辑
            // 这会导致任务配置无法正确更新到新配置中
            Config.SetValue(ScopedKey(key), value);
        }
    public T GetValue<T>(string key, T defaultValue)
    {
        var scopedKey = ScopedKey(key);
        if (Config.ContainsKey(scopedKey))
        {
            return Config.GetValue<T>(scopedKey, defaultValue);
        }

        if (TryGetLegacyValue(key, defaultValue, out var legacyValue))
        {
            return legacyValue;
        }

        if (Config.ContainsKey(key))
        {
            var value = Config.GetValue<T>(key, defaultValue);
            SetValue(key, value);
            return value;
        }

        return defaultValue;
    }

    public T GetValue<T>(string key, T defaultValue, List<T> whitelist)
    {
        var scopedKey = ScopedKey(key);
        if (Config.ContainsKey(scopedKey))
        {
            return Config.GetValue<T>(scopedKey, defaultValue, whitelist);
        }

        if (TryGetLegacyValue(key, defaultValue, whitelist, out var legacyValue))
        {
            return legacyValue;
        }

        if (Config.ContainsKey(key))
        {
            var value = Config.GetValue<T>(key, defaultValue, whitelist);
            SetValue(key, value);
            return value;
        }

        return defaultValue;
    }

    public T GetValue<T>(string key, T defaultValue, Dictionary<object, T> options)
    {
        var scopedKey = ScopedKey(key);
        if (Config.ContainsKey(scopedKey))
        {
            return Config.GetValue<T>(scopedKey, defaultValue, options);
        }

        if (TryGetLegacyValue(key, defaultValue, options, out var legacyValue))
        {
            return legacyValue;
        }

        if (Config.ContainsKey(key))
        {
            var value = Config.GetValue<T>(key, defaultValue, options);
            SetValue(key, value);
            return value;
        }

        return defaultValue;
    }

    public T GetValue<T>(string key, T defaultValue, T? noValue = default, params JsonConverter[] valueConverters)
    {
        var scopedKey = ScopedKey(key);
        if (Config.ContainsKey(scopedKey))
        {
            return Config.GetValue<T>(scopedKey, defaultValue, noValue, valueConverters);
        }

        if (TryGetLegacyValue(key, defaultValue, noValue, valueConverters, out var legacyValue))
        {
            return legacyValue;
        }

        if (Config.ContainsKey(key))
        {
            var value = Config.GetValue<T>(key, defaultValue, noValue, valueConverters);
            SetValue(key, value);
            return value;
        }

        return defaultValue;
    }

    public T GetValue<T>(string key, T defaultValue, List<T>? noValue = null, params JsonConverter[] valueConverters)
    {
        var scopedKey = ScopedKey(key);
        if (Config.ContainsKey(scopedKey))
        {
            return Config.GetValue<T>(scopedKey, defaultValue, noValue, valueConverters);
        }

        if (TryGetLegacyValue(key, defaultValue, noValue, valueConverters, out var legacyValue))
        {
            return legacyValue;
        }

        if (Config.ContainsKey(key))
        {
            var value = Config.GetValue<T>(key, defaultValue, noValue, valueConverters);
            SetValue(key, value);
            return value;
        }

        return defaultValue;
    }

    public bool TryGetValue<T>(string key, out T output, params JsonConverter[] valueConverters)
    {
        var scopedKey = ScopedKey(key);
        if (Config.TryGetValue(scopedKey, out output, valueConverters))
        {
            return true;
        }

        if (valueConverters is { Length: > 0 }
            && TryGetLegacyValue<T>(key, default!, noValue: (T?)default, valueConverters, out var legacyConverted))
        {
            output = legacyConverted;
            return true;
        }

        if (TryGetLegacyValue<T>(key, default!, out var legacyValue))
        {
            output = legacyValue;
            return true;
        }

        if (Config.TryGetValue(key, out output, valueConverters))
        {
            SetValue(key, output);
            return true;
        }

        output = default!;
        return false;
    }
}
