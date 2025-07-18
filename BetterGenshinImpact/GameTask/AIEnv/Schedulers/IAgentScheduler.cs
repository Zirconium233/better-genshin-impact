using BetterGenshinImpact.GameTask.AIEnv.Environment;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AIEnv.Schedulers;

/// <summary>
/// AI代理调度器接口
/// 负责决策逻辑，从环境获取观察并生成动作指令
/// </summary>
public interface IAgentScheduler
{
    /// <summary>
    /// 启动调度器
    /// </summary>
    /// <param name="env">AI环境实例</param>
    Task Start(AIEnvironment env);

    /// <summary>
    /// 停止调度器
    /// </summary>
    void Stop();

    /// <summary>
    /// 发送用户指令
    /// </summary>
    /// <param name="prompt">用户提示词或指令</param>
    void SendUserPrompt(string prompt);

    /// <summary>
    /// 获取调度器状态
    /// </summary>
    string GetStatus();

    /// <summary>
    /// 调度器是否正在运行
    /// </summary>
    bool IsRunning { get; }
}
