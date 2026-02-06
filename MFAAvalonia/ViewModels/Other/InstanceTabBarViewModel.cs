using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.MessageBox;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace MFAAvalonia.ViewModels.Other;

public partial class InstanceTabBarViewModel : ViewModelBase
{
    public ObservableCollection<InstanceTabViewModel> Tabs { get; } = new();

    [ObservableProperty]
    private InstanceTabViewModel? _activeTab;

    public bool IsSingleInstance => Tabs.Count <= 1;
    public bool IsMultiInstance => Tabs.Count > 1;

    public InstanceTabBarViewModel()
    {
        ReloadTabs();
        Tabs.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsSingleInstance));
            OnPropertyChanged(nameof(IsMultiInstance));
        };
        MaaProcessor.Processors.CollectionChanged += (_, _) =>
        {
            DispatcherHelper.PostOnMainThread(ReloadTabs);
        };
    }

    public void ReloadTabs()
    {
        var processors = MaaProcessor.Processors.ToList();
        
        // Use smart sync instead of clear/add to preserve view state
        var toRemove = Tabs.Where(t => !processors.Contains(t.Processor)).ToList();
        foreach (var t in toRemove)
        {
            Tabs.Remove(t);
        }

        foreach (var processor in processors)
        {
            if (Tabs.All(t => t.Processor != processor))
            {
                Tabs.Add(new InstanceTabViewModel(processor));
            }
            else
            {
                var existing = Tabs.First(t => t.Processor == processor);
                existing.UpdateName();
            }
        }
        
        var current = MaaProcessorManager.Instance.Current;
        InstanceTabViewModel? targetTab = null;
        if (current != null)
        {
            targetTab = Tabs.FirstOrDefault(t => t.InstanceId == current.InstanceId);
        }

        var lastActive = GlobalConfiguration.GetValue(ConfigurationKeys.LastActiveInstance, "");
        if (!string.IsNullOrWhiteSpace(lastActive)
            && (targetTab == null || !lastActive.Equals(targetTab.InstanceId, System.StringComparison.OrdinalIgnoreCase)))
        {
            var lastActiveTab = Tabs.FirstOrDefault(t => t.InstanceId.Equals(lastActive, System.StringComparison.OrdinalIgnoreCase));
            if (lastActiveTab != null)
            {
                targetTab = lastActiveTab;
            }
        }

        if (targetTab != null && ActiveTab != targetTab)
        {
            ActiveTab = targetTab;
        }
        else if (Tabs.Count > 0 && ActiveTab == null)
        {
            ActiveTab = Tabs.First();
        }

        OnPropertyChanged(nameof(IsSingleInstance));
        OnPropertyChanged(nameof(IsMultiInstance));
    }

    partial void OnActiveTabChanged(InstanceTabViewModel? oldValue, InstanceTabViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsActive = false;
        if (newValue != null)
        {
            newValue.IsActive = true;
            SwitchToInstance(newValue.Processor);
        }
    }

    private void SwitchToInstance(MaaProcessor processor)
    {
        // 切换前保存当前实例的任务状态
        var current = MaaProcessorManager.Instance.Current;
        var vm = current?.ViewModel;
        if (vm != null)
        {
            var instanceConfig = current.InstanceConfiguration;
            var taskItems = vm.TaskItemViewModels
                .Where(m => !m.IsResourceOptionItem)
                .Select(m => m.InterfaceItem)
                .ToList();

            var nonNullCount = taskItems.Count(t => t != null);
            var shouldSave = true;
            if (nonNullCount == 0 && instanceConfig.ContainsKey(ConfigurationKeys.TaskItems))
            {
                var saved = instanceConfig.GetValue(ConfigurationKeys.TaskItems, new List<MaaInterface.MaaInterfaceTask>());
                if (saved.Count > 0)
                {
                    shouldSave = false;
                }
            }

            if (shouldSave)
            {
                instanceConfig.SetValue(ConfigurationKeys.TaskItems, taskItems);
            }
        }

        if (current == null || current != processor)
        {
            if (!MaaProcessorManager.Instance.SwitchCurrent(processor.InstanceId))
            {
                return;
            }
        }

        ConfigurationManager.SetCurrentForInstance(processor.InstanceId);
        Instances.ReloadConfigurationForSwitch(false);
        DispatcherHelper.PostOnMainThread(() =>
        {
            processor.ViewModel?.ReloadInstanceRuntime(false);
        });
        if (Instances.IsResolved<ViewModels.Windows.RootViewModel>())
        {
            Instances.RootViewModel.RefreshConfigReadOnly();
        }
    }

    [RelayCommand]
    private void AddInstance()
    {
        var processor = MaaProcessorManager.Instance.CreateInstance(false);
        processor.InitializeData();
        
        var tab = new InstanceTabViewModel(processor);
        Tabs.Add(tab);
        ActiveTab = tab;
    }

    [RelayCommand]
    private async Task CloseInstance(InstanceTabViewModel? tab)
    {
        if (tab == null) return;
        
        if (Tabs.Count <= 1)
        {
            ToastHelper.Info("不能关闭最后一个实例");
            return;
        }

        if (tab.IsRunning)
        {
             var result = await SukiUI.MessageBox.SukiMessageBox.ShowDialog(new SukiMessageBoxHost
             {
                 Content = "实例正在运行，确定要停止并关闭吗？",
                 ActionButtonsPreset = SukiUI.MessageBox.SukiMessageBoxButtons.YesNo,
                 IconPreset = SukiUI.MessageBox.SukiMessageBoxIcons.Warning
             }, new SukiUI.MessageBox.SukiMessageBoxOptions { Title = "关闭实例" });

             if (!result.Equals(SukiMessageBoxResult.Yes)) return;

             tab.Processor.Stop(MFATask.MFATaskStatus.STOPPED);
        }

        if (MaaProcessorManager.Instance.RemoveInstance(tab.InstanceId))
        {
            Tabs.Remove(tab);
            if (ActiveTab == tab || ActiveTab == null)
            {
                ActiveTab = Tabs.FirstOrDefault();
            }
        }
    }
    
    [RelayCommand]
    private void RenameInstance(InstanceTabViewModel? tab)
    {
        if(tab == null) return;
        
        Instances.DialogManager.CreateDialog()
            .WithTitle(LangKeys.TaskRename.ToLocalization())
            .WithViewModel(dialog => new RenameInstanceDialogViewModel(dialog, tab))
            .TryShow();
    }

    [RelayCommand]
    private void DuplicateInstance(InstanceTabViewModel? tab)
    {
        if (tab == null) return;

        tab.TaskQueueViewModel.DuplicateInstanceCommand.Execute(null);
    }

    [RelayCommand]
    private void ExportInstanceProfile(InstanceTabViewModel? tab)
    {
        if (tab == null) return;

        tab.TaskQueueViewModel.ExportInstanceProfileCommand.Execute(null);
    }

    [RelayCommand]
    private void ImportInstanceProfile(InstanceTabViewModel? tab)
    {
        if (tab == null) return;

        tab.TaskQueueViewModel.ImportInstanceProfileCommand.Execute(null);
    }
}
