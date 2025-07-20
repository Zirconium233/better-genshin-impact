using BetterGenshinImpact.GameTask.AIEnv.Environment;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.IO;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.Helpers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.View.Windows;

namespace BetterGenshinImpact.GameTask.AIEnv.Schedulers;

/// <summary>
/// 脚本调度器
/// 加载并执行BGI战斗脚本，将脚本语法转换为Action Script格式
/// </summary>
public class ScriptScheduler : IAgentScheduler
{
    private readonly AIEnvParam _param;
    private readonly ILogger<ScriptScheduler> _logger;

    private AIEnvironment? _env;
    private Task? _schedulerTask;
    private CancellationTokenSource? _cts;
    private string _selectedScriptPath = string.Empty;
    private readonly object _scriptLock = new();

    public bool IsRunning { get; private set; }

    public ScriptScheduler(AIEnvParam param)
    {
        _param = param;
        _logger = App.GetLogger<ScriptScheduler>();
    }

    public async Task Start(AIEnvironment env)
    {
        if (IsRunning)
        {
            _logger.LogWarning("脚本调度器已经在运行中");
            return;
        }

        _env = env ?? throw new ArgumentNullException(nameof(env));
        _logger.LogInformation("启动脚本调度器...");

        _cts = new CancellationTokenSource();
        _schedulerTask = Task.Run(SchedulerLoop, _cts.Token);
        IsRunning = true;

        _logger.LogInformation("脚本调度器启动完成，等待用户选择脚本");
        await Task.CompletedTask;
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        _logger.LogInformation("停止脚本调度器...");

        IsRunning = false;
        _cts?.Cancel();

        try
        {
            _schedulerTask?.Wait(5000);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "等待脚本调度器任务完成时发生异常");
        }

        _cts?.Dispose();
        _logger.LogInformation("脚本调度器已停止");
    }

    public void SendUserPrompt(string prompt)
    {
        lock (_scriptLock)
        {
            _logger.LogInformation("脚本调度器接收到发送指令请求");

            // 立即触发脚本选择对话框
            _ = Task.Run(async () =>
            {
                try
                {
                    await TriggerScriptSelection();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理脚本选择时发生异常");
                }
            });
        }
    }

    public string GetStatus()
    {
        if (!IsRunning)
        {
            return "未启动";
        }

        if (string.IsNullOrEmpty(_selectedScriptPath))
        {
            return "等待选择脚本";
        }

        var queueStatus = _env?.GetActionQueueStatus();
        if (queueStatus != null && !string.IsNullOrEmpty(queueStatus.RemainingActions))
        {
            return "执行脚本中";
        }

        return "脚本执行完成";
    }

    /// <summary>
    /// 调度器主循环
    /// </summary>
    private async Task SchedulerLoop()
    {
        _logger.LogInformation("开始脚本调度器循环，等待用户选择脚本");

        while (!_cts!.Token.IsCancellationRequested && IsRunning)
        {
            try
            {
                // 脚本调度器只在收到用户指令时才工作
                // 主循环只是保持运行状态，等待SendUserPrompt调用
                await Task.Delay(1000, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "脚本调度器循环中发生异常");
                await Task.Delay(1000, _cts.Token);
            }
        }

        _logger.LogInformation("脚本调度器循环已结束");
    }

    /// <summary>
    /// 触发脚本选择
    /// </summary>
    private async Task TriggerScriptSelection()
    {
        try
        {
            _logger.LogInformation("显示脚本选择对话框");

            // 在UI线程上显示脚本选择对话框
            string? selectedScript = null;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                selectedScript = ShowScriptSelectionDialog();
            });

            if (!string.IsNullOrWhiteSpace(selectedScript))
            {
                lock (_scriptLock)
                {
                    _selectedScriptPath = selectedScript;
                }

                // 执行选中的脚本
                await ExecuteScript(selectedScript);
            }
            else
            {
                _logger.LogInformation("用户取消脚本选择或未选择脚本");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "脚本调度器触发脚本选择时发生异常");
        }
    }

    /// <summary>
    /// 显示脚本选择对话框
    /// </summary>
    private string? ShowScriptSelectionDialog()
    {
        try
        {
            // 加载战斗脚本列表
            var scriptFolder = Global.Absolute(@"User\AutoFight\");
            var scripts = LoadCombatScripts(scriptFolder);

            if (scripts.Length == 0)
            {
                MessageBox.Show("未找到战斗脚本文件，请先在 User/AutoFight 目录下放置脚本文件。",
                    "脚本调度器", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            // 使用新的脚本选择对话框
            return ScriptSelectionDialog.ShowDialog(scripts.ToList(), scriptFolder, Application.Current.MainWindow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示脚本选择对话框时发生异常");
            return null;
        }
    }

    /// <summary>
    /// 加载战斗脚本列表
    /// </summary>
    private string[] LoadCombatScripts(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                _logger.LogWarning("战斗脚本目录不存在: {Folder}", folder);
                return Array.Empty<string>();
            }

            var files = Directory.GetFiles(folder, "*.txt", SearchOption.AllDirectories);
            var scripts = new List<string> { "根据队伍自动选择" };

            foreach (var file in files)
            {
                var scriptName = file.Replace(folder, "").Replace(".txt", "");
                if (scriptName.StartsWith('\\'))
                {
                    scriptName = scriptName[1..];
                }
                scripts.Add(scriptName);
            }

            return scripts.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载战斗脚本列表时发生异常");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// 执行选中的脚本
    /// </summary>
    private async Task ExecuteScript(string scriptPath)
    {
        try
        {
            _logger.LogInformation("开始执行脚本: {ScriptPath}", scriptPath);

            if (scriptPath == "auto")
            {
                // 自动选择脚本逻辑 - Placeholder 实现
                _logger.LogInformation("使用自动选择脚本模式");
                var autoScript = "w(0.2),attack(1),e,q,charge(0.5)"; // 简单的自动战斗序列
                _env?.AddCommands(autoScript);
                _logger.LogInformation("提交自动战斗脚本: {Script}", autoScript);
                return;
            }

            if (!File.Exists(scriptPath))
            {
                _logger.LogError("脚本文件不存在: {ScriptPath}", scriptPath);
                return;
            }

            // 读取脚本内容
            var scriptContent = await File.ReadAllTextAsync(scriptPath);
            _logger.LogInformation("读取脚本内容，长度: {Length} 字符", scriptContent.Length);

            // 转换脚本为Action Script格式
            var translator = new ScriptTranslator();
            var actionScript = translator.TranslateCombatScript(scriptContent);

            if (!string.IsNullOrWhiteSpace(actionScript))
            {
                _env?.AddCommands(actionScript);
                _logger.LogInformation("脚本转换并提交成功: {ActionScript}", actionScript);
            }
            else
            {
                _logger.LogWarning("脚本转换结果为空，无法执行");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行脚本时发生异常: {ScriptPath}", scriptPath);
        }
    }
}

/// <summary>
/// 脚本转换器 (Placeholder)
/// 负责将BGI脚本语法转换为Action Script格式
/// </summary>
public class ScriptTranslator
{
    private readonly ILogger<ScriptTranslator> _logger;
    private CombatScenes? _combatScenes;

    public ScriptTranslator()
    {
        _logger = App.GetLogger<ScriptTranslator>();
    }

    /// <summary>
    /// 转换战斗脚本
    /// </summary>
    public string TranslateCombatScript(string combatScript)
    {
        try
        {
            _logger.LogInformation("开始转换战斗脚本: {Script}", combatScript);

            // 初始化队伍信息
            InitializeCombatScenes();

            var lines = combatScript.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var actionCommands = new List<string>();

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("//"))
                {
                    continue; // 跳过空行和注释
                }

                var translatedActions = TranslateLine(trimmedLine);
                if (!string.IsNullOrEmpty(translatedActions))
                {
                    actionCommands.Add(translatedActions);
                }
            }

            var result = string.Join(",", actionCommands);
            _logger.LogInformation("脚本转换完成: {Result}", result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "战斗脚本转换失败");
            return "w(1),attack(3),e,q"; // 返回默认脚本
        }
    }

    /// <summary>
    /// 初始化队伍信息
    /// </summary>
    private void InitializeCombatScenes()
    {
        try
        {
            if (_combatScenes == null)
            {
                var imageRegion = TaskControl.CaptureToRectArea();
                _combatScenes = new CombatScenes().InitializeTeam(imageRegion);

                if (!_combatScenes.CheckTeamInitialized())
                {
                    _logger.LogWarning("队伍角色识别失败，将使用默认转换");
                    _combatScenes = null;
                }
                else
                {
                    var avatars = _combatScenes.GetAvatars();
                    _logger.LogInformation("识别到队伍角色: {Names}",
                        string.Join(", ", avatars.Select(a => a.Name)));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化队伍信息失败");
            _combatScenes = null;
        }
    }

    /// <summary>
    /// 转换单行脚本
    /// </summary>
    private string TranslateLine(string line)
    {
        try
        {
            // 解析BGI战斗脚本格式: "角色名 动作序列"
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                _logger.LogWarning("脚本行格式不正确: {Line}", line);
                return string.Empty;
            }

            var characterName = parts[0].Trim();
            var actions = parts[1].Trim();

            // 获取角色切换指令
            var switchCommand = GetSwitchCommand(characterName);

            // 转换动作序列
            var actionCommands = TranslateActions(actions);

            // 组合切换和动作指令
            var commands = new List<string>();
            if (!string.IsNullOrEmpty(switchCommand))
            {
                commands.Add(switchCommand);
            }
            if (!string.IsNullOrEmpty(actionCommands))
            {
                commands.Add(actionCommands);
            }

            return string.Join(",", commands);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "转换脚本行失败: {Line}", line);
            return string.Empty;
        }
    }

    /// <summary>
    /// 获取角色切换指令
    /// </summary>
    private string GetSwitchCommand(string characterName)
    {
        try
        {
            if (_combatScenes == null)
            {
                return string.Empty;
            }

            var avatars = _combatScenes.GetAvatars();

            // 查找角色在队伍中的位置
            for (int i = 0; i < avatars.Count; i++)
            {
                if (avatars[i].Name == characterName)
                {
                    // 角色序号从1开始
                    return $"sw({i + 1})";
                }
            }

            _logger.LogWarning("未找到角色 {CharacterName} 在当前队伍中", characterName);
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取角色切换指令失败: {CharacterName}", characterName);
            return string.Empty;
        }
    }

    /// <summary>
    /// 转换动作序列
    /// </summary>
    private string TranslateActions(string actions)
    {
        try
        {
            var actionList = new List<string>();
            var parts = actions.Split(',', StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                var action = part.Trim();
                var translatedAction = TranslateSingleAction(action);
                if (!string.IsNullOrEmpty(translatedAction))
                {
                    actionList.Add(translatedAction);
                }
            }

            return string.Join(",", actionList);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "转换动作序列失败: {Actions}", actions);
            return string.Empty;
        }
    }

    /// <summary>
    /// 转换单个动作
    /// </summary>
    private string TranslateSingleAction(string action)
    {
        try
        {
            // 处理带参数的动作，如 s(0.2), e(hold), wait(0.3)
            if (action.Contains('(') && action.Contains(')'))
            {
                var actionName = action.Substring(0, action.IndexOf('(')).ToLower();
                var parameter = action.Substring(action.IndexOf('(') + 1,
                    action.IndexOf(')') - action.IndexOf('(') - 1);

                return actionName switch
                {
                    "s" => $"s({parameter})",
                    "w" => $"w({parameter})",
                    "a" => $"a({parameter})",
                    "d" => $"d({parameter})",
                    "e" when parameter == "hold" => "e_hold",
                    "e" => $"e({parameter})",
                    "q" => $"q({parameter})",
                    "wait" => $"wait({parameter})",
                    "attack" => $"attack({parameter})",
                    "charge" => $"charge({parameter})",
                    _ => action // 保持原样
                };
            }
            else
            {
                // 处理无参数的动作
                return action.ToLower() switch
                {
                    "s" => "s(0.1)",
                    "w" => "w(0.1)",
                    "a" => "a(0.1)",
                    "d" => "d(0.1)",
                    "e" => "e",
                    "q" => "q",
                    "attack" => "attack(1)",
                    "jump" => "jump",
                    "charge" => "charge(1.0)",
                    _ => action // 保持原样
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "转换单个动作失败: {Action}", action);
            return action; // 返回原动作
        }
    }

    /// <summary>
    /// 转换地图追踪脚本 (Placeholder)
    /// </summary>
    public string TranslatePathingScript(string pathingScript)
    {
        _logger.LogWarning("TranslatePathingScript Placeholder: 地图追踪脚本转换尚未实现");
        _logger.LogInformation("输入脚本: {Script}", pathingScript);
        
        // Placeholder: 简单的转换示例
        return "w(2.0),f,w(1.0)";
    }

    /// <summary>
    /// 转换键鼠脚本 (Placeholder)
    /// </summary>
    public string TranslateKeyMouseScript(string keyMouseScript)
    {
        _logger.LogWarning("TranslateKeyMouseScript Placeholder: 键鼠脚本转换尚未实现");
        _logger.LogInformation("输入脚本长度: {Length}", keyMouseScript.Length);
        
        // Placeholder: 需要解析JSON格式的键鼠脚本
        return "w(0.5),attack(1),w(0.5)";
    }
}

/// <summary>
/// 脚本加载器 (Placeholder)
/// 负责从文件系统加载各种类型的BGI脚本
/// </summary>
public class ScriptLoader
{
    private readonly ILogger<ScriptLoader> _logger;

    public ScriptLoader()
    {
        _logger = App.GetLogger<ScriptLoader>();
    }

    /// <summary>
    /// 加载战斗脚本文件 (Placeholder)
    /// </summary>
    public async Task<string> LoadCombatScriptAsync(string filePath)
    {
        _logger.LogWarning("LoadCombatScriptAsync Placeholder: 战斗脚本加载尚未实现");
        _logger.LogInformation("脚本路径: {FilePath}", filePath);
        
        await Task.Delay(100); // 模拟文件读取
        return "// Placeholder combat script";
    }

    /// <summary>
    /// 加载地图追踪脚本文件 (Placeholder)
    /// </summary>
    public async Task<string> LoadPathingScriptAsync(string filePath)
    {
        _logger.LogWarning("LoadPathingScriptAsync Placeholder: 地图追踪脚本加载尚未实现");
        _logger.LogInformation("脚本路径: {FilePath}", filePath);
        
        await Task.Delay(100); // 模拟文件读取
        return "{}"; // Placeholder JSON
    }

    /// <summary>
    /// 加载键鼠脚本文件 (Placeholder)
    /// </summary>
    public async Task<string> LoadKeyMouseScriptAsync(string filePath)
    {
        _logger.LogWarning("LoadKeyMouseScriptAsync Placeholder: 键鼠脚本加载尚未实现");
        _logger.LogInformation("脚本路径: {FilePath}", filePath);
        
        await Task.Delay(100); // 模拟文件读取
        return "{}"; // Placeholder JSON
    }
}
