using System;
using System.Collections.Generic;
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
    /// 玩家动作
    /// </summary>
    [JsonProperty("player_action")]
    public PlayerAction? PlayerAction { get; set; }

    /// <summary>
    /// 结构化状态
    /// </summary>
    [JsonProperty("structured_state")]
    public StructuredState StructuredState { get; set; } = new();
}

/// <summary>
/// 玩家动作
/// </summary>
public class PlayerAction
{
    /// <summary>
    /// 移动
    /// </summary>
    [JsonProperty("movement")]
    public MovementEnum Movement { get; set; } = MovementEnum.NO_OP;

    /// <summary>
    /// 角色动作
    /// </summary>
    [JsonProperty("character_action")]
    public CharacterActionEnum CharacterAction { get; set; } = CharacterActionEnum.NO_OP;

    /// <summary>
    /// 视角控制
    /// </summary>
    [JsonProperty("camera_control")]
    public CameraControl CameraControl { get; set; } = new();

    /// <summary>
    /// 目标锁定
    /// </summary>
    [JsonProperty("target_lock")]
    public bool TargetLock { get; set; } = false;
}

/// <summary>
/// 视角控制
/// </summary>
public class CameraControl
{
    /// <summary>
    /// 俯仰角变化量 (像素)
    /// </summary>
    [JsonProperty("pitch_delta")]
    public float PitchDelta { get; set; } = 0.0f;

    /// <summary>
    /// 偏航角变化量 (像素)
    /// </summary>
    [JsonProperty("yaw_delta")]
    public float YawDelta { get; set; } = 0.0f;

    /// <summary>
    /// 是否为零变化
    /// </summary>
    public bool IsZero() => Math.Abs(PitchDelta) < 0.1f && Math.Abs(YawDelta) < 0.1f;
}

/// <summary>
/// 移动枚举
/// </summary>
public enum MovementEnum
{
    NO_OP,
    FORWARD,
    BACKWARD,
    LEFT,
    RIGHT,
    FORWARD_LEFT,
    FORWARD_RIGHT,
    BACKWARD_LEFT,
    BACKWARD_RIGHT,
    FORWARD_SPRINT,
    FORWARD_LEFT_SPRINT,
    FORWARD_RIGHT_SPRINT
}

/// <summary>
/// 角色动作枚举
/// </summary>
public enum CharacterActionEnum
{
    NO_OP,
    NORMAL_ATTACK,
    CHARGED_ATTACK,
    ELEMENTAL_SKILL,
    ELEMENTAL_SKILL_HOLD,
    ELEMENTAL_BURST,
    DODGE,
    JUMP,
    SWITCH_TO_1,
    SWITCH_TO_2,
    SWITCH_TO_3,
    SWITCH_TO_4,
    INTERACT,
    QUICK_USE_GADGET
}

/// <summary>
/// 结构化状态
/// </summary>
public class StructuredState
{
    /// <summary>
    /// 游戏上下文
    /// </summary>
    [JsonProperty("game_context")]
    public GameContext GameContext { get; set; } = new();

    /// <summary>
    /// 玩家队伍
    /// </summary>
    [JsonProperty("player_team")]
    public List<CharacterState> PlayerTeam { get; set; } = new();

    /// <summary>
    /// 当前激活角色索引
    /// </summary>
    [JsonProperty("active_character_index")]
    public int ActiveCharacterIndex { get; set; } = 0;

    /// <summary>
    /// 敌人列表
    /// </summary>
    [JsonProperty("enemies")]
    public List<EnemyState> Enemies { get; set; } = new();

    /// <summary>
    /// 战斗事件
    /// </summary>
    [JsonProperty("combat_events")]
    public List<CombatEvent> CombatEvents { get; set; } = new();
}

/// <summary>
/// 游戏上下文
/// </summary>
public class GameContext
{
    /// <summary>
    /// 游戏阶段
    /// </summary>
    [JsonProperty("game_phase")]
    public string GamePhase { get; set; } = "unknown";

    /// <summary>
    /// 是否在战斗中
    /// </summary>
    [JsonProperty("in_combat")]
    public bool InCombat { get; set; } = false;

    /// <summary>
    /// 是否在菜单中
    /// </summary>
    [JsonProperty("in_menu")]
    public bool InMenu { get; set; } = false;

    /// <summary>
    /// 是否在加载中
    /// </summary>
    [JsonProperty("loading")]
    public bool Loading { get; set; } = false;
}

/// <summary>
/// 角色状态
/// </summary>
public class CharacterState
{
    /// <summary>
    /// 角色名称
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 血量百分比
    /// </summary>
    [JsonProperty("hp_percent")]
    public float HpPercent { get; set; } = 1.0f;

    /// <summary>
    /// 能量百分比
    /// </summary>
    [JsonProperty("energy_percent")]
    public float EnergyPercent { get; set; } = 0.0f;

    /// <summary>
    /// 元素技能冷却时间 (秒)
    /// </summary>
    [JsonProperty("skill_cooldown")]
    public float SkillCooldown { get; set; } = 0.0f;

    /// <summary>
    /// 元素爆发是否可用
    /// </summary>
    [JsonProperty("burst_available")]
    public bool BurstAvailable { get; set; } = false;
}

/// <summary>
/// 敌人状态
/// </summary>
public class EnemyState
{
    /// <summary>
    /// 血量百分比
    /// </summary>
    [JsonProperty("hp_percent")]
    public float HpPercent { get; set; } = 1.0f;

    /// <summary>
    /// 距离估计
    /// </summary>
    [JsonProperty("distance")]
    public float Distance { get; set; } = 0.0f;

    /// <summary>
    /// 屏幕位置
    /// </summary>
    [JsonProperty("position_on_screen")]
    public float[] PositionOnScreen { get; set; } = new float[2];
}

/// <summary>
/// 战斗事件
/// </summary>
public class CombatEvent
{
    /// <summary>
    /// 事件类型
    /// </summary>
    [JsonProperty("event_type")]
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// 事件时间戳
    /// </summary>
    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }

    /// <summary>
    /// 事件数据
    /// </summary>
    [JsonProperty("data")]
    public Dictionary<string, object> Data { get; set; } = new();
}
