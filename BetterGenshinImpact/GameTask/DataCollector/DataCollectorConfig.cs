using System;
using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.GameTask.DataCollector;

/// <summary>
/// AI数据采集器配置
/// </summary>
[Serializable]
public partial class DataCollectorConfig : ObservableObject
{
    /// <summary>
    /// 数据集保存目录
    /// </summary>
    [ObservableProperty]
    private string _datasetPath = @"User\AIDataset";

    /// <summary>
    /// 是否启用自动触发器
    /// </summary>
    [ObservableProperty]
    private bool _autoTriggerEnabled = true;

    /// <summary>
    /// 采集开始触发器类型
    /// </summary>
    [ObservableProperty]
    private CollectionTriggerType _startTriggerType = CollectionTriggerType.DomainStart;

    /// <summary>
    /// 采集结束触发器类型
    /// </summary>
    [ObservableProperty]
    private CollectionTriggerType _endTriggerType = CollectionTriggerType.DomainReward;

    /// <summary>
    /// 数据采集频率 (FPS)
    /// </summary>
    [ObservableProperty]
    private int _collectionFps = 10;

    /// <summary>
    /// 最大内存使用量 (MB)
    /// </summary>
    [ObservableProperty]
    private int _maxMemoryUsageMb = 4096;

    /// <summary>
    /// 是否保存原始截图
    /// </summary>
    [ObservableProperty]
    private bool _saveRawScreenshots = true;

    /// <summary>
    /// 截图压缩质量 (1-100)
    /// </summary>
    [ObservableProperty]
    private int _screenshotQuality = 85;

    /// <summary>
    /// 是否启用调试模式
    /// </summary>
    [ObservableProperty]
    private bool _debugMode = false;

    /// <summary>
    /// 会话ID前缀
    /// </summary>
    [ObservableProperty]
    private string _sessionIdPrefix = "session";

    /// <summary>
    /// 是否在游戏失焦时自动停止采集
    /// </summary>
    [ObservableProperty]
    private bool _stopOnGameUnfocused = true;

    /// <summary>
    /// 动作检测敏感度 (毫秒)
    /// </summary>
    [ObservableProperty]
    private int _actionDetectionSensitivity = 50;

    /// <summary>
    /// 是否采集无动作帧
    /// </summary>
    [ObservableProperty]
    private bool _collectNoActionFrames = false;

    /// <summary>
    /// 无动作帧采集间隔 (毫秒)
    /// </summary>
    [ObservableProperty]
    private int _noActionFrameInterval = 500;

    /// <summary>
    /// 是否采集结构化状态（包括敌人检测等）
    /// </summary>
    [ObservableProperty]
    private bool _collectStructuredState = true;

    /// <summary>
    /// 是否采集玩家队伍状态
    /// </summary>
    [ObservableProperty]
    private bool _collectPlayerTeam = false;

    /// <summary>
    /// 是否采集敌人状态
    /// </summary>
    [ObservableProperty]
    private bool _collectEnemies = false;

    /// <summary>
    /// 是否采集战斗事件
    /// </summary>
    [ObservableProperty]
    private bool _collectCombatEvents = false;
}

/// <summary>
/// 采集触发器类型
/// </summary>
public enum CollectionTriggerType
{
    [Description("手动控制")]
    Manual,
    
    [Description("秘境开始")]
    DomainStart,
    
    [Description("战斗开始")]
    CombatStart,
    
    [Description("秘境奖励")]
    DomainReward,
    
    [Description("战斗结束")]
    CombatEnd,
    
    [Description("游戏聚焦")]
    GameFocused,
    
    [Description("游戏失焦")]
    GameUnfocused
}
