using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.DataCollector.Model;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static Vanara.PInvoke.User32;

namespace BetterGenshinImpact.GameTask.DataCollector;

/// <summary>
/// 数据采集器状态
/// </summary>
public enum DataCollectorState
{
    Stopped,        // 已停止
    WaitingTrigger, // 等待触发器
    Collecting      // 采集中
}

/// <summary>
/// AI数据采集器任务
/// </summary>
public class DataCollectorTask : ISoloTask
{
    public string Name => "AI数据采集器";

    private readonly ILogger<DataCollectorTask> _logger = App.GetLogger<DataCollectorTask>();
    private readonly DataCollectorParam _taskParam;
    private readonly DataCollectorConfig _config;
    private readonly InputMonitor _inputMonitor;
    private readonly StateExtractor _stateExtractor;
    private readonly List<DataRecord> _dataBuffer = new();
    private readonly object _bufferLock = new();

    private CancellationToken _ct;
    private CancellationTokenSource? _internalCts;
    private Timer? _collectionTimer;
    private Timer? _triggerCheckTimer;
    private int _frameIndex = 0;
    private long _sessionStartTime;
    private string _sessionPath = string.Empty;
    private string _framesPath = string.Empty;
    private long _lastNoActionFrameTime = 0;
    private DataCollectorState _currentState = DataCollectorState.Stopped;
    private readonly object _stateLock = new();
    private volatile bool _stopRequested = false;

    public DataCollectorTask(DataCollectorParam taskParam)
    {
        _taskParam = taskParam;
        _config = TaskContext.Instance().Config.DataCollectorConfig;
        _inputMonitor = new InputMonitor();
        _stateExtractor = new StateExtractor();
    }

    /// <summary>
    /// 更新任务参数，确保使用最新配置
    /// </summary>
    /// <param name="generateNewSessionId">是否生成新的会话ID，默认为false</param>
    public void UpdateTaskParam(bool generateNewSessionId = false)
    {
        _taskParam.SetDefault(generateNewSessionId);
        if (generateNewSessionId)
        {
            _logger.LogInformation("任务参数已更新为最新配置，并生成了新的会话ID: {SessionId}", _taskParam.SessionId);
        }
        else
        {
            _logger.LogInformation("任务参数已更新为最新配置，保留原会话ID: {SessionId}", _taskParam.SessionId);
        }
    }

    /// <summary>
    /// 生成新的会话ID
    /// </summary>
    public void GenerateNewSessionId()
    {
        _taskParam.GenerateNewSessionId();
        _logger.LogInformation("已生成新的会话ID: {SessionId}", _taskParam.SessionId);
    }

    public async Task Start(CancellationToken ct)
    {
        _ct = ct;
        _internalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _logger.LogInformation("启动AI数据采集器");

        try
        {
            // 不在这里初始化session，等到真正开始采集时再初始化

            // 根据配置决定启动模式
            if (_config.AutoTriggerEnabled)
            {
                await StartTriggerMode();
            }
            else
            {
                await StartManualMode();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("数据采集被取消");
        }
        catch (OutOfMemoryException e)
        {
            _logger.LogError(e, "发生OOM异常，停止数据采集任务");
            _stopRequested = true;
            _internalCts?.Cancel();
            throw; // OOM异常需要向上传播，触发UI reset
        }
        catch (Exception e)
        {
            _logger.LogError(e, "数据采集过程中发生异常，当作手动终止处理");
            // 非OOM异常当作手动终止处理，触发清理和重启
            _ = Task.Run(async () =>
            {
                try
                {
                    await CleanupAndRestart();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "异常处理中的清理和重启失败");
                }
            });
        }
        finally
        {
            await Cleanup();
        }
    }

    /// <summary>
    /// 请求停止数据采集 - 只停止当前采集，触发清理和重启循环
    /// </summary>
    public async Task RequestStop()
    {
        _logger.LogInformation("请求停止数据采集，将触发清理和重启循环");

        DataCollectorState currentState;
        bool shouldStartCollection = false;

        // 在lock中获取状态并执行同步操作
        lock (_stateLock)
        {
            currentState = _currentState;

            if (currentState == DataCollectorState.Collecting)
            {
                // 停止当前采集
                StopDataCollection();

                // 设置状态为停止，保存数据，然后重启
                SetState(DataCollectorState.Stopped);
            }
            else if (currentState == DataCollectorState.WaitingTrigger)
            {
                // 如果当前在等待触发，标记需要开始采集
                shouldStartCollection = true;
            }
        }

        // 在lock外执行异步操作
        if (currentState == DataCollectorState.Collecting)
        {
            // 触发清理缓冲区和重启监视器的逻辑
            _ = Task.Run(async () =>
            {
                try
                {
                    // 短暂延迟，让UI显示"等待后处理"状态
                    await Task.Delay(500);

                    // 清理和重启
                    await CleanupAndRestart();

                    // 自动转换到等待触发状态
                    SetState(DataCollectorState.WaitingTrigger);

                    // 启动触发器检查
                    if (_triggerCheckTimer == null && _config.AutoTriggerEnabled)
                    {
                        _triggerCheckTimer = new Timer(CheckTriggers, null, 0, 500);
                        _logger.LogInformation("触发器检查已启动");
                    }

                    _logger.LogInformation("已自动转换到等待触发状态");
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "清理和重启过程中发生异常");
                    // 如果清理重启失败，设置为等待触发状态
                    SetState(DataCollectorState.WaitingTrigger);
                }
            });
        }
        else if (shouldStartCollection)
        {
            // 如果当前在等待触发，生成新的会话ID并开始采集
            GenerateNewSessionId();

            // 重新初始化会话以使用新的SessionId
            try
            {
                await InitializeSession();

                lock (_stateLock)
                {
                    SetState(DataCollectorState.Collecting);
                    StartDataCollection();
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "重新初始化会话失败");
                return;
            }
        }
    }

    /// <summary>
    /// 请求完全停止任务
    /// </summary>
    public void RequestFullStop()
    {
        _logger.LogInformation("请求完全停止数据采集任务");

        lock (_stateLock)
        {
            // 如果当前正在采集，先停止采集
            if (_currentState == DataCollectorState.Collecting)
            {
                _logger.LogInformation("检测到正在采集，先停止当前采集");
                StopDataCollection();
            }

            _stopRequested = true;
            SetState(DataCollectorState.Stopped);
            _internalCts?.Cancel();
        }
    }

    // 移除RestartToWaitingTrigger方法，因为现在状态自动转换

    /// <summary>
    /// 启动触发器检查
    /// </summary>
    private void StartTriggerChecking()
    {
        if (_triggerCheckTimer == null && _config.AutoTriggerEnabled)
        {
            _triggerCheckTimer = new Timer(CheckTriggers, null, 0, 500);
            _logger.LogInformation("触发器检查已启动");
        }
    }

    /// <summary>
    /// 清理缓冲区并重启监视器
    /// </summary>
    private async Task CleanupAndRestart()
    {
        _logger.LogInformation("开始清理缓冲区和重启监视器");

        try
        {
            // 保存当前数据到文件
            if (_dataBuffer.Count > 0)
            {
                await SaveDataToFile();
                _logger.LogInformation("已保存 {Count} 条数据记录", _dataBuffer.Count);
            }

            // 更新任务参数，确保使用最新配置（但不生成新SessionId，因为会在开始采集时生成）
            UpdateTaskParam(generateNewSessionId: false);

            // 清理缓冲区
            ClearBuffers();

            // 重启监视器（不重新初始化会话，因为会在开始采集时初始化新会话）
            RestartMonitors();

            // 不在这里设置状态，由调用者决定状态

            _logger.LogInformation("清理和重启完成，恢复到等待触发状态");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "清理和重启过程中发生异常");
            throw;
        }
    }

    /// <summary>
    /// 保存当前会话数据
    /// </summary>
    private async Task SaveCurrentSession()
    {
        try
        {
            // 这里可以添加保存逻辑，比如刷新文件缓冲区等
            _logger.LogInformation("保存当前会话数据");
            await Task.Delay(100); // 确保文件写入完成
        }
        catch (Exception e)
        {
            _logger.LogError(e, "保存会话数据时发生异常");
        }
    }

    /// <summary>
    /// 清理缓冲区
    /// </summary>
    private void ClearBuffers()
    {
        try
        {
            _logger.LogInformation("清理缓冲区");
            _frameIndex = 0;

            // 清理数据缓冲区
            lock (_bufferLock)
            {
                _dataBuffer.Clear();
                _logger.LogInformation("数据缓冲区已清理，缓冲区大小: {Count}", _dataBuffer.Count);
            }

            // 清理输入事件
            _inputMonitor?.ClearEvents();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "清理缓冲区时发生异常");
        }
    }

    /// <summary>
    /// 重启监视器
    /// </summary>
    private void RestartMonitors()
    {
        try
        {
            _logger.LogInformation("重启监视器");

            // 重启输入监视器
            _inputMonitor.StopMonitoring();
            var gameHandle = SystemControl.FindGenshinImpactHandle();
            if (gameHandle != IntPtr.Zero)
            {
                _inputMonitor.StartMonitoring(gameHandle);
            }

            // 重新初始化状态提取器
            using var imageRegion = TaskControl.CaptureToRectArea();
            _stateExtractor.InitializeCombatScenes(imageRegion);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "重启监视器时发生异常");
        }
    }

    /// <summary>
    /// 获取当前状态
    /// </summary>
    public DataCollectorState GetCurrentState()
    {
        lock (_stateLock)
        {
            return _currentState;
        }
    }

    /// <summary>
    /// 手动开始采集
    /// </summary>
    public void StartCollectionManually()
    {
        lock (_stateLock)
        {
            if (_currentState == DataCollectorState.WaitingTrigger)
            {
                SetState(DataCollectorState.Collecting);
                StartDataCollection();
            }
        }
    }

    /// <summary>
    /// 手动停止采集
    /// </summary>
    public void StopCollectionManually()
    {
        lock (_stateLock)
        {
            if (_currentState == DataCollectorState.Collecting)
            {
                StopDataCollection();
                if (_config.AutoTriggerEnabled)
                {
                    SetState(DataCollectorState.WaitingTrigger);
                }
                else
                {
                    SetState(DataCollectorState.Stopped);
                }
            }
        }
    }

    /// <summary>
    /// 设置状态
    /// </summary>
    private void SetState(DataCollectorState newState)
    {
        lock (_stateLock)
        {
            var oldState = _currentState;
            _currentState = newState;
            _logger.LogInformation("数据采集器状态变更: {OldState} -> {NewState}", oldState, newState);
        }
    }

    /// <summary>
    /// 启动触发器模式
    /// </summary>
    private async Task StartTriggerMode()
    {
        SetState(DataCollectorState.WaitingTrigger);
        // 不在这里初始化session，等到真正开始采集时再初始化

        // 启动触发器检查定时器 - 优化为500ms间隔以减少CPU占用
        _triggerCheckTimer = new Timer(CheckTriggers, null, 0, 500); // 每500ms检查一次触发器
        _logger.LogInformation("触发器模式已启动，等待触发器触发");

        // 等待取消信号
        while (!_ct.IsCancellationRequested && !_stopRequested)
        {
            await Task.Delay(1000, _ct);

            // 检查游戏是否失焦
            if (_config.StopOnGameUnfocused && !SystemControl.IsGenshinImpactActive())
            {
                _logger.LogInformation("游戏失焦，停止数据采集");
                break;
            }
        }
    }

    /// <summary>
    /// 启动手动模式
    /// </summary>
    private async Task StartManualMode()
    {
        SetState(DataCollectorState.WaitingTrigger);
        // 不在这里初始化session，等到真正开始采集时再初始化
        _logger.LogInformation("手动模式已启动，等待手动开始采集");

        // 等待取消信号
        while (!_ct.IsCancellationRequested && !_stopRequested)
        {
            await Task.Delay(1000, _ct);

            // 检查游戏是否失焦
            if (_config.StopOnGameUnfocused && !SystemControl.IsGenshinImpactActive())
            {
                _logger.LogInformation("游戏失焦，停止数据采集");
                break;
            }
        }
    }

    /// <summary>
    /// 初始化会话
    /// </summary>
    private async Task InitializeSession()
    {
        _sessionStartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        
        // 创建会话目录
        _sessionPath = Path.Combine(Path.GetFullPath(_taskParam.DatasetPath), _taskParam.SessionId);
        _framesPath = Path.Combine(_sessionPath, "frames");
        
        Directory.CreateDirectory(_sessionPath);
        Directory.CreateDirectory(_framesPath);

        _logger.LogInformation("会话初始化完成: {SessionId}, 路径: {SessionPath}", 
            _taskParam.SessionId, _sessionPath);

        // 初始化输入监控
        var gameHandle = SystemControl.FindGenshinImpactHandle();
        if (gameHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("未找到原神游戏窗口");
        }

        _inputMonitor.StartMonitoring(gameHandle);
        _logger.LogInformation("输入监控已启动");

        // 初始化状态提取器
        using var imageRegion = TaskControl.CaptureToRectArea();
        _stateExtractor.InitializeCombatScenes(imageRegion);
        _logger.LogInformation("状态提取器已初始化");
    }

    /// <summary>
    /// 检查触发器
    /// </summary>
    private void CheckTriggers(object? state)
    {
        if (_ct.IsCancellationRequested || _stopRequested)
            return;

        try
        {
            lock (_stateLock)
            {
                if (_currentState == DataCollectorState.WaitingTrigger)
                {
                    if (CheckStartTrigger())
                    {
                        // 在锁外启动异步任务
                        GenerateNewSessionId();

                        // 释放锁后再异步初始化会话
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await InitializeSession();

                                // 重新获取锁来设置状态和启动采集
                                lock (_stateLock)
                                {
                                    if (!_ct.IsCancellationRequested && !_stopRequested)
                                    {
                                        SetState(DataCollectorState.Collecting);
                                        StartDataCollection();
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                _logger.LogError(e, "触发器启动采集时发生异常");
                            }
                        });
                    }
                }
                else if (_currentState == DataCollectorState.Collecting)
                {
                    if (CheckEndTrigger())
                    {
                        StopDataCollection();
                        SetState(DataCollectorState.WaitingTrigger);
                    }
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "触发器检查过程中发生异常");
        }
    }

    /// <summary>
    /// 检查开始触发器
    /// </summary>
    private bool CheckStartTrigger()
    {
        return _config.StartTriggerType switch
        {
            CollectionTriggerType.Manual => false, // 手动模式不自动触发
            CollectionTriggerType.DomainStart => CheckDomainStart(),
            CollectionTriggerType.CombatStart => CheckCombatStart(),
            CollectionTriggerType.GameFocused => SystemControl.IsGenshinImpactActive(),
            _ => false
        };
    }

    /// <summary>
    /// 检查结束触发器
    /// </summary>
    private bool CheckEndTrigger()
    {
        return _config.EndTriggerType switch
        {
            CollectionTriggerType.Manual => false, // 手动模式不自动停止
            CollectionTriggerType.DomainReward => CheckDomainReward(),
            CollectionTriggerType.CombatEnd => CheckCombatEnd(),
            CollectionTriggerType.GameUnfocused => !SystemControl.IsGenshinImpactActive(),
            _ => false
        };
    }

    /// <summary>
    /// 开始数据采集
    /// </summary>
    private void StartDataCollection()
    {
        var collectionInterval = 1000 / _taskParam.CollectionFps; // 毫秒

        _collectionTimer = new Timer(CollectFrame, null, 0, collectionInterval);
        _logger.LogInformation("数据采集已启动, FPS: {Fps}, 间隔: {Interval}ms",
            _taskParam.CollectionFps, collectionInterval);
    }

    /// <summary>
    /// 停止数据采集
    /// </summary>
    private void StopDataCollection()
    {
        _collectionTimer?.Dispose();
        _collectionTimer = null;
        _logger.LogInformation("数据采集已停止");
    }

    /// <summary>
    /// 检查秘境开始 - 检测"启动"按钮并自动按F键进入
    /// 优化版本：缩小检测区域以提高性能
    /// </summary>
private bool CheckDomainStart()
{
    try
    {
        using var imageRegion = TaskControl.CaptureToRectArea();

        // 根据图片估计"启动"文本位置，设计更精确的检测区域
        var searchX = imageRegion.Width * 0.5; // 从屏幕中间开始
        var searchY = imageRegion.Height * 0.55; // 从屏幕中下部开始
        var searchWidth = imageRegion.Width * 0.2; // 范围覆盖到右侧，稍微扩大以应对不同分辨率
        var searchHeight = imageRegion.Height * 0.1; // 范围覆盖到下方

        // 确保搜索区域不超出屏幕范围
        searchX = Math.Max(0, searchX);
        searchY = Math.Max(0, searchY);
        searchWidth = Math.Min(imageRegion.Width - searchX, searchWidth);
        searchHeight = Math.Min(imageRegion.Height - searchY, searchHeight);

        var ocrList = imageRegion.FindMulti(RecognitionObject.Ocr(
            (int)searchX, (int)searchY, (int)searchWidth, (int)searchHeight));
        var startChallengeFound = ocrList.Any(ocr => ocr.Text.Contains("启动"));

        if (startChallengeFound)
        {
            _logger.LogInformation("检测到启动按钮");
            // 数据采集器不应该发送按键，只做检测
            return true;
        }

        return false;
    }
    catch (Exception e)
    {
        _logger.LogDebug(e, "秘境开始检测失败");
        return false;
    }
}

    /// <summary>
    /// 检查战斗开始 - 使用快速检测方法
    /// </summary>
    private bool CheckCombatStart()
    {
        try
        {
            using var imageRegion = TaskControl.CaptureToRectArea();
            // 使用轻量级的战斗检测，避免完整状态提取
            return _stateExtractor.IsInCombat(imageRegion);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 检查秘境奖励 - 复用AutoDomain的检测逻辑
    /// </summary>
    private bool CheckDomainReward()
    {
        try
        {
            using var imageRegion = TaskControl.CaptureToRectArea();

            // 检测石化古树奖励界面
            var regionList = imageRegion.FindMulti(RecognitionObject.Ocr(
                imageRegion.Width * 0.25, imageRegion.Height * 0.2,
                imageRegion.Width * 0.5, imageRegion.Height * 0.6));
            var hasTreeReward = regionList.Any(t => t.Text.Contains("石化古树"));

            // 检测挑战完成提示
            var endTipsRect = imageRegion.DeriveCrop(new Rect(0, 0, imageRegion.Width, (int)(imageRegion.Height * 0.3)));
            var endTipsText = OcrFactory.Paddle.Ocr(endTipsRect.SrcMat);
            var hasChallengeComplete = endTipsText.Contains("挑战达成") || endTipsText.Contains("挑战完成");

            return hasTreeReward || hasChallengeComplete;
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "秘境奖励检测失败");
            return false;
        }
    }

    /// <summary>
    /// 检查战斗结束 - 优化版本，避免完整状态提取
    /// </summary>
    private bool CheckCombatEnd()
    {
        try
        {
            using var imageRegion = TaskControl.CaptureToRectArea();
            // 使用轻量级的战斗检测，避免完整状态提取
            return !_stateExtractor.IsInCombat(imageRegion);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 采集单帧数据
    /// </summary>
    private void CollectFrame(object? state)
    {
        // 检查是否正在采集
        lock (_stateLock)
        {
            if (_currentState != DataCollectorState.Collecting || _ct.IsCancellationRequested || _stopRequested)
                return;
        }

        var frameStartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        try
        {
            using var imageRegion = TaskControl.CaptureToRectArea();

            // 检测玩家动作
            var playerAction = _inputMonitor.DetectPlayerAction();

            // 检查是否需要采集无动作帧
            var currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (playerAction == null && !_taskParam.CollectNoActionFrames)
            {
                // 检查是否需要采集无动作帧
                if (currentTime - _lastNoActionFrameTime < _taskParam.NoActionFrameInterval)
                {
                    return; // 跳过此帧
                }
                _lastNoActionFrameTime = currentTime;
            }

            // 提取结构化状态（可选，根据配置决定）
            StructuredState? structuredState = null;
            if (_config.CollectStructuredState)
            {
                var extractStartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                // 根据配置选择提取选项，默认只提取基本信息
                var extractionOptions = StateExtractionOptions.Default;
                if (_config.CollectPlayerTeam)
                {
                    extractionOptions.ExtractPlayerTeam = true;
                }
                if (_config.CollectEnemies)
                {
                    extractionOptions.ExtractEnemies = true;
                }
                if (_config.CollectCombatEvents)
                {
                    extractionOptions.ExtractCombatEvents = true;
                }

                structuredState = _stateExtractor.ExtractStructuredState(imageRegion, extractionOptions);
                var extractTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - extractStartTime;

                // 检查处理时间是否过长
                var frameInterval = 1000 / _taskParam.CollectionFps;
                if (extractTime > frameInterval * 0.8)
                {
                    _logger.LogWarning("状态提取耗时过长: {ExtractTime}ms, 帧间隔: {FrameInterval}ms, 建议禁用部分检测功能",
                        extractTime, frameInterval);

                    // 如果处理时间超过帧间隔的80%，自动停止采集
                    if (extractTime > frameInterval)
                    {
                        _logger.LogError("状态提取耗时超过帧间隔，自动停止数据采集");
                        StopCollectionManually();
                        return;
                    }
                }
            }

            // 保存截图
            var framePath = SaveScreenshot(imageRegion.SrcMat);

            // 创建数据记录
            var dataRecord = new DataRecord
            {
                SessionId = _taskParam.SessionId,
                FrameIndex = _frameIndex++,
                TimeOffsetMs = currentTime - _sessionStartTime,
                FramePath = framePath,
                PlayerAction = playerAction,
                StructuredState = structuredState ?? new StructuredState()
            };

            // 添加到缓冲区
            lock (_bufferLock)
            {
                _dataBuffer.Add(dataRecord);
            }

            // 定期检查内存使用量 (每100帧检查一次)
            if (_frameIndex % 100 == 0)
            {
                CheckMemoryUsage();

                if (_taskParam.DebugMode)
                {
                    _logger.LogDebug("已采集 {FrameCount} 帧数据", _frameIndex);
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "采集帧数据时发生异常");
        }
    }

    /// <summary>
    /// 保存截图
    /// </summary>
    private string SaveScreenshot(Mat image)
    {
        if (!_taskParam.SaveRawScreenshots)
            return string.Empty;

        var fileName = $"frame_{_frameIndex:D8}.jpg";
        var filePath = Path.Combine(_framesPath, fileName);

        // 异步保存以避免阻塞采集线程
        Task.Run(() =>
        {
            try
            {
                Mat imageToSave = image;

                // 检查是否需要遮罩UID
                if (TaskContext.Instance().Config.CommonConfig.ScreenshotUidCoverEnabled)
                {
                    imageToSave = image.Clone();
                    var assetScale = TaskContext.Instance().SystemInfo.ScaleTo1080PRatio;
                    var rect = new Rect(
                        (int)(image.Width - MaskWindowConfig.UidCoverRightBottomRect.X * assetScale),
                        (int)(image.Height - MaskWindowConfig.UidCoverRightBottomRect.Y * assetScale),
                        (int)(MaskWindowConfig.UidCoverRightBottomRect.Width * assetScale),
                        (int)(MaskWindowConfig.UidCoverRightBottomRect.Height * assetScale)
                    );
                    imageToSave.Rectangle(rect, Scalar.White, -1);
                }

                // 压缩保存
                var encodeParams = new int[] { (int)ImwriteFlags.JpegQuality, _taskParam.ScreenshotQuality };
                imageToSave.ImWrite(filePath, encodeParams);

                // 如果创建了副本，需要释放
                if (imageToSave != image)
                {
                    imageToSave.Dispose();
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "保存截图时发生异常: {FilePath}", filePath);
            }
        });

        return Path.Combine("frames", fileName); // 返回相对路径
    }

    /// <summary>
    /// 检查内存使用量
    /// </summary>
    private void CheckMemoryUsage()
    {
        var process = Process.GetCurrentProcess();
        var memoryUsageMb = process.WorkingSet64 / 1024 / 1024;
        
        if (memoryUsageMb > _taskParam.MaxMemoryUsageMb)
        {
            _logger.LogWarning("内存使用量超过限制: {Current}MB > {Max}MB，触发OOM保护", 
                memoryUsageMb, _taskParam.MaxMemoryUsageMb);
            
            throw new OutOfMemoryException($"内存使用量超过限制: {memoryUsageMb}MB");
        }

        if (_taskParam.DebugMode && memoryUsageMb > _taskParam.MaxMemoryUsageMb * 0.8)
        {
            _logger.LogDebug("内存使用量警告: {Current}MB (限制: {Max}MB)", 
                memoryUsageMb, _taskParam.MaxMemoryUsageMb);
        }
    }

    /// <summary>
    /// 清理资源
    /// </summary>
    private async Task Cleanup()
    {
        _logger.LogInformation("开始清理资源");

        // 停止所有定时器
        _collectionTimer?.Dispose();
        _triggerCheckTimer?.Dispose();
        _inputMonitor?.Dispose();

        // 清理内部取消令牌源
        _internalCts?.Dispose();
        _internalCts = null;

        // 设置状态为停止
        SetState(DataCollectorState.Stopped);

        // 保存数据到文件
        if (_dataBuffer.Count > 0)
        {
            await SaveDataToFile();
        }
        else
        {
            _logger.LogWarning("没有采集到任何数据");
        }

        _logger.LogInformation("资源清理完成");
    }

    /// <summary>
    /// 保存数据到文件
    /// </summary>
    private async Task SaveDataToFile()
    {
        try
        {
            var dataFilePath = Path.Combine(_sessionPath, "data.jsonl");
            var metadataFilePath = Path.Combine(_sessionPath, "metadata.json");

            // 保存数据记录
            using var writer = new StreamWriter(dataFilePath);

            // 复制数据到临时列表以避免在lock中使用await
            List<DataRecord> tempBuffer;
            lock (_bufferLock)
            {
                tempBuffer = [.. _dataBuffer];
            }

            foreach (var record in tempBuffer)
            {
                var json = JsonConvert.SerializeObject(record, Formatting.None);
                await writer.WriteLineAsync(json);
            }

            // 保存元数据
            var metadata = new
            {
                session_id = _taskParam.SessionId,
                start_time = _sessionStartTime,
                end_time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                total_frames = _frameIndex,
                collection_fps = _taskParam.CollectionFps,
                dataset_version = "1.0"
            };

            var metadataJson = JsonConvert.SerializeObject(metadata, Formatting.Indented);
            await File.WriteAllTextAsync(metadataFilePath, metadataJson);

            // 计算数据集大小
            var datasetSize = CalculateDirectorySize(_sessionPath);
            
            _logger.LogInformation("数据集保存完成:");
            _logger.LogInformation("  会话ID: {SessionId}", _taskParam.SessionId);
            _logger.LogInformation("  保存路径: {Path}", _sessionPath);
            _logger.LogInformation("  总帧数: {Frames}", _frameIndex);
            _logger.LogInformation("  数据集大小: {Size:F2} MB", datasetSize / 1024.0 / 1024.0);
            _logger.LogInformation("  数据文件: {DataFile}", dataFilePath);
            _logger.LogInformation("  元数据文件: {MetadataFile}", metadataFilePath);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "保存数据到文件时发生异常");
            throw;
        }
    }

    /// <summary>
    /// 计算目录大小
    /// </summary>
    private long CalculateDirectorySize(string directoryPath)
    {
        var directory = new DirectoryInfo(directoryPath);
        return directory.GetFiles("*", SearchOption.AllDirectories)
            .Sum(file => file.Length);
    }
}
