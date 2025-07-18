using BetterGenshinImpact.GameTask.AIEnv.Test;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AIEnv.Test;

/// <summary>
/// AI环境测试运行器
/// 提供简单的测试入口点
/// </summary>
public static class TestRunner
{
    private static readonly ILogger _logger = App.GetLogger<AIEnvIntegrationTest>();

    /// <summary>
    /// 运行AI环境集成测试（需要AI环境已启动）
    /// </summary>
    public static async Task<bool> RunAIEnvTests(AIEnvTask? aiEnvTask = null)
    {
        try
        {
            _logger.LogInformation("=== AI环境集成测试开始 ===");

            var aiEnvironment = aiEnvTask?.GetAIEnvironment();
            var integrationTest = new AIEnvIntegrationTest(aiEnvironment);
            var result = await integrationTest.RunAllTests();

            _logger.LogInformation("=== AI环境集成测试结束: {Result} ===",
                result ? "成功" : "失败");

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI环境测试运行器异常");
            return false;
        }
    }

    /// <summary>
    /// 快速验证AI环境基本功能（需要AI环境已启动）
    /// </summary>
    public static async Task<bool> QuickValidation(AIEnvTask? aiEnvTask = null)
    {
        try
        {
            _logger.LogInformation("=== AI环境快速验证开始 ===");

            var aiEnvironment = aiEnvTask?.GetAIEnvironment();
            var integrationTest = new AIEnvIntegrationTest(aiEnvironment);
            var result = await integrationTest.TestBasicFunctionality();

            _logger.LogInformation("=== AI环境快速验证结束: {Result} ===",
                result ? "成功" : "失败");

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI环境快速验证异常");
            return false;
        }
    }

    /// <summary>
    /// 运行性能测试
    /// </summary>
    public static async Task<bool> RunPerformanceTests()
    {
        try
        {
            _logger.LogInformation("=== AI环境性能测试开始 ===");

            var performanceTest = new PerformanceTest();
            var result = await performanceTest.RunAllPerformanceTests();

            _logger.LogInformation("=== AI环境性能测试结束: {Result} ===",
                result ? "成功" : "失败");

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI环境性能测试异常");
            return false;
        }
    }

    /// <summary>
    /// 运行错误处理测试
    /// </summary>
    public static async Task<bool> RunErrorHandlingTests()
    {
        try
        {
            _logger.LogInformation("=== AI环境错误处理测试开始 ===");

            var errorHandlingTest = new ErrorHandlingTest();
            var result = await errorHandlingTest.RunAllErrorHandlingTests();

            _logger.LogInformation("=== AI环境错误处理测试结束: {Result} ===",
                result ? "成功" : "失败");

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI环境错误处理测试异常");
            return false;
        }
    }

    /// <summary>
    /// 运行完整测试套件（包括所有测试类型）
    /// </summary>
    public static async Task<bool> RunCompleteTestSuite()
    {
        try
        {
            _logger.LogInformation("=== AI环境完整测试套件开始 ===");

            var integrationResult = await RunAIEnvTests();
            var performanceResult = await RunPerformanceTests();
            var errorHandlingResult = await RunErrorHandlingTests();

            var allPassed = integrationResult && performanceResult && errorHandlingResult;

            _logger.LogInformation("=== AI环境完整测试套件结束: {Result} ===",
                allPassed ? "全部通过" : "部分失败");
            _logger.LogInformation("测试结果详情: 集成测试={Integration}, 性能测试={Performance}, 错误处理={ErrorHandling}",
                integrationResult ? "通过" : "失败",
                performanceResult ? "通过" : "失败",
                errorHandlingResult ? "通过" : "失败");

            return allPassed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI环境完整测试套件异常");
            return false;
        }
    }
}
