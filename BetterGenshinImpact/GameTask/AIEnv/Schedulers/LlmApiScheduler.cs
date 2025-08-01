using BetterGenshinImpact.GameTask.AIEnv.Environment;
using BetterGenshinImpact.GameTask.AIEnv.Model;
using BetterGenshinImpact.View.Windows;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;

namespace BetterGenshinImpact.GameTask.AIEnv.Schedulers;

/// <summary>
/// LLM调度器配置
/// </summary>
public class LlmSchedulerConfig
{
    public string MemoryMode { get; set; } = "NearDense";
    public string PromptMode { get; set; } = "General";
    public bool EnableThought { get; set; } = false;
    public string OutputLengthControl { get; set; } = "Variable";
    public string ApiKey { get; set; } = "";
    public string ModelName { get; set; } = "gpt-4-vision-preview";
    public string ApiEndpoint { get; set; } = "https://api.openai.com/v1/chat/completions";
    public int MaxTokens { get; set; } = 1000;
    public double Temperature { get; set; } = 0.7;
    public int RecentFrameCount { get; set; } = 3;
    public int DistantFrameInterval { get; set; } = 2;
    public int FixedIntervalSeconds { get; set; } = 5;
    public string CallTriggerType { get; set; } = "ActionGroupCompleted";
    public string EndTriggerType { get; set; } = "LlmExit";
}

/// <summary>
/// 历史帧数据
/// </summary>
public class FrameData
{
    public long RelativeTimestampMs { get; set; } // 相对于对话开始的时间戳
    public long AbsoluteTimestampMs { get; set; } // 绝对时间戳
    public Observation Observation { get; set; } = new();
    public string? LlmResponse { get; set; }
}

/// <summary>
/// 记忆管理模式枚举
/// </summary>
public enum MemoryMode
{
    /// <summary>
    /// 单帧模式 - LLM作为无记忆的纯预测器
    /// </summary>
    SingleFrame,

    /// <summary>
    /// 近期记忆模式 - 近密集远稀疏采样
    /// </summary>
    NearDense,

    /// <summary>
    /// Agent记忆管理模式 - 压缩上下文保留关键内容 (Placeholder)
    /// </summary>
    Agent
}

/// <summary>
/// ReplayBuffer - 管理历史观察数据
/// </summary>
public class ReplayBuffer
{
    private readonly ConcurrentQueue<FrameData> _buffer = new();
    private readonly int _maxSize;
    private readonly ILogger<ReplayBuffer> _logger;
    private readonly object _lock = new();
    private long _dialogStartTimestamp = 0;
    private long _lastObservationTimestamp = 0;

    public ReplayBuffer(int maxSize, ILogger<ReplayBuffer> logger)
    {
        _maxSize = maxSize;
        _logger = logger;
    }

    /// <summary>
    /// 设置对话开始时间戳
    /// </summary>
    public void SetDialogStartTimestamp(long timestamp)
    {
        lock (_lock)
        {
            _dialogStartTimestamp = timestamp;
            _logger.LogInformation("设置对话开始时间戳: {Timestamp}", timestamp);
        }
    }

    /// <summary>
    /// 添加新的观察数据（仅在时间戳变化时添加）
    /// </summary>
    public bool TryAddObservation(Observation observation, string? llmResponse = null)
    {
        lock (_lock)
        {
            // 检查时间戳是否变化
            if (observation.TimestampMs == _lastObservationTimestamp)
            {
                return false; // 时间戳未变化，不添加
            }

            _lastObservationTimestamp = observation.TimestampMs;

            var frameData = new FrameData
            {
                AbsoluteTimestampMs = observation.TimestampMs,
                RelativeTimestampMs = observation.TimestampMs - _dialogStartTimestamp,
                Observation = observation,
                LlmResponse = llmResponse
            };

            _buffer.Enqueue(frameData);

            // 保持缓冲区大小
            while (_buffer.Count > _maxSize)
            {
                _buffer.TryDequeue(out _);
            }

            return true;
        }
    }

    /// <summary>
    /// 根据记忆模式采样历史帧
    /// </summary>
    public List<FrameData> SampleFrames(MemoryMode mode, int recentCount = 3, int distantInterval = 2)
    {
        lock (_lock)
        {
            var allFrames = _buffer.ToList();
            if (allFrames.Count == 0) return new List<FrameData>();

            return mode switch
            {
                MemoryMode.SingleFrame => allFrames.TakeLast(1).ToList(),
                MemoryMode.NearDense => SampleNearDense(allFrames, recentCount, distantInterval),
                MemoryMode.Agent => SampleAgent(allFrames), // Placeholder
                _ => allFrames.TakeLast(1).ToList()
            };
        }
    }

    /// <summary>
    /// 近密集远稀疏采样
    /// </summary>
    private List<FrameData> SampleNearDense(List<FrameData> frames, int recentCount, int distantInterval)
    {
        var result = new List<FrameData>();
        var frameCount = frames.Count;

        if (frameCount <= recentCount)
        {
            return frames;
        }

        // 近期密集采样
        var recentFrames = frames.TakeLast(recentCount).ToList();
        result.AddRange(recentFrames);

        // 远期稀疏采样
        var remainingFrames = frames.Take(frameCount - recentCount).ToList();
        for (int i = remainingFrames.Count - 1; i >= 0; i -= distantInterval)
        {
            result.Insert(0, remainingFrames[i]);
        }

        return result.OrderBy(f => f.RelativeTimestampMs).ToList();
    }

    /// <summary>
    /// Agent模式采样 (Placeholder)
    /// </summary>
    private List<FrameData> SampleAgent(List<FrameData> frames)
    {
        // Placeholder: 简单返回最近几帧
        return frames.TakeLast(5).ToList();
    }

    /// <summary>
    /// 获取缓冲区大小
    /// </summary>
    public int Count => _buffer.Count;

    /// <summary>
    /// 清空缓冲区
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            while (_buffer.TryDequeue(out _)) { }
        }
    }
}

/// <summary>
/// LLM调度器状态
/// </summary>
public enum LlmSchedulerState
{
    WaitingUserPrompt,    // 等待用户指令
    LlmAgentLoop,         // LLM Agent循环
    WaitingTrigger,       // 等待触发器
    LlmInferencing,       // LLM推理中
    Terminating           // 终止中
}

/// <summary>
/// LLM API调度器
/// 通过HTTP API调用外部LLM服务进行决策
/// </summary>
public class LlmApiScheduler : IAgentScheduler
{
    private readonly AIEnvParam _param;
    private readonly ILogger<LlmApiScheduler> _logger;

    private AIEnvironment? _env;
    private Task? _schedulerTask;
    private Task? _observationTask;
    private CancellationTokenSource? _cts;
    private string _userPrompt = string.Empty;
    private readonly object _promptLock = new();

    // 核心组件
    private ReplayBuffer? _replayBuffer;
    private VlmApiClient? _apiClient;
    private PromptManager? _promptManager;
    private LlmSchedulerConfig _config = new();

    // 状态管理
    private LlmSchedulerState _currentState = LlmSchedulerState.WaitingUserPrompt;
    private int _consecutiveErrors = 0;
    private DateTime _lastTriggerTime = DateTime.MinValue;

    public bool IsRunning { get; private set; }

    public LlmApiScheduler(AIEnvParam param)
    {
        _param = param;
        _logger = App.GetLogger<LlmApiScheduler>();
    }

    public async Task Start(AIEnvironment env)
    {
        if (IsRunning)
        {
            _logger.LogWarning("LLM调度器已经在运行中");
            return;
        }

        _env = env ?? throw new ArgumentNullException(nameof(env));
        _logger.LogInformation("启动LLM调度器...");

        // 初始化组件
        await InitializeComponentsAsync();

        _cts = new CancellationTokenSource();

        // 启动观察任务（100ms周期）
        _observationTask = Task.Run(ObservationLoop, _cts.Token);

        // 启动调度器主循环
        _schedulerTask = Task.Run(SchedulerLoop, _cts.Token);

        IsRunning = true;
        _currentState = LlmSchedulerState.WaitingUserPrompt;

        _logger.LogInformation("LLM调度器启动完成，状态: {State}", _currentState);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 初始化调度器组件
    /// </summary>
    private async Task InitializeComponentsAsync()
    {
        // 初始化ReplayBuffer (最多100帧)
        _replayBuffer = new ReplayBuffer(100, App.GetLogger<ReplayBuffer>());

        _logger.LogInformation("LLM调度器组件初始化完成");
        await Task.CompletedTask;
    }

    /// <summary>
    /// 观察循环 - 100ms周期采集观察数据
    /// </summary>
    private async Task ObservationLoop()
    {
        _logger.LogInformation("开始观察循环，周期: 100ms");

        while (!_cts!.Token.IsCancellationRequested && IsRunning)
        {
            try
            {
                var observation = _env?.GetLatestObservation();
                if (observation != null && _replayBuffer != null)
                {
                    var added = _replayBuffer.TryAddObservation(observation);
                    if (added&&_param.DebugMode)
                    {
                        _logger.LogDebug("添加新观察数据，时间戳: {Timestamp}", observation.TimestampMs);
                    }
                }

                await Task.Delay(100, _cts.Token); // 100ms周期
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "观察循环中发生异常");
                await Task.Delay(1000, _cts.Token);
            }
        }

        _logger.LogInformation("观察循环已结束");
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        _logger.LogInformation("停止LLM调度器...");

        IsRunning = false;
        _cts?.Cancel();

        try
        {
            _schedulerTask?.Wait(5000);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "等待LLM调度器任务完成时发生异常");
        }

        _cts?.Dispose();
        _logger.LogInformation("LLM调度器已停止");
    }

    public void SendUserPrompt(string prompt)
    {
        lock (_promptLock)
        {
            _logger.LogInformation("LLM调度器接收到发送指令请求");

            // 立即触发配置对话框
            _ = Task.Run(async () =>
            {
                try
                {
                    await TriggerConfigDialog();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理LLM配置对话框时发生异常");
                }
            });
        }
    }

    /// <summary>
    /// 触发配置对话框
    /// </summary>
    private async Task TriggerConfigDialog()
    {
        try
        {
            _logger.LogInformation("显示LLM调度器配置对话框");

            // 在UI线程上显示配置对话框
            (string? userPrompt, LlmSchedulerConfig? config) result = (null, null);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                result = ShowConfigDialog();
            });

            if (!string.IsNullOrWhiteSpace(result.userPrompt) && result.config != null)
            {
                // 更新配置
                _config = result.config;
                _userPrompt = result.userPrompt;

                // 初始化LLM组件
                await InitializeLlmComponentsAsync();

                // 设置对话开始时间戳
                var latestObs = _env?.GetLatestObservation();
                if (latestObs != null)
                {
                    _replayBuffer?.SetDialogStartTimestamp(latestObs.TimestampMs);
                }

                // 切换到LLM Agent循环状态
                _currentState = LlmSchedulerState.LlmAgentLoop;
                _consecutiveErrors = 0;
                _lastTriggerTime = DateTime.MinValue;

                _logger.LogInformation("开始LLM Agent循环，用户指令: {Prompt}", _userPrompt);
            }
            else
            {
                _logger.LogInformation("用户取消配置或输入为空");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM调度器触发配置对话框时发生异常");
        }
    }

    /// <summary>
    /// 初始化LLM相关组件
    /// </summary>
    private async Task InitializeLlmComponentsAsync()
    {
        // 初始化API客户端
        _apiClient = new VlmApiClient(_config.ApiEndpoint, _config.ApiKey, _config.ModelName);

        // 初始化Prompt管理器
        _promptManager = new PromptManager(_config.PromptMode, _config.EnableThought, _config.OutputLengthControl);
        await _promptManager.InitializeAsync();

        _logger.LogInformation("LLM组件初始化完成");
    }

    /// <summary>
    /// 显示配置对话框
    /// </summary>
    private (string? userPrompt, LlmSchedulerConfig? config) ShowConfigDialog()
    {
        try
        {
            // 使用专门的LLM配置对话框
            var result = LlmConfigDialog.ShowConfigDialog(Application.Current.MainWindow);
            if (result.userPrompt == null || result.config == null)
            {
                return (null, null);
            }

            // 从全局配置中获取API配置
            var aiEnvConfig = TaskContext.Instance().Config.AIEnvConfig;
            result.config.ApiEndpoint = aiEnvConfig.ApiEndpoint;
            result.config.ApiKey = ""; // API密钥留空，适用于本地模型

            // 如果模型名称为空，设置为适合llama.cpp的默认值
            if (string.IsNullOrEmpty(result.config.ModelName))
            {
                result.config.ModelName = "llama-model"; // 本地模型的通用名称
            }

            return (result.userPrompt, result.config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示配置对话框时发生异常");
            return (null, null);
        }
    }

    public string GetStatus()
    {
        if (!IsRunning)
        {
            return "未启动";
        }

        if (_consecutiveErrors >= 5)
        {
            return $"错误停止 (连续错误: {_consecutiveErrors})";
        }

        var stateText = _currentState switch
        {
            LlmSchedulerState.WaitingUserPrompt => "等待用户指令",
            LlmSchedulerState.LlmAgentLoop => "LLM Agent循环",
            LlmSchedulerState.WaitingTrigger => "等待触发器",
            LlmSchedulerState.LlmInferencing => "LLM推理中",
            LlmSchedulerState.Terminating => "终止中",
            _ => "未知状态"
        };

        var queueStatus = _env?.GetActionQueueStatus();
        if (queueStatus != null && !string.IsNullOrEmpty(queueStatus.RemainingActions))
        {
            return $"{stateText} (执行中: {CountActionGroups(queueStatus.RemainingActions)}组)";
        }

        return stateText;
    }

    /// <summary>
    /// 调度器主循环 - 状态机
    /// </summary>
    private async Task SchedulerLoop()
    {
        _logger.LogInformation("开始LLM调度器状态机循环");

        while (!_cts!.Token.IsCancellationRequested && IsRunning)
        {
            try
            {
                switch (_currentState)
                {
                    case LlmSchedulerState.WaitingUserPrompt:
                        // 等待用户指令状态，什么都不做
                        await Task.Delay(1000, _cts.Token);
                        break;

                    case LlmSchedulerState.LlmAgentLoop:
                        // 进入LLM Agent循环，切换到等待触发器状态
                        _currentState = LlmSchedulerState.WaitingTrigger;
                        _logger.LogInformation("进入LLM Agent循环，切换到等待触发器状态");
                        break;

                    case LlmSchedulerState.WaitingTrigger:
                        // 检查是否需要触发LLM调用
                        if (await ShouldTriggerLlmCall())
                        {
                            _currentState = LlmSchedulerState.LlmInferencing;
                            _logger.LogInformation("触发LLM推理");
                        }
                        else
                        {
                            await Task.Delay(200, _cts.Token); // 200ms检查频率
                        }
                        break;

                    case LlmSchedulerState.LlmInferencing:
                        // 执行LLM推理
                        await ProcessLlmCall();

                        // 检查是否应该终止
                        if (await ShouldStop())
                        {
                            _currentState = LlmSchedulerState.Terminating;
                            _logger.LogInformation("满足终止条件，开始清理");
                        }
                        else
                        {
                            // 回到等待触发器状态
                            _currentState = LlmSchedulerState.WaitingTrigger;
                        }
                        break;

                    case LlmSchedulerState.Terminating:
                        // 清理对话历史，回到等待用户指令状态
                        await CleanupDialog();
                        _currentState = LlmSchedulerState.WaitingUserPrompt;
                        _logger.LogInformation("清理完成，回到等待用户指令状态");
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LLM调度器状态机中发生异常，当前状态: {State}", _currentState);
                _consecutiveErrors++;

                if (_consecutiveErrors >= 5) // 使用固定的错误上限
                {
                    _logger.LogError("连续错误次数达到上限，强制终止");
                    _currentState = LlmSchedulerState.Terminating;
                }

                await Task.Delay(1000, _cts.Token);
            }
        }

        _logger.LogInformation("LLM调度器状态机循环已结束");
    }

    /// <summary>
    /// 清理对话历史
    /// </summary>
    private async Task CleanupDialog()
    {
        try
        {
            _userPrompt = string.Empty;
            _consecutiveErrors = 0;
            _lastTriggerTime = DateTime.MinValue;

            // 清空ReplayBuffer中的LLM响应
            // 注意：不清空观察数据，只清空LLM相关的状态

            _logger.LogInformation("对话历史清理完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理对话历史时发生异常");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 更新ReplayBuffer
    /// </summary>
    private async Task UpdateReplayBuffer()
    {
        try
        {
            var observation = _env?.GetLatestObservation();
            if (observation != null)
            {
                _replayBuffer?.TryAddObservation(observation);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新ReplayBuffer时发生异常");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 检查是否应该触发LLM调用
    /// </summary>
    private async Task<bool> ShouldTriggerLlmCall()
    {
        if (string.IsNullOrEmpty(_userPrompt))
        {
            return false; // 没有用户指令
        }

        var queueStatus = _env?.GetActionQueueStatus();
        if (queueStatus == null) return false;

        return _config.CallTriggerType switch
        {
            "ActionGroupCompleted" => string.IsNullOrEmpty(queueStatus.RemainingActions),
            "OnlyOneRemaining" => CountActionGroups(queueStatus.RemainingActions) <= 1,
            "FixedInterval" => (DateTime.Now - _lastTriggerTime).TotalSeconds >= _config.FixedIntervalSeconds,
            "AllCompleted" => string.IsNullOrEmpty(queueStatus.RemainingActions),
            _ => false
        };
    }

    /// <summary>
    /// 计算动作组数量
    /// </summary>
    private int CountActionGroups(string remainingActions)
    {
        if (string.IsNullOrEmpty(remainingActions)) return 0;
        return remainingActions.Split(',').Length;
    }

    /// <summary>
    /// 处理LLM调用
    /// </summary>
    private async Task ProcessLlmCall()
    {
        try
        {
            _logger.LogInformation("触发LLM调用");
            _lastTriggerTime = DateTime.Now;

            // 获取历史帧
            var memoryMode = Enum.Parse<MemoryMode>(_config.MemoryMode);
            var historyFrames = _replayBuffer?.SampleFrames(memoryMode, _config.RecentFrameCount, _config.DistantFrameInterval) ?? new List<FrameData>();

            // 构建Prompt
            var prompt = _promptManager?.BuildPrompt(historyFrames, _userPrompt) ?? "";

            // 检查API客户端是否已初始化
            if (_apiClient == null)
            {
                _logger.LogError("API客户端未初始化");
                return;
            }

            // 获取最新帧的图像
            var latestObservation = _env?.GetLatestObservation();
            if (latestObservation == null || string.IsNullOrEmpty(latestObservation.FrameBase64))
            {
                _logger.LogWarning("无法获取最新观察数据或图像");
                return;
            }

            // 调用API
            var response = await _apiClient.CallVlmApiAsync(prompt, latestObservation.FrameBase64, _config.Temperature, _config.MaxTokens);

            // 解析响应
            var actionScript = _apiClient.ParseApiResponse(response, _config.EnableThought);

            // 验证动作脚本
            if (!_apiClient.ValidateActionScript(actionScript))
            {
                _logger.LogWarning("LLM返回的动作脚本验证失败: {ActionScript}", actionScript);
                _consecutiveErrors++;
                return;
            }

            // 检查退出指令
            if (actionScript.Trim().ToLower() == "exit")
            {
                _logger.LogInformation("LLM输出退出指令，结束当前会话");
                await CleanupDialog();
                _currentState = LlmSchedulerState.WaitingUserPrompt;
                return;
            }

            // 提交动作脚本
            _env?.AddCommands(actionScript);
            _logger.LogInformation("LLM调度器提交动作脚本: {ActionScript}", actionScript);

            // 重置错误计数
            _consecutiveErrors = 0;

            // 更新ReplayBuffer中的LLM响应
            if (_replayBuffer != null && latestObservation != null)
            {
                _replayBuffer.TryAddObservation(latestObservation, response);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理LLM调用时发生异常");
            _consecutiveErrors++;

            // 如果连续错误过多，停止调度器
            if (_consecutiveErrors >= 5)
            {
                _logger.LogError("连续错误次数过多({Count})，停止调度器", _consecutiveErrors);
                Stop();
            }
        }
    }

    /// <summary>
    /// 检查是否应该停止
    /// </summary>
    private async Task<bool> ShouldStop()
    {
        // 检查结束触发器
        if (_config.EndTriggerType.Contains("DomainEnd"))
        {
            // 这里可以添加秘境结束检测逻辑
            // 目前暂时返回false
        }

        if (_config.EndTriggerType.Contains("EnvException"))
        {
            if (_consecutiveErrors >= 5)
            {
                return true;
            }
        }

        return await Task.FromResult(false);
    }

    #region 测试方法

    /// <summary>
    /// 测试1: Obs和Prompt测试
    /// </summary>
    public async Task TestObsAndPrompt()
    {
        try
        {
            _logger.LogInformation("=== 开始Obs和Prompt测试 ===");

            if (_replayBuffer == null)
            {
                _logger.LogWarning("ReplayBuffer未初始化");
                return;
            }

            // 初始化PromptManager用于测试
            if (_promptManager == null)
            {
                _promptManager = new PromptManager(_config.MemoryMode, _config.EnableThought, _config.OutputLengthControl);
                await _promptManager.InitializeAsync();
            }

            // 打印ReplayBuffer信息
            var bufferCount = _replayBuffer.Count;
            _logger.LogInformation("ReplayBuffer长度: {Count}", bufferCount);

            // 获取前10帧和后10帧的时间戳
            var allFrames = _replayBuffer.SampleFrames(MemoryMode.NearDense, 100, 1); // 获取所有帧
            if (allFrames.Count > 0)
            {
                var first10 = allFrames.Take(10).ToList();
                var last10 = allFrames.TakeLast(10).ToList();

                _logger.LogInformation("前10帧时间戳: [{Timestamps}]",
                    string.Join(",", first10.Select(f => f.RelativeTimestampMs)));
                _logger.LogInformation("后10帧时间戳: [{Timestamps}]",
                    string.Join(",", last10.Select(f => f.RelativeTimestampMs)));

                // 计算平均时间差
                if (allFrames.Count > 1)
                {
                    var timeDiffs = new List<long>();
                    for (int i = 1; i < allFrames.Count; i++)
                    {
                        timeDiffs.Add(allFrames[i].RelativeTimestampMs - allFrames[i-1].RelativeTimestampMs);
                    }
                    var avgDiff = timeDiffs.Average();
                    _logger.LogInformation("平均时间差: {AvgDiff}ms", avgDiff);
                }
            }

            // 测试Prompt构建
            await TestPromptBuilding();

            _logger.LogInformation("=== Obs和Prompt测试完成 ===");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Obs和Prompt测试失败");
        }
    }

    /// <summary>
    /// 测试Prompt构建
    /// </summary>
    private async Task TestPromptBuilding()
    {
        if (_promptManager == null || _replayBuffer == null)
        {
            _logger.LogWarning("PromptManager或ReplayBuffer未初始化");
            return;
        }

        // 测试不同配置下的Prompt构建
        var testConfigs = new[]
        {
            new { MemoryMode = "NearDense", EnableThought = false, OutputLength = "Variable" },
            new { MemoryMode = "SingleFrame", EnableThought = false, OutputLength = "Variable" },
            new { MemoryMode = "NearDense", EnableThought = true, OutputLength = "Fixed3" }
        };

        bool isFirstConfig = true;
        foreach (var config in testConfigs)
        {
            _logger.LogInformation("测试配置: MemoryMode={MemoryMode}, EnableThought={EnableThought}, OutputLength={OutputLength}",
                config.MemoryMode, config.EnableThought, config.OutputLength);

            var memoryMode = Enum.Parse<MemoryMode>(config.MemoryMode);
            var historyFrames = _replayBuffer.SampleFrames(memoryMode, 3, 2);

            // 创建临时PromptManager
            var tempPromptManager = new PromptManager("General", config.EnableThought, config.OutputLength);
            await tempPromptManager.InitializeAsync();

            var prompt = tempPromptManager.BuildPrompt(historyFrames, "测试用户指令");

            // 第一次配置时打印完整的Prompt JSON格式
            if (isFirstConfig)
            {
                var latestObservation = _env?.GetLatestObservation();
                if (latestObservation != null && !string.IsNullOrEmpty(latestObservation.FrameBase64))
                {
                    // 实际解码Base64图像获取真实分辨率
                    string resolution = "未知";
                    try
                    {
                        var imageBytes = Convert.FromBase64String(latestObservation.FrameBase64);
                        using var ms = new MemoryStream(imageBytes);
                        using var image = System.Drawing.Image.FromStream(ms);
                        resolution = $"{image.Width}x{image.Height}";
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("解码图像失败: {Error}", ex.Message);
                        resolution = "解码失败";
                    }

                    // 构建完整的API请求JSON（图像用分辨率和大小代替）
                    var imageInfo = $"[图像数据: 分辨率{resolution}, 大小约{latestObservation.FrameBase64.Length}字符]";

                    var fullRequestJson = new
                    {
                        model = "test-model",
                        messages = new[]
                        {
                            new
                            {
                                role = "user",
                                content = new object[]
                                {
                                    new { type = "text", text = prompt },
                                    new { type = "image_url", image_url = new { url = imageInfo } }
                                }
                            }
                        },
                        temperature = 0.7,
                        max_tokens = 1000
                    };

                    _logger.LogInformation("完整Prompt JSON格式: {FullPromptJson}",
                        JsonSerializer.Serialize(fullRequestJson, new JsonSerializerOptions { WriteIndented = true }));
                }
                isFirstConfig = false;
            }

            // 打印Prompt信息（不包含图像）
            var promptInfo = new
            {
                FrameCount = historyFrames.Count,
                PromptLength = prompt.Length,
                HasThought = config.EnableThought,
                OutputControl = config.OutputLength
            };

            _logger.LogInformation("Prompt信息: {PromptInfo}", JsonSerializer.Serialize(promptInfo));
        }
    }

    /// <summary>
    /// 测试2: 触发器测试
    /// </summary>
    public async Task TestTriggers()
    {
        try
        {
            _logger.LogInformation("=== 开始触发器测试 ===");

            // 模拟配置
            _config = new LlmSchedulerConfig
            {
                CallTriggerType = "ActionGroupCompleted",
                EndTriggerType = "LlmExit"
            };

            // 模拟用户指令
            _userPrompt = "测试触发器";
            _currentState = LlmSchedulerState.LlmAgentLoop;

            // 创建测试API客户端
            var testApiClient = new TestVlmApiClient();
            _apiClient = testApiClient;

            // 模拟触发器测试 - 让框架通过exit命令回到等待用户指令状态
            int testCount = 0;
            const int maxTests = 5; // 最多5次测试，防止无限循环

            while (IsRunning && _currentState == LlmSchedulerState.LlmAgentLoop && testCount < maxTests)
            {
                testCount++;
                _logger.LogInformation("第{Index}次触发器测试", testCount);

                // 模拟触发条件满足
                await ProcessLlmCall();

                // 检查是否通过exit命令回到等待用户指令状态
                if (_currentState == LlmSchedulerState.WaitingUserPrompt)
                {
                    _logger.LogInformation("检测到exit命令，状态已切换回等待用户指令，触发器测试正常结束");
                    break;
                }

                await Task.Delay(1000); // 等待执行
            }

            // 如果达到最大测试次数仍未回到等待状态，记录警告
            if (testCount >= maxTests && _currentState == LlmSchedulerState.LlmAgentLoop)
            {
                _logger.LogWarning("触发器测试达到最大次数({MaxTests})，未检测到exit命令", maxTests);
            }

            _logger.LogInformation("=== 触发器测试完成 ===");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "触发器测试失败");
        }
    }

    /// <summary>
    /// 测试3: API调用测试
    /// </summary>
    public async Task TestApiCall()
    {
        try
        {
            _logger.LogInformation("=== 开始API调用测试 ===");

            if (string.IsNullOrEmpty(_config.ApiKey))
            {
                _logger.LogWarning("API密钥未配置，跳过API调用测试");
                return;
            }

            var apiClient = new VlmApiClient(_config.ApiEndpoint, _config.ApiKey, _config.ModelName);

            // 简单的hello测试
            try
            {
                var response = await apiClient.CallVlmApiAsync("Hello", "dummy_image_data", 0.7, 100);
                _logger.LogInformation("API调用成功，响应: {Response}", response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API调用失败");
            }

            _logger.LogInformation("=== API调用测试完成 ===");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API调用测试失败");
        }
    }

    /// <summary>
    /// 测试4: 完整测试
    /// </summary>
    public async Task TestComplete()
    {
        try
        {
            _logger.LogInformation("=== 开始完整测试 ===");

            // 设置测试配置
            _config = new LlmSchedulerConfig
            {
                CallTriggerType = "ActionGroupCompleted",
                EndTriggerType = "LlmExit"
            };

            _userPrompt = "往前走1秒，然后退回来，再普攻5下";

            // 创建测试API客户端
            var testApiClient = new TestVlmApiClient();
            _apiClient = testApiClient;

            // 启动完整测试流程
            _currentState = LlmSchedulerState.LlmAgentLoop;

            var testStartTime = DateTime.Now;
            var maxTestDuration = TimeSpan.FromMinutes(2); // 最大测试时间2分钟
            var requiredCommands = new HashSet<string> { "w", "s", "attack(5)", "exit" };
            var executedCommands = new HashSet<string>();

            while (_currentState != LlmSchedulerState.WaitingUserPrompt &&
                   DateTime.Now - testStartTime < maxTestDuration)
            {
                // 模拟状态机执行
                switch (_currentState)
                {
                    case LlmSchedulerState.LlmAgentLoop:
                        _currentState = LlmSchedulerState.WaitingTrigger;
                        break;
                    case LlmSchedulerState.WaitingTrigger:
                        _currentState = LlmSchedulerState.LlmInferencing;
                        break;
                    case LlmSchedulerState.LlmInferencing:
                        await ProcessLlmCall();

                        // 检查执行的命令
                        var lastResponse = testApiClient.GetLastResponse();
                        if (!string.IsNullOrEmpty(lastResponse))
                        {
                            foreach (var cmd in requiredCommands)
                            {
                                if (lastResponse.Contains(cmd))
                                {
                                    executedCommands.Add(cmd);
                                }
                            }
                        }

                        if (await ShouldStop())
                        {
                            _currentState = LlmSchedulerState.Terminating;
                        }
                        else
                        {
                            _currentState = LlmSchedulerState.WaitingTrigger;
                        }
                        break;
                    case LlmSchedulerState.Terminating:
                        await CleanupDialog();
                        _currentState = LlmSchedulerState.WaitingUserPrompt;
                        break;
                }

                await Task.Delay(500); // 模拟执行间隔
            }

            // 检查测试结果
            var allCommandsExecuted = requiredCommands.All(cmd => executedCommands.Contains(cmd));
            if (allCommandsExecuted)
            {
                _logger.LogInformation("完整测试通过：所有必需命令都已执行 [{Commands}]",
                    string.Join(", ", executedCommands));
            }
            else
            {
                var missingCommands = requiredCommands.Except(executedCommands);
                _logger.LogWarning("完整测试部分通过：缺少命令 [{Missing}]，已执行 [{Executed}]",
                    string.Join(", ", missingCommands), string.Join(", ", executedCommands));
            }

            _logger.LogInformation("=== 完整测试完成 ===");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "完整测试失败");
        }
    }

    #endregion
}

/// <summary>
/// 测试用的VLM API客户端
/// </summary>
public class TestVlmApiClient : VlmApiClient
{
    private int _callCount = 0;
    private string _lastResponse = "";

    public TestVlmApiClient() : base("http://localhost:8080/v1/chat/completions", "", "test")
    {
    }

    public override async Task<string> CallVlmApiAsync(string prompt, string imageBase64, double temperature = 0.7, int maxTokens = 1000)
    {
        _callCount++;

        // 验证输入格式
        var logger = App.GetLogger<TestVlmApiClient>();
        logger.LogInformation("测试API调用 #{Count}", _callCount);
        logger.LogInformation("Prompt长度: {Length}", prompt.Length);
        logger.LogInformation("图像数据长度: {Length}", imageBase64.Length);

        // 返回预定义的测试响应
        _lastResponse = _callCount switch
        {
            1 => "w(2.0)&a(2.0),s(2.0),attack(2)",
            2 => "moveby(150, 50),attack(5), e",
            3 => "exit",
            _ => "w(1.0),attack(1)"
        };

        logger.LogInformation("返回测试响应: {Response}", _lastResponse);

        await Task.Delay(100); // 模拟API延迟
        return _lastResponse;
    }

    public string GetLastResponse() => _lastResponse;
}

/// <summary>
/// Prompt管理器
/// 负责管理和构建LLM提示词
/// </summary>
public class PromptManager
{
    private readonly string _promptMode;
    private readonly bool _enableThought;
    private readonly string _outputLengthControl;
    private readonly ILogger<PromptManager> _logger;

    private string _systemPrompt = string.Empty;
    private string _outputFormatPrompt = string.Empty;
    private readonly string _promptFilePath = Path.Combine("Assets", "AIEnv", "prompts.txt");

    public PromptManager(string promptMode, bool enableThought, string outputLengthControl)
    {
        _promptMode = promptMode;
        _enableThought = enableThought;
        _outputLengthControl = outputLengthControl;
        _logger = App.GetLogger<PromptManager>();
    }

    /// <summary>
    /// 初始化Prompt管理器
    /// </summary>
    public async Task InitializeAsync()
    {
        await LoadPromptsAsync();
        _logger.LogInformation("Prompt管理器初始化完成，模式: {Mode}", _promptMode);
    }

    /// <summary>
    /// 加载Prompt配置
    /// </summary>
    private async Task LoadPromptsAsync()
    {
        try
        {
            // 尝试从文件读取
            if (File.Exists(_promptFilePath))
            {
                var content = await File.ReadAllTextAsync(_promptFilePath);
                ParsePromptFile(content);
                _logger.LogInformation("从文件加载Prompt: {FilePath}", _promptFilePath);
            }
            else
            {
                // 使用硬编码的默认Prompt
                LoadDefaultPrompts();
                // 创建默认文件
                await CreateDefaultPromptFileAsync();
                _logger.LogInformation("使用默认Prompt并创建配置文件: {FilePath}", _promptFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载Prompt配置失败，使用默认配置");
            LoadDefaultPrompts();
        }
    }

    /// <summary>
    /// 解析Prompt文件
    /// </summary>
    private void ParsePromptFile(string content)
    {
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var currentSection = "";
        var currentContent = new StringBuilder();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
            {
                // 保存上一个section
                if (!string.IsNullOrEmpty(currentSection))
                {
                    SaveSection(currentSection, currentContent.ToString().Trim());
                }

                // 开始新section
                currentSection = trimmedLine[1..^1];
                currentContent.Clear();
            }
            else if (!trimmedLine.StartsWith("#")) // 忽略注释
            {
                currentContent.AppendLine(trimmedLine);
            }
        }

        // 保存最后一个section
        if (!string.IsNullOrEmpty(currentSection))
        {
            SaveSection(currentSection, currentContent.ToString().Trim());
        }
    }

    /// <summary>
    /// 保存配置段
    /// </summary>
    private void SaveSection(string section, string content)
    {
        switch (section.ToLower())
        {
            case "system_general":
                if (_promptMode == "General") _systemPrompt = content;
                break;
            case "system_finetuned":
                if (_promptMode == "FineTuned") _systemPrompt = content;
                break;
            case "output_format":
                _outputFormatPrompt = content;
                break;
        }
    }

    /// <summary>
    /// 加载默认Prompt
    /// </summary>
    private void LoadDefaultPrompts()
    {
        if (_promptMode == "General")
        {
            _systemPrompt = GetDefaultGeneralSystemPrompt();
        }
        else
        {
            _systemPrompt = GetDefaultFineTunedSystemPrompt();
        }

        _outputFormatPrompt = GetDefaultOutputFormatPrompt();
    }

    /// <summary>
    /// 创建默认Prompt文件
    /// </summary>
    private async Task CreateDefaultPromptFileAsync()
    {
        try
        {
            var directory = Path.GetDirectoryName(_promptFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var defaultContent = GenerateDefaultPromptFileContent();
            await File.WriteAllTextAsync(_promptFilePath, defaultContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建默认Prompt文件失败");
        }
    }

    /// <summary>
    /// 生成默认Prompt文件内容
    /// </summary>
    private string GenerateDefaultPromptFileContent()
    {
        return $@"# LLM API调度器Prompt配置文件
# 支持的section: [system_general], [system_finetuned], [output_format]
# 以#开头的行为注释

[system_general]
{GetDefaultGeneralSystemPrompt()}

[system_finetuned]
{GetDefaultFineTunedSystemPrompt()}

[output_format]
{GetDefaultOutputFormatPrompt()}
";
    }

    /// <summary>
    /// 构建完整的Prompt
    /// </summary>
    public string BuildPrompt(List<FrameData> historyFrames, string userMessage)
    {
        var promptBuilder = new StringBuilder();

        // 系统Prompt
        promptBuilder.AppendLine(_systemPrompt);
        promptBuilder.AppendLine();

        // 历史帧信息
        if (historyFrames.Count > 0)
        {
            promptBuilder.AppendLine("## 历史观察信息:");
            for (int i = 0; i < historyFrames.Count; i++)
            {
                var frame = historyFrames[i];
                var relativeTime = i == historyFrames.Count - 1 ? "当前" : $"-{(historyFrames.Last().RelativeTimestampMs - frame.RelativeTimestampMs)}ms";

                promptBuilder.AppendLine($"### 帧 {i + 1} ({relativeTime}):");

                // 动作队列状态
                var queueStatus = frame.Observation.ActionQueueStatus;
                promptBuilder.AppendLine($"- 剩余动作: {queueStatus.RemainingActions}");
                promptBuilder.AppendLine($"- 预计完成时间: {queueStatus.EstimatedCompletionMs}ms");

                // 结构化状态
                if (frame.Observation.StructuredState != null)
                {
                    var state = frame.Observation.StructuredState;
                    promptBuilder.AppendLine($"- 游戏状态: 菜单={state.GameContext.InMenu}, 战斗={state.GameContext.InCombat}, 秘境={state.GameContext.InDomain}");
                    if (state.PlayerTeam.TeamMembers.Count > 0)
                    {
                        var activeChar = state.PlayerTeam.TeamMembers.FirstOrDefault(m => m.Index == state.PlayerTeam.ActiveCharacterIndex);
                        promptBuilder.AppendLine($"- 当前角色: {activeChar?.Name ?? "未知"}");
                    }
                }

                // LLM历史回答
                if (!string.IsNullOrEmpty(frame.LlmResponse))
                {
                    promptBuilder.AppendLine($"- 上次回答: {frame.LlmResponse}");
                }

                promptBuilder.AppendLine();
            }
        }

        // 输出格式要求
        promptBuilder.AppendLine(_outputFormatPrompt);
        promptBuilder.AppendLine();

        // 用户消息
        promptBuilder.AppendLine($"## 用户指令:");
        promptBuilder.AppendLine(userMessage);

        return promptBuilder.ToString();
    }

    /// <summary>
    /// 获取默认通用系统Prompt
    /// </summary>
    private string GetDefaultGeneralSystemPrompt()
    {
        return @"你是一个原神游戏AI助手，负责根据游戏画面和状态信息生成动作指令。

## 游戏环境说明:
- 这是原神游戏，一个开放世界动作RPG游戏
- 你需要根据当前游戏状态和用户指令，生成合适的动作脚本
- 动作脚本将被游戏执行器解析并执行

## 动作脚本语法详解:

### 基础动作:
- w(时长) / s(时长) / a(时长) / d(时长): 移动动作，时长单位秒，如 w(2.0) 表示前进2秒
- attack(次数): 普通攻击，如 attack(2) 表示攻击2次
- charge(时长): 重击，时长>=0.3秒，如 charge(1.5)
- e / e(hold): 元素战技，e为点按，e(hold)为长按
- q: 元素爆发
- jump: 跳跃
- dash(时长): 冲刺，默认0.1秒
- sw(1-4): 切换到队伍第N个角色，如 sw(2) 切换到第2个角色
- f: 交互/拾取
- t: 特定玩法内交互操作
- moveby(x,y): 鼠标相对移动，如 moveby(150,50)
- wait(时长): 等待指定时间，如 wait(0.5)

### 动作组合语法:
- 同步执行(使用&连接): w(2.0)&a(1.0) 表示前进的同时左移(斜向移动)
- 顺序执行(使用,连接): w(1.0),dash(0.3),e 表示前进1秒，然后冲刺，然后释放技能
- 混合执行: w(2.0)&a(1.0),sw(2),q 表示斜向移动，完成后切换角色2，然后放大招

### 冲突规则:
- 移动冲突: w/s冲突，a/d冲突，但w&a组合允许(斜向移动)
- 攻击冲突: attack/charge/e/q互斥，不能同时执行
- 兼容动作: 移动+攻击、视角+移动、视角+攻击可以同时执行

### 动作合并逻辑:
- 新输入会覆盖未执行的动作组
- 正在执行的动作组会与新输入合并，冲突动作被新动作覆盖
- 例: 正在执行w(2.0)，新输入s(1.0)会立即停止前进并开始后退

### 特殊指令:
- exit: 退出调度器，完成任务时使用

## 使用建议:
- 保持动作脚本简洁有效，避免过长序列
- 移动+攻击组合在原神中意义有限，移动会被攻击中断
- 优先使用斜向移动: w&a, w&d, s&a, s&d
- 时长参数要合理，移动一般0.1-3.0秒，攻击次数一般1-5次
- 根据游戏状态调整策略，如战斗中多用攻击，探索中多用移动";
    }

    /// <summary>
    /// 获取默认微调系统Prompt
    /// </summary>
    private string GetDefaultFineTunedSystemPrompt()
    {
        return @"Generate action script for Genshin Impact based on current game state.

Available actions: w/a/s/d(move), attack, e/q(skills), charge, jump, sw(1-4)(switch)
Format: action(duration) or action, use comma for sequence, & for parallel
Example: w(1.0),attack(2),e

Keep responses concise and effective.";
    }

    /// <summary>
    /// 获取默认输出格式Prompt
    /// </summary>
    private string GetDefaultOutputFormatPrompt()
    {
        var formatPrompt = new StringBuilder();

        if (_enableThought)
        {
            formatPrompt.AppendLine("## 输出格式要求:");
            formatPrompt.AppendLine("请按照以下JSON格式回答:");
            formatPrompt.AppendLine("{");
            formatPrompt.AppendLine("  \"thought\": \"你的思考过程和策略分析\",");
            formatPrompt.AppendLine("  \"action\": \"动作脚本\"");
            formatPrompt.AppendLine("}");
        }
        else
        {
            formatPrompt.AppendLine("## 输出格式要求:");
            formatPrompt.AppendLine("直接输出动作脚本，不需要额外说明。");
        }

        // 输出长度控制
        switch (_outputLengthControl)
        {
            case "Fixed3":
                formatPrompt.AppendLine("请生成恰好3个动作组的脚本。");
                break;
            case "Range2-5":
                formatPrompt.AppendLine("请生成2-5个动作组的脚本。");
                break;
            case "Single":
                formatPrompt.AppendLine("请生成单个动作组的脚本。");
                break;
            case "Variable":
            default:
                formatPrompt.AppendLine("根据情况生成合适长度的脚本。");
                break;
        }

        return formatPrompt.ToString();
    }
}

/// <summary>
/// VLM API客户端
/// 负责与外部视觉语言模型API通信
/// </summary>
public class VlmApiClient
{
    private readonly string _apiEndpoint;
    private readonly string _apiKey;
    private readonly string _modelName;
    private readonly ILogger<VlmApiClient> _logger;
    private readonly HttpClient _httpClient;

    public VlmApiClient(string apiEndpoint, string apiKey, string modelName)
    {
        _apiEndpoint = apiEndpoint;
        _apiKey = apiKey;
        _modelName = modelName;
        _logger = App.GetLogger<VlmApiClient>();

        // 初始化HTTP客户端
        _httpClient = new HttpClient();
        if (!string.IsNullOrEmpty(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }
        // Content-Type应该在请求时设置，不在默认头中
    }

    /// <summary>
    /// 调用VLM API
    /// </summary>
    public virtual async Task<string> CallVlmApiAsync(string prompt, string imageBase64, double temperature = 0.7, int maxTokens = 1000)
    {
        try
        {
            _logger.LogInformation("调用VLM API: {Endpoint}, 模型: {Model}", _apiEndpoint, _modelName);
            _logger.LogDebug("提示词长度: {PromptLength}, 图像数据长度: {ImageLength}",
                prompt.Length, imageBase64.Length);

            // 构建请求体
            var requestBody = new
            {
                model = _modelName,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = prompt },
                            new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{imageBase64}" } }
                        }
                    }
                },
                temperature = temperature,
                max_tokens = maxTokens
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            // 发送请求
            var response = await _httpClient.PostAsync(_apiEndpoint, content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("API原始响应: {Response}", responseContent);

            // 解析响应
            using var jsonDoc = JsonDocument.Parse(responseContent);
            var choices = jsonDoc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                var message = firstChoice.GetProperty("message");
                var result = message.GetProperty("content").GetString() ?? "";

                _logger.LogInformation("API调用成功，响应长度: {ResponseLength}", result.Length);
                return result;
            }
            else
            {
                throw new InvalidOperationException("API响应中没有choices");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "调用VLM API失败");
            throw;
        }
    }

    /// <summary>
    /// 解析API响应
    /// </summary>
    public string ParseApiResponse(string response, bool enableThought)
    {
        try
        {
            if (enableThought)
            {
                // 尝试解析JSON格式
                var jsonDoc = JsonDocument.Parse(response);
                if (jsonDoc.RootElement.TryGetProperty("action", out var actionElement))
                {
                    var action = actionElement.GetString() ?? "";
                    if (jsonDoc.RootElement.TryGetProperty("thought", out var thoughtElement))
                    {
                        var thought = thoughtElement.GetString() ?? "";
                        _logger.LogInformation("LLM思考过程: {Thought}", thought);
                    }
                    return action;
                }
            }

            // 直接返回响应内容
            return response.Trim();
        }
        catch (JsonException)
        {
            // JSON解析失败，直接返回原始响应
            _logger.LogWarning("API响应不是有效的JSON格式，直接使用原始响应");
            return response.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "解析API响应时发生异常");
            return response.Trim();
        }
    }

    /// <summary>
    /// 验证动作脚本格式
    /// </summary>
    public bool ValidateActionScript(string actionScript)
    {
        if (string.IsNullOrWhiteSpace(actionScript))
        {
            return false;
        }

        // 检查是否为退出指令
        if (actionScript.Trim().ToLower() == "exit")
        {
            return true;
        }

        // 使用ActionQueueManager的解析逻辑进行验证
        try
        {
            var trimmed = actionScript.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 1000)
            {
                return false; // 防止过长的脚本
            }

            // 创建临时的ActionQueueManager来验证脚本
            var tempParam = new AIEnvParam("LlmApiScheduler");
            var tempQueueManager = new ActionQueueManager(tempParam);
            var actionGroups = tempQueueManager.ParseActionScript(actionScript);

            // 如果能成功解析且不为空，则认为是有效的
            return actionGroups != null && actionGroups.Count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "验证动作脚本时发生异常: {ActionScript}", actionScript);
            return false;
        }
    }
}
