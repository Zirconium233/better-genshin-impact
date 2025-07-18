using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.GameTask.AIEnv.Model;

/// <summary>
/// 观察数据
/// 包含当前帧截图、结构化状态和动作队列状态
/// </summary>
public class Observation
{
    /// <summary>
    /// 时间戳 (毫秒)
    /// </summary>
    [JsonPropertyName("timestamp_ms")]
    public long TimestampMs { get; set; }

    /// <summary>
    /// 当前帧截图 (Base64编码)
    /// </summary>
    [JsonPropertyName("frame_base64")]
    public string FrameBase64 { get; set; } = string.Empty;

    /// <summary>
    /// 结构化状态 (可选)
    /// </summary>
    [JsonPropertyName("structured_state")]
    public StructuredState? StructuredState { get; set; }

    /// <summary>
    /// 时间戳
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    /// <summary>
    /// 动作队列状态
    /// </summary>
    [JsonPropertyName("action_queue_status")]
    public ActionQueueStatus ActionQueueStatus { get; set; } = new();
}

/// <summary>
/// 结构化状态
/// </summary>
public class StructuredState
{
    /// <summary>
    /// 游戏上下文
    /// </summary>
    [JsonPropertyName("game_context")]
    public GameContext GameContext { get; set; } = new();

    /// <summary>
    /// 玩家队伍信息
    /// </summary>
    [JsonPropertyName("player_team")]
    public PlayerTeam PlayerTeam { get; set; } = new();
}

/// <summary>
/// 游戏上下文
/// </summary>
public class GameContext
{
    /// <summary>
    /// 是否在菜单中
    /// </summary>
    [JsonPropertyName("in_menu")]
    public bool InMenu { get; set; }

    /// <summary>
    /// 是否在战斗中 (性能影响大，默认不开启)
    /// </summary>
    [JsonPropertyName("in_combat")]
    public bool InCombat { get; set; }

    /// <summary>
    /// 是否在秘境中
    /// </summary>
    [JsonPropertyName("in_domain")]
    public bool InDomain { get; set; }

    /// <summary>
    /// 当前角色是否低血量
    /// </summary>
    [JsonPropertyName("is_current_low_hp")]
    public bool IsCurrentLowHp { get; set; }
}

/// <summary>
/// 玩家队伍信息
/// </summary>
public class PlayerTeam
{
    /// <summary>
    /// 队伍角色列表
    /// </summary>
    [JsonPropertyName("team_members")]
    public List<TeamMember> TeamMembers { get; set; } = new();

    /// <summary>
    /// 当前激活角色索引 (0-3)
    /// </summary>
    [JsonPropertyName("active_character_index")]
    public int ActiveCharacterIndex { get; set; }
}

/// <summary>
/// 队伍成员
/// </summary>
public class TeamMember
{
    /// <summary>
    /// 角色名称
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 角色索引 (0-3)
    /// </summary>
    [JsonPropertyName("index")]
    public int Index { get; set; }
}

/// <summary>
/// 动作队列状态
/// </summary>
public class ActionQueueStatus
{
    /// <summary>
    /// 剩余动作
    /// </summary>
    [JsonPropertyName("remaining_actions")]
    public string RemainingActions { get; set; } = string.Empty;

    /// <summary>
    /// 预计完成时间 (毫秒)
    /// </summary>
    [JsonPropertyName("estimated_completion_ms")]
    public long EstimatedCompletionMs { get; set; }

    /// <summary>
    /// 上一次输入
    /// </summary>
    [JsonPropertyName("previous_input")]
    public string PreviousInput { get; set; } = string.Empty;

    /// <summary>
    /// 已完成的指令
    /// </summary>
    [JsonPropertyName("finished_commands")]
    public FinishedCommands FinishedCommands { get; set; } = new();

    /// <summary>
    /// 正在运行的指令
    /// </summary>
    [JsonPropertyName("running")]
    public RunningCommands RunningCommands { get; set; } = new();
}

/// <summary>
/// 已完成的指令
/// </summary>
public class FinishedCommands
{
    /// <summary>
    /// 完成数量
    /// </summary>
    [JsonPropertyName("nums")]
    public int Nums { get; set; }

    /// <summary>
    /// 已完成的指令
    /// </summary>
    [JsonPropertyName("finished")]
    public string Finished { get; set; } = string.Empty;
}

/// <summary>
/// 正在运行的指令
/// </summary>
public class RunningCommands
{
    /// <summary>
    /// 运行数量
    /// </summary>
    [JsonPropertyName("nums")]
    public int Nums { get; set; }

    /// <summary>
    /// 正在运行的指令列表
    /// </summary>
    [JsonPropertyName("running")]
    public List<string> Running { get; set; } = new();
}
