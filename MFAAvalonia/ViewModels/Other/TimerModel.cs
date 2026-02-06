using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.ViewModels.Pages;
using System;
using System.Linq;

namespace MFAAvalonia.ViewModels.Other;

public partial class TimerModel : ViewModelBase
{
    private readonly TaskQueueViewModel _vm;
    private readonly InstanceConfiguration _config;
    private readonly DispatcherTimer _dispatcherTimer;
    private readonly DateTime?[] _lastPreSwitchTimes = new DateTime?[8];
    private readonly DateTime?[] _lastTriggerTimes = new DateTime?[8];

    public TimerProperties[] Timers { get; set; } = new TimerProperties[8];

    [ObservableProperty] private bool _customConfig;
    [ObservableProperty] private bool _forceScheduledStart;

    partial void OnCustomConfigChanged(bool value)
    {
        _config.SetValue(ConfigurationKeys.CustomConfig, value.ToString());
    }

    partial void OnForceScheduledStartChanged(bool value)
    {
        _config.SetValue(ConfigurationKeys.ForceScheduledStart, value.ToString());
    }

    public TimerModel(TaskQueueViewModel vm)
    {
        _vm = vm;
        _config = vm.Processor.InstanceConfiguration;

        CustomConfig = Convert.ToBoolean(_config.GetValue(ConfigurationKeys.CustomConfig, bool.FalseString));
        ForceScheduledStart = Convert.ToBoolean(_config.GetValue(ConfigurationKeys.ForceScheduledStart, bool.FalseString));

        for (var i = 0; i < 8; i++)
        {
            Timers[i] = new TimerProperties(i, _config, this);
        }

        _dispatcherTimer = new();
        _dispatcherTimer.Interval = TimeSpan.FromMinutes(1);
        _dispatcherTimer.Tick += CheckTimerElapsed;
        _dispatcherTimer.Start();
    }

    public void ReloadFromConfig()
    {
        CustomConfig = Convert.ToBoolean(_config.GetValue(ConfigurationKeys.CustomConfig, bool.FalseString));
        ForceScheduledStart = Convert.ToBoolean(_config.GetValue(ConfigurationKeys.ForceScheduledStart, bool.FalseString));

        Array.Fill(_lastPreSwitchTimes, null);
        Array.Fill(_lastTriggerTimes, null);

        foreach (var timer in Timers)
        {
            timer.ReloadFromConfig();
        }
    }

    private void CheckTimerElapsed(object? sender, EventArgs e)
    {
        var currentTime = DateTime.Now;
        foreach (var timer in Timers)
        {
            if (!timer.IsOn)
                continue;

            var nextOccurrence = GetNextOccurrence(currentTime, timer.ScheduleConfig, timer.Time);
            if (nextOccurrence == null)
                continue;

            var scheduledTime = nextOccurrence.Value;
            var preSwitchTime = scheduledTime.AddMinutes(-2);

            if (IsSameMinute(currentTime, preSwitchTime)
                && !IsSameMinute(_lastPreSwitchTimes[timer.TimerId], preSwitchTime))
            {
                _lastPreSwitchTimes[timer.TimerId] = preSwitchTime;
                TryPreSwitch(timer);
            }

            if (IsSameMinute(currentTime, scheduledTime)
                && !IsSameMinute(_lastTriggerTimes[timer.TimerId], scheduledTime))
            {
                _lastTriggerTimes[timer.TimerId] = scheduledTime;
                ExecuteTimerTask(timer.TimerId);
            }
        }
    }

    private void TryPreSwitch(TimerProperties timer)
    {
        if (!CustomConfig)
        {
            return;
        }

        var instanceId = _vm.Processor.InstanceId;
        var targetConfig = timer.TimerConfig ?? ConfigurationManager.GetConfigNameForInstance(instanceId);
        var currentConfig = ConfigurationManager.GetConfigNameForInstance(instanceId);
        if (string.Equals(targetConfig, currentConfig, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_vm.IsRunning)
        {
            if (!ForceScheduledStart)
            {
                return;
            }

            _vm.StopTask(() => ConfigurationManager.SwitchConfigurationForInstance(instanceId, targetConfig));
            return;
        }

        ConfigurationManager.SwitchConfigurationForInstance(instanceId, targetConfig);
    }

    private void ExecuteTimerTask(int timerId)
    {
        var timer = Timers.FirstOrDefault(t => t.TimerId == timerId, null);
        if (timer != null)
        {
            // Use _vm directly, no need to check ActiveTab
            if (ForceScheduledStart && _vm.IsRunning)
                _vm.StopTask(_vm.StartTask);
            else
                _vm.StartTask();
        }
    }

    private static bool IsSameMinute(DateTime now, DateTime target)
    {
        return now.Year == target.Year
               && now.Month == target.Month
               && now.Day == target.Day
               && now.Hour == target.Hour
               && now.Minute == target.Minute;
    }

    private static bool IsSameMinute(DateTime? value, DateTime target)
    {
        return value.HasValue && IsSameMinute(value.Value, target);
    }

    private static DateTime? GetNextOccurrence(DateTime now, TimerScheduleConfig schedule, TimeSpan time)
    {
        var currentMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);

        switch (schedule.ScheduleType)
        {
            case TimerScheduleType.Daily:
            {
                var candidate = now.Date.Add(time);
                if (candidate < currentMinute)
                {
                    candidate = candidate.AddDays(1);
                }

                return candidate;
            }
            case TimerScheduleType.Weekly:
            {
                if (schedule.SelectedDaysOfWeek.Count == 0)
                {
                    return null;
                }

                for (var i = 0; i < 14; i++)
                {
                    var date = now.Date.AddDays(i);
                    if (!schedule.SelectedDaysOfWeek.Contains(date.DayOfWeek))
                    {
                        continue;
                    }

                    var candidate = date.Add(time);
                    if (candidate >= currentMinute)
                    {
                        return candidate;
                    }
                }

                return null;
            }
            case TimerScheduleType.Monthly:
            {
                if (schedule.SelectedDaysOfMonth.Count == 0)
                {
                    return null;
                }

                for (var i = 0; i < 366; i++)
                {
                    var date = now.Date.AddDays(i);
                    if (!schedule.SelectedDaysOfMonth.Contains(date.Day))
                    {
                        continue;
                    }

                    var candidate = date.Add(time);
                    if (candidate >= currentMinute)
                    {
                        return candidate;
                    }
                }

                return null;
            }
            default:
                return null;
        }
    }

    public partial class TimerProperties : ViewModelBase
    {
        private readonly InstanceConfiguration _config;
        private readonly TimerModel _parent;

        public TimerProperties(int timeId, InstanceConfiguration config, TimerModel parent)
        {
            TimerId = timeId;
            _config = config;
            _parent = parent;

            _isOn = _config.GetValue($"Timer.Timer{timeId + 1}", bool.FalseString) == bool.TrueString;
            _time = TimeSpan.Parse(_config.GetValue($"Timer.Timer{timeId + 1}Time", $"{timeId * 3}:0"));
            
            var defaultConfig = ConfigurationManager.GetConfigNameForInstance(_parent._vm.Processor.InstanceId);
            var timerConfig = _config.GetValue($"Timer.Timer{timeId + 1}.Config", defaultConfig);
            if (timerConfig == null || !ConfigurationManager.Configs.Any(c => c.Name.Equals(timerConfig)))
            {
                _timerConfig = defaultConfig;
            }
            else
            {
                _timerConfig = timerConfig;
            }

            ScheduleConfig = new TimerScheduleConfig(_config.GetValue($"Timer.Timer{timeId + 1}.Schedule", string.Empty));
            
            TimerName = $"{LangKeys.Timer.ToLocalization()} {TimerId + 1}";
            LanguageHelper.LanguageChanged += OnLanguageChanged;
        }

        public int TimerId { get; set; }

        [ObservableProperty] private string _timerName;

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            TimerName = $"{LangKeys.Timer.ToLocalization()} {TimerId + 1}";
        }

        private bool _isOn;
        public bool IsOn
        {
            get => _isOn;
            set
            {
                SetProperty(ref _isOn, value);
                _config.SetValue($"Timer.Timer{TimerId + 1}", value.ToString());
            }
        }

        private TimeSpan _time;
        public TimeSpan Time
        {
            get => _time;
            set
            {
                SetProperty(ref _time, value);
                _config.SetValue($"Timer.Timer{TimerId + 1}Time", value.ToString(@"h\:mm"));
            }
        }

        private string? _timerConfig;
        public string? TimerConfig
        {
            get => _timerConfig;
            set
            {
                var defaultConfig = ConfigurationManager.GetConfigNameForInstance(_parent._vm.Processor.InstanceId);
                SetProperty(ref _timerConfig, value ?? defaultConfig);
                _config.SetValue($"Timer.Timer{TimerId + 1}.Config", _timerConfig);
            }
        }

        private TimerScheduleConfig _scheduleConfig;
        public TimerScheduleConfig ScheduleConfig
        {
            get => _scheduleConfig;
            set
            {
                SetNewProperty(ref _scheduleConfig, value);
                _config.SetValue($"Timer.Timer{TimerId + 1}.Schedule", _scheduleConfig?.Serialize() ?? string.Empty);
                OnPropertyChanged(nameof(ScheduleDisplayText));
            }
        }

        public string ScheduleDisplayText => _scheduleConfig.GetDisplayText();

        public void UpdateScheduleConfig()
        {
            _config.SetValue($"Timer.Timer{TimerId + 1}.Schedule", _scheduleConfig.Serialize());
            OnPropertyChanged(nameof(ScheduleDisplayText));
        }

        public void ReloadFromConfig()
        {
            var isOn = _config.GetValue($"Timer.Timer{TimerId + 1}", bool.FalseString) == bool.TrueString;
            SetProperty(ref _isOn, isOn);

            var time = TimeSpan.Parse(_config.GetValue($"Timer.Timer{TimerId + 1}Time", $"{TimerId * 3}:0"));
            SetProperty(ref _time, time);

            var defaultConfig = ConfigurationManager.GetConfigNameForInstance(_parent._vm.Processor.InstanceId);
            var timerConfig = _config.GetValue($"Timer.Timer{TimerId + 1}.Config", defaultConfig);
            if (timerConfig == null || !ConfigurationManager.Configs.Any(c => c.Name.Equals(timerConfig)))
            {
                timerConfig = defaultConfig;
            }

            SetProperty(ref _timerConfig, timerConfig);

            var schedule = new TimerScheduleConfig(_config.GetValue($"Timer.Timer{TimerId + 1}.Schedule", string.Empty));
            SetNewProperty(ref _scheduleConfig, schedule);
            OnPropertyChanged(nameof(ScheduleDisplayText));
        }
    }
}
