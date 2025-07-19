using BetterGenshinImpact.GameTask.AIEnv.Environment;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Threading;
using BetterGenshinImpact.View.Windows;

namespace BetterGenshinImpact.GameTask.AIEnv.Schedulers;

/// <summary>
/// LLM API调度器 (Placeholder实现)
/// 通过HTTP API调用外部LLM服务进行决策
/// </summary>
public class LlmApiScheduler : IAgentScheduler
{
    private readonly AIEnvParam _param;
    private readonly ILogger<LlmApiScheduler> _logger;

    private AIEnvironment? _env;
    private Task? _schedulerTask;
    private CancellationTokenSource? _cts;
    private string _userPrompt = string.Empty;
    private readonly object _promptLock = new();

    public bool IsRunning { get; private set; }

    public LlmApiScheduler(AIEnvParam param)
    {
        _param = param;
        _logger = App.GetLogger<LlmApiScheduler>();
    }

    public async Task Start(AIEnvironment env)
    {
        if (IsRunning)
        {
            _logger.LogWarning("LLM调度器已经在运行中");
            return;
        }

        _env = env ?? throw new ArgumentNullException(nameof(env));
        _logger.LogInformation("启动LLM调度器...");
        _logger.LogWarning("LLM调度器当前为Placeholder实现，仅支持基本的用户输入处理");
        _logger.LogInformation("API端点: {ApiEndpoint}", _param.ApiEndpoint);

        _cts = new CancellationTokenSource();
        _schedulerTask = Task.Run(SchedulerLoop, _cts.Token);
        IsRunning = true;

        _logger.LogInformation("LLM调度器启动完成，等待用户输入Prompt");
        await Task.CompletedTask;
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        _logger.LogInformation("停止LLM调度器...");

        IsRunning = false;
        _cts?.Cancel();

        try
        {
            _schedulerTask?.Wait(5000);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "等待LLM调度器任务完成时发生异常");
        }

        _cts?.Dispose();
        _logger.LogInformation("LLM调度器已停止");
    }

    public void SendUserPrompt(string prompt)
    {
        lock (_promptLock)
        {
            _userPrompt = prompt ?? string.Empty;
            _logger.LogInformation("LLM调度器接收到发送指令请求");

            // 立即触发用户输入对话框
            _ = Task.Run(async () =>
            {
                try
                {
                    await TriggerUserPromptInput();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理用户Prompt时发生异常");
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

        var queueStatus = _env?.GetActionQueueStatus();
        if (queueStatus != null && !string.IsNullOrEmpty(queueStatus.RemainingActions))
        {
            return "执行中";
        }

        return "等待Prompt";
    }

    /// <summary>
    /// 调度器主循环
    /// </summary>
    private async Task SchedulerLoop()
    {
        _logger.LogInformation("开始LLM调度器循环，等待用户输入Prompt");

        while (!_cts!.Token.IsCancellationRequested && IsRunning)
        {
            try
            {
                // LLM调度器只在收到用户指令时才工作
                // 主循环只是保持运行状态，等待SendUserPrompt调用
                await Task.Delay(1000, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LLM调度器循环中发生异常");
                await Task.Delay(1000, _cts.Token);
            }
        }

        _logger.LogInformation("LLM调度器循环已结束");
    }

    /// <summary>
    /// 触发用户Prompt输入
    /// </summary>
    private async Task TriggerUserPromptInput()
    {
        try
        {
            _logger.LogInformation("显示LLM调度器Prompt输入对话框");

            // 在UI线程上显示输入对话框
            string? userPrompt = null;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                userPrompt = ShowPromptInputDialog();
            });

            if (!string.IsNullOrWhiteSpace(userPrompt))
            {
                // 处理特殊指令
                if (userPrompt.Trim().ToLower() == "exit")
                {
                    _logger.LogInformation("用户输入退出指令，停止调度器");
                    Stop();
                    return;
                }

                // Placeholder: 简单的Prompt处理
                var actionScript = ProcessPrompt(userPrompt);
                if (!string.IsNullOrWhiteSpace(actionScript))
                {
                    _env?.AddCommands(actionScript);
                    _logger.LogInformation("LLM调度器提交动作脚本: {ActionScript}", actionScript);
                }
            }
            else
            {
                _logger.LogInformation("用户取消输入或输入为空");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM调度器触发Prompt输入时发生异常");
        }
    }

    /// <summary>
    /// 显示Prompt输入对话框
    /// </summary>
    private string? ShowPromptInputDialog()
    {
        try
        {
            return AIEnvInputDialog.ShowInputDialog(Application.Current.MainWindow, _userPrompt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示Prompt输入对话框时发生异常");
            return null;
        }
    }

    /// <summary>
    /// 处理用户Prompt (Placeholder实现)
    /// </summary>
    private string ProcessPrompt(string prompt)
    {
        _logger.LogInformation("处理用户Prompt: {Prompt}", prompt);
        _logger.LogWarning("LLM调度器Placeholder: 使用简单的关键词匹配生成动作脚本");

        // Placeholder: 简单的关键词匹配
        var lowerPrompt = prompt.ToLower();

        if (lowerPrompt.Contains("攻击") || lowerPrompt.Contains("attack"))
        {
            return "attack(3),e,q";
        }
        else if (lowerPrompt.Contains("移动") || lowerPrompt.Contains("move"))
        {
            return "w(2),a(1),d(1),s(1)";
        }
        else if (lowerPrompt.Contains("技能") || lowerPrompt.Contains("skill"))
        {
            return "e,q,charge(1.5)";
        }
        else if (lowerPrompt.Contains("跳跃") || lowerPrompt.Contains("jump"))
        {
            return "jump,w(1)";
        }
        else
        {
            // 默认动作
            return "w(1),attack(2),e";
        }
    }
}

/// <summary>
/// VLM API客户端 (Placeholder)
/// 负责与外部视觉语言模型API通信
/// </summary>
public class VlmApiClient
{
    private readonly string _apiEndpoint;
    private readonly ILogger<VlmApiClient> _logger;

    public VlmApiClient(string apiEndpoint)
    {
        _apiEndpoint = apiEndpoint;
        _logger = App.GetLogger<VlmApiClient>();
    }

    /// <summary>
    /// 调用VLM API (Placeholder)
    /// </summary>
    public async Task<string> CallVlmApiAsync(string prompt, string imageBase64)
    {
        _logger.LogWarning("VlmApiClient Placeholder: 实际API调用尚未实现");
        _logger.LogInformation("计划调用API: {ApiEndpoint}", _apiEndpoint);
        _logger.LogInformation("提示词长度: {PromptLength}, 图像数据长度: {ImageLength}", 
            prompt.Length, imageBase64.Length);

        // Placeholder返回
        await Task.Delay(1000); // 模拟API调用延迟
        return "w(1.0),e"; // 示例动作脚本
    }

    /// <summary>
    /// 构建多模态提示词 (Placeholder)
    /// </summary>
    public string BuildMultiModalPrompt(string systemPrompt, string userPrompt, object[] historyFrames)
    {
        _logger.LogWarning("BuildMultiModalPrompt Placeholder: 提示词构建逻辑尚未实现");
        
        // Placeholder实现
        return $"System: {systemPrompt}\nUser: {userPrompt}\nHistory frames: {historyFrames.Length}";
    }
}
