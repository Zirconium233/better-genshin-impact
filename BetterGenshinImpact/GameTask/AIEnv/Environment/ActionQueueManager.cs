using BetterGenshinImpact.GameTask.AIEnv.Model;
using BetterGenshinImpact.GameTask.Model;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

    // 错误处理
    private int _errorCount = 0;
    private readonly int _maxErrorCount;

    public ActionQueueManager(AIEnvParam param)
    {
        _param = param;
        _logger = App.GetLogger<ActionQueueManager>();
        _maxErrorCount = param.MaxErrorCount;
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
                    throw new ArgumentException($"解析动作脚本失败或为空: {actionScript}");
                }

                // 验证动作脚本
                if (!ValidateActionScript(actionGroups))
                {
                    _logger.LogError("动作脚本验证失败: {ActionScript}", actionScript);
                    throw new ArgumentException($"动作脚本验证失败: {actionScript}");
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
                _errorCount++;
                _logger.LogError(ex, "添加动作指令失败: {ActionScript}, 错误计数: {ErrorCount}/{MaxErrorCount}",
                    actionScript, _errorCount, _maxErrorCount);

                // 如果错误次数达到阈值，抛出特殊异常并清零计数
                if (_errorCount >= _maxErrorCount)
                {
                    _logger.LogError("错误次数达到上限 {MaxErrorCount}，抛出异常并重置计数", _maxErrorCount);
                    _errorCount = 0; // 清零计数
                    throw new InvalidOperationException($"动作队列错误次数达到上限 {_maxErrorCount}，请检查输入");
                }

                // 重新抛出原始异常
                throw;
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
            // 智能分割动作组，考虑括号内的逗号
            var groups = SplitActionGroups(actionScript);

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
    /// 智能分割动作组，考虑括号内的逗号
    /// </summary>
    private List<string> SplitActionGroups(string actionScript)
    {
        var groups = new List<string>();
        var currentGroup = new StringBuilder();
        var parenthesesLevel = 0;

        for (int i = 0; i < actionScript.Length; i++)
        {
            char c = actionScript[i];

            if (c == '(')
            {
                parenthesesLevel++;
                currentGroup.Append(c);
            }
            else if (c == ')')
            {
                parenthesesLevel--;
                currentGroup.Append(c);
            }
            else if (c == ',' && parenthesesLevel == 0)
            {
                // 只有在括号外的逗号才作为分隔符
                if (currentGroup.Length > 0)
                {
                    groups.Add(currentGroup.ToString().Trim());
                    currentGroup.Clear();
                }
            }
            else
            {
                currentGroup.Append(c);
            }
        }

        // 添加最后一个组
        if (currentGroup.Length > 0)
        {
            groups.Add(currentGroup.ToString().Trim());
        }

        return groups;
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
        var actionTypeLower = actionType.ToLower();

        // 即时动作，统一设为200ms
        if (actionTypeLower == "moveby" || actionTypeLower == "sw" ||
            actionTypeLower == "jump" || actionTypeLower == "f" || actionTypeLower == "t")
        {
            return 0.2; // 即时动作，200ms
        }

        var defaultDuration = actionTypeLower switch
        {
            "w" or "a" or "s" or "d" => 0.1,   // 移动默认0.1秒
            "attack" => 0.2,                   // attack默认200ms（单次攻击）
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

        // 特殊处理attack动作：判断是基于次数还是基于时间
        if (actionTypeLower == "attack")
        {
            if (parameter.Contains('.'))
            {
                // 基于时间的attack(0.5) - 有持续时间
                if (double.TryParse(parameter, out var attackDuration))
                {
                    return attackDuration;
                }
            }
            else
            {
                // 基于次数的attack(2) - 计算实际持续时间
                if (int.TryParse(parameter, out var attackCount))
                {
                    // 每次攻击200ms，最后一次不需要等待间隔
                    // 所以总时间 = (次数 - 1) * 200ms + 单次攻击时间(~50ms)
                    // 简化为: 次数 * 200ms，因为最后一次攻击也需要一定时间完成
                    var totalMs = Math.Max(1, Math.Min(attackCount, 10)) * 200;
                    return totalMs / 1000.0; // 转换为秒
                }
            }
            // 默认单次攻击
            return 0.2; // 200ms
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
    /// 特殊处理attack动作：attack(2) + attack(3) = attack(3)，但会尝试给执行器增加次数
    /// </summary>
    private ActionGroup? MergeWithRunningActions(ActionGroup runningGroup, ActionGroup newGroup)
    {
        var mergedActions = new List<GameAction>();
        var newActionTypes = newGroup.Actions.Select(a => a.Type.ToLower()).ToHashSet();

        // 1. 处理正在执行的动作
        foreach (var runningAction in runningGroup.Actions)
        {
            if (!HasConflictingActionType(runningAction.Type, newActionTypes))
            {
                mergedActions.Add(runningAction);
            }
            else
            {
                // 特殊处理attack动作的合并
                if (runningAction.Type.ToLower() == "attack")
                {
                    var newAttackAction = newGroup.Actions.FirstOrDefault(a => a.Type.ToLower() == "attack");
                    if (newAttackAction != null)
                    {
                        // 尝试合并attack次数
                        var mergedAttackAction = MergeAttackActions(runningAction, newAttackAction);
                        if (mergedAttackAction != null)
                        {
                            mergedActions.Add(mergedAttackAction);
                            if (_param.DebugMode)
                            {
                                _logger.LogDebug("合并attack动作: {Running} + {New} = {Merged}",
                                    runningAction.ToString(), newAttackAction.ToString(), mergedAttackAction.ToString());
                            }
                            continue;
                        }
                    }
                }

                if (_param.DebugMode)
                {
                    _logger.LogDebug("动作 {Action} 被新动作替换", runningAction.ToString());
                }
            }
        }

        // 2. 添加新动作（排除已经合并的attack动作）
        foreach (var newAction in newGroup.Actions)
        {
            if (newAction.Type.ToLower() == "attack")
            {
                // 检查是否已经在合并过程中处理了
                var existingAttack = mergedActions.FirstOrDefault(a => a.Type.ToLower() == "attack");
                if (existingAttack == null)
                {
                    mergedActions.Add(newAction);
                }
            }
            else
            {
                mergedActions.Add(newAction);
            }
        }

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
    /// 合并两个attack动作，返回合并后的动作
    /// </summary>
    private GameAction? MergeAttackActions(GameAction runningAttack, GameAction newAttack)
    {
        try
        {
            // 解析当前正在执行的attack次数
            int runningCount = 1;
            if (!string.IsNullOrEmpty(runningAttack.Parameter) && int.TryParse(runningAttack.Parameter, out int parsedRunning))
            {
                runningCount = parsedRunning;
            }

            // 解析新的attack次数
            int newCount = 1;
            if (!string.IsNullOrEmpty(newAttack.Parameter) && int.TryParse(newAttack.Parameter, out int parsedNew))
            {
                newCount = parsedNew;
            }

            // 使用新的次数（替换逻辑），但可以通知执行器增加次数
            // 这里简化为直接使用新的次数，实际可以实现更复杂的合并逻辑
            return new GameAction
            {
                Type = "attack",
                Parameter = newCount.ToString(),
                DurationSeconds = 0.0 // attack是即时动作
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "合并attack动作失败");
            return newAttack; // 返回新动作作为fallback
        }
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

    /// <summary>
    /// 获取错误计数
    /// </summary>
    public int GetErrorCount()
    {
        return _errorCount;
    }

    /// <summary>
    /// 重置错误计数
    /// </summary>
    public void ResetErrorCount()
    {
        _errorCount = 0;
        _logger.LogInformation("动作队列错误计数已重置");
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
