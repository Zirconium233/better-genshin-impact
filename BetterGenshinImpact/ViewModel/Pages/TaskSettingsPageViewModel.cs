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
using BetterGenshinImpact.GameTask.AIEnv;
using BetterGenshinImpact.GameTask.AIEnv.Schedulers;
using BetterGenshinImpact.GameTask.AIEnv.Environment;
using Microsoft.Extensions.Logging;
using BetterGenshinImpact.Helpers;
using Wpf.Ui;
using Wpf.Ui.Violeta.Controls;
using BetterGenshinImpact.ViewModel.Pages.View;
using System.Linq;
using System.Reflection;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.GameTask.GetGridIcons;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.GameTask.DataCollector;
using BetterGenshinImpact.Model;
using System.Windows.Forms;
using Fischless.HotkeyCapture;
using BetterGenshinImpact.GameTask.UseRedeemCode;
using TextBox = Wpf.Ui.Controls.TextBox;

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
    
    
    [ObservableProperty]
    private bool _switchAutoRedeemCodeEnabled;

    [ObservableProperty]
    private string _switchAutoRedeemCodeButtonText = "启动";

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
    private KeyboardHook? _dataCollectorHotkey;

    // AI环境相关属性
    [ObservableProperty]
    private bool _switchAIEnvEnabled;

    [ObservableProperty]
    private string _switchAIEnvButtonText = "启动AI环境";

    [ObservableProperty]
    private string _aiEnvStatus = "未启动";

    private AIEnvTask? _currentAIEnvTask;
    private CancellationTokenSource? _aiEnvCts;

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

        // 初始化数据采集器快捷键 (反引号键)
        _dataCollectorHotkey = new KeyboardHook();
        _dataCollectorHotkey.KeyPressedEvent += OnDataCollectorHotkeyPressed;
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
            await new TaskRunner().RunSoloTaskAsync(new GetGridIconsTask(Config.GetGridIconsConfig.GridName, Config.GetGridIconsConfig.StarAsSuffix, Config.GetGridIconsConfig.MaxNumToGet));
        }
        finally
        {
            SwitchGetGridIconsEnabled = false;
        }
    }

    [RelayCommand]
    private void OnGoToGetGridIconsFolder()
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
                // 启动数据采集独立任务
                SwitchDataCollectorEnabled = true;
                DataCollectorStatusText = "等待触发器";
                DataCollectorActionButtonEnabled = true;
                DataCollectorActionButtonText = "开始采集";

                // 每次启动都创建新的参数对象，确保使用最新配置
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
                            Toast.Information("数据采集任务已停止");
                            ResetDataCollectorUI();
                        });
                    }
                    catch (OutOfMemoryException e)
                    {
                        UIDispatcherHelper.Invoke(() =>
                        {
                            Toast.Error($"内存不足，数据采集任务已停止: {e.Message}");
                            ResetDataCollectorUI(); // OOM异常时reset UI
                        });
                    }
                    catch (Exception e)
                    {
                        UIDispatcherHelper.Invoke(() =>
                        {
                            Toast.Error($"数据采集任务失败: {e.Message}");
                            ResetDataCollectorUI();
                        });
                    }
                });

                // 启动状态监控
                StartDataCollectorStatusMonitoring();

                // 注册快捷键 (反引号键)
                _dataCollectorHotkey?.RegisterHotKey(Keys.Oemtilde);

                Toast.Information("数据采集任务已启动，按 ` 键切换采集状态");
            }
            else
            {
                // 停止数据采集独立任务
                Debug.WriteLine("用户请求停止数据采集任务");
                _currentDataCollectorTask.RequestFullStop();
                _dataCollectorCts?.Cancel();

                // 注销快捷键
                _dataCollectorHotkey?.UnregisterHotKey();

                // 立即重置UI到初始状态，确保下次启动时重新创建任务实例
                ResetDataCollectorUI();
                Toast.Information("数据采集任务已停止");
            }
        }
        catch (Exception e)
        {
            Toast.Error($"数据采集器操作失败: {e.Message}");
            ResetDataCollectorUI();
        }
    }

    [RelayCommand]
    private async Task OnDataCollectorAction()
    {
        if (_currentDataCollectorTask == null) return;

        try
        {
            // 每次操作前更新任务参数，确保使用最新配置
            _currentDataCollectorTask.UpdateTaskParam();

            var currentState = _currentDataCollectorTask.GetCurrentState();
            if (currentState == DataCollectorState.WaitingTrigger)
            {
                // 从等待触发状态切换到采集状态
                await _currentDataCollectorTask.RequestStop(); // 这里利用RequestStop的逻辑，它会检测当前状态

                // 立即更新UI状态
                DataCollectorStatusText = "正在采集";
                DataCollectorActionButtonText = "停止采集(`)";
                Toast.Information("开始数据采集");
            }
            else if (currentState == DataCollectorState.Collecting)
            {
                // 从采集状态切换到等待触发状态
                await _currentDataCollectorTask.RequestStop(); // 这里利用RequestStop的逻辑，它会检测当前状态

                // 立即更新UI状态
                DataCollectorStatusText = "等待触发器";
                DataCollectorActionButtonText = "开始采集";
                Toast.Information("停止数据采集");
            }
            // 移除Stopped状态的处理，因为Stopped状态应该自动转换，不需要手动重启
        }
        catch (Exception ex)
        {
            Toast.Error($"数据采集操作失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 数据采集器快捷键事件处理 (反引号键)
    /// </summary>
    private async void OnDataCollectorHotkeyPressed(object? sender, KeyPressedEventArgs e)
    {
        // 只有在数据采集器启动时才响应快捷键
        if (_currentDataCollectorTask == null || !DataCollectorActionButtonEnabled) return;

        try
        {
            // 调用数据采集操作，相当于点击了采集按钮
            await OnDataCollectorAction();
        }
        catch (Exception ex)
        {
            UIDispatcherHelper.Invoke(() =>
            {
                Toast.Error($"快捷键操作失败: {ex.Message}");
            });
        }
    }

    private void ResetDataCollectorUI()
    {
        // 注销快捷键
        _dataCollectorHotkey?.UnregisterHotKey();

        // 更新UI状态
        SwitchDataCollectorEnabled = false;
        DataCollectorStatusText = "已停止";
        DataCollectorActionButtonEnabled = false;
        DataCollectorActionButtonText = "开始采集";

        // 保存对旧任务和取消令牌的引用，以便清理
        var oldTask = _currentDataCollectorTask;
        var oldCts = _dataCollectorCts;

        // 立即清空引用，确保下次启动时创建新实例
        _currentDataCollectorTask = null;
        _dataCollectorCts = null;

        // 如果有旧任务，尝试清理资源
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



    private void StartDataCollectorStatusMonitoring()
    {
        // 每秒检查一次状态
        var timer = new System.Timers.Timer(1000);
        timer.Elapsed += (sender, e) =>
        {
            if (_currentDataCollectorTask == null || _dataCollectorCts?.IsCancellationRequested == true)
            {
                timer.Stop();
                return;
            }

            UIDispatcherHelper.Invoke(() =>
            {
                // 检查任务是否已经结束
                var currentState = _currentDataCollectorTask.GetCurrentState();

                // 根据状态更新UI
                switch (currentState)
                {
                    case DataCollectorState.Stopped:
                        // 采集停止，显示等待后处理，禁用按钮
                        DataCollectorStatusText = "等待后处理";
                        DataCollectorActionButtonEnabled = false;
                        DataCollectorActionButtonText = "等待后处理";
                        break;
                    case DataCollectorState.WaitingTrigger:
                        DataCollectorStatusText = "等待触发器";
                        DataCollectorActionButtonEnabled = true;
                        DataCollectorActionButtonText = "开始采集";
                        break;
                    case DataCollectorState.Collecting:
                        DataCollectorStatusText = "正在采集";
                        DataCollectorActionButtonEnabled = true;
                        DataCollectorActionButtonText = "停止采集";
                        break;
                }
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

    // AI环境相关命令
    [RelayCommand]
    private async Task OnSwitchAIEnv()
    {
        try
        {
            if (!SwitchAIEnvEnabled)
            {
                // 启动AI环境
                if (!TaskContext.Instance().IsInitialized)
                {
                    Toast.Warning("请先启动截图器！");
                    return;
                }

                SwitchAIEnvEnabled = true;
                SwitchAIEnvButtonText = "停止";
                AiEnvStatus = "启动中...";

                _aiEnvCts = new CancellationTokenSource();
                var param = new AIEnvParam();
                _currentAIEnvTask = new AIEnvTask(param);

                // 在后台运行任务
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _currentAIEnvTask.Start(_aiEnvCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        UIDispatcherHelper.Invoke(() =>
                        {
                            Toast.Information("AI环境任务已停止");
                            ResetAIEnvUI();
                        });
                    }
                    catch (Exception e)
                    {
                        UIDispatcherHelper.Invoke(() =>
                        {
                            Toast.Error($"AI环境任务失败: {e.Message}");
                            ResetAIEnvUI();
                        });
                    }
                });

                // 启动状态监控
                StartAIEnvStatusMonitoring();

                Toast.Information("AI环境任务已启动");
            }
            else
            {
                // 停止AI环境
                StopAIEnv();
            }
        }
        catch (Exception ex)
        {
            Toast.Error($"操作AI环境失败: {ex.Message}");
            ResetAIEnvUI();
        }
    }

    [RelayCommand]
    private void OnSendUserPrompt()
    {
        if (_currentAIEnvTask == null || !SwitchAIEnvEnabled)
        {
            Toast.Warning("AI环境未启动");
            return;
        }

        try
        {
            // 获取用户输入的提示词
            var prompt = Config.AIEnvConfig.UserPrompt;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                Toast.Warning("请先设置用户提示词");
                return;
            }

            _currentAIEnvTask.SendUserPrompt(prompt);
            Toast.Information($"已发送用户指令: {prompt}");
        }
        catch (Exception ex)
        {
            Toast.Error($"发送用户指令失败: {ex.Message}");
        }
    }

    private void StopAIEnv()
    {
        try
        {
            _aiEnvCts?.Cancel();
            _currentAIEnvTask?.Stop();
            ResetAIEnvUI();
            Toast.Information("AI环境已停止");
        }
        catch (Exception ex)
        {
            Toast.Error($"停止AI环境失败: {ex.Message}");
        }
    }

    private void ResetAIEnvUI()
    {
        SwitchAIEnvEnabled = false;
        SwitchAIEnvButtonText = "启动AI环境";
        AiEnvStatus = "未启动";
    }

    private void StartAIEnvStatusMonitoring()
    {
        // 每秒检查一次状态
        var timer = new System.Timers.Timer(1000);
        timer.Elapsed += (sender, e) =>
        {
            if (_currentAIEnvTask == null || _aiEnvCts?.IsCancellationRequested == true)
            {
                timer.Stop();
                return;
            }

            UIDispatcherHelper.Invoke(() =>
            {
                try
                {
                    var status = _currentAIEnvTask.GetStatus();
                    AiEnvStatus = status;
                }
                catch (Exception ex)
                {
                    AiEnvStatus = $"状态获取失败: {ex.Message}";
                }
            });
        };
        timer.Start();
    }

    #region LLM调度器测试方法

    [RelayCommand]
    private async Task OnTestLlmObsAndPrompt()
    {
        // 检查调度器类型
        if (Config.AIEnvConfig.SchedulerType != "LlmApiScheduler")
        {
            Toast.Warning("请先选择LLM API调度器");
            return;
        }

        if (_currentAIEnvTask == null || !SwitchAIEnvEnabled)
        {
            Toast.Warning("AI环境未启动");
            return;
        }

        try
        {
            var scheduler = _currentAIEnvTask.GetScheduler() as LlmApiScheduler;
            if (scheduler == null)
            {
                Toast.Warning("当前调度器不是LLM调度器");
                return;
            }

            await scheduler.TestObsAndPrompt();
            Toast.Information("Obs和Prompt测试完成，请查看日志");
        }
        catch (Exception ex)
        {
            Toast.Error($"Obs和Prompt测试失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task OnTestLlmTriggers()
    {
        // 检查调度器类型
        if (Config.AIEnvConfig.SchedulerType != "LlmApiScheduler")
        {
            Toast.Warning("请先选择LLM API调度器");
            return;
        }

        if (_currentAIEnvTask == null || !SwitchAIEnvEnabled)
        {
            Toast.Warning("AI环境未启动");
            return;
        }

        try
        {
            var scheduler = _currentAIEnvTask.GetScheduler() as LlmApiScheduler;
            if (scheduler == null)
            {
                Toast.Warning("当前调度器不是LLM调度器");
                return;
            }

            await scheduler.TestTriggers();
            Toast.Information("触发器测试完成，请查看日志");
        }
        catch (Exception ex)
        {
            Toast.Error($"触发器测试失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task OnTestLlmApiCall()
    {
        // 检查调度器类型
        if (Config.AIEnvConfig.SchedulerType != "LlmApiScheduler")
        {
            Toast.Warning("请先选择LLM API调度器");
            return;
        }

        if (_currentAIEnvTask == null || !SwitchAIEnvEnabled)
        {
            Toast.Warning("AI环境未启动");
            return;
        }

        try
        {
            var scheduler = _currentAIEnvTask.GetScheduler() as LlmApiScheduler;
            if (scheduler == null)
            {
                Toast.Warning("当前调度器不是LLM调度器");
                return;
            }

            await scheduler.TestApiCall();
            Toast.Information("API调用测试完成，请查看日志");
        }
        catch (Exception ex)
        {
            Toast.Error($"API调用测试失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task OnTestLlmComplete()
    {
        // 检查调度器类型
        if (Config.AIEnvConfig.SchedulerType != "LlmApiScheduler")
        {
            Toast.Warning("请先选择LLM API调度器");
            return;
        }

        if (_currentAIEnvTask == null || !SwitchAIEnvEnabled)
        {
            Toast.Warning("AI环境未启动");
            return;
        }

        try
        {
            var scheduler = _currentAIEnvTask.GetScheduler() as LlmApiScheduler;
            if (scheduler == null)
            {
                Toast.Warning("当前调度器不是LLM调度器");
                return;
            }

            await scheduler.TestComplete();
            Toast.Information("完整测试完成，请查看日志");
        }
        catch (Exception ex)
        {
            Toast.Error($"完整测试失败: {ex.Message}");
        }
    }

    #endregion

    #region AI环境测试命令

    [RelayCommand]
    private async Task RunActionTestAsync()
    {
        try
        {
            Toast.Information("开始执行动作测试...");

            if (_currentAIEnvTask?.GetAIEnvironment() == null)
            {
                Toast.Warning("AI环境未启动，请先启动AI环境");
                return;
            }

            var aiEnv = _currentAIEnvTask.GetAIEnvironment()!;
            var logger = App.GetLogger<TaskSettingsPageViewModel>();

            // 第一组：正常走路测试
            logger.LogInformation("=== 第一组：正常走路测试 ===");
            Toast.Information("执行第一组：正常走路测试");
            aiEnv.AddCommands("w(2.0)");
            await Task.Delay(2500);
            aiEnv.AddCommands("a(1.5)");
            await Task.Delay(2000);
            aiEnv.AddCommands("s(1.0)");
            await Task.Delay(1500);
            aiEnv.AddCommands("d(1.0)");
            await Task.Delay(1500);

            // 第二组：切人和转鼠标测试
            logger.LogInformation("=== 第二组：切人和转鼠标测试 ===");
            Toast.Information("执行第二组：切人和转鼠标测试");
            aiEnv.AddCommands("sw(1)");
            await Task.Delay(1000);
            aiEnv.AddCommands("sw(2)");
            await Task.Delay(1000);
            aiEnv.AddCommands("moveby(100,50)");
            await Task.Delay(500);
            aiEnv.AddCommands("moveby(-100,-50)");
            await Task.Delay(500);

            // 第三组：放技能测试
            logger.LogInformation("=== 第三组：放技能测试 ===");
            Toast.Information("执行第三组：放技能测试");
            aiEnv.AddCommands("e");
            await Task.Delay(1000);
            aiEnv.AddCommands("q");
            await Task.Delay(1000);
            aiEnv.AddCommands("attack(3)"); // 基于次数的攻击
            await Task.Delay(1000);
            aiEnv.AddCommands("attack(0.5)"); // 基于时间的攻击
            await Task.Delay(1000);
            aiEnv.AddCommands("charge(1.0)");
            await Task.Delay(1500);

            // 第四组：动作组覆盖测试
            logger.LogInformation("=== 第四组：动作组覆盖测试 ===");
            Toast.Information("执行第四组：动作组覆盖测试");

            // 先发送长时间动作
            aiEnv.AddCommands("w(5)&a(2),e");
            logger.LogInformation("已发送: w(5)&a(2),e");

            // 等待1秒后发送覆盖动作
            await Task.Delay(1000);
            aiEnv.AddCommands("s(2)&w(1),e(hold)");
            logger.LogInformation("已发送覆盖动作: s(2)&w(1),e(hold)");

            // 等待动作完成
            await Task.Delay(3000);

            // 第五组：额外测试 - 主要命令顺序执行
            logger.LogInformation("=== 第五组：额外测试 - 主要命令顺序执行 ===");
            Toast.Information("执行第五组：主要命令顺序执行测试");

            // 顺序执行主要命令：attack(次数), attack(时间), e, charge, jump, f, q, e(hold)
            aiEnv.AddCommands("attack(2),attack(0.3),e,charge(1.0),jump,f,q,e(hold)");
            logger.LogInformation("已发送主要命令序列: attack(2),attack(0.3),e,charge(1.0),jump,f,q,e(hold)");

            // 等待所有动作完成
            await Task.Delay(5000);

            Toast.Success("动作测试完成！请查看日志了解详细执行情况");
        }
        catch (Exception ex)
        {
            Toast.Error($"执行动作测试时发生错误: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RunObservationTestAsync()
    {
        try
        {
            Toast.Information("开始Obs测试...");

            if (_currentAIEnvTask?.GetAIEnvironment() == null)
            {
                Toast.Warning("AI环境未启动，请先启动AI环境");
                return;
            }

            var aiEnv = _currentAIEnvTask.GetAIEnvironment()!;
            var logger = App.GetLogger<TaskSettingsPageViewModel>();

            // 获取观察数据
            var observation = aiEnv.GetLatestObservation();
            if (observation == null)
            {
                Toast.Warning("无法获取观察数据，请确保AI环境正在运行");
                return;
            }

            // 解析图片信息（不打印base64）
            var frameInfo = "无图片数据";
            if (!string.IsNullOrEmpty(observation.FrameBase64))
            {
                try
                {
                    var imageBytes = Convert.FromBase64String(observation.FrameBase64);
                    frameInfo = $"图片大小: {imageBytes.Length} bytes, Base64长度: {observation.FrameBase64.Length} chars";
                }
                catch
                {
                    frameInfo = "图片数据格式错误";
                }
            }

            // 转换为JSON格式输出（不包含base64）
            var obsInfo = new
            {
                timestamp_ms = observation.TimestampMs,
                frame_info = frameInfo,
                structured_state = observation.StructuredState,
                action_queue_status = observation.ActionQueueStatus
            };

            var json = System.Text.Json.JsonSerializer.Serialize(obsInfo, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            logger.LogInformation("=== 观察数据格式测试 ===");
            logger.LogInformation("观察数据JSON格式:\n{ObsJson}", json);

            // 添加几个动作组再观察
            Toast.Information("添加长时间动作并观察队列变化...");
            aiEnv.AddCommands("w(1.0)");
            aiEnv.AddCommands("a(1.5)");
            aiEnv.AddCommands("charge(2.0)");

            await Task.Delay(500); // 等待队列更新

            var observation2 = aiEnv.GetLatestObservation();
            if (observation2 != null)
            {
                var obsInfo2 = new
                {
                    timestamp_ms = observation2.TimestampMs,
                    action_queue_status = observation2.ActionQueueStatus
                };

                var json2 = System.Text.Json.JsonSerializer.Serialize(obsInfo2, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

                logger.LogInformation("=== 添加动作后的队列状态 ===");
                logger.LogInformation("动作队列状态:\n{QueueJson}", json2);
            }

            Toast.Success("Obs测试完成！请查看日志了解观察数据格式");
        }
        catch (Exception ex)
        {
            Toast.Error($"执行Obs测试时发生错误: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RunPerformanceTestAsync()
    {
        try
        {
            Toast.Information("开始性能测试...");

            if (_currentAIEnvTask?.GetAIEnvironment() == null)
            {
                Toast.Warning("AI环境未启动，请先启动AI环境");
                return;
            }

            var aiEnv = _currentAIEnvTask.GetAIEnvironment()!;
            var logger = App.GetLogger<TaskSettingsPageViewModel>();

            // 组1：快速获取测试
            await RunQuickObservationTest(aiEnv, logger);

            // 组2：观察间隔测试
            await RunObservationIntervalTest(aiEnv, logger);

            // 组3：详细性能统计测试
            await RunDetailedPerformanceTest(aiEnv, logger);

            Toast.Success("性能测试完成！请查看日志了解详细结果");
        }
        catch (Exception ex)
        {
            Toast.Error($"执行性能测试时发生错误: {ex.Message}");
        }
    }

    private async Task RunQuickObservationTest(AIEnvironment aiEnv, ILogger logger)
    {
        logger.LogInformation("=== 组1：快速获取测试 ===");
        var times = new List<double>();

        for (int i = 0; i < 5; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            var observation = aiEnv.GetLatestObservation();
            stopwatch.Stop();

            var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
            times.Add(elapsedMs);

            logger.LogInformation("第{Index}次快速获取耗时: {ElapsedMs:F2}ms", i + 1, elapsedMs);
            await Task.Delay(10); // 短暂延迟
        }

        var avgTime = times.Average();
        logger.LogInformation("快速获取平均耗时: {AvgTime:F2}ms", avgTime);
    }

    private async Task RunObservationIntervalTest(AIEnvironment aiEnv, ILogger logger)
    {
        logger.LogInformation("=== 组2：观察间隔测试 ===");

        // 从配置获取环境FPS
        var envFps = Config.AIEnvConfig.EnvFps;
        var expectedIntervalMs = 1000.0 / envFps;
        logger.LogInformation("环境配置FPS: {EnvFps}, 预期间隔: {ExpectedInterval:F2}ms", envFps, expectedIntervalMs);

        var timestamps = new List<long>();
        var firstObservation = aiEnv.GetLatestObservation();
        if (firstObservation == null)
        {
            logger.LogWarning("无法获取初始观察数据");
            return;
        }

        var baseTimestamp = firstObservation.TimestampMs;
        timestamps.Add(0); // 第一帧偏移为0

        // 50ms频率获取观察数据，共获取20次
        for (int i = 1; i < 20; i++)
        {
            await Task.Delay(50);
            var observation = aiEnv.GetLatestObservation();
            if (observation != null)
            {
                var offset = observation.TimestampMs - baseTimestamp;
                timestamps.Add(offset);
                logger.LogInformation("第{Index}次观察，时间戳偏移: {Offset}ms", i + 1, offset);
            }
        }

        // 分析时间戳偏移
        var intervals = new List<long>();
        for (int i = 1; i < timestamps.Count; i++)
        {
            var interval = timestamps[i] - timestamps[i - 1];
            if (interval > 0) // 只记录有变化的间隔
            {
                intervals.Add(interval);
            }
        }

        if (intervals.Count > 0)
        {
            var minInterval = intervals.Min();
            var maxInterval = intervals.Max();
            var avgInterval = intervals.Average();
            var stdDev = Math.Sqrt(intervals.Select(x => Math.Pow(x - avgInterval, 2)).Average());

            logger.LogInformation("=== 帧更新频率分析 ===");
            logger.LogInformation("最小更新间隔: {MinInterval}ms", minInterval);
            logger.LogInformation("最大更新间隔: {MaxInterval}ms", maxInterval);
            logger.LogInformation("平均更新间隔: {AvgInterval:F2}ms", avgInterval);
            logger.LogInformation("标准差: {StdDev:F2}ms", stdDev);
            logger.LogInformation("与预期间隔偏差: {Deviation:F2}ms", Math.Abs(avgInterval - expectedIntervalMs));
        }
        else
        {
            logger.LogWarning("未检测到帧更新");
        }
    }

    private async Task RunDetailedPerformanceTest(AIEnvironment aiEnv, ILogger logger)
    {
        logger.LogInformation("=== 组3：详细性能统计测试 ===");

        // 开启AI环境的性能统计
        aiEnv.EnablePerformanceStats(true);

        // 等待收集10次统计数据
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(250); // 等待一个观察周期
            var observation = aiEnv.GetLatestObservation();
            logger.LogInformation("第{Index}次详细性能测试观察完成", i + 1);
        }

        // 获取并打印性能统计
        var stats = aiEnv.GetPerformanceStats();
        logger.LogInformation("=== 详细性能统计结果 ===");
        logger.LogInformation("帧提取平均时间: {CaptureTime:F2}ms", stats.AvgCaptureTimeMs);
        logger.LogInformation("帧压缩平均时间: {CompressTime:F2}ms", stats.AvgCompressTimeMs);
        logger.LogInformation("状态提取平均时间: {StateTime:F2}ms", stats.AvgStateExtractionTimeMs);
        logger.LogInformation("队列获取平均时间: {QueueTime:F2}ms", stats.AvgQueueTimeMs);
        logger.LogInformation("总处理平均时间: {TotalTime:F2}ms", stats.AvgTotalTimeMs);

        // 关闭性能统计
        aiEnv.EnablePerformanceStats(false);
    }

    [RelayCommand]
    private async Task RunExceptionTestAsync()
    {
        try
        {
            Toast.Information("开始异常测试...");

            if (_currentAIEnvTask?.GetAIEnvironment() == null)
            {
                Toast.Warning("AI环境未启动，请先启动AI环境");
                return;
            }

            var aiEnv = _currentAIEnvTask.GetAIEnvironment()!;
            var logger = App.GetLogger<TaskSettingsPageViewModel>();

            logger.LogInformation("=== 异常测试：极端输入和错误处理 ===");

            // 重置错误计数
            aiEnv.ResetErrorCount();
            var initialErrorCount = aiEnv.GetErrorCount();
            logger.LogInformation("初始错误计数: {InitialCount}", initialErrorCount);

            // 测试各种不合法输入
            var invalidInputs = new[]
            {
                "", // 空字符串
                "   ", // 空白字符
                "invalid_action", // 无效动作
                "w()", // 缺少参数
                "w(-1)", // 负数参数
                "w(999999)", // 超大参数
                "w(abc)", // 非数字参数
                "w(1)&w(2)&w(3)&w(4)&w(5)", // 过多同时动作
                "w(1),a(2),s(3),d(4),e,q,attack(5),attack(0.8),charge(2),sw(1),sw(2)", // 过长指令，包含两种attack语法
                "w(1)&", // 语法错误
                ",w(1)", // 语法错误
                "w(1),,a(2)", // 双逗号
                "w(1)&&a(2)", // 双&符号
                "invalid_action2", // 再次无效动作
                "w(-999)", // 再次负数
                "超长动作名称测试", // 中文无效动作
                "null", // 字符串null
                "undefined" // 未定义动作
            };

            var testExceptionCount = 0;
            foreach (var input in invalidInputs)
            {
                try
                {
                    logger.LogInformation("测试输入: '{Input}'", input);
                    aiEnv.AddCommands(input);
                    await Task.Delay(100);

                    // 检查环境错误计数是否增加
                    var currentErrorCount = aiEnv.GetErrorCount();
                    if (currentErrorCount > initialErrorCount)
                    {
                        logger.LogInformation("输入 '{Input}' 导致环境内部错误计数增加到: {ErrorCount}", input, currentErrorCount);
                        initialErrorCount = currentErrorCount;
                    }
                }
                catch (Exception ex)
                {
                    testExceptionCount++;
                    logger.LogWarning("输入 '{Input}' 引发测试异常: {Error}", input, ex.Message);
                }

                // 检查环境是否因错误过多而停止
                if (!aiEnv.IsRunning)
                {
                    logger.LogWarning("环境在处理输入 '{Input}' 后停止运行", input);
                    break;
                }
            }

            logger.LogInformation("=== 异常测试结果 ===");
            logger.LogInformation("总测试输入: {TotalInputs}", invalidInputs.Length);
            logger.LogInformation("测试过程中引发异常数量: {TestExceptionCount}", testExceptionCount);

            // 检查最终状态
            var isStillRunning = aiEnv.IsRunning;
            var finalErrorCount = aiEnv.GetErrorCount();
            logger.LogInformation("环境运行状态: {IsRunning}", isStillRunning ? "正常运行" : "已停止");
            logger.LogInformation("环境最终错误计数: {FinalErrorCount}", finalErrorCount);

            if (finalErrorCount >= 5)
            {
                logger.LogWarning("环境内部错误数量达到{ErrorCount}次，检查环境是否应该终止", finalErrorCount);
                if (isStillRunning)
                {
                    Toast.Warning($"环境在{finalErrorCount}次内部错误后仍在运行，可能需要改进错误处理机制");
                }
                else
                {
                    Toast.Information($"环境在{finalErrorCount}次内部错误后正确终止运行");
                }
            }
            else
            {
                Toast.Success($"异常测试完成！环境内部错误计数: {finalErrorCount}，测试异常: {testExceptionCount}");
            }
        }
        catch (Exception ex)
        {
            Toast.Error($"执行异常测试时发生错误: {ex.Message}");
        }
    }

    #endregion
    [RelayCommand]
    private async Task OnGoToGetGridIconsUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://bettergi.com/feats/task/getGridIcons.html"));
    }
    
    [RelayCommand]
    private async Task OnSwitchAutoRedeemCode()
    {
        var multilineTextBox = new TextBox
        {
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            PlaceholderText = "请在此输入兑换码，每行一条记录"
        };
        var p = new PromptDialog(
            "输入兑换码",
            "自动使用兑换码",
            multilineTextBox,
            null);
        p.Height = 500;
        p.ShowDialog();
        if (p.DialogResult == true && !string.IsNullOrWhiteSpace(multilineTextBox.Text))
        { 
            char[] separators = ['\r', '\n'];
                 var codes = multilineTextBox.Text.Split(separators, StringSplitOptions.RemoveEmptyEntries)

                .Select(code => code.Trim())
                .Where(code => !string.IsNullOrEmpty(code))
                .ToList();

            if (codes.Count == 0)
            {
                Toast.Warning("没有有效的兑换码");
                return;
            }
            
            SwitchAutoRedeemCodeEnabled = true;
            await new TaskRunner()
                .RunSoloTaskAsync(new UseRedemptionCodeTask(codes));
            SwitchAutoRedeemCodeEnabled = false;
        }
    }
}