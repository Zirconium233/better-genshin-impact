using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AIEnv;

/// <summary>
/// AI环境任务参数
/// </summary>
public class AIEnvParam : BaseTaskParam
{
    /// <summary>
    /// 调度器类型
    /// </summary>
    public string SchedulerType { get; set; } = "HumanScheduler";

    /// <summary>
    /// 环境运行频率
    /// </summary>
    public int EnvFps { get; set; } = 5;

    /// <summary>
    /// 是否启用调试模式
    /// </summary>
    public bool DebugMode { get; set; } = false;

    /// <summary>
    /// 是否收集结构化状态
    /// </summary>
    public bool CollectStructuredState { get; set; } = true;

    /// <summary>
    /// 是否开启动作合并机制
    /// </summary>
    public bool EnableMergeActions { get; set; } = false;

    /// <summary>
    /// API端点地址
    /// </summary>
    public string ApiEndpoint { get; set; } = "http://127.0.0.1:8000/v1/chat/completions";

    /// <summary>
    /// 用户初始提示词
    /// </summary>
    public string UserPrompt { get; set; } = "帮我战斗";

    /// <summary>
    /// 最大错误次数
    /// </summary>
    public int MaxErrorCount { get; set; } = 5;

    /// <summary>
    /// 历史帧缓冲大小
    /// </summary>
    public int ReplayBufferSize { get; set; } = 10;

    /// <summary>
    /// 最大动作持续时间（秒）
    /// </summary>
    public float MaxActionDuration { get; set; } = 5.0f;

    /// <summary>
    /// 动作队列大小
    /// </summary>
    public int ActionQueueSize { get; set; } = 20;

    /// <summary>
    /// 截图质量 (1-100)
    /// </summary>
    public int ScreenshotQuality { get; set; } = 85;

    public AIEnvParam()
    {
        SetDefault();
    }

    public AIEnvParam(string schedulerType, bool debugMode = false)
    {
        SchedulerType = schedulerType;
        DebugMode = debugMode;
        SetDefault();
    }

    /// <summary>
    /// 从配置中设置默认值
    /// </summary>
    public void SetDefault()
    {
        var config = TaskContext.Instance().Config.AIEnvConfig;
        
        SchedulerType = config.SchedulerType;
        EnvFps = config.EnvFps;
        DebugMode = config.DebugMode;
        CollectStructuredState = config.CollectStructuredState;
        EnableMergeActions = config.EnableMergeActions;
        ApiEndpoint = config.ApiEndpoint;
        UserPrompt = config.UserPrompt;
        MaxErrorCount = config.ErrorTerminationCount;
        ReplayBufferSize = config.ReplayBufferSize;
    }
}
