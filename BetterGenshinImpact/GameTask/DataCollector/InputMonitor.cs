using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.DataCollector.Model;
using Gma.System.MouseKeyHook;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace BetterGenshinImpact.GameTask.DataCollector;

/// <summary>
/// 输入监控器
/// </summary>
public class InputMonitor : IDisposable
{
    private readonly ILogger<InputMonitor> _logger = App.GetLogger<InputMonitor>();
    private readonly ConcurrentQueue<InputEvent> _inputEvents = new();
    private readonly Dictionary<GIActions, bool> _actionStates = new();
    private readonly Dictionary<Keys, GIActions> _keyToActionMap = new();
    private readonly Queue<MouseMovement> _mouseMovements = new();
    private readonly object _lockObject = new();
    private bool _disposed = false;
    private IKeyboardMouseEvents? _globalHook;
    private int _lastMouseX = 0;
    private int _lastMouseY = 0;
    private bool _isFirstMouseMove = true;

    public InputMonitor()
    {
        InitializeActionStates();
        InitializeKeyMapping();
    }

    /// <summary>
    /// 初始化动作状态
    /// </summary>
    private void InitializeActionStates()
    {
        // 初始化所有GIActions状态为false
        foreach (GIActions action in Enum.GetValues<GIActions>())
        {
            _actionStates[action] = false;
        }
    }

    /// <summary>
    /// 初始化按键到动作的映射
    /// </summary>
    private void InitializeKeyMapping()
    {
        // 创建KeyId到Keys的映射（简化处理，使用默认键位）
        _keyToActionMap[Keys.W] = GIActions.MoveForward;
        _keyToActionMap[Keys.S] = GIActions.MoveBackward;
        _keyToActionMap[Keys.A] = GIActions.MoveLeft;
        _keyToActionMap[Keys.D] = GIActions.MoveRight;
        _keyToActionMap[Keys.LShiftKey] = GIActions.SprintKeyboard;
        _keyToActionMap[Keys.RShiftKey] = GIActions.SprintKeyboard;
        _keyToActionMap[Keys.LButton] = GIActions.NormalAttack;
        _keyToActionMap[Keys.RButton] = GIActions.SwitchAimingMode; // 右键瞄准模式
        _keyToActionMap[Keys.E] = GIActions.ElementalSkill;
        _keyToActionMap[Keys.Q] = GIActions.ElementalBurst;
        _keyToActionMap[Keys.Space] = GIActions.Jump;
        _keyToActionMap[Keys.D1] = GIActions.SwitchMember1;
        _keyToActionMap[Keys.D2] = GIActions.SwitchMember2;
        _keyToActionMap[Keys.D3] = GIActions.SwitchMember3;
        _keyToActionMap[Keys.D4] = GIActions.SwitchMember4;
        _keyToActionMap[Keys.F] = GIActions.PickUpOrInteract;
        _keyToActionMap[Keys.T] = GIActions.QuickUseGadget;
        _keyToActionMap[Keys.MButton] = GIActions.SwitchAimingMode; // 中键锁定
    }

    /// <summary>
    /// 开始监控
    /// </summary>
    public void StartMonitoring(IntPtr gameHandle)
    {
        _logger.LogInformation("开始输入监控");

        // 使用全局钩子监控输入
        _globalHook = Hook.GlobalEvents();
        _globalHook.KeyDown += OnKeyDown;
        _globalHook.KeyUp += OnKeyUp;
        _globalHook.MouseMoveExt += OnMouseMove;
    }

    /// <summary>
    /// 停止监控
    /// </summary>
    public void StopMonitoring()
    {
        _logger.LogInformation("停止输入监控");

        if (_globalHook != null)
        {
            _globalHook.KeyDown -= OnKeyDown;
            _globalHook.KeyUp -= OnKeyUp;
            _globalHook.MouseMoveExt -= OnMouseMove;
            _globalHook.Dispose();
            _globalHook = null;
        }
    }

    /// <summary>
    /// 按键按下事件
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        lock (_lockObject)
        {
            // 检查是否有对应的GIAction
            if (_keyToActionMap.TryGetValue(e.KeyCode, out var action))
            {
                _actionStates[action] = true;
                _inputEvents.Enqueue(new InputEvent
                {
                    Type = InputEventType.KeyDown,
                    Key = e.KeyCode,
                    Action = action,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            }
        }
    }

    /// <summary>
    /// 按键释放事件
    /// </summary>
    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        lock (_lockObject)
        {
            // 检查是否有对应的GIAction
            if (_keyToActionMap.TryGetValue(e.KeyCode, out var action))
            {
                _actionStates[action] = false;
                _inputEvents.Enqueue(new InputEvent
                {
                    Type = InputEventType.KeyUp,
                    Key = e.KeyCode,
                    Action = action,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            }
        }
    }

    /// <summary>
    /// 鼠标移动事件
    /// </summary>
    private void OnMouseMove(object? sender, MouseEventExtArgs e)
    {
        lock (_lockObject)
        {
            // 计算相对移动量
            if (_isFirstMouseMove)
            {
                _lastMouseX = e.X;
                _lastMouseY = e.Y;
                _isFirstMouseMove = false;
                return;
            }

            var deltaX = e.X - _lastMouseX;
            var deltaY = e.Y - _lastMouseY;

            // 只有移动量不为0时才记录
            if (deltaX != 0 || deltaY != 0)
            {
                _mouseMovements.Enqueue(new MouseMovement
                {
                    DeltaX = deltaX,
                    DeltaY = deltaY,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });

                _lastMouseX = e.X;
                _lastMouseY = e.Y;
            }

            // 保持队列大小
            while (_mouseMovements.Count > 100)
            {
                _mouseMovements.Dequeue();
            }
        }
    }

    /// <summary>
    /// 检测玩家动作
    /// </summary>
    public PlayerAction? DetectPlayerAction()
    {
        lock (_lockObject)
        {
            var movement = DetectMovement();
            var characterAction = DetectCharacterAction();
            var cameraControl = DetectCameraControl();
            var targetLock = DetectTargetLock();

            // 如果没有任何动作，返回null
            if (movement == MovementEnum.NO_OP && 
                characterAction == CharacterActionEnum.NO_OP && 
                cameraControl.IsZero() && 
                !targetLock)
            {
                return null;
            }

            return new PlayerAction
            {
                Movement = movement,
                CharacterAction = characterAction,
                CameraControl = cameraControl,
                TargetLock = targetLock
            };
        }
    }

    /// <summary>
    /// 检测移动
    /// </summary>
    private MovementEnum DetectMovement()
    {
        // 使用GIActions检测移动
        bool forward = IsActionPressed(GIActions.MoveForward);
        bool backward = IsActionPressed(GIActions.MoveBackward);
        bool left = IsActionPressed(GIActions.MoveLeft);
        bool right = IsActionPressed(GIActions.MoveRight);
        bool sprint = IsActionPressed(GIActions.SprintKeyboard);

        // 8方向移动检测
        if (forward && left)
            return sprint ? MovementEnum.FORWARD_LEFT_SPRINT : MovementEnum.FORWARD_LEFT;
        if (forward && right)
            return sprint ? MovementEnum.FORWARD_RIGHT_SPRINT : MovementEnum.FORWARD_RIGHT;
        if (backward && left)
            return MovementEnum.BACKWARD_LEFT;
        if (backward && right)
            return MovementEnum.BACKWARD_RIGHT;
        if (forward)
            return sprint ? MovementEnum.FORWARD_SPRINT : MovementEnum.FORWARD;
        if (backward)
            return MovementEnum.BACKWARD;
        if (left)
            return MovementEnum.LEFT;
        if (right)
            return MovementEnum.RIGHT;

        return MovementEnum.NO_OP;
    }

    /// <summary>
    /// 检测角色动作
    /// </summary>
    private CharacterActionEnum DetectCharacterAction()
    {
        // 检查最近的动作事件
        var recentEvents = _inputEvents.Where(e =>
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - e.Timestamp < 100).ToList();

        foreach (var evt in recentEvents.Where(e => e.Type == InputEventType.KeyDown))
        {
            switch (evt.Action)
            {
                case GIActions.NormalAttack:
                    // 检查是否长按左键进行重击
                    if (IsActionPressed(GIActions.NormalAttack) &&
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - evt.Timestamp > 500)
                    {
                        return CharacterActionEnum.CHARGED_ATTACK;
                    }
                    return CharacterActionEnum.NORMAL_ATTACK;
                case GIActions.ElementalSkill:
                    // 检查是否长按E键
                    return IsActionPressed(GIActions.ElementalSkill) ?
                        CharacterActionEnum.ELEMENTAL_SKILL_HOLD : CharacterActionEnum.ELEMENTAL_SKILL;
                case GIActions.ElementalBurst:
                    return CharacterActionEnum.ELEMENTAL_BURST;
                case GIActions.Jump:
                    return CharacterActionEnum.JUMP;
                case GIActions.SwitchMember1:
                    return CharacterActionEnum.SWITCH_TO_1;
                case GIActions.SwitchMember2:
                    return CharacterActionEnum.SWITCH_TO_2;
                case GIActions.SwitchMember3:
                    return CharacterActionEnum.SWITCH_TO_3;
                case GIActions.SwitchMember4:
                    return CharacterActionEnum.SWITCH_TO_4;
                case GIActions.PickUpOrInteract:
                    return CharacterActionEnum.INTERACT;
                case GIActions.QuickUseGadget:
                    return CharacterActionEnum.QUICK_USE_GADGET;
            }
        }

        return CharacterActionEnum.NO_OP;
    }

    /// <summary>
    /// 检测视角控制
    /// </summary>
    private CameraControl DetectCameraControl()
    {
        var cameraControl = new CameraControl();
        
        if (_mouseMovements.Count > 0)
        {
            var recentMovements = _mouseMovements.Where(m => 
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - m.Timestamp < 100).ToList();

            if (recentMovements.Count > 0)
            {
                cameraControl.YawDelta = recentMovements.Sum(m => m.DeltaX);
                cameraControl.PitchDelta = recentMovements.Sum(m => m.DeltaY);
            }
        }

        return cameraControl;
    }

    /// <summary>
    /// 检测目标锁定
    /// </summary>
    private bool DetectTargetLock()
    {
        // 使用中键锁定目标（简化处理）
        return IsActionPressed(GIActions.SwitchAimingMode);
    }

    /// <summary>
    /// 检查动作是否激活
    /// </summary>
    private bool IsActionPressed(GIActions action)
    {
        return _actionStates.TryGetValue(action, out bool pressed) && pressed;
    }

    /// <summary>
    /// 清理输入事件队列
    /// </summary>
    public void ClearEvents()
    {
        lock (_lockObject)
        {
            while (_inputEvents.TryDequeue(out _)) { }
            _mouseMovements.Clear();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            StopMonitoring();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 输入事件
/// </summary>
internal class InputEvent
{
    public InputEventType Type { get; set; }
    public Keys Key { get; set; }
    public GIActions Action { get; set; }
    public long Timestamp { get; set; }
}

/// <summary>
/// 鼠标移动
/// </summary>
internal class MouseMovement
{
    public float DeltaX { get; set; }
    public float DeltaY { get; set; }
    public long Timestamp { get; set; }
}

/// <summary>
/// 输入事件类型
/// </summary>
internal enum InputEventType
{
    KeyDown,
    KeyUp
}
