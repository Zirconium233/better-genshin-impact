using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.GameTask.AIEnv.Model;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using Compunet.YoloSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AIEnv.Environment;

/// <summary>
/// AI环境状态提取器
/// 直接实现状态提取逻辑，不依赖DataCollector
/// </summary>
public class StateExtractor
{
    private readonly AIEnvParam _param;
    private readonly ILogger<StateExtractor> _logger;

    // 性能监控
    private readonly List<double> _processingTimes = new();
    private int _frameCount = 0;
    private readonly Stopwatch _stopwatch = new();

    // 复制DataCollector的实现
    private CombatScenes? _combatScenes;
    private readonly BgiYoloPredictor _predictor;

    // 状态缓存 - 在菜单中时保持进入菜单前的状态
    private bool _lastInCombat = false;
    private bool _lastInDomain = false;

    // 性能优化缓存
    private AIEnv.Model.GameContext? _cachedGameContext = null;
    private long _lastGameContextUpdateTime = 0;
    private long _gameContextCacheIntervalMs = 1000; // 1秒缓存间隔

    // 角色队伍缓存
    private long _lastCombatScenesUpdateTime = 0;

    public StateExtractor(AIEnvParam param)
    {
        _param = param;
        _logger = App.GetLogger<StateExtractor>();

        // 初始化YOLO预测器
        _predictor = App.ServiceProvider.GetRequiredService<BgiOnnxFactory>().CreateYoloPredictor(BgiOnnxModel.BgiWorld);

        // 设置缓存间隔
        _gameContextCacheIntervalMs = 500; // 硬编码缓存间隔为500ms

        _logger.LogInformation("AI环境状态提取器已初始化");
    }

    /// <summary>
    /// 捕获当前帧并转换为Base64
    /// </summary>
    public async Task<string> CaptureFrameAsync()
    {
        _stopwatch.Restart();
        try
        {
            var imageRegion = TaskControl.CaptureToRectArea();
            try
            {
                var mat = imageRegion.SrcMat;

                // 压缩图像以减少数据传输量
                var config = TaskContext.Instance().Config.AIEnvConfig;
                var encodeParams = new int[] { (int)ImwriteFlags.JpegQuality, config.ScreenshotQuality };
                var imageBytes = mat.ImEncode(".jpg", encodeParams);

                var result = Convert.ToBase64String(imageBytes);

                // 记录性能
                RecordPerformance("CaptureFrame");

                return result;
            }
            finally
            {
                // 确保释放ImageRegion资源
                imageRegion?.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "捕获帧失败");
            return string.Empty;
        }
        finally
        {
            _stopwatch.Stop();
        }
    }

    /// <summary>
    /// 提取结构化状态 - 直接实现，不依赖DataCollector
    /// </summary>
    public async Task<AIEnv.Model.StructuredState> ExtractStructuredStateAsync()
    {
        _stopwatch.Restart();
        try
        {
            var imageRegion = TaskControl.CaptureToRectArea();

            var state = new AIEnv.Model.StructuredState();

            // 1. 快速菜单检测
            var inMenu = Bv.IsInBigMapUi(imageRegion) ||
                         Bv.IsInTalkUi(imageRegion) ||
                         Bv.IsInAnyClosableUi(imageRegion) ||
                         Bv.IsInPartyViewUi(imageRegion) ||
                         Bv.IsInRevivePrompt(imageRegion) ||
                         Bv.IsInBlessingOfTheWelkinMoon(imageRegion);

            // 2. 提取队伍信息
            state.PlayerTeam = ExtractPlayerTeam(imageRegion);

            // 3. 检测当前角色低血量状态
            var isCurrentLowHp = GetCurrentCharacterLowHp(imageRegion);

            // 4. 提取游戏上下文
            state.GameContext = new AIEnv.Model.GameContext
            {
                InMenu = inMenu,
                InCombat = false, // 硬编码为false，不检测战斗状态
                InDomain = false, // 暂时硬编码为false，避免复杂的秘境检测
                IsCurrentLowHp = isCurrentLowHp
            };

            // 记录性能
            RecordPerformance("ExtractStructuredState");

            if (_param.DebugMode)
            {
                _logger.LogDebug("提取结构化状态: 菜单={InMenu}, 秘境={InDomain}, 低血量={LowHp}, 队伍={TeamCount}",
                    state.GameContext.InMenu,
                    state.GameContext.InDomain,
                    state.GameContext.IsCurrentLowHp,
                    state.PlayerTeam.TeamMembers.Count);
            }

            return state;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "提取结构化状态失败");
            return new AIEnv.Model.StructuredState
            {
                GameContext = new AIEnv.Model.GameContext { InMenu = true },
                PlayerTeam = new AIEnv.Model.PlayerTeam { TeamMembers = new List<AIEnv.Model.TeamMember>(), ActiveCharacterIndex = 0 }
            };
        }
        finally
        {
            _stopwatch.Stop();
        }
    }



    /// <summary>
    /// 从已有的ImageRegion生成Base64（避免重复截图）
    /// </summary>
    public async Task<string> CaptureFrameFromImageRegionAsync(ImageRegion imageRegion)
    {
        _stopwatch.Restart();
        try
        {
            var mat = imageRegion.SrcMat;

            // 压缩图像以减少数据传输量
            var config = TaskContext.Instance().Config.AIEnvConfig;
            var encodeParams = new int[] { (int)ImwriteFlags.JpegQuality, config.ScreenshotQuality };
            var imageBytes = mat.ImEncode(".jpg", encodeParams);

            var result = Convert.ToBase64String(imageBytes);

            // 记录性能
            RecordPerformance("CaptureFrameFromImageRegion");

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从ImageRegion生成Base64失败");
            return string.Empty;
        }
        finally
        {
            _stopwatch.Stop();
        }
    }

    /// <summary>
    /// 从已有的ImageRegion提取状态（避免重复截图）
    /// </summary>
    public async Task<AIEnv.Model.StructuredState> ExtractStructuredStateFromImageRegionAsync(ImageRegion imageRegion)
    {
        _stopwatch.Restart();
        try
        {
            return ExtractStructuredState(imageRegion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从ImageRegion提取结构化状态失败");
            return new AIEnv.Model.StructuredState();
        }
        finally
        {
            _stopwatch.Stop();
        }
    }

    /// <summary>
    /// 提取结构化状态 - 核心实现
    /// </summary>
    public AIEnv.Model.StructuredState ExtractStructuredState(ImageRegion imageRegion)
    {
        var state = new AIEnv.Model.StructuredState();

        try
        {
            // 1. 快速菜单检测
            var inMenu = Bv.IsInBigMapUi(imageRegion) ||
                         Bv.IsInTalkUi(imageRegion) ||
                         Bv.IsInAnyClosableUi(imageRegion) ||
                         Bv.IsInPartyViewUi(imageRegion) ||
                         Bv.IsInRevivePrompt(imageRegion) ||
                         Bv.IsInBlessingOfTheWelkinMoon(imageRegion);

            // 2. 提取队伍信息和角色索引（带缓存）
            state.PlayerTeam = ExtractPlayerTeam(imageRegion);

            // 3. 检测当前角色低血量状态（只检测当前角色）
            var isCurrentLowHp = GetCurrentCharacterLowHp(imageRegion);

            // 4. 高效提取游戏上下文（使用Optimized缓存机制方法）
            state.GameContext = ExtractGameContextOptimized(imageRegion, inMenu, isCurrentLowHp);

            // 记录性能
            RecordPerformance("ExtractStructuredState");

            if (_param.DebugMode)
            {
                _logger.LogDebug("提取结构化状态: 菜单={InMenu}, 秘境={InDomain}, 低血量={LowHp}, 队伍={TeamCount}",
                    state.GameContext.InMenu,
                    state.GameContext.InDomain,
                    state.GameContext.IsCurrentLowHp,
                    state.PlayerTeam.TeamMembers.Count);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "状态提取失败");
            // 提供默认状态
            state.GameContext = new AIEnv.Model.GameContext { InMenu = true };
            state.PlayerTeam = new AIEnv.Model.PlayerTeam { TeamMembers = new List<AIEnv.Model.TeamMember>(), ActiveCharacterIndex = 0 };
        }

        return state;
    }

    /// <summary>
    /// 优化的游戏上下文提取 - 使用缓存机制提升性能
    /// </summary>
    private AIEnv.Model.GameContext ExtractGameContextOptimized(ImageRegion imageRegion, bool inMenu, bool isCurrentLowHp)
    {
        var currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 如果在菜单中，直接返回菜单状态，保持之前的战斗/秘境状态
        if (inMenu)
        {
            return new AIEnv.Model.GameContext
            {
                InMenu = true,
                InCombat = _lastInCombat,
                InDomain = _lastInDomain,
                IsCurrentLowHp = isCurrentLowHp
            };
        }

        // 不在菜单中，检查缓存是否有效
        if (_cachedGameContext != null &&
            (currentTime - _lastGameContextUpdateTime) < _gameContextCacheIntervalMs)
        {
            // 缓存有效，但更新当前角色血量状态（这个检测很快）
            var cachedContext = new AIEnv.Model.GameContext
            {
                InMenu = _cachedGameContext.InMenu,
                InCombat = _cachedGameContext.InCombat,
                InDomain = _cachedGameContext.InDomain,
                IsCurrentLowHp = isCurrentLowHp
            };
            return cachedContext;
        }

        // 缓存过期或无效，重新检测
        var context = new AIEnv.Model.GameContext
        {
            InMenu = false,
            InCombat = false, // 硬编码为false，不检测战斗状态
            InDomain = false, // 硬编码为false，不检测秘境状态
            IsCurrentLowHp = isCurrentLowHp
        };

        // 更新缓存状态
        _lastInCombat = context.InCombat;
        _lastInDomain = context.InDomain;
        _cachedGameContext = context;
        _lastGameContextUpdateTime = currentTime;

        return context;
    }

    /// <summary>
    /// 记录性能数据
    /// </summary>
    private void RecordPerformance(string operation)
    {
        if (_stopwatch.IsRunning)
        {
            var elapsedMs = _stopwatch.Elapsed.TotalMilliseconds;
            _processingTimes.Add(elapsedMs);
            _frameCount++;

            if (_param.DebugMode && _frameCount % 100 == 0)
            {
                var avgTime = _processingTimes.Average();
                _logger.LogDebug("{Operation} 平均处理时间: {AvgTime:F2}ms (最近{Count}帧)",
                    operation, avgTime, _processingTimes.Count);
            }

            // 保持最近1000帧的数据
            if (_processingTimes.Count > 1000)
            {
                _processingTimes.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// 停止状态提取器
    /// </summary>
    public void Stop()
    {
        _processingTimes.Clear();
        _logger.LogInformation("AI环境状态提取器已停止");
    }

    /// <summary>
    /// 初始化战斗场景 - 复用BGI的CombatScenes，带缓存机制
    /// </summary>
    public void InitializeCombatScenes(ImageRegion imageRegion)
    {
        var currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 检查缓存是否有效
        if (_combatScenes != null &&
            _combatScenes.CheckTeamInitialized() &&
            (currentTime - _lastCombatScenesUpdateTime) < _gameContextCacheIntervalMs)
        {
            // 缓存有效，直接返回
            return;
        }

        try
        {
            _combatScenes = new CombatScenes().InitializeTeam(imageRegion);
            _lastCombatScenesUpdateTime = currentTime;
            if (_param.DebugMode)
            {
                _logger.LogDebug("战斗场景初始化完成");
            }
        }
        catch (Exception e)
        {
            if (_param.DebugMode)
            {
                _logger.LogWarning(e, "战斗场景初始化失败");
            }
            _combatScenes = null;
            _lastCombatScenesUpdateTime = currentTime; // 即使失败也更新时间，避免频繁重试
        }
    }

    /// <summary>
    /// 提取玩家队伍状态 - 优化版，处理菜单返回问题
    /// </summary>
    private AIEnv.Model.PlayerTeam ExtractPlayerTeam(ImageRegion imageRegion)
    {
        var team = new List<AIEnv.Model.TeamMember>();

        try
        {
            // 如果没有初始化队伍或队伍检查失败，尝试重新初始化
            if (_combatScenes == null || !_combatScenes.CheckTeamInitialized())
            {
                InitializeCombatScenes(imageRegion);
            }

            if (_combatScenes != null && _combatScenes.CheckTeamInitialized())
            {
                var avatars = _combatScenes.GetAvatars();
                for (int i = 0; i < avatars.Count; i++)
                {
                    var teamMember = new AIEnv.Model.TeamMember
                    {
                        Name = avatars[i].Name,
                        Index = i
                    };
                    team.Add(teamMember);
                }
            }
        }
        catch (Exception e)
        {
            if (_param.DebugMode)
            {
                _logger.LogDebug(e, "玩家队伍状态提取失败");
            }
        }

        return new AIEnv.Model.PlayerTeam
        {
            TeamMembers = team,
            ActiveCharacterIndex = GetActiveCharacterIndex(imageRegion)
        };
    }

    /// <summary>
    /// 获取当前激活角色索引
    /// </summary>
    private int GetActiveCharacterIndex(ImageRegion imageRegion)
    {
        try
        {
            if (_combatScenes != null)
            {
                var avatars = _combatScenes.GetAvatars();
                for (int i = 0; i < avatars.Count; i++)
                {
                    if (avatars[i].IsActive(imageRegion))
                    {
                        return i;
                    }
                }
            }
        }
        catch (Exception e)
        {
            if (_param.DebugMode)
            {
                _logger.LogDebug(e, "获取激活角色索引失败");
            }
        }

        return 0;
    }

    /// <summary>
    /// 检测当前角色低血量状态 - 简化版本
    /// </summary>
    private bool GetCurrentCharacterLowHp(ImageRegion imageRegion)
    {
        try
        {
            return Bv.CurrentAvatarIsLowHp(imageRegion);
        }
        catch (Exception e)
        {
            if (_param.DebugMode)
            {
                _logger.LogDebug(e, "当前角色血量检测失败");
            }
            return false;
        }
    }
}
