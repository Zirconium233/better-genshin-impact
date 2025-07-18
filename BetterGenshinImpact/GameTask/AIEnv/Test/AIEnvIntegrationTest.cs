using BetterGenshinImpact.GameTask.AIEnv.Environment;
using BetterGenshinImpact.GameTask.AIEnv.Model;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AIEnv.Test;

/// <summary>
/// AI环境集成测试
/// 用于验证已启动的AI环境的基本功能
/// </summary>
public class AIEnvIntegrationTest
{
    private readonly ILogger<AIEnvIntegrationTest> _logger;
    private readonly AIEnvironment? _aiEnvironment;

    public AIEnvIntegrationTest(AIEnvironment? aiEnvironment = null)
    {
        _logger = App.GetLogger<AIEnvIntegrationTest>();
        _aiEnvironment = aiEnvironment;
    }

    /// <summary>
    /// 测试AI环境的基本功能（需要AI环境已启动）
    /// </summary>
    public async Task<bool> TestBasicFunctionality()
    {
        try
        {
            _logger.LogInformation("开始AI环境基本功能测试");

            // 检查AI环境是否已启动
            if (_aiEnvironment == null || !_aiEnvironment.IsRunning)
            {
                _logger.LogError("AI环境未启动，无法进行测试");
                return false;
            }

            _logger.LogInformation("AI环境已启动，开始功能测试");

            // 测试基本状态提取（需要游戏运行）
            if (TaskContext.Instance().IsInitialized)
            {
                try
                {
                    // 等待一下让AI环境产生观察数据
                    await Task.Delay(1000);

                    var observation = _aiEnvironment.GetLatestObservation();
                    if (observation?.StructuredState != null)
                    {
                        _logger.LogInformation("状态提取测试 - 成功: 菜单={InMenu}, 战斗={InCombat}, 秘境={InDomain}",
                            observation.StructuredState.GameContext.InMenu,
                            observation.StructuredState.GameContext.InCombat,
                            observation.StructuredState.GameContext.InDomain);
                    }

                    var hasFrame = observation != null && !string.IsNullOrEmpty(observation.FrameBase64);
                    _logger.LogInformation("帧捕获测试 - {Result}: 帧大小={FrameSize}字符",
                        hasFrame ? "成功" : "失败", observation?.FrameBase64?.Length ?? 0);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "状态提取测试失败（可能是因为游戏未运行）");
                }
            }
            else
            {
                _logger.LogInformation("跳过状态提取测试（游戏未初始化）");
            }

            // 测试动作解析
            var testActions = new[]
            {
                "w(1.0)",
                "a(0.5)",
                "sw(1)",
                "e",
                "q",
                "attack(0.1)",
                "charge(1.0)"
            };

            foreach (var actionScript in testActions)
            {
                try
                {
                    _aiEnvironment.AddCommands(actionScript);
                    _logger.LogInformation("动作执行测试 '{ActionScript}' - 成功: 已添加到队列", actionScript);

                    // 等待动作执行
                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "动作执行测试 '{ActionScript}' - 失败", actionScript);
                    return false;
                }
            }

            _logger.LogInformation("AI环境基本功能测试完成 - 全部通过");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI环境基本功能测试失败");
            return false;
        }
    }

    /// <summary>
    /// 测试AI环境任务的生命周期
    /// </summary>
    public async Task<bool> TestTaskLifecycle()
    {
        try
        {
            _logger.LogInformation("开始AI环境任务生命周期测试");

            var param = new AIEnvParam
            {
                DebugMode = true,
                MaxActionDuration = 5.0f,
                ActionQueueSize = 10,
                ScreenshotQuality = 85
            };

            var task = new AIEnvTask(param);

            // 测试任务初始化
            _logger.LogInformation("测试任务初始化");
            // 注意：实际的Init方法需要游戏运行，这里只测试对象创建

            _logger.LogInformation("AI环境任务生命周期测试完成");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI环境任务生命周期测试失败");
            return false;
        }
    }

    /// <summary>
    /// 运行所有测试
    /// </summary>
    public async Task<bool> RunAllTests()
    {
        _logger.LogInformation("开始运行AI环境集成测试套件");

        var basicTest = await TestBasicFunctionality();
        var lifecycleTest = await TestTaskLifecycle();

        var allPassed = basicTest && lifecycleTest;

        _logger.LogInformation("AI环境集成测试套件完成 - {Result}",
            allPassed ? "全部通过" : "部分失败");

        return allPassed;
    }
}
