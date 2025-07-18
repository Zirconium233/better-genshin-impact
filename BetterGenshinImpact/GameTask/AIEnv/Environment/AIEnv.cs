using BetterGenshinImpact.GameTask.AIEnv.Model;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AIEnv.Environment;

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

        try
        {
            _actionQueueManager?.AddCommands(actionScript);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "添加动作指令失败: {ActionScript}", actionScript);
        }
    }

    /// <summary>
    /// 获取动作队列状态
    /// </summary>
    public ActionQueueStatus GetActionQueueStatus()
    {
        return _actionQueueManager?.GetStatus() ?? new ActionQueueStatus();
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
        var intervalMs = 1000 / _param.EnvFps;
        
        _observationTask = Task.Run(async () =>
        {
            if (_param.DebugMode)
            {
                _logger.LogDebug("开始观察循环，间隔: {IntervalMs}ms", intervalMs);
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
                    await Task.Delay(intervalMs, _cancellationToken);
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
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 只进行一次截图
        using var imageRegion = TaskControl.CaptureToRectArea();

        // 从同一个ImageRegion生成Base64和提取状态
        var frameBase64 = await _stateExtractor!.CaptureFrameFromImageRegionAsync(imageRegion);

        // 提取结构化状态
        StructuredState? structuredState = null;
        if (_param.CollectStructuredState)
        {
            structuredState = await _stateExtractor.ExtractStructuredStateFromImageRegionAsync(imageRegion);
        }

        // 获取动作队列状态
        var actionQueueStatus = GetActionQueueStatus();

        return new Observation
        {
            TimestampMs = timestamp,
            FrameBase64 = frameBase64,
            StructuredState = structuredState,
            ActionQueueStatus = actionQueueStatus
        };
    }
}
