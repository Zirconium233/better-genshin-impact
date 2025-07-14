using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.GameTask.AutoFight.Model;
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
/// 状态提取器
/// </summary>
public class StateExtractor
{
    private readonly ILogger<StateExtractor> _logger = App.GetLogger<StateExtractor>();
    private readonly BgiYoloPredictor _yoloPredictor;
    private CombatScenes? _combatScenes;

    public StateExtractor()
    {
        _yoloPredictor = App.ServiceProvider.GetRequiredService<BgiOnnxFactory>().CreateYoloPredictor(BgiOnnxModel.BgiWorld);
    }

    /// <summary>
    /// 初始化战斗场景
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
    /// 提取结构化状态
    /// </summary>
    public StructuredState ExtractStructuredState(ImageRegion imageRegion)
    {
        var state = new StructuredState();

        try
        {
            // 提取游戏上下文
            state.GameContext = ExtractGameContext(imageRegion);

            // 提取玩家队伍状态
            state.PlayerTeam = ExtractPlayerTeam(imageRegion);

            // 获取当前激活角色索引
            state.ActiveCharacterIndex = GetActiveCharacterIndex(imageRegion);

            // 提取敌人状态
            state.Enemies = ExtractEnemies(imageRegion);

            // 提取战斗事件
            state.CombatEvents = ExtractCombatEvents(imageRegion);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "状态提取过程中发生异常");
        }

        return state;
    }

    /// <summary>
    /// 提取游戏上下文 - 复用BGI现有的状态检测
    /// </summary>
    private GameContext ExtractGameContext(ImageRegion imageRegion)
    {
        var context = new GameContext();

        try
        {
            // 使用BGI现有的状态检测方法
            context.InMenu = Bv.IsInMainUi(imageRegion) || Bv.IsInBigMapUi(imageRegion) ||
                           Bv.IsInAnyClosableUi(imageRegion) || Bv.IsInPartyViewUi(imageRegion);

            // 检测是否在对话中
            var inTalk = Bv.IsInTalkUi(imageRegion);

            // 检测是否在复苏界面
            var inRevive = Bv.IsInRevivePrompt(imageRegion);

            // 检测是否在战斗中 - 通过检测血条和UI元素
            context.InCombat = DetectCombatState(imageRegion);

            // 检测是否在加载中 - 通过检测特定UI状态
            context.Loading = DetectLoadingState(imageRegion);

            // 确定游戏阶段
            if (context.Loading)
                context.GamePhase = "loading";
            else if (inRevive)
                context.GamePhase = "revive";
            else if (inTalk)
                context.GamePhase = "dialogue";
            else if (context.InMenu)
                context.GamePhase = "menu";
            else if (context.InCombat)
                context.GamePhase = "combat";
            else
                context.GamePhase = "exploration";
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "游戏上下文提取失败");
            context.GamePhase = "unknown";
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
    /// 提取玩家队伍状态
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
                        HpPercent = EstimateHpPercent(avatar, imageRegion),
                        EnergyPercent = EstimateEnergyPercent(avatar, imageRegion),
                        SkillCooldown = EstimateSkillCooldown(avatar, imageRegion),
                        BurstAvailable = EstimateBurstAvailable(avatar, imageRegion)
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
    /// 提取敌人状态
    /// </summary>
    private List<EnemyState> ExtractEnemies(ImageRegion imageRegion)
    {
        var enemies = new List<EnemyState>();

        try
        {
            // 使用YOLO检测敌人
            var detections = _yoloPredictor.Detect(imageRegion);

            foreach (var kvp in detections)
            {
                var label = kvp.Key;
                var rects = kvp.Value;

                // 过滤敌人类型的检测结果
                if (IsEnemyType(label))
                {
                    foreach (var rect in rects)
                    {
                        var enemy = new EnemyState
                        {
                            HpPercent = EstimateEnemyHp(rect, imageRegion),
                            Distance = CalculateDistance(rect),
                            PositionOnScreen = new float[] { rect.X + rect.Width / 2, rect.Y + rect.Height / 2 }
                        };
                        enemies.Add(enemy);
                    }
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "敌人状态提取失败");
        }

        return enemies;
    }

    /// <summary>
    /// 提取战斗事件
    /// </summary>
    private List<CombatEvent> ExtractCombatEvents(ImageRegion imageRegion)
    {
        var events = new List<CombatEvent>();

        try
        {
            // 检测伤害数字
            var damageEvents = DetectDamageNumbers(imageRegion);
            events.AddRange(damageEvents);

            // 检测元素反应
            var reactionEvents = DetectElementalReactions(imageRegion);
            events.AddRange(reactionEvents);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "战斗事件提取失败");
        }

        return events;
    }

    /// <summary>
    /// 估算血量百分比 - 复用BGI的血量检测逻辑
    /// </summary>
    private float EstimateHpPercent(Avatar avatar, ImageRegion imageRegion)
    {
        try
        {
            // 检测当前角色是否低血量
            var isLowHp = Bv.CurrentAvatarIsLowHp(imageRegion);

            // 如果是当前激活角色且检测到低血量，返回较低的血量值
            if (avatar.IsActive(imageRegion) && isLowHp)
            {
                return 0.3f; // 低血量状态，估算为30%
            }

            // 对于非激活角色或满血状态，返回默认值
            // TODO: 可以通过OCR识别具体的血量数值
            return avatar.IsActive(imageRegion) ? 1.0f : 0.8f;
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "血量估算失败: {AvatarName}", avatar.Name);
            return 1.0f;
        }
    }

    /// <summary>
    /// 估算能量百分比 - 基于角色状态和时间估算
    /// </summary>
    private float EstimateEnergyPercent(Avatar avatar, ImageRegion imageRegion)
    {
        try
        {
            // 如果是当前激活角色，可以尝试更精确的检测
            if (avatar.IsActive(imageRegion))
            {
                // TODO: 可以通过识别能量球的数量来估算
                // 目前简化处理：根据角色的爆发状态估算能量
                if (avatar.IsBurstReady)
                {
                    return 1.0f; // 爆发可用，能量满
                }
                else
                {
                    // 根据技能CD状态估算能量恢复进度
                    var skillCd = avatar.GetSkillCdSeconds();
                    var maxSkillCd = avatar.CombatAvatar.SkillCd;
                    if (maxSkillCd > 0)
                    {
                        var progress = Math.Max(0, (maxSkillCd - skillCd) / maxSkillCd);
                        return (float)(0.3 + progress * 0.5); // 30%-80%范围
                    }
                    return 0.7f;
                }
            }

            // 非激活角色返回估算值
            return 0.6f;
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "能量估算失败: {AvatarName}", avatar.Name);
            return 0.5f;
        }
    }

    /// <summary>
    /// 估算技能冷却时间 - 复用BGI的技能CD检测
    /// </summary>
    private float EstimateSkillCooldown(Avatar avatar, ImageRegion imageRegion)
    {
        try
        {
            // 使用BGI现有的技能CD计算方法
            return (float)avatar.GetSkillCdSeconds();
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "技能CD估算失败: {AvatarName}", avatar.Name);
            return 0.0f;
        }
    }

    /// <summary>
    /// 估算爆发是否可用 - 复用BGI的爆发状态检测
    /// </summary>
    private bool EstimateBurstAvailable(Avatar avatar, ImageRegion imageRegion)
    {
        try
        {
            // 使用BGI现有的爆发状态
            return avatar.IsBurstReady;
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "爆发可用性估算失败: {AvatarName}", avatar.Name);
            return false;
        }
    }

    /// <summary>
    /// 检查是否为敌人类型 - 基于BGI的YOLO标签
    /// </summary>
    private bool IsEnemyType(string label)
    {
        if (string.IsNullOrEmpty(label))
            return false;

        // 根据BGI的YOLO模型标签判断是否为敌人
        var enemyLabels = new[]
        {
            "monster", "enemy", "hilichurl", "slime", "abyss",
            "fatui", "treasure_hoarder", "ruin", "specter",
            "whopperflower", "mitachurl", "samachurl", "lawachurl",
            "cicin", "agent", "skirmisher", "mirror_maiden",
            "pyro_hypostasis", "electro_hypostasis", "geo_hypostasis",
            "cryo_hypostasis", "hydro_hypostasis", "anemo_hypostasis",
            "dendro_hypostasis", "regisvine", "wolf", "childe",
            "dvalin", "azhdaha", "signora", "raiden", "scaramouche"
        };

        var lowerLabel = label.ToLower();
        return enemyLabels.Any(enemyLabel => lowerLabel.Contains(enemyLabel));
    }

    /// <summary>
    /// 估算敌人血量
    /// </summary>
    private float EstimateEnemyHp(Rect rect, ImageRegion imageRegion)
    {
        try
        {
            // 这里应该实现具体的敌人血量识别逻辑
            // 暂时返回默认值
            return 1.0f;
        }
        catch
        {
            return 1.0f;
        }
    }

    /// <summary>
    /// 计算距离
    /// </summary>
    private float CalculateDistance(Rect rect)
    {
        try
        {
            // 基于检测框大小估算距离
            var area = rect.Width * rect.Height;
            return Math.Max(0.1f, 1000.0f / area); // 简单的距离估算
        }
        catch
        {
            return 10.0f;
        }
    }

    /// <summary>
    /// 检测伤害数字
    /// </summary>
    private List<CombatEvent> DetectDamageNumbers(ImageRegion imageRegion)
    {
        var events = new List<CombatEvent>();
        
        try
        {
            // 这里应该实现伤害数字检测逻辑
            // 暂时返回空列表
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "伤害数字检测失败");
        }

        return events;
    }

    /// <summary>
    /// 检测元素反应
    /// </summary>
    private List<CombatEvent> DetectElementalReactions(ImageRegion imageRegion)
    {
        var events = new List<CombatEvent>();
        
        try
        {
            // 这里应该实现元素反应检测逻辑
            // 暂时返回空列表
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "元素反应检测失败");
        }

        return events;
    }
}
