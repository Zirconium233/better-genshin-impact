using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.AIEnv.Environment;
using BetterGenshinImpact.GameTask.AIEnv.Schedulers;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AIEnv;

/// <summary>
/// AI环境主任务
/// 负责创建和管理AIEnv与Scheduler
/// </summary>
public class AIEnvTask : ISoloTask
{
    public string Name => "AI环境";

    private readonly AIEnvParam _taskParam;
    private readonly AIEnvConfig _config;
    private readonly ILogger<AIEnvTask> _logger;

    private AIEnvironment? _aiEnv;
    private IAgentScheduler? _scheduler;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// 获取AI环境实例（用于测试）
    /// </summary>
    public AIEnvironment? GetAIEnvironment() => _aiEnv;

    public AIEnvTask(AIEnvParam taskParam)
    {
        _taskParam = taskParam;
        _config = TaskContext.Instance().Config.AIEnvConfig;
        _logger = App.GetLogger<AIEnvTask>();
    }

    public async Task Start(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("→ 开始启动AI环境任务");
            
            // 创建取消令牌源
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            
            // 初始化
            await InitializeAsync();
            
            // 启动环境
            await StartEnvironmentAsync();
            
            // 启动调度器
            await StartSchedulerAsync();
            
            _logger.LogInformation("→ AI环境任务启动完成");
            
            // 等待取消信号
            await Task.Delay(Timeout.Infinite, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("→ AI环境任务被取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "→ AI环境任务执行异常");
            throw;
        }
        finally
        {
            await CleanupAsync();
        }
    }

    /// <summary>
    /// 初始化任务
    /// </summary>
    private async Task InitializeAsync()
    {
        _logger.LogInformation("初始化AI环境...");
        
        // 确保游戏窗口激活
        SystemControl.ActivateWindow();
        await Task.Delay(1000);
        
        // 检查OCR初始化 - 通过尝试访问Paddle服务来验证
        try
        {
            var _ = OcrFactory.Paddle;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("OCR服务未初始化，请先启动截图器", ex);
        }
        
        _logger.LogInformation("AI环境初始化完成");
    }

    /// <summary>
    /// 启动环境
    /// </summary>
    private async Task StartEnvironmentAsync()
    {
        _logger.LogInformation("启动AI环境实例...");
        
        _aiEnv = new AIEnvironment(_taskParam, _cts!.Token);
        await _aiEnv.StartAsync();
        
        _logger.LogInformation("AI环境实例启动完成");
    }

    /// <summary>
    /// 启动调度器
    /// </summary>
    private async Task StartSchedulerAsync()
    {
        _logger.LogInformation("启动调度器: {SchedulerType}", _taskParam.SchedulerType);
        
        _scheduler = CreateScheduler(_taskParam.SchedulerType);
        await _scheduler.Start(_aiEnv!);
        
        _logger.LogInformation("调度器启动完成");
    }

    /// <summary>
    /// 创建调度器实例
    /// </summary>
    private IAgentScheduler CreateScheduler(string schedulerType)
    {
        return schedulerType switch
        {
            "HumanScheduler" => new HumanScheduler(_taskParam),
            "LlmApiScheduler" => new LlmApiScheduler(_taskParam), // Placeholder
            "ScriptScheduler" => new ScriptScheduler(_taskParam), // Placeholder
            _ => throw new ArgumentException($"未知的调度器类型: {schedulerType}")
        };
    }

    /// <summary>
    /// 停止任务
    /// </summary>
    public void Stop()
    {
        _logger.LogInformation("停止AI环境任务...");

        try
        {
            // 取消所有操作
            _cts?.Cancel();

            // 停止环境
            if (_aiEnv != null)
            {
                _aiEnv.StopAsync().Wait(5000); // 等待最多5秒
            }

            // 停止调度器
            _scheduler?.Stop();

            _logger.LogInformation("AI环境任务已停止");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止AI环境任务时发生异常");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// 发送用户指令给调度器
    /// </summary>
    public void SendUserPrompt(string prompt)
    {
        if (_scheduler != null)
        {
            _logger.LogInformation("发送用户指令: {Prompt}", prompt);
            _scheduler.SendUserPrompt(prompt);
        }
        else
        {
            _logger.LogWarning("调度器未初始化，无法发送用户指令");
        }
    }

    /// <summary>
    /// 获取当前状态
    /// </summary>
    public string GetStatus()
    {
        if (_aiEnv == null || _scheduler == null)
        {
            return "未启动";
        }

        return _aiEnv.IsRunning ? "运行中" : "已停止";
    }

    /// <summary>
    /// 清理资源
    /// </summary>
    private async Task CleanupAsync()
    {
        _logger.LogInformation("清理AI环境资源...");
        
        try
        {
            _scheduler?.Stop();
            if (_aiEnv != null)
            {
                await _aiEnv.StopAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理资源时发生异常");
        }
        finally
        {
            _cts?.Dispose();
            _logger.LogInformation("AI环境资源清理完成");
        }
    }
}
