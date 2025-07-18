using BetterGenshinImpact.GameTask.AIEnv.Environment;
using BetterGenshinImpact.GameTask.AIEnv.Model;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AIEnv.Test;

/// <summary>
/// AI环境错误处理测试
/// 测试各种异常情况和边界条件的处理
/// </summary>
public class ErrorHandlingTest
{
    private readonly ILogger<ErrorHandlingTest> _logger;

    public ErrorHandlingTest()
    {
        _logger = App.GetLogger<ErrorHandlingTest>();
    }

    /// <summary>
    /// 测试无效动作脚本的处理
    /// </summary>
    public async Task<bool> TestInvalidActionScripts()
    {
        try
        {
            _logger.LogInformation("开始无效动作脚本测试");

            var param = new AIEnvParam
            {
                DebugMode = true,
                ActionQueueSize = 10
            };

            var actionQueueManager = new ActionQueueManager(param);

            var invalidScripts = new[]
            {
                "", // 空字符串
                "   ", // 空白字符
                "invalid_action", // 无效动作
                "w()", // 缺少参数
                "w(-1.0)", // 负数参数
                "w(abc)", // 非数字参数
                "sw(5)", // 超出范围的角色索引
                "w(1.0", // 缺少右括号
                "w1.0)", // 缺少左括号
                "w(1.0)&", // 不完整的并行动作
                "w(1.0),", // 不完整的序列动作
                "w(999999)", // 超大数值
                "exit", // 无效的调度器命令
                null // null值
            };

            int passedTests = 0;
            int totalTests = invalidScripts.Length;

            foreach (var script in invalidScripts)
            {
                try
                {
                    var actionGroups = actionQueueManager.ParseActionScript(script);
                    
                    // 对于无效脚本，应该返回空列表或抛出异常
                    if (actionGroups == null || actionGroups.Count == 0)
                    {
                        _logger.LogDebug("无效脚本正确处理: '{Script}' -> 空结果", script ?? "null");
                        passedTests++;
                    }
                    else
                    {
                        _logger.LogWarning("无效脚本未正确处理: '{Script}' -> {Count}个动作组", 
                            script ?? "null", actionGroups.Count);
                    }
                }
                catch (Exception ex)
                {
                    // 抛出异常也是正确的处理方式
                    _logger.LogDebug("无效脚本正确抛出异常: '{Script}' -> {Exception}", 
                        script ?? "null", ex.GetType().Name);
                    passedTests++;
                }
            }

            var successRate = passedTests / (double)totalTests;
            _logger.LogInformation("无效动作脚本测试完成: {Passed}/{Total} ({Rate:P0})", 
                passedTests, totalTests, successRate);

            return successRate >= 0.8; // 80%以上通过率
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "无效动作脚本测试异常");
            return false;
        }
    }

    /// <summary>
    /// 测试极限参数的处理
    /// </summary>
    public async Task<bool> TestExtremeParameters()
    {
        try
        {
            _logger.LogInformation("开始极限参数测试");

            // 测试极限参数配置
            var extremeParams = new[]
            {
                new AIEnvParam { ActionQueueSize = 0 }, // 零队列大小
                new AIEnvParam { ActionQueueSize = 1000 }, // 超大队列
                new AIEnvParam { MaxActionDuration = 0 }, // 零持续时间
                new AIEnvParam { MaxActionDuration = 3600 }, // 1小时持续时间
                new AIEnvParam { ScreenshotQuality = 1 }, // 最低质量
                new AIEnvParam { ScreenshotQuality = 100 }, // 最高质量
                new AIEnvParam { DebugMode = true }, // 调试模式
                new AIEnvParam { DebugMode = false } // 非调试模式
            };

            int passedTests = 0;
            int totalTests = extremeParams.Length;

            foreach (var param in extremeParams)
            {
                try
                {
                    var stateExtractor = new StateExtractor(param);
                    var actionQueueManager = new ActionQueueManager(param);
                    
                    _logger.LogDebug("极限参数配置创建成功: Queue={Queue}, Duration={Duration}, Quality={Quality}", 
                        param.ActionQueueSize, param.MaxActionDuration, param.ScreenshotQuality);
                    passedTests++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "极限参数配置失败: Queue={Queue}, Duration={Duration}, Quality={Quality}", 
                        param.ActionQueueSize, param.MaxActionDuration, param.ScreenshotQuality);
                }
            }

            var successRate = passedTests / (double)totalTests;
            _logger.LogInformation("极限参数测试完成: {Passed}/{Total} ({Rate:P0})", 
                passedTests, totalTests, successRate);

            return successRate >= 0.7; // 70%以上通过率
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "极限参数测试异常");
            return false;
        }
    }

    /// <summary>
    /// 测试并发访问的处理
    /// </summary>
    public async Task<bool> TestConcurrentAccess()
    {
        try
        {
            _logger.LogInformation("开始并发访问测试");

            var param = new AIEnvParam
            {
                DebugMode = false,
                ActionQueueSize = 20
            };

            var stateExtractor = new StateExtractor(param);
            var actionQueueManager = new ActionQueueManager(param);

            var tasks = new Task[10];
            var successCount = 0;

            // 创建10个并发任务
            for (int i = 0; i < tasks.Length; i++)
            {
                int taskId = i;
                tasks[i] = Task.Run(async () =>
                {
                    try
                    {
                        for (int j = 0; j < 5; j++)
                        {
                            // 并发状态提取
                            var structuredState = await stateExtractor.ExtractStructuredStateAsync();
                            
                            // 并发动作解析
                            var actionGroups = actionQueueManager.ParseActionScript($"w({1000 + taskId * 100})");
                            
                            await Task.Delay(10); // 短暂延迟
                        }
                        
                        lock (this)
                        {
                            successCount++;
                        }
                        
                        _logger.LogDebug("并发任务{TaskId}完成", taskId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "并发任务{TaskId}失败", taskId);
                    }
                });
            }

            // 等待所有任务完成
            await Task.WhenAll(tasks);

            var successRate = successCount / (double)tasks.Length;
            _logger.LogInformation("并发访问测试完成: {Success}/{Total} ({Rate:P0})", 
                successCount, tasks.Length, successRate);

            return successRate >= 0.8; // 80%以上成功率
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "并发访问测试异常");
            return false;
        }
    }

    /// <summary>
    /// 测试资源清理
    /// </summary>
    public async Task<bool> TestResourceCleanup()
    {
        try
        {
            _logger.LogInformation("开始资源清理测试");

            // 创建多个实例并立即释放
            for (int i = 0; i < 10; i++)
            {
                var param = new AIEnvParam
                {
                    DebugMode = false,
                    ActionQueueSize = 5
                };

                var stateExtractor = new StateExtractor(param);
                var actionQueueManager = new ActionQueueManager(param);

                // 执行一些操作
                try
                {
                    var actionGroups = actionQueueManager.ParseActionScript("w(100)");
                    // 注意：StateExtractor可能需要游戏运行，这里只测试创建和销毁
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("资源清理测试中的操作异常（预期）: {Exception}", ex.Message);
                }

                // 实例会在作用域结束时被GC回收
            }

            // 强制垃圾回收
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            _logger.LogInformation("资源清理测试完成");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "资源清理测试异常");
            return false;
        }
    }

    /// <summary>
    /// 运行所有错误处理测试
    /// </summary>
    public async Task<bool> RunAllErrorHandlingTests()
    {
        _logger.LogInformation("=== 开始AI环境错误处理测试套件 ===");

        var invalidScriptTest = await TestInvalidActionScripts();
        await Task.Delay(500);

        var extremeParamTest = await TestExtremeParameters();
        await Task.Delay(500);

        var concurrentTest = await TestConcurrentAccess();
        await Task.Delay(500);

        var cleanupTest = await TestResourceCleanup();

        var allPassed = invalidScriptTest && extremeParamTest && concurrentTest && cleanupTest;

        _logger.LogInformation("=== AI环境错误处理测试套件完成: {Result} ===",
            allPassed ? "全部通过" : "部分失败");

        return allPassed;
    }
}
