using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation;
using BetterGenshinImpact.GameTask.AutoMusicGame;
using BetterGenshinImpact.GameTask.AutoSkip.Model;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.AutoWood;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.Common.Element.Assets;

using BetterGenshinImpact.Helpers;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Violeta.Controls;
using BetterGenshinImpact.ViewModel.Pages.View;
using System.Linq;
using System.Reflection;
using System.Collections.Frozen;
using System.Diagnostics;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.GameTask.GetGridIcons;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.GameTask.DataCollector;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class TaskSettingsPageViewModel : ViewModel
{
    public AllConfig Config { get; set; }

    private readonly INavigationService _navigationService;
    private readonly TaskTriggerDispatcher _taskDispatcher;

    private CancellationTokenSource? _cts;
    private static readonly object _locker = new();

    // [ObservableProperty]
    // private string[] _strategyList;

    [ObservableProperty]
    private bool _switchAutoGeniusInvokationEnabled;

    [ObservableProperty]
    private string _switchAutoGeniusInvokationButtonText = "启动";

    [ObservableProperty]
    private int _autoWoodRoundNum;

    [ObservableProperty]
    private int _autoWoodDailyMaxCount = 2000;

    [ObservableProperty]
    private bool _switchAutoWoodEnabled;

    [ObservableProperty]
    private string _switchAutoWoodButtonText = "启动";

    //[ObservableProperty]
    //private string[] _combatStrategyList;

    [ObservableProperty]
    private int _autoDomainRoundNum;

    [ObservableProperty]
    private bool _switchAutoDomainEnabled;

    [ObservableProperty]
    private string _switchAutoDomainButtonText = "启动";

    [ObservableProperty]
    private int _autoStygianOnslaughtRoundNum;

    [ObservableProperty]
    private bool _switchAutoStygianOnslaughtEnabled;

    [ObservableProperty]
    private string _switchAutoStygianOnslaughtButtonText = "启动";

    [ObservableProperty]
    private bool _switchAutoFightEnabled;

    [ObservableProperty]
    private string _switchAutoFightButtonText = "启动";

    [ObservableProperty]
    private string _switchAutoTrackButtonText = "启动";

    [ObservableProperty]
    private string _switchAutoTrackPathButtonText = "启动";

    [ObservableProperty]
    private bool _switchAutoMusicGameEnabled;

    [ObservableProperty]
    private string _switchAutoMusicGameButtonText = "启动";

    [ObservableProperty]
    private bool _switchAutoAlbumEnabled;

    [ObservableProperty]
    private string _switchAutoAlbumButtonText = "启动";

    [ObservableProperty]
    private List<string> _domainNameList;

    public static List<string> ArtifactSalvageStarList = ["4", "3", "2", "1"];

    [ObservableProperty]
    private List<string> _autoMusicLevelList = ["传说", "大师", "困难", "普通", "所有"];

    [ObservableProperty]
    private AutoFightViewModel? _autoFightViewModel;

    [ObservableProperty]
    private OneDragonFlowViewModel? _oneDragonFlowViewModel;

    [ObservableProperty]
    private bool _switchAutoFishingEnabled;

    [ObservableProperty]
    private string _switchAutoFishingButtonText = "启动";

    [ObservableProperty]
    private FrozenDictionary<Enum, string> _fishingTimePolicyDict = Enum.GetValues(typeof(FishingTimePolicy))
        .Cast<FishingTimePolicy>()
        .ToFrozenDictionary(
            e => (Enum)e,
            e => e.GetType()
                .GetField(e.ToString())?
                .GetCustomAttribute<DescriptionAttribute>()?
                .Description ?? e.ToString());

    private bool saveScreenshotOnKeyTick;
    public bool SaveScreenshotOnKeyTick
    {
        get => Config.CommonConfig.ScreenshotEnabled && saveScreenshotOnKeyTick;
        set => SetProperty(ref saveScreenshotOnKeyTick, value);
    }

    [ObservableProperty]
    private bool _switchArtifactSalvageEnabled;

    [ObservableProperty]
    private bool _switchGetGridIconsEnabled;
    [ObservableProperty]
    private string _switchGetGridIconsButtonText = "启动";
    [ObservableProperty]
    private FrozenDictionary<Enum, string> _gridNameDict = Enum.GetValues(typeof(GridScreenName))
        .Cast<GridScreenName>()
        .ToFrozenDictionary(
            e => (Enum)e,
            e => e.GetType()
                .GetField(e.ToString())?
                .GetCustomAttribute<DescriptionAttribute>()?
                .Description ?? e.ToString());

    // AI数据采集器相关属性
    [ObservableProperty]
    private bool _switchDataCollectorEnabled;

    [ObservableProperty]
    private string _switchDataCollectorButtonText = "启动";

    [ObservableProperty]
    private string _dataCollectorStatusText = "已停止";

    [ObservableProperty]
    private string _dataCollectorActionButtonText = "开始采集";

    [ObservableProperty]
    private bool _dataCollectorActionButtonEnabled = false;

    [ObservableProperty]
    private FrozenDictionary<Enum, string> _collectionTriggerTypeDict = Enum.GetValues(typeof(CollectionTriggerType))
        .Cast<CollectionTriggerType>()
        .ToFrozenDictionary(
            e => (Enum)e,
            e => e.GetType()
                .GetField(e.ToString())?
                .GetCustomAttribute<DescriptionAttribute>()?
                .Description ?? e.ToString());

    private DataCollectorTask? _currentDataCollectorTask;
    private CancellationTokenSource? _dataCollectorCts;

    public TaskSettingsPageViewModel(IConfigService configService, INavigationService navigationService, TaskTriggerDispatcher taskTriggerDispatcher)
    {
        Config = configService.Get();
        _navigationService = navigationService;
        _taskDispatcher = taskTriggerDispatcher;

        //_strategyList = LoadCustomScript(Global.Absolute(@"User\AutoGeniusInvokation"));

        //_combatStrategyList = ["根据队伍自动选择", .. LoadCustomScript(Global.Absolute(@"User\AutoFight"))];

        _domainNameList = ["", .. MapLazyAssets.Instance.DomainNameList];
        _autoFightViewModel = new AutoFightViewModel(Config);
        _oneDragonFlowViewModel = new OneDragonFlowViewModel();
    }


    [RelayCommand]
    private async Task OnSOneDragonFlow()
    {
        if (OneDragonFlowViewModel == null || OneDragonFlowViewModel.SelectedConfig == null)
        {
            OneDragonFlowViewModel.OnNavigatedTo();
            if (OneDragonFlowViewModel == null || OneDragonFlowViewModel.SelectedConfig == null)
            {
                Toast.Warning("未设置任务!");
                return;
            }
        }
        await OneDragonFlowViewModel.OnOneKeyExecute();
    }

    [RelayCommand]
    private async Task OnStopSoloTask()
    {
        CancellationContext.Instance.Cancel();
        SwitchAutoGeniusInvokationEnabled = false;
        SwitchAutoWoodEnabled = false;
        SwitchAutoDomainEnabled = false;
        SwitchAutoFightEnabled = false;
        SwitchAutoMusicGameEnabled = false;
        await Task.Delay(800);
    }

    [RelayCommand]
    private void OnStrategyDropDownOpened(string type)
    {
        AutoFightViewModel?.OnStrategyDropDownOpened(type);
    }

    [RelayCommand]
    public void OnGoToHotKeyPage()
    {
        _navigationService.Navigate(typeof(HotKeyPage));
    }

    [RelayCommand]
    public async Task OnSwitchAutoGeniusInvokation()
    {
        if (GetTcgStrategy(out var content))
        {
            return;
        }

        SwitchAutoGeniusInvokationEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoGeniusInvokationTask(new GeniusInvokationTaskParam(content)));
        SwitchAutoGeniusInvokationEnabled = false;
    }

    public bool GetTcgStrategy(out string content)
    {
        content = string.Empty;
        if (string.IsNullOrEmpty(Config.AutoGeniusInvokationConfig.StrategyName))
        {
            Toast.Warning("请先选择策略");
            return true;
        }

        var path = Global.Absolute(@"User\AutoGeniusInvokation\" + Config.AutoGeniusInvokationConfig.StrategyName + ".txt");

        if (!File.Exists(path))
        {
            Toast.Error("策略文件不存在");
            return true;
        }

        content = File.ReadAllText(path);
        return false;
    }

    [RelayCommand]
    public async Task OnGoToAutoGeniusInvokationUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://bettergi.com/feats/task/tcg.html"));
    }

    [RelayCommand]
    public async Task OnSwitchAutoWood()
    {
        SwitchAutoWoodEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoWoodTask(new WoodTaskParam(AutoWoodRoundNum, AutoWoodDailyMaxCount)));
        SwitchAutoWoodEnabled = false;
    }

    [RelayCommand]
    public async Task OnGoToAutoWoodUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://bettergi.com/feats/task/felling.html"));
    }

    [RelayCommand]
    public async Task OnSwitchAutoFight()
    {
        if (GetFightStrategy(out var path))
        {
            return;
        }

        var param = new AutoFightParam(path, Config.AutoFightConfig);

        SwitchAutoFightEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoFightTask(param));
        SwitchAutoFightEnabled = false;
    }

    [RelayCommand]
    public async Task OnGoToAutoFightUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://bettergi.com/feats/task/domain.html"));
    }

    [RelayCommand]
    public async Task OnSwitchAutoDomain()
    {
        if (GetFightStrategy(out var path))
        {
            return;
        }

        SwitchAutoDomainEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoDomainTask(new AutoDomainParam(AutoDomainRoundNum, path)));
        SwitchAutoDomainEnabled = false;
    }

    public bool GetFightStrategy(out string path)
    {
        return GetFightStrategy(Config.AutoFightConfig.StrategyName, out path);
    }

    public bool GetFightStrategy(string strategyName, out string path)
    {
        if (string.IsNullOrEmpty(strategyName))
        {
            UIDispatcherHelper.Invoke(() => { Toast.Warning("请先在下拉列表配置中选择战斗策略！"); });
            path = string.Empty;
            return true;
        }

        path = Global.Absolute(@"User\AutoFight\" + strategyName + ".txt");
        if ("根据队伍自动选择".Equals(strategyName))
        {
            path = Global.Absolute(@"User\AutoFight\");
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            UIDispatcherHelper.Invoke(() => { Toast.Error("当前选择的自动战斗策略文件不存在"); });
            return true;
        }

        return false;
    }

    [RelayCommand]
    public async Task OnGoToAutoDomainUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://bettergi.com/feats/task/domain.html"));
    }

    [RelayCommand]
    private async Task OnSwitchAutoStygianOnslaught()
    {
        if (GetFightStrategy(Config.AutoStygianOnslaughtConfig.StrategyName, out var path))
        {
            return;
        }

        SwitchAutoStygianOnslaughtEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoStygianOnslaughtTask(Config.AutoStygianOnslaughtConfig, path));
        SwitchAutoStygianOnslaughtEnabled = false;
    }

    [RelayCommand]
    private async Task OnGoToAutoStygianOnslaughtUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://bettergi.com/feats/task/stygian.html"));
    }


    [RelayCommand]
    public void OnOpenFightFolder()
    {
        AutoFightViewModel?.OnOpenFightFolder();
    }

    [Obsolete]
    [RelayCommand]
    public void OnSwitchAutoTrack()
    {
        // try
        // {
        //     lock (_locker)
        //     {
        //         if (SwitchAutoTrackButtonText == "启动")
        //         {
        //             _cts?.Cancel();
        //             _cts = new CancellationTokenSource();
        //             var param = new AutoTrackParam(_cts);
        //             _taskDispatcher.StartIndependentTask(IndependentTaskEnum.AutoTrack, param);
        //             SwitchAutoTrackButtonText = "停止";
        //         }
        //         else
        //         {
        //             _cts?.Cancel();
        //             SwitchAutoTrackButtonText = "启动";
        //         }
        //     }
        // }
        // catch (Exception ex)
        // {
        //     MessageBox.Error(ex.Message);
        // }
    }

    [RelayCommand]
    public async Task OnGoToAutoTrackUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://bettergi.com/feats/task/track.html"));
    }

    [Obsolete]
    [RelayCommand]
    public void OnSwitchAutoTrackPath()
    {
        // try
        // {
        //     lock (_locker)
        //     {
        //         if (SwitchAutoTrackPathButtonText == "启动")
        //         {
        //             _cts?.Cancel();
        //             _cts = new CancellationTokenSource();
        //             var param = new AutoTrackPathParam(_cts);
        //             _taskDispatcher.StartIndependentTask(IndependentTaskEnum.AutoTrackPath, param);
        //             SwitchAutoTrackPathButtonText = "停止";
        //         }
        //         else
        //         {
        //             _cts?.Cancel();
        //             SwitchAutoTrackPathButtonText = "启动";
        //         }
        //     }
        // }
        // catch (Exception ex)
        // {
        //     MessageBox.Error(ex.Message);
        // }
    }

    [RelayCommand]
    private async Task OnGoToAutoTrackPathUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://bettergi.com/feats/task/track.html"));
    }

    [RelayCommand]
    private async Task OnSwitchAutoMusicGame()
    {
        SwitchAutoMusicGameEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoMusicGameTask(new AutoMusicGameParam()));
        SwitchAutoMusicGameEnabled = false;
    }

    [RelayCommand]
    private async Task OnGoToAutoMusicGameUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://bettergi.com/feats/task/music.html"));
    }

    [RelayCommand]
    private async Task OnSwitchAutoAlbum()
    {
        SwitchAutoAlbumEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoAlbumTask(new AutoMusicGameParam()));
        SwitchAutoAlbumEnabled = false;
    }

    [RelayCommand]
    private async Task OnSwitchAutoFishing()
    {
        SwitchAutoFishingEnabled = true;
        var param = AutoFishingTaskParam.BuildFromConfig(TaskContext.Instance().Config.AutoFishingConfig, SaveScreenshotOnKeyTick);
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoFishingTask(param));
        SwitchAutoFishingEnabled = false;
    }

    [RelayCommand]
    private async Task OnGoToAutoFishingUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://bettergi.com/feats/task/fish.html"));
    }

    [RelayCommand]
    private async Task OnGoToTorchPreviousVersionsAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://pytorch.org/get-started/previous-versions"));
    }

    [RelayCommand]
    private void OnOpenLocalScriptRepo()
    {
        AutoFightViewModel?.OnOpenLocalScriptRepo();
    }

    [RelayCommand]
    private async Task OnSwitchArtifactSalvage()
    {
        SwitchArtifactSalvageEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoArtifactSalvageTask(int.Parse(Config.AutoArtifactSalvageConfig.MaxArtifactStar), Config.AutoArtifactSalvageConfig.RegularExpression, Config.AutoArtifactSalvageConfig.MaxNumToCheck));
        SwitchArtifactSalvageEnabled = false;
    }

    [RelayCommand]
    private void OnOpenArtifactSalvageTestOCRWindow()
    {
        if (!TaskContext.Instance().IsInitialized)
        {
            PromptDialog.Prompt("请先启动截图器！", "");    // todo 自动启动截图器
            return;
        }
        OcrDialog ocrDialog = new OcrDialog(0.70, 0.098, 0.24, 0.52, "圣遗物分解", this.Config.AutoArtifactSalvageConfig.RegularExpression);
        ocrDialog.ShowDialog();
    }

    [RelayCommand]
    private async Task OnSwitchGetGridIcons()
    {
        try
        {
            SwitchGetGridIconsEnabled = true;
            await new TaskRunner().RunSoloTaskAsync(new GetGridIconsTask(Config.GetGridIconsConfig.GridName, Config.GetGridIconsConfig.MaxNumToGet));
        }
        finally
        {
            SwitchGetGridIconsEnabled = false;
        }
    }

    [RelayCommand]
    private void OnGoToGridIconsFolder()
    {
        var path = Global.Absolute(@"log\gridIcons\");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        Process.Start("explorer.exe", path);
    }

    // AI数据采集器相关命令
    [RelayCommand]
    private async Task OnSwitchDataCollector()
    {
        try
        {
            if (_currentDataCollectorTask == null)
            {
                // 启动数据采集器
                SwitchDataCollectorEnabled = true;
                DataCollectorStatusText = "等待触发器";
                DataCollectorActionButtonEnabled = true;
                UpdateDataCollectorActionButton();

                var param = new DataCollectorParam();
                param.SetDefault();

                _currentDataCollectorTask = new DataCollectorTask(param);
                _dataCollectorCts = new CancellationTokenSource();

                // 在后台运行任务
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _currentDataCollectorTask.Start(_dataCollectorCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        UIDispatcherHelper.Invoke(() =>
                        {
                            Toast.Information("数据采集已停止");
                        });
                    }
                    catch (OutOfMemoryException e)
                    {
                        UIDispatcherHelper.Invoke(() =>
                        {
                            Toast.Error($"内存不足，数据采集已停止: {e.Message}");
                            ResetDataCollectorUI(); // OOM异常时reset UI
                        });
                    }
                    catch (Exception e)
                    {
                        UIDispatcherHelper.Invoke(() =>
                        {
                            Toast.Error($"数据采集失败: {e.Message}");
                            // 非OOM异常不reset UI，让任务继续运行
                        });
                    }
                    finally
                    {
                        // 无论任务如何结束，都重置UI状态
                        UIDispatcherHelper.Invoke(() =>
                        {
                            ResetDataCollectorUI();
                        });
                    }
                });

                // 启动状态监控
                StartDataCollectorStatusMonitoring();
            }
            else
            {
                // 停止数据采集器 - 只停止采集，不停止整个任务
                if (_currentDataCollectorTask != null)
                {
                    try
                    {
                        Debug.WriteLine("用户请求停止数据采集器");
                        _currentDataCollectorTask.RequestStop();

                        // 更新UI状态为等待触发，不要reset整个UI
                        SwitchDataCollectorEnabled = false;
                        DataCollectorStatusText = "等待触发器";
                        DataCollectorActionButtonEnabled = true;
                        UpdateDataCollectorActionButton();

                        Toast.Information("数据采集已停止");
                    }
                    catch (Exception ex)
                    {
                        Toast.Error($"停止数据采集失败: {ex.Message}");
                        ResetDataCollectorUI();
                    }
                }
                else
                {
                    ResetDataCollectorUI();
                }
            }
        }
        catch (Exception e)
        {
            Toast.Error($"数据采集器操作失败: {e.Message}");
            ResetDataCollectorUI();
        }
    }

    [RelayCommand]
    private void OnDataCollectorAction()
    {
        if (_currentDataCollectorTask == null) return;

        try
        {
            var currentState = _currentDataCollectorTask.GetCurrentState();
            if (currentState == DataCollectorState.WaitingTrigger)
            {
                _currentDataCollectorTask.StartCollectionManually();
                // 立即更新UI状态
                UpdateDataCollectorActionButton();
            }
            else if (currentState == DataCollectorState.Collecting)
            {
                _currentDataCollectorTask.StopCollectionManually();
                // 立即更新UI状态
                UpdateDataCollectorActionButton();
            }
        }
        catch (Exception ex)
        {
            Toast.Error($"数据采集操作失败: {ex.Message}");
            // 发生异常时重置UI
            ResetDataCollectorUI();
        }
    }

    private void ResetDataCollectorUI()
    {
        SwitchDataCollectorEnabled = false;
        DataCollectorStatusText = "已停止";
        DataCollectorActionButtonEnabled = false;
        DataCollectorActionButtonText = "开始采集";

        // 清理任务引用和取消令牌源
        var oldTask = _currentDataCollectorTask;
        var oldCts = _dataCollectorCts;
        _currentDataCollectorTask = null;
        _dataCollectorCts = null;

        // 如果有旧任务，尝试清理资源（但不重复调用RequestStop）
        if (oldTask != null)
        {
            try
            {
                // 这里不需要调用RequestStop，因为调用ResetDataCollectorUI之前已经调用过了
                Debug.WriteLine("清理旧数据采集任务引用");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"清理旧数据采集任务时发生异常: {ex.Message}");
            }
        }

        // 清理取消令牌源
        if (oldCts != null)
        {
            try
            {
                oldCts.Cancel();
                oldCts.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"清理取消令牌源时发生异常: {ex.Message}");
            }
        }
    }

    private void UpdateDataCollectorActionButton()
    {
        if (_currentDataCollectorTask == null)
        {
            DataCollectorActionButtonEnabled = false;
            return;
        }

        var currentState = _currentDataCollectorTask.GetCurrentState();
        switch (currentState)
        {
            case DataCollectorState.WaitingTrigger:
                DataCollectorStatusText = "等待触发器";
                DataCollectorActionButtonText = "开始采集";
                DataCollectorActionButtonEnabled = true;
                break;
            case DataCollectorState.Collecting:
                DataCollectorStatusText = "采集中";
                DataCollectorActionButtonText = "停止采集";
                DataCollectorActionButtonEnabled = true;
                break;
            case DataCollectorState.Stopped:
                DataCollectorStatusText = "已停止";
                DataCollectorActionButtonEnabled = false;
                break;
        }
    }

    private void StartDataCollectorStatusMonitoring()
    {
        // 每秒检查一次状态
        var timer = new System.Timers.Timer(1000);
        timer.Elapsed += (sender, e) =>
        {
            if (_currentDataCollectorTask == null)
            {
                timer.Stop();
                return;
            }

            UIDispatcherHelper.Invoke(() =>
            {
                // 检查任务是否已经结束
                var currentState = _currentDataCollectorTask.GetCurrentState();
                if (currentState == DataCollectorState.Stopped)
                {
                    // 任务已结束，重置UI
                    ResetDataCollectorUI();
                    timer.Stop();
                    return;
                }

                // 根据状态更新UI
                switch (currentState)
                {
                    case DataCollectorState.WaitingTrigger:
                        DataCollectorStatusText = "等待触发器";
                        break;
                    case DataCollectorState.Collecting:
                        DataCollectorStatusText = "正在采集";
                        break;
                }

                UpdateDataCollectorActionButton();
            });
        };
        timer.Start();
    }

    [RelayCommand]
    private void OnOpenDatasetFolder()
    {
        var path = Global.Absolute(Config.DataCollectorConfig.DatasetPath);
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        Process.Start("explorer.exe", path);
    }

    [RelayCommand]
    private async Task OnGoToDataCollectorDocAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://github.com/babalae/better-genshin-impact"));
    }
}