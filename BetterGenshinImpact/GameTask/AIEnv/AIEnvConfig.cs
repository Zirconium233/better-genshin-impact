using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.ComponentModel.DataAnnotations;

namespace BetterGenshinImpact.GameTask.AIEnv;

/// <summary>
/// AI环境配置
/// </summary>
[Serializable]
public partial class AIEnvConfig : ObservableValidator
{
    /// <summary>
    /// 环境运行频率 (Hz)
    /// </summary>
    [ObservableProperty]
    [Range(1, 20)]
    private int _envFps = 5;

    /// <summary>
    /// 时间补偿（毫秒），用于补偿处理时间以达到准确的帧间隔
    /// </summary>
    [ObservableProperty]
    [Range(0, 200)]
    private int _timeCompensationMs = 70;

    /// <summary>
    /// 调度器类型
    /// </summary>
    [ObservableProperty]
    private string _schedulerType = "HumanScheduler";

    /// <summary>
    /// API端点地址
    /// </summary>
    [ObservableProperty]
    private string _apiEndpoint = "http://127.0.0.1:8000/v1/chat/completions";

    /// <summary>
    /// 调用触发器类型
    /// </summary>
    [ObservableProperty]
    private string _callTriggerType = "ActionGroupCompleted";

    /// <summary>
    /// 结束触发器 - LLM exit指令
    /// </summary>
    [ObservableProperty]
    private bool _endOnLlmExit = true;

    /// <summary>
    /// 结束触发器 - 秘境结束
    /// </summary>
    [ObservableProperty]
    private bool _endOnDomainEnd = true;

    /// <summary>
    /// 错误终止次数
    /// </summary>
    [ObservableProperty]
    [Range(1, 20)]
    private int _errorTerminationCount = 5;

    /// <summary>
    /// 调试模式
    /// </summary>
    [ObservableProperty]
    private bool _debugMode = false;

    /// <summary>
    /// 收集结构化状态
    /// </summary>
    [ObservableProperty]
    private bool _collectStructuredState = true;

    /// <summary>
    /// 开启合并机制
    /// </summary>
    [ObservableProperty]
    private bool _enableMergeActions = false;

    /// <summary>
    /// 最大动作持续时间(秒) - 超过此时间的动作被视为错误输入
    /// </summary>
    [ObservableProperty]
    [Range(1, 30)]
    private double _maxActionDuration = 10.0;

    /// <summary>
    /// 截图质量 (1-100)
    /// </summary>
    [ObservableProperty]
    [Range(1, 100)]
    private int _screenshotQuality = 85;

    /// <summary>
    /// 启用秘境检测
    /// </summary>
    [ObservableProperty]
    private bool _enableDomainDetection = true;

    /// <summary>
    /// 用户初始提示词
    /// </summary>
    [ObservableProperty]
    private string _userPrompt = "帮我战斗";

    /// <summary>
    /// 历史帧缓冲大小
    /// </summary>
    [ObservableProperty]
    [Range(5, 50)]
    private int _replayBufferSize = 10;

    /// <summary>
    /// 连续失败重试次数
    /// </summary>
    [ObservableProperty]
    [Range(1, 10)]
    private int _maxRetryCount = 3;
}
