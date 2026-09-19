using System;
using HarmonyLib;
using MilitaryAidScaling.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace MilitaryAidScaling.AidScaling
{
    // 唯一的核心补丁：挂在一切袭击类事件的公共入口 IncidentWorker.TryExecute，
    // 在事件执行前把"手动呼叫的友军支援"改写为目标规模（等点语义，v0.7）。
    //
    // 为什么选这个入口：
    //  - 原版三条呼叫路径（通讯台 FactionDialogMaker / 爵位许可 RoyalTitlePermitWorker_CallAid
    //    / 灵能 CompAbilityEffect_RandomAlliedMilitaryAid）最终都调用它；
    //  - 米莉拉帝国等 mod 的自定义 worker（IncidentWorker_Raid_Ariandel 等继承
    //    IncidentWorker_Raid 的子类）也经过它——所以不需要任何针对性兼容代码；
    //  - Prefix 在事件执行前改 parms，后续人数生成/抵达方式/信件全部自然采用新值；
    //  - Elite Raid 的 L2/L3 压缩在生成阶段拿到的已是本 mod 改写后的点数/人数，
    //    "先缩放、后压缩"天然叠加，本 mod 对其处理零干预。
    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
    public static class IncidentWorker_TryExecute_ScaleAid_Patch
    {
        // 参数名必须与原方法签名一致（IncidentWorker.TryExecute(IncidentParms parms)），
        // Harmony 按名字匹配注入；__instance 是 Harmony 约定的被补丁实例。
        public static void Prefix(IncidentWorker __instance, IncidentParms parms)
        {
            try
            {
                if (parms == null || !AX_MAS_Mod.Enabled)
                {
                    return;
                }

                // 只处理袭击家族事件（含 RaidFriendly 与各 mod 的自定义子类）。
                // 不挂在 TryExecuteWorker 上的原因：那是虚方法继承，patch 基类实现
                // 会波及所有袭击子类，不如在非虚公共入口做实例过滤干净。
                if (!(__instance is IncidentWorker_Raid))
                {
                    return;
                }

                if (!(parms.target is Map))
                {
                    return;
                }

                // 只处理盟友派系（敌对袭击、玩家自己的单位都不碰）。
                Faction faction = parms.faction;
                if (faction == null || faction == Faction.OfPlayer)
                {
                    return;
                }
                if (faction.HostileTo(Faction.OfPlayer))
                {
                    return;
                }

                // "手动呼叫"判别（设计 §2.3/§2.3.1）：
                //  - raidArrivalModeForQuickMilitaryAid：原版三条路径都会设置的官方标记；
                //  - pawnKind != null：固定人数支援模式（帝国小队），讲述者自发
                //    支援从不设置此字段。
                // 讲述者自发支援两者皆无——它已经过 DefaultThreatPointsNow 缩放，
                // 这里必须放行，否则会二次放大。
                bool manualCall =
                    parms.raidArrivalModeForQuickMilitaryAid || parms.pawnKind != null;
                if (!manualCall)
                {
                    return;
                }

                AX_MAS_ModSettings settings = AX_MAS_Mod.Instance.Settings;

                if (parms.pawnKind != null)
                {
                    // pawnCount 模式：等点换算 目标点数 ÷ 兵种战力 = 人数，
                    // 上下取整间随机保留原版风味；不干预下游 mod。
                    float combatPower = parms.pawnKind.combatPower;
                    if (combatPower <= 0f)
                    {
                        return; // 异常兵种数据，保持原值
                    }
                    float targetPoints = AX_MAS_Mod.TargetAidPoints(
                        combatPower * parms.pawnCount, parms.target);
                    targetPoints = AX_MAS_Mod.AdjustGroupTarget(parms, targetPoints);
                    float scaled = targetPoints / combatPower;
                    int lo = Mathf.Max(1, Mathf.CeilToInt(scaled));
                    int hi = Mathf.Max(lo, Mathf.FloorToInt(scaled));
                    int before = parms.pawnCount;
                    parms.pawnCount = Rand.RangeInclusive(lo, hi);
                    // 下限 = 原版编制人数
                    bool floored = settings.floorAtVanillaMin && parms.pawnCount < before;
                    if (floored)
                    {
                        parms.pawnCount = before;
                    }
                    AX_MAS_Mod.NotifyGroupScaled(
                        "AX_MAS_Msg_CountScaled".Translate(
                            parms.pawnKind.LabelCap, before, parms.pawnCount));
                    if (AX_MAS_Mod.DebugLogging)
                    {
                        // 实际到场价值 = 人数 × 战力（targetPoints 为钳制前换算值）
                        float effectivePoints = parms.pawnCount * combatPower;
                        string floorNote = floored
                            ? $"，下限钳制生效 (换算目标 {targetPoints:0} 低于原编制，保持原人数 / vanilla-minimum floor applied)"
                            : "";
                        Log.Message(
                            $"[Military Aid Scaling] {faction.Name} {parms.pawnKind.defName}: 人数已由 {before} 调整至 {parms.pawnCount}，实际 ≈ {effectivePoints:0} 点{floorNote} | PawnCount scaled from {before} to {parms.pawnCount}, effective ≈ {effectivePoints:0} points"
                        );
                    }
                }
                else
                {
                    // points 模式：目标点数直接替换原版固定值（800~1000），不钳制。
                    float before = parms.points;
                    float target = AX_MAS_Mod.TargetAidPoints(before, parms.target);
                    target = AX_MAS_Mod.AdjustGroupTarget(parms, target);
                    if (settings.floorAtVanillaMin && target < AX_MAS_Mod.VanillaMinAidPoints)
                    {
                        // 原版下限，防极小规模支援
                        target = AX_MAS_Mod.VanillaMinAidPoints;
                    }
                    parms.points = target;
                    AX_MAS_Mod.NotifyGroupScaled(
                        "AX_MAS_Msg_PointsScaled".Translate(
                            before.ToString("0"), parms.points.ToString("0")));
                    if (AX_MAS_Mod.DebugLogging)
                    {
                        Log.Message(
                            $"[Military Aid Scaling] {faction.Name}: 支援袭击点数由 {before} 调整至 {parms.points} | Aid raid points scaled from {before} to {parms.points}"
                        );
                    }
                }
            }
            catch (Exception e)
            {
                // 任何异常都不能影响原事件执行（支援必须照常到场）。
                Log.Error(
                    "[Military Aid Scaling] 袭击规模调整失败 | Failed to scale raid: " + e
                );
            }
        }
    }
}
