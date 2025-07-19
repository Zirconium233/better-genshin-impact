using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AIEnv.Environment;

/// <summary>
/// 动作执行器
/// 负责执行动作队列中的指令
/// </summary>
public class ActionExecutor
{
    private readonly AIEnvParam _param;
    private readonly ActionQueueManager _queueManager;
    private readonly ILogger<ActionExecutor> _logger;

    private Task? _executionTask;
    private CancellationTokenSource? _cts;
    private readonly Dictionary<string, CancellationTokenSource> _runningActions = new();
    private readonly object _executionLock = new(); // 新增锁对象，用于保护对正在执行动作的访问
    private ActionGroup? _currentActionGroup; // 将当前动作组提升为字段

    public bool IsRunning { get; private set; }

    public ActionExecutor(AIEnvParam param, ActionQueueManager queueManager)
    {
        _param = param;
        _queueManager = queueManager;
        _logger = App.GetLogger<ActionExecutor>();
    }

    /// <summary>
    /// 启动执行器
    /// </summary>
    public void Start()
    {
        if (IsRunning)
        {
            _logger.LogWarning("动作执行器已经在运行中");
            return;
        }

        if (_param.DebugMode)
        {
            _logger.LogDebug("启动动作执行器...");
        }

        _cts = new CancellationTokenSource();
        _executionTask = Task.Run(ExecutionLoop, _cts.Token);
        IsRunning = true;

        _logger.LogInformation("动作执行器启动完成");
    }

    /// <summary>
    /// 停止执行器
    /// </summary>
    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        _logger.LogInformation("停止动作执行器...");

        IsRunning = false;
        _cts?.Cancel();

        // 停止所有正在运行的动作
        StopAllRunningActions();

        try
        {
            _executionTask?.Wait(5000); // 等待最多5秒
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "等待执行任务完成时发生异常");
        }

        _cts?.Dispose();
        _logger.LogInformation("动作执行器已停止");
    }

    /// <summary>
    /// 核心中断方法：中断当前所有动作
    /// </summary>
    public void InterruptAllActions()
    {
        lock (_executionLock)
        {
            if (_param.DebugMode)
            {
                _logger.LogDebug("接收到全部中断指令，正在停止所有动作...");
            }
            StopAllRunningActions();
            _currentActionGroup = null;
        }
    }

    /// <summary>
    /// 核心合并方法：中断冲突的动作
    /// </summary>
    public void InterruptConflictingActions(ActionGroup newActionGroup)
    {
        lock (_executionLock)
        {
            if (_currentActionGroup == null) return;

            if (_param.DebugMode)
            {
                _logger.LogDebug("接收到合并指令，新动作组: {NewGroup}", newActionGroup.ToString());
            }

            var newActionTypes = newActionGroup.Actions.Select(a => a.Type.ToLower()).ToHashSet();
            var actionsToStop = new List<string>();

            // 查找与新动作冲突的正在执行的动作
            foreach (var runningAction in _currentActionGroup.Actions)
            {
                var actionKey = $"{runningAction.Type}_{runningAction.Parameter}";
                if (_runningActions.ContainsKey(actionKey) && HasConflict(runningAction.Type, newActionTypes))
                {
                    actionsToStop.Add(actionKey);
                }
            }

            // 停止冲突的动作
            foreach (var key in actionsToStop)
            {
                if (_runningActions.TryGetValue(key, out var cts))
                {
                    if (_param.DebugMode) _logger.LogDebug("中断冲突动作: {Key}", key);
                    cts.Cancel();
                    _runningActions.Remove(key);
                }
            }
        }
    }

    /// <summary>
    /// 简单的冲突检测逻辑
    /// </summary>
    private bool HasConflict(string runningActionType, HashSet<string> newActionTypes)
    {
        var type = runningActionType.ToLower();
        if (newActionTypes.Contains(type)) return true;

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

        if (conflictingTypes.TryGetValue(type, out var conflicts))
        {
            return conflicts.Any(conflict => newActionTypes.Contains(conflict));
        }

        return false;
    }

    /// <summary>
    /// 执行循环
    /// </summary>
    private async Task ExecutionLoop()
    {
        _logger.LogInformation("开始动作执行循环");

        while (!_cts!.Token.IsCancellationRequested && IsRunning)
        {
            try
            {
                lock (_executionLock)
                {
                    _currentActionGroup = _queueManager.GetNextActionGroup();
                }

                if (_currentActionGroup != null)
                {
                    await ExecuteActionGroup(_currentActionGroup);

                    lock (_executionLock)
                    {
                        _currentActionGroup = null;
                    }
                }
                else
                {
                    // 没有动作时短暂等待
                    await Task.Delay(50, _cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行动作时发生异常");
                await Task.Delay(1000, _cts.Token); // 异常后等待1秒
            }
        }

        _logger.LogInformation("动作执行循环已结束");
    }

    /// <summary>
    /// 执行动作组
    /// </summary>
    private async Task ExecuteActionGroup(ActionGroup actionGroup)
    {
        if (_param.DebugMode)
        {
            _logger.LogDebug("开始执行动作组: {ActionGroup}", actionGroup.ToString());
        }

        var tasks = new List<Task>();

        // 并行执行动作组中的所有动作
        foreach (var action in actionGroup.Actions)
        {
            var task = ExecuteAction(action);
            tasks.Add(task);
        }

        try
        {
            // 等待所有动作完成
            await Task.WhenAll(tasks);
            
            // 标记动作组完成
            _queueManager.MarkActionGroupCompleted(actionGroup);

            if (_param.DebugMode)
            {
                _logger.LogDebug("动作组执行完成: {ActionGroup}", actionGroup.ToString());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行动作组失败: {ActionGroup}", actionGroup.ToString());
        }
    }

    /// <summary>
    /// 执行单个动作
    /// </summary>
    private async Task ExecuteAction(GameAction action)
    {
        var actionKey = $"{action.Type}_{action.Parameter}";
        var actionCts = new CancellationTokenSource();

        lock (_runningActions)
        {
            _runningActions[actionKey] = actionCts;
        }

        try
        {
            // 确保游戏窗口有焦点
            if (!SystemControl.IsGenshinImpactActive())
            {
                if (_param.DebugMode)
                {
                    _logger.LogDebug("游戏窗口不在前台，尝试激活窗口");
                }
                SystemControl.ActivateWindow();
                await Task.Delay(100); // 等待窗口激活
            }
            if (_param.DebugMode)
            {
                _logger.LogDebug("执行动作: {Action}", action.ToString());
            }

            await ExecuteSpecificAction(action, actionCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (_param.DebugMode)
            {
                _logger.LogDebug("动作被取消: {Action}", action.ToString());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行动作失败: {Action}", action.ToString());
        }
        finally
        {
            lock (_runningActions)
            {
                _runningActions.Remove(actionKey);
            }
            actionCts.Dispose();
        }
    }

    /// <summary>
    /// 执行具体动作
    /// </summary>
    private async Task ExecuteSpecificAction(GameAction action, CancellationToken cancellationToken)
    {
        switch (action.Type.ToLower())
        {
            case "w":
            case "a":
            case "s":
            case "d":
                await ExecuteMovementAction(action, cancellationToken);
                break;
            case "e":
                await ExecuteSkillAction(action, cancellationToken);
                break;
            case "q":
                await ExecuteBurstAction(action, cancellationToken);
                break;
            case "attack":
                await ExecuteAttackAction(action, cancellationToken);
                break;
            case "charge":
                await ExecuteChargeAction(action, cancellationToken);
                break;
            case "sw":
                await ExecuteSwitchAction(action, cancellationToken);
                break;
            case "jump":
                await ExecuteJumpAction(action, cancellationToken);
                break;
            case "dash":
                await ExecuteDashAction(action, cancellationToken);
                break;
            case "f":
                await ExecuteInteractAction(action, cancellationToken);
                break;
            case "wait":
                await ExecuteWaitAction(action, cancellationToken);
                break;
            case "moveby":
                await ExecuteMouseMoveAction(action, cancellationToken);
                break;
            default:
                _logger.LogWarning("未知的动作类型: {ActionType}", action.Type);
                break;
        }
    }

    /// <summary>
    /// 执行移动动作
    /// </summary>
    private async Task ExecuteMovementAction(GameAction action, CancellationToken cancellationToken)
    {
        var giAction = action.Type.ToUpper() switch
        {
            "W" => GIActions.MoveForward,
            "A" => GIActions.MoveLeft,
            "S" => GIActions.MoveBackward,
            "D" => GIActions.MoveRight,
            _ => GIActions.MoveForward
        };

        Simulation.SendInput.SimulateAction(giAction, KeyType.KeyDown);

        try
        {
            await Task.Delay((int)(action.DurationSeconds * 1000), cancellationToken);
        }
        finally
        {
            Simulation.SendInput.SimulateAction(giAction, KeyType.KeyUp);
        }
    }

    /// <summary>
    /// 执行技能动作
    /// </summary>
    private async Task ExecuteSkillAction(GameAction action, CancellationToken cancellationToken)
    {
        if (action.Parameter == "hold")
        {
            Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyDown);
            try
            {
                await Task.Delay((int)(action.DurationSeconds * 1000), cancellationToken);
            }
            finally
            {
                Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyUp);
            }
        }
        else
        {
            Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyPress);
        }
    }

    /// <summary>
    /// 执行爆发动作
    /// </summary>
    private async Task ExecuteBurstAction(GameAction action, CancellationToken cancellationToken)
    {
        Simulation.SendInput.SimulateAction(GIActions.ElementalBurst, KeyType.KeyPress);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 执行攻击动作 - 兼容基于次数和基于时间的语法
    /// attack(2) - 攻击2次
    /// attack(0.5) - 持续攻击0.5秒
    /// </summary>
    private async Task ExecuteAttackAction(GameAction action, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(action.Parameter))
        {
            // 默认单次攻击
            Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyPress);
            return;
        }

        // 判断是基于次数还是基于时间
        if (action.Parameter.Contains('.'))
        {
            // 基于时间的攻击 - attack(0.5)
            await ExecuteTimeBasedAttack(action, cancellationToken);
        }
        else
        {
            // 基于次数的攻击 - attack(2)
            await ExecuteCountBasedAttack(action, cancellationToken);
        }
    }

    /// <summary>
    /// 执行基于次数的攻击
    /// </summary>
    private async Task ExecuteCountBasedAttack(GameAction action, CancellationToken cancellationToken)
    {
        int attackCount = 1;
        if (int.TryParse(action.Parameter, out int parsedCount))
        {
            attackCount = Math.Max(1, Math.Min(parsedCount, 10)); // 限制在1-10次之间
        }

        _logger.LogInformation("执行普通攻击 {Count} 次", attackCount);

        // 参考AutoFight的实现，每次攻击间隔200ms
        for (int i = 0; i < attackCount; i++)
        {
            // 检查是否被取消
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("攻击动作被取消，已执行 {ExecutedCount}/{TotalCount} 次", i, attackCount);
                break;
            }

            // 检查是否有冲突的新动作（打断逻辑）
            if (ShouldInterruptAttack())
            {
                _logger.LogInformation("检测到冲突动作，攻击被打断，已执行 {ExecutedCount}/{TotalCount} 次", i, attackCount);
                break;
            }

            // 执行单次攻击
            Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyPress);

            // 如果不是最后一次攻击，等待间隔
            if (i < attackCount - 1)
            {
                await Task.Delay(200, cancellationToken); // 200ms间隔，参考AutoFight实现
            }
        }
    }

    /// <summary>
    /// 执行基于时间的攻击
    /// </summary>
    private async Task ExecuteTimeBasedAttack(GameAction action, CancellationToken cancellationToken)
    {
        if (!double.TryParse(action.Parameter, out double durationSeconds))
        {
            durationSeconds = 0.1; // 默认0.1秒
        }

        _logger.LogInformation("执行持续攻击 {Duration} 秒", durationSeconds);

        if (durationSeconds > 0.1) // 持续攻击
        {
            Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyDown);
            try
            {
                await Task.Delay((int)(durationSeconds * 1000), cancellationToken);
            }
            finally
            {
                Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyUp);
            }
        }
        else // 单次攻击
        {
            Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyPress);
        }
    }

    /// <summary>
    /// 检查是否应该打断当前攻击
    /// </summary>
    private bool ShouldInterruptAttack()
    {
        // 检查是否有新的冲突动作在队列中等待
        // 这里可以检查队列中是否有charge、e、q等冲突动作
        // 简化实现：如果有新的动作组在等待，就打断当前攻击
        return false; // Placeholder，可以根据需要实现更复杂的打断逻辑
    }

    /// <summary>
    /// 执行重击动作
    /// </summary>
    private async Task ExecuteChargeAction(GameAction action, CancellationToken cancellationToken)
    {
        // 重击通常是长按普通攻击键
        Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyDown);
        try
        {
            await Task.Delay((int)(action.DurationSeconds * 1000), cancellationToken);
        }
        finally
        {
            Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyUp);
        }
    }

    /// <summary>
    /// 执行角色切换动作
    /// </summary>
    private async Task ExecuteSwitchAction(GameAction action, CancellationToken cancellationToken)
    {
        if (int.TryParse(action.Parameter, out var characterIndex) && characterIndex >= 1 && characterIndex <= 4)
        {
            var giAction = characterIndex switch
            {
                1 => GIActions.SwitchMember1,
                2 => GIActions.SwitchMember2,
                3 => GIActions.SwitchMember3,
                4 => GIActions.SwitchMember4,
                _ => GIActions.SwitchMember1
            };

            Simulation.SendInput.SimulateAction(giAction, KeyType.KeyPress);
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// 执行跳跃动作
    /// </summary>
    private async Task ExecuteJumpAction(GameAction action, CancellationToken cancellationToken)
    {
        Simulation.SendInput.SimulateAction(GIActions.Jump, KeyType.KeyPress);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 执行冲刺动作
    /// </summary>
    private async Task ExecuteDashAction(GameAction action, CancellationToken cancellationToken)
    {
        if (action.DurationSeconds > 0.1) // 持续冲刺
        {
            Simulation.SendInput.SimulateAction(GIActions.SprintKeyboard, KeyType.KeyDown);
            try
            {
                await Task.Delay((int)(action.DurationSeconds * 1000), cancellationToken);
            }
            finally
            {
                Simulation.SendInput.SimulateAction(GIActions.SprintKeyboard, KeyType.KeyUp);
            }
        }
        else // 单次冲刺
        {
            Simulation.SendInput.SimulateAction(GIActions.SprintKeyboard, KeyType.KeyPress);
        }
    }
    

    /// <summary>
    /// 执行交互动作
    /// </summary>
    private async Task ExecuteInteractAction(GameAction action, CancellationToken cancellationToken)
    {
        Simulation.SendInput.SimulateAction(GIActions.PickUpOrInteract, KeyType.KeyPress);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 执行等待动作
    /// </summary>
    private async Task ExecuteWaitAction(GameAction action, CancellationToken cancellationToken)
    {
        await Task.Delay((int)(action.DurationSeconds * 1000), cancellationToken);
    }

    /// <summary>
    /// 执行鼠标移动动作
    /// </summary>
    private async Task ExecuteMouseMoveAction(GameAction action, CancellationToken cancellationToken)
    {
        var parts = action.Parameter.Split(',');
        if (parts.Length == 2 && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y))
        {
            Simulation.SendInput.Mouse.MoveMouseBy(x, y);
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// 停止所有正在运行的动作
    /// </summary>
    private void StopAllRunningActions()
    {
        lock (_runningActions)
        {
            foreach (var cts in _runningActions.Values)
            {
                cts.Cancel();
            }
            _runningActions.Clear();
        }
    }
}
