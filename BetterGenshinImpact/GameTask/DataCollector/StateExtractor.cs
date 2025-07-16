using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.DataCollector.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using Compunet.YoloSharp;

namespace BetterGenshinImpact.GameTask.DataCollector;

/// <summary>
/// 简化的状态提取选项 - 队伍信息始终提取（用于菜单检测）
/// </summary>
public class StateExtractionOptions
{
    /// <summary>
    /// 默认选项
    /// </summary>
    public static StateExtractionOptions Default => new();
}

/// <summary>
/// 状态提取器 - 重构版，专注于队伍信息和脚本生成
/// </summary>
public class StateExtractor
{
    private readonly ILogger<StateExtractor> _logger = App.GetLogger<StateExtractor>();
    private CombatScenes? _combatScenes;

    // 状态缓存 - 在菜单中时保持进入菜单前的状态
    private bool _lastInCombat = false;
    private bool _lastInDomain = false;

    // 性能优化缓存
    private GameContext? _cachedGameContext = null;
    private long _lastGameContextUpdateTime = 0;
    private long _gameContextCacheIntervalMs = 1000; // 1秒缓存间隔，可配置

    // 配置选项
    private bool _enableCombatDetection = false;
    private bool _enableDomainDetection = false;

    public StateExtractor()
    {
        _logger.LogInformation("状态提取器已初始化");
    }

    /// <summary>
    /// 设置游戏上下文缓存间隔
    /// </summary>
    /// <param name="intervalMs">缓存间隔（毫秒），默认1000ms</param>
    public void SetGameContextCacheInterval(long intervalMs)
    {
        _gameContextCacheIntervalMs = Math.Max(100, intervalMs); // 最小100ms
        _logger.LogInformation("游戏上下文缓存间隔设置为: {Interval}ms", _gameContextCacheIntervalMs);
    }

    /// <summary>
    /// 配置状态检测选项
    /// </summary>
    /// <param name="enableCombatDetection">是否启用战斗检测</param>
    /// <param name="enableDomainDetection">是否启用秘境检测</param>
    /// <param name="cacheIntervalMs">缓存间隔（毫秒）</param>
    public void ConfigureDetection(bool enableCombatDetection, bool enableDomainDetection, int cacheIntervalMs)
    {
        _enableCombatDetection = enableCombatDetection;
        _enableDomainDetection = enableDomainDetection;
        SetGameContextCacheInterval(cacheIntervalMs);

        _logger.LogInformation("状态检测配置: 战斗检测={Combat}, 秘境检测={Domain}, 缓存间隔={Cache}ms",
            enableCombatDetection, enableDomainDetection, cacheIntervalMs);
    }

    /// <summary>
    /// 初始化战斗场景 - 复用BGI的CombatScenes
    /// </summary>
    public void InitializeCombatScenes(ImageRegion imageRegion)
    {
        try
        {
            _combatScenes = new CombatScenes().InitializeTeam(imageRegion);
            _logger.LogDebug("战斗场景初始化完成");
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "战斗场景初始化失败");
            _combatScenes = null;
        }
    }

    /// <summary>
    /// 提取结构化状态 - 优化版，高效复用检测结果
    /// </summary>
    public StructuredState ExtractStructuredState(ImageRegion imageRegion)
    {
        var state = new StructuredState();

        try
        {
            // 1. 始终提取队伍信息和角色索引（这两个很快）
            state.PlayerTeam = ExtractPlayerTeam(imageRegion);
            state.ActiveCharacterIndex = GetActiveCharacterIndex(imageRegion);

            // 2. 基于队伍信息快速判断菜单状态
            var inMenu = state.PlayerTeam.Count != 4;

            // 3. 高效提取游戏上下文（使用缓存机制）
            state.GameContext = ExtractGameContextOptimized(imageRegion, inMenu);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "状态提取失败");
            // 提供默认状态
            state.GameContext = new GameContext { InMenu = true };
        }

        return state;
    }

    /// <summary>
    /// 快速检测是否在战斗中 - 轻量级版本
    /// </summary>
    public bool IsInCombat(ImageRegion imageRegion)
    {
        try
        {
            // 使用最快的检测方法
            return !Bv.IsInMainUi(imageRegion) &&
                   !Bv.IsInBigMapUi(imageRegion) &&
                   !Bv.IsInAnyClosableUi(imageRegion) &&
                   !Bv.IsInTalkUi(imageRegion);
        }
        catch
        {
            return false;
        }
    }



    /// <summary>
    /// 优化的游戏上下文提取 - 使用缓存机制提升性能
    /// </summary>
    private GameContext ExtractGameContextOptimized(ImageRegion imageRegion, bool inMenu)
    {
        var currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 如果在菜单中，直接返回菜单状态，保持之前的战斗/秘境状态
        if (inMenu)
        {
            return new GameContext
            {
                InMenu = true,
                InCombat = _lastInCombat,
                InDomain = _lastInDomain
            };
        }

        // 不在菜单中，检查缓存是否有效
        if (_cachedGameContext != null &&
            (currentTime - _lastGameContextUpdateTime) < _gameContextCacheIntervalMs)
        {
            // 缓存有效，直接返回
            return _cachedGameContext;
        }

        // 缓存过期或无效，重新检测
        var context = new GameContext
        {
            InMenu = false,
            InCombat = _enableCombatDetection && DetectInCombat(imageRegion),
            InDomain = _enableDomainDetection && DetectInDomain(imageRegion)
        };

        // 更新缓存状态
        _lastInCombat = context.InCombat;
        _lastInDomain = context.InDomain;
        _cachedGameContext = context;
        _lastGameContextUpdateTime = currentTime;

        return context;
    }



    /// <summary>
    /// 检测是否在战斗中 - 复用AutoFight的快速检测逻辑
    /// </summary>
    private bool DetectInCombat(ImageRegion imageRegion)
    {
        try
        {
            // 使用最快的检测方法 - 复用IsInCombat的逻辑
            return !Bv.IsInMainUi(imageRegion) &&
                   !Bv.IsInBigMapUi(imageRegion) &&
                   !Bv.IsInAnyClosableUi(imageRegion) &&
                   !Bv.IsInTalkUi(imageRegion) &&
                   !Bv.IsInRevivePrompt(imageRegion);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "战斗检测失败");
            return false;
        }
    }

    /// <summary>
    /// 检测是否在秘境中 - 复用AutoDomain的检测逻辑
    /// </summary>
    private bool DetectInDomain(ImageRegion imageRegion)
    {
        try
        {
            // 检测秘境特有的UI元素
            // 1. 检测挑战达成提示
            var endTipsRect = imageRegion.DeriveCrop(new Rect(0, 0, imageRegion.Width, (int)(imageRegion.Height * 0.3)));
            var endTipsText = OcrFactory.Paddle.Ocr(endTipsRect.SrcMat);
            if (endTipsText.Contains("挑战达成") || endTipsText.Contains("挑战完成"))
            {
                return true;
            }

            // 2. 检测秘境奖励界面
            var regionList = imageRegion.FindMulti(RecognitionObject.Ocr(
                imageRegion.Width * 0.25, imageRegion.Height * 0.2,
                imageRegion.Width * 0.5, imageRegion.Height * 0.6));
            if (regionList.Any(t => t.Text.Contains("石化古树") || t.Text.Contains("地脉异常")))
            {
                return true;
            }

            // 3. 检测启动按钮（秘境入口）
            var ocrList = imageRegion.FindMulti(RecognitionObject.Ocr(
                imageRegion.Width * 0.7, imageRegion.Height * 0.8,
                imageRegion.Width * 0.25, imageRegion.Height * 0.15));
            if (ocrList.Any(ocr => ocr.Text.Contains("启动")))
            {
                return true;
            }

            return false;
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "秘境检测失败");
            return false;
        }
    }



    /// <summary>
    /// 提取玩家队伍状态 - 优化版，处理菜单返回问题
    /// </summary>
    private List<CharacterState> ExtractPlayerTeam(ImageRegion imageRegion)
    {
        var team = new List<CharacterState>();

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
                foreach (var avatar in avatars)
                {
                    var characterState = new CharacterState
                    {
                        Name = avatar.Name,
                        IsCurrentLowHp = GetIsCurrentLowHp(avatar, imageRegion)
                    };
                    team.Add(characterState);
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "玩家队伍状态提取失败");
        }

        return team;
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
            _logger.LogDebug(e, "获取激活角色索引失败");
        }

        return 0;
    }

    /// <summary>
    /// 获取当前角色是否血量较低 - 复用BGI的CurrentAvatarIsLowHp检测
    /// </summary>
    private static bool GetIsCurrentLowHp(Avatar _, ImageRegion imageRegion)
    {
        try
        {
            // 复用BGI的血量检测逻辑
            return Bv.CurrentAvatarIsLowHp(imageRegion);
        }
        catch
        {
            return false; // 检测失败时默认不是低血量
        }
    }



    /// <summary>
    /// 从输入监控器生成脚本格式的动作 - 基于action_report.md的设计
    /// </summary>
    public string GenerateActionScriptFromInput(InputMonitor inputMonitor, long timeWindowMs = 200)
    {
        try
        {
            // 直接使用InputMonitor的脚本生成功能
            return inputMonitor.GenerateActionScript(timeWindowMs);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "从输入生成动作脚本失败");
            return $"wait({timeWindowMs / 1000.0:F1})";
        }
    }

    /// <summary>
    /// 验证生成的动作脚本是否有效 - 简化版，跳过sw()格式验证
    /// </summary>
    public bool ValidateActionScript(string actionScript)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(actionScript))
            {
                return false;
            }

            // 对于包含sw()或pause()的脚本，直接认为有效（因为BGI解析器不认识这些格式）
            if (actionScript.Contains("sw(") || actionScript.Contains("pause("))
            {
                return true;
            }

            // 对于wait()脚本，直接认为有效
            if (actionScript.StartsWith("wait("))
            {
                return true;
            }

            // 对于包含&分隔符的脚本，直接认为有效（新的同步格式）
            if (actionScript.Contains('&'))
            {
                return true;
            }

            // 其他脚本使用BGI解析器验证
            var commands = CombatScriptParser.ParseLineCommands(actionScript, CombatScriptParser.CurrentAvatarName);
            return commands.Count > 0;
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "动作脚本验证失败: {Script}", actionScript);
            return false;
        }
    }

    /// <summary>
    /// 生成动作序列 - 将多个时间窗口的动作合并为Action Chunk
    /// </summary>
    public string GenerateActionChunk(InputMonitor inputMonitor, int chunkCount = 5, long timeWindowMs = 200)
    {
        try
        {
            var actions = new List<string>();

            for (int i = 0; i < chunkCount; i++)
            {
                var actionScript = inputMonitor.GenerateActionScript(timeWindowMs);
                if (!string.IsNullOrWhiteSpace(actionScript) && !actionScript.StartsWith("wait"))
                {
                    actions.Add(actionScript);
                }
            }

            if (actions.Count == 0)
            {
                return $"wait({timeWindowMs * chunkCount / 1000.0:F1})";
            }

            // 合并动作，去重连续的相同动作
            var mergedActions = new List<string>();
            string lastAction = string.Empty;

            foreach (var action in actions)
            {
                if (action != lastAction)
                {
                    mergedActions.Add(action);
                    lastAction = action;
                }
            }

            // 使用&连接同步动作，符合新的格式规范
            return string.Join("&", mergedActions);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "生成动作序列失败");
            return $"wait({timeWindowMs * chunkCount / 1000.0:F1})";
        }
    }
}
