using BetterGenshinImpact.GameTask.AIEnv.Model;
using BetterGenshinImpact.GameTask.Model;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AIEnv.Environment;

/// <summary>
/// 动作队列管理器
/// 负责解析、管理和调度动作脚本
/// </summary>
public class ActionQueueManager
{
    private readonly AIEnvParam _param;
    private readonly ILogger<ActionQueueManager> _logger;

    private readonly ConcurrentQueue<ActionGroup> _actionQueue = new();
    private readonly List<ActionGroup> _runningActions = new();
    private readonly List<ActionGroup> _finishedActions = new();

    private string _lastInput = string.Empty;
    private readonly object _queueLock = new();
    private ActionGroup? _currentActionGroup;
    private ActionExecutor? _actionExecutor; // 新增ActionExecutor引用

    public ActionQueueManager(AIEnvParam param)
    {
        _param = param;
        _logger = App.GetLogger<ActionQueueManager>();
    }

    /// <summary>
    /// 设置ActionExecutor引用（解决循环依赖）
    /// </summary>
    public void SetActionExecutor(ActionExecutor actionExecutor)
    {
        _actionExecutor = actionExecutor;
    }

    /// <summary>
    /// 添加动作指令
    /// </summary>
    public void AddCommands(string actionScript)
    {
        if (string.IsNullOrWhiteSpace(actionScript))
        {
            return;
        }

        lock (_queueLock)
        {
            try
            {
                // 解析动作脚本
                var actionGroups = ParseActionScript(actionScript);
                
                if (actionGroups.Count == 0)
                {
                    _logger.LogWarning("解析动作脚本失败或为空: {ActionScript}", actionScript);
                    return;
                }

                // 验证动作脚本
                if (!ValidateActionScript(actionGroups))
                {
                    _logger.LogError("动作脚本验证失败: {ActionScript}", actionScript);
                    return;
                }

                // 处理队列合并或替换
                if (_param.EnableMergeActions)
                {
                    MergeActions(actionGroups);
                }
                else
                {
                    ReplaceActions(actionGroups);
                }

                _lastInput = actionScript;

                if (_param.DebugMode)
                {
                    _logger.LogDebug("添加动作指令成功: {ActionScript}, 队列长度: {QueueLength}",
                        actionScript, _actionQueue.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加动作指令失败: {ActionScript}", actionScript);
            }
        }
    }

    /// <summary>
    /// 获取下一个动作组
    /// </summary>
    public ActionGroup? GetNextActionGroup()
    {
        lock (_queueLock)
        {
            if (_actionQueue.TryDequeue(out var actionGroup))
            {
                _currentActionGroup = actionGroup;
                _runningActions.Add(actionGroup);

                if (_param.DebugMode)
                {
                    _logger.LogDebug("获取下一个动作组: {ActionCount}个动作", actionGroup.Actions.Count);
                }

                return actionGroup;
            }
            return null;
        }
    }

    /// <summary>
    /// 标记动作组完成
    /// </summary>
    public void MarkActionGroupCompleted(ActionGroup actionGroup)
    {
        lock (_queueLock)
        {
            _runningActions.Remove(actionGroup);
            _finishedActions.Add(actionGroup);

            if (_currentActionGroup == actionGroup)
            {
                _currentActionGroup = null;
            }

            if (_param.DebugMode)
            {
                _logger.LogDebug("动作组完成: {ActionGroup}", actionGroup.ToString());
            }
        }
    }

    /// <summary>
    /// 获取队列状态
    /// </summary>
    public ActionQueueStatus GetStatus()
    {
        lock (_queueLock)
        {
            var remainingActions = string.Join(",", _actionQueue.Select(ag => ag.ToString()));
            var runningCommands = _runningActions.Select(ag => ag.ToString()).ToList();
            var finishedCommands = string.Join(",", _finishedActions.TakeLast(5).Select(ag => ag.ToString()));

            // 估算完成时间 (简化计算，转换为毫秒用于兼容)
            var estimatedMs = (long)(_actionQueue.Sum(ag => ag.EstimatedDurationSeconds) * 1000) +
                             (long)(_runningActions.Sum(ag => ag.EstimatedDurationSeconds) * 1000);

            return new ActionQueueStatus
            {
                RemainingActions = remainingActions,
                EstimatedCompletionMs = estimatedMs,
                PreviousInput = _lastInput,
                FinishedCommands = new FinishedCommands
                {
                    Nums = _finishedActions.Count,
                    Finished = finishedCommands
                },
                RunningCommands = new RunningCommands
                {
                    Nums = _runningActions.Count,
                    Running = runningCommands
                }
            };
        }
    }

    /// <summary>
    /// 清空队列
    /// </summary>
    public void Clear()
    {
        lock (_queueLock)
        {
            _actionQueue.Clear();
            _runningActions.Clear();
            _finishedActions.Clear();
            _lastInput = string.Empty;
            _currentActionGroup = null;

            if (_param.DebugMode)
            {
                _logger.LogDebug("动作队列已清空");
            }
        }
    }

    /// <summary>
    /// 停止并清理所有资源
    /// </summary>
    public void Stop()
    {
        lock (_queueLock)
        {
            // 清空所有队列和状态
            Clear();

            _logger.LogInformation("ActionQueueManager已停止");
        }
    }

    /// <summary>
    /// 解析动作脚本
    /// </summary>
    public List<ActionGroup> ParseActionScript(string actionScript)
    {
        var actionGroups = new List<ActionGroup>();

        try
        {
            // 按逗号分割动作组
            var groups = actionScript.Split(',', StringSplitOptions.RemoveEmptyEntries);

            foreach (var group in groups)
            {
                var trimmedGroup = group.Trim();
                if (string.IsNullOrEmpty(trimmedGroup))
                    continue;

                var actionGroup = ParseActionGroup(trimmedGroup);
                if (actionGroup != null)
                {
                    actionGroups.Add(actionGroup);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "解析动作脚本失败: {ActionScript}", actionScript);
        }

        return actionGroups;
    }





    /// <summary>
    /// 解析单个动作组
    /// </summary>
    private ActionGroup? ParseActionGroup(string groupText)
    {
        try
        {
            var actions = new List<GameAction>();
            
            // 按&分割同步动作
            var syncActions = groupText.Split('&', StringSplitOptions.RemoveEmptyEntries);

            foreach (var actionText in syncActions)
            {
                var action = ParseSingleAction(actionText.Trim());
                if (action != null)
                {
                    actions.Add(action);
                }
            }

            if (actions.Count > 0)
            {
                return new ActionGroup
                {
                    Actions = actions,
                    EstimatedDurationSeconds = actions.Max(a => a.DurationSeconds)
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "解析动作组失败: {GroupText}", groupText);
        }

        return null;
    }

    /// <summary>
    /// 解析单个动作
    /// </summary>
    private GameAction? ParseSingleAction(string actionText)
    {
        try
        {
            // 使用正则表达式解析动作
            var match = Regex.Match(actionText, @"^(\w+)(?:\(([^)]*)\))?$");
            if (!match.Success)
            {
                _logger.LogWarning("无法解析动作: {ActionText}", actionText);
                return null;
            }

            var actionType = match.Groups[1].Value;
            var parameter = match.Groups[2].Value;

            return new GameAction
            {
                Type = actionType,
                Parameter = parameter,
                DurationSeconds = ParseDuration(actionType, parameter)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "解析单个动作失败: {ActionText}", actionText);
            return null;
        }
    }

    /// <summary>
    /// 解析动作持续时间（单位：秒）
    /// </summary>
    private double ParseDuration(string actionType, string parameter)
    {
        var defaultDuration = actionType.ToLower() switch
        {
            "w" or "a" or "s" or "d" => 0.1,   // 移动默认0.1秒
            "attack" => 0.1,                   // 攻击默认0.1秒
            "charge" => 0.3,                   // 重击默认0.3秒
            "e" => 0.1,                        // 技能默认0.1秒
            "q" => 0.1,                        // 大招默认0.1秒
            "dash" => 0.1,                     // 冲刺默认0.1秒
            "wait" => 0.2,                     // 等待默认0.2秒（5FPS）
            _ => 0.1
        };

        if (string.IsNullOrEmpty(parameter))
        {
            return defaultDuration;
        }

        // 解析数值参数（秒）
        if (double.TryParse(parameter, out var duration))
        {
            return duration; // 直接使用秒，不转换
        }

        // 特殊参数处理
        if (parameter == "hold")
        {
            return 0.5; // hold动作默认0.5秒
        }

        return defaultDuration;
    }

    /// <summary>
    /// 验证动作脚本
    /// </summary>
    private bool ValidateActionScript(List<ActionGroup> actionGroups)
    {
        if (actionGroups == null || actionGroups.Count == 0)
        {
            _logger.LogWarning("动作脚本为空");
            return false;
        }

        foreach (var group in actionGroups)
        {
            if (group.Actions == null || group.Actions.Count == 0)
            {
                _logger.LogWarning("动作组为空");
                return false;
            }

            foreach (var action in group.Actions)
            {
                // 检查是否为无效的命令（如exit）
                if (IsInvalidCommand(action.Type))
                {
                    _logger.LogError("检测到无效命令: {Command}，AI环境不支持此命令", action.Type);
                    return false;
                }

                // 检查动作类型是否支持
                if (!IsSupportedActionType(action.Type))
                {
                    _logger.LogError("不支持的动作类型: {ActionType}", action.Type);
                    return false;
                }

                // 检查动作持续时间是否合理
                var maxDurationSeconds = TaskContext.Instance().Config.AIEnvConfig.MaxActionDuration;
                if (action.DurationSeconds > maxDurationSeconds)
                {
                    _logger.LogError("动作持续时间过长: {Action}, 持续时间: {Duration}秒，最大允许: {MaxDuration}秒",
                        action.Type, action.DurationSeconds, maxDurationSeconds);
                    return false;
                }

                // 检查持续时间是否为负数
                if (action.DurationSeconds < 0)
                {
                    _logger.LogError("动作持续时间不能为负数: {Action}, 持续时间: {Duration}秒",
                        action.Type, action.DurationSeconds);
                    return false;
                }

                // 检查参数是否合理
                if (!ValidateActionParameter(action))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// 检查是否为无效命令（如exit等调度器命令）
    /// </summary>
    private bool IsInvalidCommand(string actionType)
    {
        var invalidCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "exit", "stop", "pause", "resume", "restart", "quit", "end"
        };

        return invalidCommands.Contains(actionType);
    }

    /// <summary>
    /// 检查是否为支持的动作类型
    /// </summary>
    private bool IsSupportedActionType(string actionType)
    {
        var supportedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "w", "a", "s", "d",           // 移动
            "attack", "charge",           // 攻击
            "e", "q",                     // 技能
            "sw",                         // 角色切换
            "dash",                       // 冲刺
            "jump",                       // 跳跃
            "f", "t",                     // 交互
            "wait",                       // 等待
            "moveby"                      // 鼠标移动
        };

        return supportedTypes.Contains(actionType);
    }

    /// <summary>
    /// 验证动作参数
    /// </summary>
    private bool ValidateActionParameter(GameAction action)
    {
        switch (action.Type.ToLower())
        {
            case "sw":
                // 角色切换参数必须是1-4
                if (!string.IsNullOrEmpty(action.Parameter))
                {
                    if (!int.TryParse(action.Parameter, out var charIndex) || charIndex < 1 || charIndex > 4)
                    {
                        _logger.LogError("角色切换参数无效: {Parameter}，必须是1-4", action.Parameter);
                        return false;
                    }
                }
                break;

            case "moveby":
                // 鼠标移动参数必须是x,y格式
                if (!string.IsNullOrEmpty(action.Parameter))
                {
                    var parts = action.Parameter.Split(',');
                    if (parts.Length != 2 ||
                        !int.TryParse(parts[0], out _) ||
                        !int.TryParse(parts[1], out _))
                    {
                        _logger.LogError("鼠标移动参数无效: {Parameter}，必须是x,y格式", action.Parameter);
                        return false;
                    }
                }
                break;
        }

        return true;
    }

    /// <summary>
    /// 合并动作 - 实现智能合并逻辑
    /// 根据设计文档：新动作替换等待执行的动作，与正在执行的动作进行精细合并
    /// </summary>
    private void MergeActions(List<ActionGroup> newActionGroups)
    {
        if (newActionGroups == null || newActionGroups.Count == 0)
        {
            return;
        }

        // 1. 中断与新动作组冲突的正在执行的动作
        if (_actionExecutor != null && newActionGroups.Count > 0)
        {
            _actionExecutor.InterruptConflictingActions(newActionGroups[0]);
        }

        // 2. 清空等待执行的动作队列
        ClearQueue();

        // 3. 添加所有新动作组到队列
        foreach (var actionGroup in newActionGroups)
        {
            _actionQueue.Enqueue(actionGroup);
        }

        if (_param.DebugMode)
        {
            _logger.LogDebug("合并动作完成，添加 {NewCount} 个新动作组到队列", newActionGroups.Count);
        }
    }



    /// <summary>
    /// 将新动作组与正在执行的动作组进行合并
    /// 例如：正在执行 a(2.0)&w(1.5)，新动作 w(0.2)，结果应该是 a(2.0)&w(0.2)
    /// </summary>
    private ActionGroup? MergeWithRunningActions(ActionGroup runningGroup, ActionGroup newGroup)
    {
        var mergedActions = new List<GameAction>();
        var newActionTypes = newGroup.Actions.Select(a => a.Type.ToLower()).ToHashSet();

        // 1. 保留正在执行的不冲突动作
        foreach (var runningAction in runningGroup.Actions)
        {
            if (!HasConflictingActionType(runningAction.Type, newActionTypes))
            {
                mergedActions.Add(runningAction);
            }
            else if (_param.DebugMode)
            {
                _logger.LogDebug("动作 {Action} 被新动作替换", runningAction.ToString());
            }
        }

        // 2. 添加所有新动作
        mergedActions.AddRange(newGroup.Actions);

        if (mergedActions.Count > 0)
        {
            return new ActionGroup
            {
                Actions = mergedActions,
                EstimatedDurationSeconds = mergedActions.Max(a => a.DurationSeconds)
            };
        }

        return null;
    }

    /// <summary>
    /// 检查动作类型是否与新动作类型集合冲突
    /// </summary>
    private bool HasConflictingActionType(string actionType, HashSet<string> newActionTypes)
    {
        var lowerType = actionType.ToLower();

        // 直接冲突
        if (newActionTypes.Contains(lowerType))
        {
            return true;
        }

        // 预定义冲突规则
        var conflictingTypes = new Dictionary<string, string[]>
        {
            ["w"] = new[] { "s" },
            ["s"] = new[] { "w" },
            ["a"] = new[] { "d" },
            ["d"] = new[] { "a" },
            ["attack"] = new[] { "charge" },
            ["charge"] = new[] { "attack" }
        };

        if (conflictingTypes.TryGetValue(lowerType, out var conflicts))
        {
            return conflicts.Any(conflict => newActionTypes.Contains(conflict));
        }

        return false;
    }

    /// <summary>
    /// 检查两个动作组是否有冲突
    /// </summary>
    private bool HasConflictingActions(ActionGroup group1, ActionGroup group2)
    {
        var conflictingTypes = new HashSet<string[]>
        {
            new[] { "w", "s" },     // 前后移动冲突
            new[] { "a", "d" },     // 左右移动冲突
            new[] { "attack", "charge" }, // 攻击和重击冲突
        };

        foreach (var action1 in group1.Actions)
        {
            foreach (var action2 in group2.Actions)
            {
                // 检查直接冲突
                if (action1.Type.Equals(action2.Type, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // 检查预定义的冲突类型
                foreach (var conflictPair in conflictingTypes)
                {
                    if ((conflictPair.Contains(action1.Type.ToLower()) && conflictPair.Contains(action2.Type.ToLower())))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 清空队列
    /// </summary>
    private void ClearQueue()
    {
        while (_actionQueue.TryDequeue(out _))
        {
            // 清空队列
        }
    }

    /// <summary>
    /// 替换动作（中断当前执行的动作）
    /// </summary>
    private void ReplaceActions(List<ActionGroup> newActionGroups)
    {
        // 1. 中断所有正在执行的动作
        if (_actionExecutor != null)
        {
            _actionExecutor.InterruptAllActions();
        }

        // 2. 清空等待队列
        ClearQueue();

        // 3. 添加新动作组
        foreach (var actionGroup in newActionGroups)
        {
            _actionQueue.Enqueue(actionGroup);
        }

        if (_param.DebugMode)
        {
            _logger.LogDebug("替换所有动作，添加 {NewCount} 个新动作组", newActionGroups.Count);
        }
    }
}

/// <summary>
/// 动作组
/// </summary>
public class ActionGroup
{
    public List<GameAction> Actions { get; set; } = new();
    public double EstimatedDurationSeconds { get; set; }

    public override string ToString()
    {
        return string.Join("&", Actions.Select(a => a.ToString()));
    }
}

/// <summary>
/// 游戏动作
/// </summary>
public class GameAction
{
    public string Type { get; set; } = string.Empty;
    public string Parameter { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }

    public override string ToString()
    {
        return string.IsNullOrEmpty(Parameter) ? Type : $"{Type}({Parameter})";
    }
}
