using BetterGenshinImpact.GameTask.AIEnv.Environment;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.View.Windows;

namespace BetterGenshinImpact.GameTask.AIEnv.Schedulers;

/// <summary>
/// 人工调度器
/// 通过弹窗让用户手动输入动作脚本
/// </summary>
public class HumanScheduler : IAgentScheduler
{
    private readonly AIEnvParam _param;
    private readonly ILogger<HumanScheduler> _logger;
    
    private AIEnvironment? _env;
    private Task? _schedulerTask;
    private CancellationTokenSource? _cts;
    private string _userPrompt = string.Empty;
    private readonly object _promptLock = new();

    public bool IsRunning { get; private set; }

    public HumanScheduler(AIEnvParam param)
    {
        _param = param;
        _logger = App.GetLogger<HumanScheduler>();
    }

    public async Task Start(AIEnvironment env)
    {
        if (IsRunning)
        {
            _logger.LogWarning("人工调度器已经在运行中");
            return;
        }

        _env = env ?? throw new ArgumentNullException(nameof(env));
        _logger.LogInformation("启动人工调度器...");

        _cts = new CancellationTokenSource();
        _schedulerTask = Task.Run(SchedulerLoop, _cts.Token);
        IsRunning = true;

        _logger.LogInformation("人工调度器启动完成");
        await Task.CompletedTask;
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        _logger.LogInformation("停止人工调度器...");

        IsRunning = false;
        _cts?.Cancel();

        try
        {
            _schedulerTask?.Wait(5000);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "等待调度器任务完成时发生异常");
        }

        _cts?.Dispose();
        _logger.LogInformation("人工调度器已停止");
    }

    public void SendUserPrompt(string prompt)
    {
        lock (_promptLock)
        {
            _userPrompt = prompt ?? string.Empty;
            _logger.LogInformation("人工调度器接收到发送指令请求");

            // 立即触发用户输入对话框
            _ = Task.Run(async () =>
            {
                try
                {
                    await TriggerUserInput();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理用户指令时发生异常");
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

        return "等待指令";
    }

    /// <summary>
    /// 调度器主循环
    /// </summary>
    private async Task SchedulerLoop()
    {
        _logger.LogInformation("开始人工调度器循环，等待用户发送指令");

        while (!_cts!.Token.IsCancellationRequested && IsRunning)
        {
            try
            {
                // 人工调度器只在收到用户指令时才工作
                // 主循环只是保持运行状态，等待SendUserPrompt调用
                await Task.Delay(1000, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "调度器循环中发生异常");
                await Task.Delay(1000, _cts.Token);
            }
        }

        _logger.LogInformation("人工调度器循环已结束");
    }



    /// <summary>
    /// 触发用户输入
    /// </summary>
    private async Task TriggerUserInput()
    {
        try
        {
            _logger.LogInformation("显示人工调度器输入对话框");

            // 在UI线程上显示输入对话框
            string? userInput = null;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                userInput = ShowInputDialog();
            });

            if (!string.IsNullOrWhiteSpace(userInput))
            {
                // 处理特殊指令
                if (userInput.Trim().ToLower() == "exit")
                {
                    _logger.LogInformation("用户输入退出指令，停止调度器");
                    Stop();
                    return;
                }

                // 提交动作脚本到环境
                _env?.AddCommands(userInput);
                _logger.LogInformation("人工调度器提交动作脚本: {ActionScript}", userInput);
            }
            else
            {
                _logger.LogInformation("用户取消输入或输入为空");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "人工调度器触发用户输入时发生异常");
        }
    }

    /// <summary>
    /// 显示输入对话框
    /// </summary>
    private string? ShowInputDialog()
    {
        try
        {
            return AIEnvInputDialog.ShowInputDialog(Application.Current.MainWindow, "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示输入对话框时发生异常");
            return null;
        }
    }
}
