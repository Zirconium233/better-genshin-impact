using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace BetterGenshinImpact.GameTask.DataCollector.Model;

/// <summary>
/// 数据记录
/// </summary>
public class DataRecord
{
    /// <summary>
    /// 会话ID
    /// </summary>
    [JsonProperty("session_id")]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// 帧索引
    /// </summary>
    [JsonProperty("frame_index")]
    public int FrameIndex { get; set; }

    /// <summary>
    /// 时间偏移 (毫秒)
    /// </summary>
    [JsonProperty("time_offset_ms")]
    public long TimeOffsetMs { get; set; }

    /// <summary>
    /// 截图文件路径
    /// </summary>
    [JsonProperty("frame_path")]
    public string FramePath { get; set; } = string.Empty;

    /// <summary>
    /// 脚本格式的动作 - 基于action_report.md的设计
    /// </summary>
    [JsonProperty("action_script")]
    public string ActionScript { get; set; } = string.Empty;

    /// <summary>
    /// 结构化状态
    /// </summary>
    [JsonProperty("structured_state")]
    public StructuredState StructuredState { get; set; } = new();

    /// <summary>
    /// 是否可以被回写修改 - 用于支持按键按下帧的回写
    /// 此字段不会序列化到JSON
    /// </summary>
    [JsonIgnore]
    public bool CanBeBackfilled { get; set; } = true;
}

/// <summary>
/// 待回写的动作事件 - 用于支持按键按下帧回写
/// </summary>
public class PendingActionEvent
{
    /// <summary>
    /// 按键
    /// </summary>
    public Keys Key { get; set; }

    /// <summary>
    /// 按下时间戳
    /// </summary>
    public long StartTime { get; set; }

    /// <summary>
    /// 按下时的帧索引 - 用于回写
    /// </summary>
    public int StartFrameIndex { get; set; }

    /// <summary>
    /// 重复次数
    /// </summary>
    public int RepeatCount { get; set; } = 0;
}



/// <summary>
/// 结构化状态 - 简化版，只保留必要信息
/// </summary>
public class StructuredState
{
    /// <summary>
    /// 游戏上下文
    /// </summary>
    [JsonProperty("game_context")]
    public GameContext GameContext { get; set; } = new();

    /// <summary>
    /// 玩家队伍 - 用于大模型切人
    /// </summary>
    [JsonProperty("player_team")]
    public List<CharacterState> PlayerTeam { get; set; } = new();

    /// <summary>
    /// 当前激活角色索引
    /// </summary>
    [JsonProperty("active_character_index")]
    public int ActiveCharacterIndex { get; set; } = 0;
}

/// <summary>
/// 游戏上下文 - 简化版，只保留核心状态
/// </summary>
public class GameContext
{
    /// <summary>
    /// 是否在菜单中 - 通过队伍检测判断
    /// </summary>
    [JsonProperty("in_menu")]
    public bool InMenu { get; set; } = false;

    /// <summary>
    /// 是否在战斗中 - 复用AutoFight检测逻辑
    /// </summary>
    [JsonProperty("in_combat")]
    public bool InCombat { get; set; } = false;

    /// <summary>
    /// 是否在秘境中 - 复用AutoDomain检测逻辑
    /// </summary>
    [JsonProperty("in_domain")]
    public bool InDomain { get; set; } = false;
}

/// <summary>
/// 角色状态 - 简化版，只保留核心信息
/// </summary>
public class CharacterState
{
    /// <summary>
    /// 角色名称
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 是否为当前角色且血量较低 - 复用BGI的CurrentAvatarIsLowHp检测
    /// </summary>
    [JsonProperty("is_current_low_hp")]
    public bool IsCurrentLowHp { get; set; } = false;
}


