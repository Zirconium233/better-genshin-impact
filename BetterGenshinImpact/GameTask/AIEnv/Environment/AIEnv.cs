using BetterGenshinImpact.GameTask.AIEnv.Model;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AIEnv.Environment;

/// <summary>
/// 性能统计数据
/// </summary>
public class PerformanceStats
{
    public double AvgCaptureTimeMs { get; set; }
    public double AvgCompressTimeMs { get; set; }
    public double AvgStateExtractionTimeMs { get; set; }
    public double AvgQueueTimeMs { get; set; }
    public double AvgTotalTimeMs { get; set; }
}

/// <summary>
/// AI环境核心类
/// 负责感知和执行，提供观察和动作接口
/// </summary>
public class AIEnvironment
{
    private readonly AIEnvParam _param;
    private readonly ILogger<AIEnvironment> _logger;
    private readonly CancellationToken _cancellationToken;

    private StateExtractor? _stateExtractor;
    private ActionExecutor? _actionExecutor;
    private ActionQueueManager? _actionQueueManager;

    private Task? _observationTask;
    private readonly object _observationLock = new();
    private Observation? _latestObservation;



    // 性能统计
    private bool _enablePerformanceStats = false;
    private readonly List<double> _captureTimesMs = new();
    private readonly List<double> _compressTimesMs = new();
    private readonly List<double> _stateExtractionTimesMs = new();
    private readonly List<double> _queueTimesMs = new();
    private readonly List<double> _totalTimesMs = new();
    private readonly object _statsLock = new();

    public bool IsRunning { get; private set; }

    public AIEnvironment(AIEnvParam param, CancellationToken cancellationToken)
    {
        _param = param;
        _cancellationToken = cancellationToken;
        _logger = App.GetLogger<AIEnvironment>();
    }

    /// <summary>
    /// 启动环境
    /// </summary>
    public async Task StartAsync()
    {
        if (IsRunning)
        {
            _logger.LogWarning("AI环境已经在运行中");
            return;
        }

        if (_param.DebugMode)
        {
            _logger.LogDebug("启动AI环境...");
        }

        try
        {
            // 初始化组件
            await InitializeComponentsAsync();

            // 启动观察循环
            StartObservationLoop();

            IsRunning = true;
            _logger.LogInformation("AI环境启动成功，运行频率: {Fps}Hz", _param.EnvFps);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动AI环境失败");
            throw;
        }
    }

    /// <summary>
    /// 停止环境
    /// </summary>
    public async Task StopAsync()
    {
        if (!IsRunning)
        {
            return;
        }

        if (_param.DebugMode)
        {
            _logger.LogDebug("停止AI环境...");
        }

        IsRunning = false;

        try
        {
            // 等待观察任务完成
            if (_observationTask != null)
            {
                await _observationTask;
            }

            // 停止动作执行器
            _actionExecutor?.Stop();

            _logger.LogInformation("AI环境已停止");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止AI环境时发生异常");
        }
    }

    /// <summary>
    /// 获取最新观察
    /// </summary>
    public Observation? GetLatestObservation()
    {
        lock (_observationLock)
        {
            return _latestObservation;
        }
    }

    /// <summary>
    /// 添加动作指令
    /// </summary>
    public void AddCommands(string actionScript)
    {
        if (!IsRunning)
        {
            _logger.LogWarning("AI环境未运行，无法添加指令");
            return;
        }

        if (string.IsNullOrWhiteSpace(actionScript))
        {
            _logger.LogWarning("动作脚本为空，忽略");
            return;
        }

        if (_param.DebugMode)
        {
            _logger.LogDebug("接收到动作脚本: {ActionScript}", actionScript);
        }

        // 直接调用ActionQueueManager，让异常向上传播
        _actionQueueManager?.AddCommands(actionScript);
    }

    /// <summary>
    /// 获取动作队列状态
    /// </summary>
    public ActionQueueStatus GetActionQueueStatus()
    {
        return _actionQueueManager?.GetStatus() ?? new ActionQueueStatus();
    }

    /// <summary>
    /// 获取错误计数
    /// </summary>
    public int GetErrorCount()
    {
        return _actionQueueManager?.GetErrorCount() ?? 0;
    }

    /// <summary>
    /// 重置错误计数
    /// </summary>
    public void ResetErrorCount()
    {
        _actionQueueManager?.ResetErrorCount();
    }

    /// <summary>
    /// 启用或禁用性能统计
    /// </summary>
    public void EnablePerformanceStats(bool enable)
    {
        lock (_statsLock)
        {
            _enablePerformanceStats = enable;
            if (enable)
            {
                // 清空之前的统计数据
                _captureTimesMs.Clear();
                _compressTimesMs.Clear();
                _stateExtractionTimesMs.Clear();
                _queueTimesMs.Clear();
                _totalTimesMs.Clear();
                _logger.LogInformation("性能统计已启用");
            }
            else
            {
                _logger.LogInformation("性能统计已禁用");
            }
        }
    }

    /// <summary>
    /// 获取性能统计结果
    /// </summary>
    public PerformanceStats GetPerformanceStats()
    {
        lock (_statsLock)
        {
            return new PerformanceStats
            {
                AvgCaptureTimeMs = _captureTimesMs.Count > 0 ? _captureTimesMs.Average() : 0,
                AvgCompressTimeMs = _compressTimesMs.Count > 0 ? _compressTimesMs.Average() : 0,
                AvgStateExtractionTimeMs = _stateExtractionTimesMs.Count > 0 ? _stateExtractionTimesMs.Average() : 0,
                AvgQueueTimeMs = _queueTimesMs.Count > 0 ? _queueTimesMs.Average() : 0,
                AvgTotalTimeMs = _totalTimesMs.Count > 0 ? _totalTimesMs.Average() : 0
            };
        }
    }

    /// <summary>
    /// 记录性能统计数据
    /// </summary>
    private void RecordPerformanceStats(double captureMs, double compressMs, double stateMs, double queueMs, double totalMs)
    {
        if (!_enablePerformanceStats) return;

        lock (_statsLock)
        {
            _captureTimesMs.Add(captureMs);
            _compressTimesMs.Add(compressMs);
            _stateExtractionTimesMs.Add(stateMs);
            _queueTimesMs.Add(queueMs);
            _totalTimesMs.Add(totalMs);

            // 限制统计数据数量，避免内存泄漏
            const int maxStats = 100;
            if (_captureTimesMs.Count > maxStats)
            {
                _captureTimesMs.RemoveAt(0);
                _compressTimesMs.RemoveAt(0);
                _stateExtractionTimesMs.RemoveAt(0);
                _queueTimesMs.RemoveAt(0);
                _totalTimesMs.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// 初始化组件
    /// </summary>
    private async Task InitializeComponentsAsync()
    {
        if (_param.DebugMode)
        {
            _logger.LogDebug("初始化AI环境组件...");
        }

        // 初始化状态提取器
        _stateExtractor = new StateExtractor(_param);

        // 初始化动作队列管理器
        _actionQueueManager = new ActionQueueManager(_param);

        // 初始化动作执行器
        _actionExecutor = new ActionExecutor(_param, _actionQueueManager);

        // 设置ActionQueueManager对ActionExecutor的引用（解决循环依赖）
        _actionQueueManager.SetActionExecutor(_actionExecutor);

        // 启动动作执行器
        _actionExecutor.Start();

        await Task.CompletedTask;
    }

    /// <summary>
    /// 启动观察循环
    /// </summary>
    private void StartObservationLoop()
    {
        var baseIntervalMs = 1000 / _param.EnvFps;
        var compensatedIntervalMs = Math.Max(1, baseIntervalMs - _param.TimeCompensationMs);

        _observationTask = Task.Run(async () =>
        {
            if (_param.DebugMode)
            {
                _logger.LogDebug("开始观察循环，基础间隔: {BaseInterval}ms, 补偿后间隔: {CompensatedInterval}ms",
                    baseIntervalMs, compensatedIntervalMs);
            }

            while (!_cancellationToken.IsCancellationRequested && IsRunning)
            {
                try
                {
                    var observation = await CaptureObservationAsync();
                    
                    lock (_observationLock)
                    {
                        _latestObservation = observation;
                    }

                    if (_param.DebugMode)
                    {
                        _logger.LogDebug("更新观察数据，时间戳: {Timestamp}", observation.TimestampMs);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "捕获观察数据时发生异常");
                }

                try
                {
                    await Task.Delay(compensatedIntervalMs, _cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            if (_param.DebugMode)
            {
                _logger.LogDebug("观察循环已结束");
            }
        }, _cancellationToken);
    }

    /// <summary>
    /// 捕获观察数据（优化版本，避免重复截图）
    /// </summary>
    private async Task<Observation> CaptureObservationAsync()
    {
        var totalStopwatch = Stopwatch.StartNew();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 1. 截图阶段
        var captureStopwatch = Stopwatch.StartNew();
        using var imageRegion = TaskControl.CaptureToRectArea();
        captureStopwatch.Stop();

        // 2. 帧压缩阶段
        var compressStopwatch = Stopwatch.StartNew();
        var frameBase64 = await _stateExtractor!.CaptureFrameFromImageRegionAsync(imageRegion);
        compressStopwatch.Stop();

        // 3. 状态提取阶段
        var stateStopwatch = Stopwatch.StartNew();
        StructuredState? structuredState = null;
        if (_param.CollectStructuredState)
        {
            structuredState = await _stateExtractor.ExtractStructuredStateFromImageRegionAsync(imageRegion);
        }
        stateStopwatch.Stop();

        // 4. 队列获取阶段
        var queueStopwatch = Stopwatch.StartNew();
        var actionQueueStatus = GetActionQueueStatus();
        queueStopwatch.Stop();

        totalStopwatch.Stop();

        // 记录性能统计
        RecordPerformanceStats(
            captureStopwatch.Elapsed.TotalMilliseconds,
            compressStopwatch.Elapsed.TotalMilliseconds,
            stateStopwatch.Elapsed.TotalMilliseconds,
            queueStopwatch.Elapsed.TotalMilliseconds,
            totalStopwatch.Elapsed.TotalMilliseconds
        );

        return new Observation
        {
            TimestampMs = timestamp,
            FrameBase64 = frameBase64,
            StructuredState = structuredState,
            ActionQueueStatus = actionQueueStatus
        };
    }
}
