using BetterGenshinImpact.Core.Recognition.OpenCv;
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
/// 简化的状态提取选项
/// </summary>
public class StateExtractionOptions
{
    /// <summary>
    /// 是否提取队伍角色信息（用于大模型切人）
    /// </summary>
    public bool ExtractTeamInfo { get; set; } = true;

    /// <summary>
    /// 默认选项 - 只提取基本信息
    /// </summary>
    public static StateExtractionOptions Default => new();

    /// <summary>
    /// 完整选项 - 提取队伍信息
    /// </summary>
    public static StateExtractionOptions Full => new()
    {
        ExtractTeamInfo = true
    };
}

/// <summary>
/// 状态提取器 - 重构版，专注于队伍信息和脚本生成
/// </summary>
public class StateExtractor
{
    private readonly ILogger<StateExtractor> _logger = App.GetLogger<StateExtractor>();
    private CombatScenes? _combatScenes;

    public StateExtractor()
    {
        _logger.LogInformation("状态提取器已初始化");
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
    /// 提取结构化状态 - 简化版，只提取必要信息
    /// </summary>
    public StructuredState ExtractStructuredState(ImageRegion imageRegion, StateExtractionOptions? options = null)
    {
        options ??= StateExtractionOptions.Default;
        var state = new StructuredState();

        try
        {
            // 提取游戏上下文 - 总是提取，因为很快
            state.GameContext = ExtractGameContext(imageRegion);

            // 根据选项决定是否提取队伍信息
            if (options.ExtractTeamInfo)
            {
                state.PlayerTeam = ExtractPlayerTeam(imageRegion);
                state.ActiveCharacterIndex = GetActiveCharacterIndex(imageRegion);
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "状态提取过程中发生异常");
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
    /// 提取游戏上下文 - 复用BGI现有的状态检测
    /// </summary>
    private GameContext ExtractGameContext(ImageRegion imageRegion)
    {
        var context = new GameContext();

        try
        {
            // 检测各种UI状态 - 按优先级检测，避免误判
            var inMainUi = Bv.IsInMainUi(imageRegion);
            var inBigMapUi = Bv.IsInBigMapUi(imageRegion);
            var inPartyViewUi = Bv.IsInPartyViewUi(imageRegion);
            var inTalk = Bv.IsInTalkUi(imageRegion);
            var inRevive = Bv.IsInRevivePrompt(imageRegion);
            var loading = DetectLoadingState(imageRegion);

            // 只有在明确的UI界面时才检测IsInAnyClosableUi，避免大世界误判
            var inAnyClosableUi = (inMainUi || inBigMapUi || inPartyViewUi) && Bv.IsInAnyClosableUi(imageRegion);

            // 精确的菜单检测 - 只有明确的UI界面才算菜单
            context.InMenu = inMainUi || inBigMapUi || inPartyViewUi || inAnyClosableUi;

            // 更精确的战斗检测 - 只有在非UI界面且非对话时才可能在战斗
            context.InCombat = !context.InMenu && !inTalk && !inRevive && !loading && DetectCombatState(imageRegion);

            context.Loading = loading;

            // 确定游戏阶段 - 修复优先级，避免大世界被误判为菜单
            if (loading)
                context.GamePhase = "loading";
            else if (inRevive)
                context.GamePhase = "revive";
            else if (inTalk)
                context.GamePhase = "dialogue";
            else if (inMainUi)
                context.GamePhase = "menu";
            else if (inBigMapUi)
                context.GamePhase = "menu";
            else if (inPartyViewUi)
                context.GamePhase = "menu";
            else if (inAnyClosableUi)
                context.GamePhase = "menu";
            else if (context.InCombat)
                context.GamePhase = "combat";
            else
                context.GamePhase = "exploration"; // 默认为探索状态
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "游戏上下文提取失败");
            // 默认状态
            context.GamePhase = "exploration";
            context.InMenu = false;
            context.InCombat = false;
            context.Loading = false;
        }

        return context;
    }

    /// <summary>
    /// 检测战斗状态 - 复用BGI的战斗检测逻辑
    /// </summary>
    private bool DetectCombatState(ImageRegion imageRegion)
    {
        try
        {
            // 检测是否有血条显示（当前角色血量低于满血时会显示）
            var hasHealthBar = Bv.CurrentAvatarIsLowHp(imageRegion);

            // 检测运动状态（战斗中可能有特殊的运动状态）
            var motionStatus = Bv.GetMotionStatus(imageRegion);

            // 简单的战斗检测：如果不在主界面且不在菜单中，可能在战斗
            var notInMainUi = !Bv.IsInMainUi(imageRegion);
            var notInMenu = !Bv.IsInBigMapUi(imageRegion) && !Bv.IsInAnyClosableUi(imageRegion);
            var notInTalk = !Bv.IsInTalkUi(imageRegion);

            // 如果有血条显示或者在游戏世界中（非UI界面），可能在战斗
            return hasHealthBar || (notInMainUi && notInMenu && notInTalk);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "战斗状态检测失败");
            return false;
        }
    }

    /// <summary>
    /// 检测加载状态
    /// </summary>
    private bool DetectLoadingState(ImageRegion imageRegion)
    {
        try
        {
            // 检测是否在空月祝福界面（一种加载状态）
            var inWelkinMoon = Bv.IsInBlessingOfTheWelkinMoon(imageRegion);

            // 可以添加更多加载状态检测
            // 例如：检测黑屏、加载图标等

            return inWelkinMoon;
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "加载状态检测失败");
            return false;
        }
    }

    /// <summary>
    /// 提取玩家队伍状态 - 复用BGI的Avatar系统
    /// </summary>
    private List<CharacterState> ExtractPlayerTeam(ImageRegion imageRegion)
    {
        var team = new List<CharacterState>();

        try
        {
            if (_combatScenes != null)
            {
                var avatars = _combatScenes.GetAvatars();
                foreach (var avatar in avatars)
                {
                    var characterState = new CharacterState
                    {
                        Name = avatar.Name,
                        HpPercent = GetAvatarHpPercent(avatar, imageRegion),
                        EnergyPercent = GetAvatarEnergyPercent(avatar),
                        SkillCooldown = GetAvatarSkillCooldown(avatar),
                        BurstAvailable = GetAvatarBurstAvailable(avatar)
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
    /// 获取角色血量百分比 - 复用BGI现有检测
    /// </summary>
    private static float GetAvatarHpPercent(Avatar avatar, ImageRegion imageRegion)
    {
        try
        {
            // 使用BGI现有的血量检测
            if (Bv.CurrentAvatarIsLowHp(imageRegion))
            {
                return 0.3f; // 低血量估计为30%
            }
            return 1.0f; // 默认满血
        }
        catch
        {
            return 1.0f;
        }
    }

    /// <summary>
    /// 获取角色能量百分比 - 复用BGI的Avatar系统
    /// </summary>
    private static float GetAvatarEnergyPercent(Avatar avatar)
    {
        try
        {
            // BGI没有直接的能量检测，使用爆发可用性推测
            // 如果爆发可用，能量应该是满的
            if (avatar.IsBurstReady)
            {
                return 1.0f; // 爆发可用，能量满
            }
            return 0.5f; // 默认50%能量
        }
        catch
        {
            return 0.5f;
        }
    }

    /// <summary>
    /// 获取角色技能冷却时间 - 复用BGI的CD管理
    /// </summary>
    private static float GetAvatarSkillCooldown(Avatar avatar)
    {
        try
        {
            // 复用BGI的技能CD检测
            if (avatar.IsSkillReady())
            {
                return 0.0f; // 技能可用
            }
            // 使用BGI的GetSkillCdSeconds方法获取精确CD时间
            return (float)avatar.GetSkillCdSeconds();
        }
        catch
        {
            return 0.0f;
        }
    }

    /// <summary>
    /// 获取角色爆发是否可用 - 复用BGI的爆发检测
    /// </summary>
    private static bool GetAvatarBurstAvailable(Avatar avatar)
    {
        try
        {
            // 复用BGI的爆发检测 - IsBurstReady是属性，不是方法
            return avatar.IsBurstReady;
        }
        catch
        {
            return false;
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
