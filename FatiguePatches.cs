using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Ftue;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;

namespace STS2Fatigue;

/// <summary>
/// Mod 日志工具类
/// </summary>
public static class ModLogger
{
    // ============================================================
    // 日志开关：设置为 false 可禁用日志，避免磁盘 IO 引起游戏延迟
    // ============================================================
    public const bool ENABLE_LOGGING = false;
    // ============================================================

    private static string _logPath = "";
    private static object _lock = new object();

    /// <summary>
    /// 初始化日志系统，创建日志文件
    /// </summary>
    public static void Init()
    {
        if (!ENABLE_LOGGING) return;

        try
        {
            // 尝试在游戏目录创建日志文件
            _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "STS2Fatigue.log");
            File.WriteAllText(_logPath, $"=== STS2Fatigue Log Started {DateTime.Now} ===\n");
        }
        catch
        {
            try
            {
                // 备用方案：在用户配置目录创建日志文件
                _logPath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "STS2Fatigue.log");
                File.WriteAllText(_logPath, $"=== STS2Fatigue Log Started {DateTime.Now} ===\n");
            }
            catch { _logPath = ""; }
        }
    }

    /// <summary>
    /// 写入日志消息
    /// </summary>
    /// <param name="message">日志消息内容</param>
    public static void Log(string message)
    {
        if (!ENABLE_LOGGING) return;

        string logLine = $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n";
        lock (_lock)
        {
            if (!string.IsNullOrEmpty(_logPath))
            {
                try { File.AppendAllText(_logPath, logLine); } catch { }
            }
        }
        System.Console.WriteLine(logLine);
    }
}

/// <summary>
/// 疲劳机制的核心 Patch 类
/// </summary>
[HarmonyPatch]
public static class FatiguePatches
{
    // 疲劳计数器：Key 为玩家 NetId，Value 为当前疲劳次数
    private static readonly Dictionary<ulong, int> _fatigueCount = new();

    /// <summary>
    /// Mod 初始化入口
    /// </summary>
    public static void Initialize()
    {
        ModLogger.Init();
        ModLogger.Log("Initialize() called");
        var harmony = new Harmony("STS2Fatigue");
        harmony.PatchAll();
        ModLogger.Log("All patches applied");
    }

    /// <summary>
    /// 重置指定玩家的疲劳计数器
    /// </summary>
    private static void ResetFatigueCount(ulong playerId)
    {
        _fatigueCount[playerId] = 0;
    }

    /// <summary>
    /// 增加并返回疲劳伤害值
    /// </summary>
    /// <param name="playerId">玩家 NetId</param>
    /// <returns>疲劳伤害值（第 n 次疲劳返回 n）</returns>
    private static int GetAndIncrementFatigueCount(ulong playerId)
    {
        if (!_fatigueCount.ContainsKey(playerId))
            _fatigueCount[playerId] = 0;
        _fatigueCount[playerId]++;
        return _fatigueCount[playerId];
    }

    /// <summary>
    /// 替换 CardPileCmd.Draw 方法，实现自定义抽牌逻辑
    /// </summary>
    [HarmonyPatch(typeof(CardPileCmd), "Draw", new System.Type[] { typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool) })]
    [HarmonyPrefix]
    public static bool DrawPrefix(PlayerChoiceContext choiceContext, decimal count, Player player, bool fromHandDraw, ref Task<IEnumerable<CardModel>> __result)
    {
        __result = CustomDraw(choiceContext, count, player, fromHandDraw);
        return false; // 跳过原方法，使用自定义逻辑
    }

    /// <summary>
    /// 自定义抽牌逻辑
    /// 实现疲劳机制：抽牌堆为空时造成疲劳伤害，不洗牌
    /// </summary>
    private static async Task<IEnumerable<CardModel>> CustomDraw(PlayerChoiceContext choiceContext, decimal count, Player player, bool fromHandDraw)
    {
        // 检查战斗是否已结束
        if (CombatManager.Instance.IsOverOrEnding)
        {
            return Array.Empty<CardModel>();
        }

        // 检查是否有遗物/能力阻止抽牌
        if (!Hook.ShouldDraw(player.Creature.CombatState, player, fromHandDraw, out AbstractModel modifier))
        {
            await Hook.AfterPreventingDraw(player.Creature.CombatState, modifier);
            return Array.Empty<CardModel>();
        }

        // 初始化变量
        CombatState combatState = player.Creature.CombatState;
        List<CardModel> result = new List<CardModel>();
        CardPile hand = PileType.Hand.GetPile(player);
        CardPile drawPile = PileType.Draw.GetPile(player);
        CardPile discardPile = PileType.Discard.GetPile(player);

        // 计算请求数量
        int drawsRequested = (count > 0m) ? (int)Math.Ceiling(count) : 0;
        if (drawsRequested == 0)
        {
            return result;
        }

        // 检查手牌空间（上限 10 张）
        int spaceInHand = Math.Max(0, 10 - hand.Cards.Count);
        if (spaceInHand == 0)
        {
            CheckIfDrawIsPossibleAndShowThoughtBubbleIfNot(player);
            return result;
        }

        ModLogger.Log($"CustomDraw: player={player.NetId}, drawsRequested={drawsRequested}, fromHandDraw={fromHandDraw}, drawPile={drawPile.Cards.Count}, discardPile={discardPile.Cards.Count}");

        // 抽牌循环
        for (int i = 0; i < drawsRequested; i++)
        {
            // 检查手牌上限
            if (hand.Cards.Count >= 10)
            {
                break;
            }

            CardModel card = drawPile.Cards.FirstOrDefault();

            if (card == null)
            {
                // 抽牌堆为空
                if (discardPile.Cards.Count > 0)
                {
                    // 弃牌堆有牌
                    if (fromHandDraw)
                    {
                        // 回合开始抽牌：停止抽牌，不洗牌，不扣血
                        ModLogger.Log($"CustomDraw: Round start draw, draw pile empty, stopping at {i} cards drawn");
                        break;
                    }
                    else
                    {
                        // 卡牌效果抽牌：造成疲劳伤害，不洗牌，继续循环
                        ModLogger.Log($"CustomDraw: Draw pile empty, applying fatigue #{i + 1} for player {player.NetId}");

                        int damage = GetAndIncrementFatigueCount(player.NetId);
                        ModLogger.Log($"CustomDraw: Fatigue #{_fatigueCount[player.NetId]} = {damage} damage");

                        Creature creature = player.Creature;
                        if (creature.CombatState != null && !creature.IsDead)
                        {
                            // 造成可格挡的伤害
                            var damageResults = await CreatureCmd.Damage(choiceContext, creature, damage, (ValueProp)0, null, null);

                            foreach (var r in damageResults)
                                ModLogger.Log($"  Damage: unblocked={r.UnblockedDamage}, blocked={r.BlockedDamage}");

                            // 仅对本地玩家播放视觉/音效
                            if (LocalContext.IsMe(player) && damageResults.Any(r => r.UnblockedDamage > 0))
                            {
                                try
                                {
                                    NGame.Instance?.ScreenShake(ShakeStrength.Weak, ShakeDuration.Short);
                                    SfxCmd.Play("event:/sfx/player/player_hurt");
                                }
                                catch (Exception e) { ModLogger.Log($"  SFX error: {e.Message}"); }
                            }
                        }

                        // 不洗牌，继续下一次抽牌尝试（抽牌堆仍然为空，会再次触发疲劳）
                        continue;
                    }
                }
                else
                {
                    // 抽牌堆和弃牌堆都为空：停止抽牌，不扣血
                    ModLogger.Log($"CustomDraw: Both piles empty, stopping at {i} cards drawn");
                    break;
                }
            }

            // 正常抽牌：检查手牌上限
            if (hand.Cards.Count >= 10)
            {
                break;
            }

            // 将卡牌加入手牌
            result.Add(card);
            await CardPileCmd.Add(card, hand);
            CombatManager.Instance.History.CardDrawn(combatState, card, fromHandDraw);
            await Hook.AfterCardDrawn(combatState, choiceContext, card, fromHandDraw);
            card.InvokeDrawn();
            NDebugAudioManager.Instance?.Play("card_deal.mp3", 0.25f, PitchVariance.Small);
        }

        ModLogger.Log($"CustomDraw: Drew {result.Count} cards for player {player.NetId}");
        return result;
    }

    /// <summary>
    /// 检查抽牌是否可能，如果不可能则显示提示气泡
    /// </summary>
    private static bool CheckIfDrawIsPossibleAndShowThoughtBubbleIfNot(Player player)
    {
        // 两个牌堆都空
        if (PileType.Draw.GetPile(player).Cards.Count + PileType.Discard.GetPile(player).Cards.Count == 0)
        {
            ThinkCmd.Play(new LocString("combat_messages", "NO_DRAW"), player.Creature, 2.0);
            return false;
        }
        // 手牌已满
        if (PileType.Hand.GetPile(player).Cards.Count >= 10)
        {
            ThinkCmd.Play(new LocString("combat_messages", "HAND_FULL"), player.Creature, 2.0);
            return false;
        }
        return true;
    }

    /// <summary>
    /// 替换 EndPlayerTurnPhaseTwoInternal 方法，在回合结束时洗牌
    /// </summary>
    [HarmonyPatch(typeof(CombatManager), "EndPlayerTurnPhaseTwoInternal")]
    [HarmonyPrefix]
    public static bool EndPlayerTurnPhaseTwoInternalPrefix(CombatManager __instance, ref Task __result)
    {
        __result = EndPlayerTurnPhaseTwoInternalWithShuffle(__instance);
        return false; // 跳过原方法，使用自定义逻辑
    }

    /// <summary>
    /// 自定义回合结束逻辑
    /// 在 Hook.AfterTurnEnd 之前为空抽牌堆的玩家洗牌
    /// </summary>
    private static async Task EndPlayerTurnPhaseTwoInternalWithShuffle(CombatManager instance)
    {
        // 使用反射获取私有字段 _state
        var stateField = typeof(CombatManager).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
        var state = (CombatState?)stateField?.GetValue(instance);

        if (state == null)
            return;

        // 验证当前是玩家回合
        if (state.CurrentSide != CombatSide.Player)
        {
            throw new InvalidOperationException($"EndPlayerTurnPhaseTwo called while the current side is {state.CurrentSide}!");
        }

        // 获取玩家列表（考虑额外回合的情况）
        var playerReadyLockField = typeof(CombatManager).GetField("_playerReadyLock", BindingFlags.NonPublic | BindingFlags.Instance);
        var playersTakingExtraTurnField = typeof(CombatManager).GetField("_playersTakingExtraTurn", BindingFlags.NonPublic | BindingFlags.Instance);

        List<Player> list;
        if (playerReadyLockField?.GetValue(instance) is Lock playerReadyLock)
        {
            using (playerReadyLock.EnterScope())
            {
                var playersTakingExtraTurn = playersTakingExtraTurnField?.GetValue(instance) as List<Player>;
                list = (playersTakingExtraTurn != null && playersTakingExtraTurn.Count > 0)
                    ? playersTakingExtraTurn.ToList()
                    : state.Players.ToList();
            }
        }
        else
        {
            list = state.Players.ToList();
        }

        // 处理每个玩家的回合结束（弃牌等）
        foreach (Player player in list)
        {
            CardPile pile = PileType.Hand.GetPile(player);
            List<CardModel> cardsToDiscard = new List<CardModel>();
            List<CardModel> cardsToRetain = new List<CardModel>();

            // 分类手牌：保留 vs 弃牌
            foreach (CardModel card in pile.Cards)
            {
                if (card.ShouldRetainThisTurn)
                {
                    cardsToRetain.Add(card);
                }
                else
                {
                    cardsToDiscard.Add(card);
                }
            }

            // 弃掉非保留的牌
            if (Hook.ShouldFlush(player.Creature?.CombatState!, player))
            {
                await CardPileCmd.Add(cardsToDiscard, PileType.Discard.GetPile(player));
            }

            // 触发保留牌的 Hook
            foreach (CardModel card in cardsToRetain)
            {
                await Hook.AfterCardRetained(state, card);
            }

            // 清理回合结束状态
            player.PlayerCombatState?.EndOfTurnCleanup();
        }

        // 新增逻辑：回合结束时为空抽牌堆的玩家洗牌
        ulong? netId = LocalContext.NetId;
        if (netId.HasValue)
        {
            foreach (Player player in state.Players)
            {
                CardPile drawPile = PileType.Draw.GetPile(player);
                CardPile discardPile = PileType.Discard.GetPile(player);

                ModLogger.Log($"EndTurnShuffle: Player {player.NetId} drawPile={drawPile.Cards.Count}, discardPile={discardPile.Cards.Count}");

                // 抽牌堆为空且弃牌堆有牌时洗牌
                if (drawPile.Cards.Count == 0 && discardPile.Cards.Count > 0)
                {
                    ModLogger.Log($"EndTurnShuffle: Shuffling for player {player.NetId}");

                    // 创建正确的多人同步上下文
                    var hookContext = new HookPlayerChoiceContext(player, netId.Value, GameActionType.Combat);
                    Task shuffleTask = CardPileCmd.Shuffle(hookContext, player);

                    // 等待任务完成或暂停
                    await hookContext.AssignTaskAndWaitForPauseOrCompletion(shuffleTask);

                    // 如果任务被暂停，等待游戏动作完成
                    if (!shuffleTask.IsCompleted && hookContext.GameAction != null)
                    {
                        await hookContext.GameAction.CompletionTask;
                    }

                    ModLogger.Log($"EndTurnShuffle: Shuffle complete for player {player.NetId}");
                }
            }
        }

        // 调用原游戏的回合结束 Hook
        await Hook.AfterTurnEnd(state, state.CurrentSide);

        // 生成校验和（多人同步用）
        RunManager.Instance.ChecksumTracker.GenerateChecksum("after player turn phase two end", null);
    }

    /// <summary>
    /// 战斗开始时重置疲劳计数器
    /// </summary>
    [HarmonyPatch(typeof(CombatManager), "SetUpCombat")]
    [HarmonyPostfix]
    public static void SetUpCombatReset(CombatState state)
    {
        ModLogger.Log($"SetUpCombat: {state.Players.Count} players");
        foreach (Player player in state.Players)
            ResetFatigueCount(player.NetId);
    }

    /// <summary>
    /// 战斗结束时清空疲劳计数器
    /// </summary>
    [HarmonyPatch(typeof(CombatManager), "EndCombatInternal")]
    [HarmonyPostfix]
    public static void EndCombatClearFatigue()
    {
        ModLogger.Log("EndCombat: Clearing fatigue counters");
        _fatigueCount.Clear();
    }
}

/// <summary>
/// Mod 入口点
/// </summary>
[MegaCrit.Sts2.Core.Modding.ModInitializer("Initialize")]
public static class ModEntry
{
    public static void Initialize() => FatiguePatches.Initialize();
}