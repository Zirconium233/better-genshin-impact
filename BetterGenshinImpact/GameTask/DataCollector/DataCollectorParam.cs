using BetterGenshinImpact.GameTask.Model;
using System;
using System.Globalization;

namespace BetterGenshinImpact.GameTask.DataCollector;

/// <summary>
/// 数据采集器任务参数
/// </summary>
public class DataCollectorParam : BaseTaskParam
{
    /// <summary>
    /// 数据集保存路径
    /// </summary>
    public string DatasetPath { get; set; } = string.Empty;

    /// <summary>
    /// 会话ID
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// 采集频率 (FPS)
    /// </summary>
    public int CollectionFps { get; set; } = 10;

    /// <summary>
    /// 最大内存使用量 (MB)
    /// </summary>
    public int MaxMemoryUsageMb { get; set; } = 4096;

    /// <summary>
    /// 是否保存原始截图
    /// </summary>
    public bool SaveRawScreenshots { get; set; } = true;

    /// <summary>
    /// 截图压缩质量
    /// </summary>
    public int ScreenshotQuality { get; set; } = 85;

    /// <summary>
    /// 是否启用调试模式
    /// </summary>
    public bool DebugMode { get; set; } = false;

    /// <summary>
    /// 动作检测敏感度 (毫秒)
    /// </summary>
    public int ActionDetectionSensitivity { get; set; } = 50;

    /// <summary>
    /// 是否采集无动作帧
    /// </summary>
    public bool CollectNoActionFrames { get; set; } = false;

    /// <summary>
    /// 无动作帧采集间隔 (毫秒)
    /// </summary>
    public int NoActionFrameInterval { get; set; } = 500;

    public DataCollectorParam() : base()
    {
    }

    public DataCollectorParam(CultureInfo? gameCultureInfo) : base(gameCultureInfo)
    {
    }

    /// <summary>
    /// 从配置中设置默认值
    /// </summary>
    public void SetDefault()
    {
        var config = TaskContext.Instance().Config.DataCollectorConfig;
        DatasetPath = config.DatasetPath;
        CollectionFps = config.CollectionFps;
        MaxMemoryUsageMb = config.MaxMemoryUsageMb;
        SaveRawScreenshots = config.SaveRawScreenshots;
        ScreenshotQuality = config.ScreenshotQuality;
        DebugMode = config.DebugMode;
        ActionDetectionSensitivity = config.ActionDetectionSensitivity;
        CollectNoActionFrames = config.CollectNoActionFrames;
        NoActionFrameInterval = config.NoActionFrameInterval;
        
        // 生成唯一的会话ID
        SessionId = GenerateSessionId(config.SessionIdPrefix);
    }

    /// <summary>
    /// 生成会话ID
    /// </summary>
    private string GenerateSessionId(string prefix)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var random = new Random().Next(1000, 9999);
        return $"{prefix}_{timestamp}_{random}";
    }
}
