using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.DataCollector.Model;
using Gma.System.MouseKeyHook;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BetterGenshinImpact.GameTask.DataCollector;

/// <summary>
/// 输入监控器 - 重构版，专注于原始输入记录和脚本生成
/// </summary>
public class InputMonitor : IDisposable
{
    private readonly ILogger<InputMonitor> _logger = App.GetLogger<InputMonitor>();
    private readonly object _lockObject = new();
    private bool _disposed = false;
    private bool _isMonitoring = false;

    // 原始输入记录 - 用于生成完整的输入日志
    private readonly ConcurrentQueue<RawInputRecord> _rawInputRecords = new();

    // 按键状态跟踪 - 用于处理交错按键和异常情况
    private readonly Dictionary<Keys, KeyState> _keyStates = new();
    private readonly Dictionary<string, MouseButtonState> _mouseStates = new();

    // 脚本生成缓存
    private readonly Queue<ActionEvent> _actionEvents = new();

    // 原始输入文件写入
    private string _rawInputFilePath = string.Empty;
    private StreamWriter? _rawInputWriter;

    public InputMonitor()
    {
        _logger.LogInformation("输入监控器已初始化");
    }

    /// <summary>
    /// 初始化原始输入文件写入
    /// </summary>
    public void InitializeRawInputFile(string sessionPath)
    {
        try
        {
            _rawInputFilePath = Path.Combine(sessionPath, "raw_inputs.jsonl");
            _rawInputWriter = new StreamWriter(_rawInputFilePath, false);
            _logger.LogInformation("原始输入文件已初始化: {FilePath}", _rawInputFilePath);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "初始化原始输入文件失败");
        }
    }

    /// <summary>
    /// 开始监控
    /// </summary>
    public void StartMonitoring()
    {
        _logger.LogInformation("开始输入监控");
        _isMonitoring = true;

        // 注册到现有的GlobalKeyMouseRecord系统
        GlobalKeyMouseRecord.Instance.InputMonitorKeyDown = OnKeyDown;
        GlobalKeyMouseRecord.Instance.InputMonitorKeyUp = OnKeyUp;
        GlobalKeyMouseRecord.Instance.InputMonitorMouseDown = OnMouseDown;
        GlobalKeyMouseRecord.Instance.InputMonitorMouseUp = OnMouseUp;

        _logger.LogInformation("输入监控已注册到现有钩子系统");
    }

    /// <summary>
    /// 停止监控
    /// </summary>
    public void StopMonitoring()
    {
        _logger.LogInformation("停止输入监控");
        _isMonitoring = false;

        // 从GlobalKeyMouseRecord系统注销
        GlobalKeyMouseRecord.Instance.InputMonitorKeyDown = null;
        GlobalKeyMouseRecord.Instance.InputMonitorKeyUp = null;
        GlobalKeyMouseRecord.Instance.InputMonitorMouseDown = null;
        GlobalKeyMouseRecord.Instance.InputMonitorMouseUp = null;

        // 清理所有状态
        lock (_lockObject)
        {
            _keyStates.Clear();
            _mouseStates.Clear();
            while (_actionEvents.TryDequeue(out _)) { }
            while (_rawInputRecords.TryDequeue(out _)) { }
        }

        // 关闭原始输入文件
        _rawInputWriter?.Close();
        _rawInputWriter?.Dispose();
        _rawInputWriter = null;
    }

    /// <summary>
    /// 按键按下事件 - 重构版，处理交错按键和异常情况
    /// </summary>
    public void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_isMonitoring) return;

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        lock (_lockObject)
        {
            try
            {
                // 记录原始输入
                var rawRecord = new RawInputRecord
                {
                    Type = "KeyDown",
                    Key = e.KeyCode.ToString(),
                    Timestamp = timestamp
                };
                _rawInputRecords.Enqueue(rawRecord);
                WriteRawInputRecord(rawRecord);

                // 处理按键状态 - 鲁棒性处理
                if (_keyStates.TryGetValue(e.KeyCode, out var existingState))
                {
                    if (existingState.IsPressed)
                    {
                        // 异常情况：重复KeyDown，记录警告并更新时间
                        _logger.LogWarning("检测到重复KeyDown事件: {Key}, 上次时间: {LastTime}, 当前时间: {CurrentTime}",
                            e.KeyCode, existingState.PressTime, timestamp);
                        existingState.PressTime = timestamp;
                        existingState.RepeatCount++;
                    }
                    else
                    {
                        existingState.IsPressed = true;
                        existingState.PressTime = timestamp;
                        existingState.RepeatCount = 0;
                    }
                }
                else
                {
                    _keyStates[e.KeyCode] = new KeyState
                    {
                        IsPressed = true,
                        PressTime = timestamp,
                        RepeatCount = 0
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理KeyDown事件时发生异常: {Key}", e.KeyCode);
            }
        }
    }

    /// <summary>
    /// 按键释放事件 - 重构版，处理交错按键和异常情况
    /// </summary>
    public void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (!_isMonitoring) return;

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        lock (_lockObject)
        {
            try
            {
                // 记录原始输入
                var rawRecord = new RawInputRecord
                {
                    Type = "KeyUp",
                    Key = e.KeyCode.ToString(),
                    Timestamp = timestamp
                };
                _rawInputRecords.Enqueue(rawRecord);
                WriteRawInputRecord(rawRecord);

                // 处理按键状态和生成动作事件
                if (_keyStates.TryGetValue(e.KeyCode, out var keyState))
                {
                    if (keyState.IsPressed)
                    {
                        var duration = timestamp - keyState.PressTime;
                        keyState.IsPressed = false;
                        keyState.ReleasTime = timestamp;

                        // 生成动作事件用于脚本生成
                        var actionEvent = new ActionEvent
                        {
                            Key = e.KeyCode,
                            Duration = duration,
                            StartTime = keyState.PressTime,
                            EndTime = timestamp,
                            RepeatCount = keyState.RepeatCount
                        };

                        _actionEvents.Enqueue(actionEvent);

                        // 保持队列大小
                        while (_actionEvents.Count > 1000)
                        {
                            _actionEvents.Dequeue();
                        }
                    }
                    else
                    {
                        // 异常情况：KeyUp但没有对应的KeyDown
                        _logger.LogWarning("检测到孤立的KeyUp事件: {Key}, 时间: {Time}", e.KeyCode, timestamp);
                    }
                }
                else
                {
                    // 异常情况：KeyUp但没有状态记录
                    _logger.LogWarning("检测到未知的KeyUp事件: {Key}, 时间: {Time}", e.KeyCode, timestamp);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理KeyUp事件时发生异常: {Key}", e.KeyCode);
            }
        }
    }

    /// <summary>
    /// 鼠标按下事件 - 处理鼠标输入
    /// </summary>
    public void OnMouseDown(object? sender, MouseEventExtArgs e)
    {
        if (!_isMonitoring) return;

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        lock (_lockObject)
        {
            try
            {
                // 记录原始输入
                var rawRecord = new RawInputRecord
                {
                    Type = "MouseDown",
                    Button = e.Button.ToString(),
                    Timestamp = timestamp
                };
                _rawInputRecords.Enqueue(rawRecord);
                WriteRawInputRecord(rawRecord);

                // 处理鼠标按钮状态
                var buttonKey = e.Button.ToString();
                if (_mouseStates.TryGetValue(buttonKey, out var existingState))
                {
                    if (existingState.IsPressed)
                    {
                        _logger.LogWarning("检测到重复MouseDown事件: {Button}, 上次时间: {LastTime}, 当前时间: {CurrentTime}",
                            e.Button, existingState.PressTime, timestamp);
                        existingState.PressTime = timestamp;
                    }
                    else
                    {
                        existingState.IsPressed = true;
                        existingState.PressTime = timestamp;
                    }
                }
                else
                {
                    _mouseStates[buttonKey] = new MouseButtonState
                    {
                        IsPressed = true,
                        PressTime = timestamp
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理MouseDown事件时发生异常: {Button}", e.Button);
            }
        }
    }

    /// <summary>
    /// 鼠标释放事件 - 处理鼠标输入
    /// </summary>
    public void OnMouseUp(object? sender, MouseEventExtArgs e)
    {
        if (!_isMonitoring) return;

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        lock (_lockObject)
        {
            try
            {
                // 记录原始输入
                var rawRecord = new RawInputRecord
                {
                    Type = "MouseUp",
                    Button = e.Button.ToString(),
                    Timestamp = timestamp
                };
                _rawInputRecords.Enqueue(rawRecord);
                WriteRawInputRecord(rawRecord);

                // 处理鼠标按钮状态和生成动作事件
                var buttonKey = e.Button.ToString();
                if (_mouseStates.TryGetValue(buttonKey, out var mouseState))
                {
                    if (mouseState.IsPressed)
                    {
                        var duration = timestamp - mouseState.PressTime;
                        mouseState.IsPressed = false;
                        mouseState.ReleasTime = timestamp;

                        // 生成动作事件用于脚本生成（只处理左键）
                        if (e.Button == MouseButtons.Left)
                        {
                            var actionEvent = new ActionEvent
                            {
                                Key = Keys.LButton,
                                Duration = duration,
                                StartTime = mouseState.PressTime,
                                EndTime = timestamp,
                                RepeatCount = 0
                            };

                            _actionEvents.Enqueue(actionEvent);

                            // 保持队列大小
                            while (_actionEvents.Count > 1000)
                            {
                                _actionEvents.Dequeue();
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("检测到孤立的MouseUp事件: {Button}, 时间: {Time}", e.Button, timestamp);
                    }
                }
                else
                {
                    _logger.LogWarning("检测到未知的MouseUp事件: {Button}, 时间: {Time}", e.Button, timestamp);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理MouseUp事件时发生异常: {Button}", e.Button);
            }
        }
    }

    /// <summary>
    /// 写入原始输入记录到文件 - 增强容错机制
    /// </summary>
    private void WriteRawInputRecord(RawInputRecord record)
    {
        try
        {
            if (_rawInputWriter != null && !_disposed)
            {
                var json = JsonConvert.SerializeObject(record);
                _rawInputWriter.WriteLine(json);
                _rawInputWriter.Flush();
            }
        }
        catch (ObjectDisposedException)
        {
            // 文件已被释放，停止写入但不记录错误
            _rawInputWriter = null;
        }
        catch (IOException e)
        {
            _logger.LogWarning(e, "写入原始输入记录失败，可能是磁盘空间不足或文件被锁定");
            // 尝试重新初始化文件写入器
            try
            {
                _rawInputWriter?.Close();
                _rawInputWriter?.Dispose();
                _rawInputWriter = null;
            }
            catch
            {
                // 忽略清理异常
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "写入原始输入记录时发生未知异常");
        }
    }

    /// <summary>
    /// 清理事件队列
    /// </summary>
    public void ClearEvents()
    {
        lock (_lockObject)
        {
            while (_actionEvents.TryDequeue(out _)) { }
            while (_rawInputRecords.TryDequeue(out _)) { }
            _keyStates.Clear();
            _mouseStates.Clear();
        }
    }

    /// <summary>
    /// 生成脚本格式的动作字符串 - 重构版，基于ActionEvent
    /// </summary>
    public string GenerateActionScript(long timeWindowMs = 200)
    {
        lock (_lockObject)
        {
            var currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var actions = new List<string>();

            // 获取时间窗口内的动作事件
            var recentEvents = _actionEvents
                .Where(e => currentTime - e.EndTime <= timeWindowMs)
                .OrderBy(e => e.StartTime)
                .ToList();

            foreach (var evt in recentEvents)
            {
                var actionScript = ConvertActionEventToScript(evt);
                if (!string.IsNullOrEmpty(actionScript))
                {
                    actions.Add(actionScript);
                }
            }

            // 如果没有动作，返回等待指令
            if (actions.Count == 0)
            {
                return $"wait({timeWindowMs / 1000.0:F1})";
            }

            return string.Join(",", actions);
        }
    }

    /// <summary>
    /// 将动作事件转换为脚本格式 - 复用BGI现有的脚本语法，增加角色切换
    /// </summary>
    private static string ConvertActionEventToScript(ActionEvent evt)
    {
        var durationSeconds = evt.Duration / 1000.0;

        return evt.Key switch
        {
            Keys.W => durationSeconds > 0.05 ? $"w({durationSeconds:F1})" : "w(0.1)",
            Keys.A => durationSeconds > 0.05 ? $"a({durationSeconds:F1})" : "a(0.1)",
            Keys.S => durationSeconds > 0.05 ? $"s({durationSeconds:F1})" : "s(0.1)",
            Keys.D => durationSeconds > 0.05 ? $"d({durationSeconds:F1})" : "d(0.1)",
            Keys.E => durationSeconds > 0.5 ? "e(hold)" : "e",
            Keys.Q => "q",
            Keys.LButton => durationSeconds > 0.3 ? $"charge({durationSeconds:F1})" : $"attack({durationSeconds:F1})",
            Keys.Space => "jump",
            Keys.LShiftKey or Keys.RShiftKey => durationSeconds > 0.05 ? $"dash({durationSeconds:F1})" : "dash(0.1)",
            // 角色切换 - 使用简短的sw()格式
            Keys.D1 => "sw(1)",
            Keys.D2 => "sw(2)",
            Keys.D3 => "sw(3)",
            Keys.D4 => "sw(4)",
            Keys.F => "f",
            Keys.T => "t",
            _ => string.Empty
        };
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
/// 原始输入记录 - 用于完整的输入日志
/// </summary>
internal class RawInputRecord
{
    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty; // "KeyDown", "KeyUp", "MouseDown", "MouseUp"

    [JsonProperty("key")]
    public string Key { get; set; } = string.Empty; // 按键名称

    [JsonProperty("button")]
    public string Button { get; set; } = string.Empty; // 鼠标按钮名称

    [JsonProperty("timestamp")]
    public long Timestamp { get; set; } // 时间戳(毫秒)
}

/// <summary>
/// 按键状态 - 用于跟踪按键的完整生命周期
/// </summary>
internal class KeyState
{
    public bool IsPressed { get; set; }
    public long PressTime { get; set; }
    public long ReleasTime { get; set; }
    public int RepeatCount { get; set; } // 重复KeyDown的次数
}

/// <summary>
/// 鼠标按钮状态
/// </summary>
internal class MouseButtonState
{
    public bool IsPressed { get; set; }
    public long PressTime { get; set; }
    public long ReleasTime { get; set; }
}

/// <summary>
/// 动作事件 - 用于脚本生成
/// </summary>
internal class ActionEvent
{
    public Keys Key { get; set; }
    public long Duration { get; set; } // 持续时间(毫秒)
    public long StartTime { get; set; }
    public long EndTime { get; set; }
    public int RepeatCount { get; set; } // 异常重复次数
}
