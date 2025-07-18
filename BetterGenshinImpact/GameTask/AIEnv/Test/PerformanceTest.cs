using BetterGenshinImpact.GameTask.AIEnv.Environment;
using BetterGenshinImpact.GameTask.AIEnv.Model;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AIEnv.Test;

/// <summary>
/// AI环境性能测试
/// 测试各组件的性能表现和资源使用情况
/// </summary>
public class PerformanceTest
{
    private readonly ILogger<PerformanceTest> _logger;

    public PerformanceTest()
    {
        _logger = App.GetLogger<PerformanceTest>();
    }

    /// <summary>
    /// 测试状态提取性能
    /// </summary>
    public async Task<bool> TestStateExtractionPerformance()
    {
        try
        {
            _logger.LogInformation("开始状态提取性能测试");

            var param = new AIEnvParam
            {
                DebugMode = false, // 关闭调试模式以获得真实性能
                ScreenshotQuality = 85
            };

            var stateExtractor = new StateExtractor(param);
            var iterations = 100;
            var stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < iterations; i++)
            {
                try
                {
                    var structuredState = await stateExtractor.ExtractStructuredStateAsync();
                    var frameBase64 = await stateExtractor.CaptureFrameAsync();
                    
                    // 验证结果不为空
                    if (structuredState == null)
                    {
                        _logger.LogWarning("第{Iteration}次迭代返回空状态", i + 1);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "第{Iteration}次迭代失败", i + 1);
                }
            }

            stopwatch.Stop();
            var avgTimeMs = stopwatch.ElapsedMilliseconds / (double)iterations;
            var fps = 1000.0 / avgTimeMs;

            _logger.LogInformation("状态提取性能测试完成:");
            _logger.LogInformation("- 总迭代次数: {Iterations}", iterations);
            _logger.LogInformation("- 总耗时: {TotalMs}ms", stopwatch.ElapsedMilliseconds);
            _logger.LogInformation("- 平均耗时: {AvgMs:F2}ms", avgTimeMs);
            _logger.LogInformation("- 理论FPS: {Fps:F2}", fps);

            // 性能要求：平均耗时应小于200ms（5FPS）
            var performanceOk = avgTimeMs < 200;
            
            if (performanceOk)
            {
                _logger.LogInformation("状态提取性能测试通过");
            }
            else
            {
                _logger.LogWarning("状态提取性能测试失败：平均耗时{AvgMs:F2}ms超过200ms阈值", avgTimeMs);
            }

            return performanceOk;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "状态提取性能测试异常");
            return false;
        }
    }

    /// <summary>
    /// 测试动作解析性能
    /// </summary>
    public async Task<bool> TestActionParsingPerformance()
    {
        try
        {
            _logger.LogInformation("开始动作解析性能测试");

            var param = new AIEnvParam
            {
                DebugMode = false,
                ActionQueueSize = 50
            };

            var actionQueueManager = new ActionQueueManager(param);
            var testScripts = new[]
            {
                "w(1.0)",
                "w(1.0)&a(0.5)",
                "sw(1),e,q,attack(0.3)",
                "w(2.0),a(1.0),d(1.0),s(2.0),sw(2),e,q,charge(1.0)",
                "sw(1),e,q,sw(2),e,attack(0.5),sw(3),q,charge(1.0),sw(4),e,q,attack(0.3)"
            };

            var iterations = 1000;
            var stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < iterations; i++)
            {
                foreach (var script in testScripts)
                {
                    try
                    {
                        var actionGroups = actionQueueManager.ParseActionScript(script);
                        
                        // 验证解析结果
                        if (actionGroups == null || actionGroups.Count == 0)
                        {
                            _logger.LogWarning("动作脚本解析失败: {Script}", script);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "动作脚本解析异常: {Script}", script);
                    }
                }
            }

            stopwatch.Stop();
            var totalOperations = iterations * testScripts.Length;
            var avgTimeMs = stopwatch.ElapsedMilliseconds / (double)totalOperations;

            _logger.LogInformation("动作解析性能测试完成:");
            _logger.LogInformation("- 总操作次数: {Operations}", totalOperations);
            _logger.LogInformation("- 总耗时: {TotalMs}ms", stopwatch.ElapsedMilliseconds);
            _logger.LogInformation("- 平均耗时: {AvgMs:F4}ms", avgTimeMs);

            // 性能要求：平均耗时应小于1ms
            var performanceOk = avgTimeMs < 1.0;
            
            if (performanceOk)
            {
                _logger.LogInformation("动作解析性能测试通过");
            }
            else
            {
                _logger.LogWarning("动作解析性能测试失败：平均耗时{AvgMs:F4}ms超过1ms阈值", avgTimeMs);
            }

            return performanceOk;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "动作解析性能测试异常");
            return false;
        }
    }

    /// <summary>
    /// 测试内存使用情况
    /// </summary>
    public async Task<bool> TestMemoryUsage()
    {
        try
        {
            _logger.LogInformation("开始内存使用测试");

            // 记录初始内存
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            var initialMemory = GC.GetTotalMemory(false);
            _logger.LogInformation("初始内存使用: {Memory:F2}MB", initialMemory / 1024.0 / 1024.0);

            var param = new AIEnvParam
            {
                DebugMode = false,
                ScreenshotQuality = 85
            };

            var stateExtractor = new StateExtractor(param);
            var actionQueueManager = new ActionQueueManager(param);

            // 执行一系列操作
            for (int i = 0; i < 50; i++)
            {
                try
                {
                    var structuredState = await stateExtractor.ExtractStructuredStateAsync();
                    var frameBase64 = await stateExtractor.CaptureFrameAsync();
                    var actionGroups = actionQueueManager.ParseActionScript("w(1.0),a(0.5),sw(1),e,q");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "第{Iteration}次内存测试操作失败", i + 1);
                }
            }

            // 记录最终内存
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            var finalMemory = GC.GetTotalMemory(false);
            var memoryIncrease = finalMemory - initialMemory;
            
            _logger.LogInformation("最终内存使用: {Memory:F2}MB", finalMemory / 1024.0 / 1024.0);
            _logger.LogInformation("内存增长: {Increase:F2}MB", memoryIncrease / 1024.0 / 1024.0);

            // 内存要求：增长应小于100MB
            var memoryOk = memoryIncrease < 100 * 1024 * 1024;
            
            if (memoryOk)
            {
                _logger.LogInformation("内存使用测试通过");
            }
            else
            {
                _logger.LogWarning("内存使用测试失败：内存增长{Increase:F2}MB超过100MB阈值", 
                    memoryIncrease / 1024.0 / 1024.0);
            }

            return memoryOk;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "内存使用测试异常");
            return false;
        }
    }

    /// <summary>
    /// 运行所有性能测试
    /// </summary>
    public async Task<bool> RunAllPerformanceTests()
    {
        _logger.LogInformation("=== 开始AI环境性能测试套件 ===");

        var stateExtractionTest = await TestStateExtractionPerformance();
        await Task.Delay(1000); // 间隔1秒

        var actionParsingTest = await TestActionParsingPerformance();
        await Task.Delay(1000); // 间隔1秒

        var memoryTest = await TestMemoryUsage();

        var allPassed = stateExtractionTest && actionParsingTest && memoryTest;

        _logger.LogInformation("=== AI环境性能测试套件完成: {Result} ===",
            allPassed ? "全部通过" : "部分失败");

        return allPassed;
    }
}
