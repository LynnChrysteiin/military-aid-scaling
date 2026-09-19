using System;
using System.Collections.Generic;
using AriandelLibrary;
using HarmonyLib;
using MilitaryAidScaling.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace MilitaryAidScaling.Compat.Ariandel
{
    // 米莉拉帝国（Milira Imperium）兼容补丁。其支援许可 = 1 默认组 + N 个
    // extraGroups，每组独立 TryExecute 且共享同一落点与折跃编队表，故：
    //  1. 等点替换会使总量 = N×X——按全许可原版占比分摊（跟随模式）；
    //  2. 各组并行折跃会同格堆叠——spawnCenter 螺旋错开（间距 14 > 编队半径 6×2）。
    // 仅在本体与 Ariandel.AriandelLibrary 同时启用时由 LoadFolders 加载。
    public static class AX_MAS_Compat_Ariandel
    {
        // ---- 一次许可呼叫的上下文（仅主线程，CallAid 同步执行期间有效） ----

        private static bool contextActive;
        private static Faction contextFaction;
        private static float contextTotalPoints;
        private static int contextGroupIndex;
        private static readonly List<string> contextLines = new List<string>();

        // 螺旋落点：第 0 组用原中心，其后每 8 组一圈、圈距 14（编队半径 6）。
        private static readonly int[] DirX = { 1, 1, 0, -1, -1, -1, 0, 1 };
        private static readonly int[] DirZ = { 0, 1, 1, 1, 0, -1, -1, -1 };

        public static void CallAid_Prefix(
            RoyalTitlePermitWorker_CallAid_Group __instance, Faction faction)
        {
            try
            {
                float total = 0f;
                RoyalAid royalAid = __instance.def?.royalAid;
                if (royalAid != null)
                {
                    if (royalAid.pawnKindDef != null && royalAid.pawnCount > 0)
                    {
                        total += royalAid.pawnKindDef.combatPower * royalAid.pawnCount;
                    }
                    else if (royalAid.points > 0f)
                    {
                        total += royalAid.points;
                    }
                }
                AL_CallAid_Group_Extension ext =
                    __instance.def?.GetModExtension<AL_CallAid_Group_Extension>();
                if (ext?.extraGroups != null)
                {
                    foreach (AL_CallAid_Group group in ext.extraGroups)
                    {
                        if (group?.pawnKindDef != null && group.pawnCount > 0)
                        {
                            total += group.pawnKindDef.combatPower * group.pawnCount;
                        }
                    }
                }
                if (total > 0f)
                {
                    contextActive = true;
                    contextFaction = faction;
                    contextTotalPoints = total;
                    contextGroupIndex = 0;
                }
            }
            catch (Exception e)
            {
                Log.Error(
                    "[Military Aid Scaling] Ariandel 兼容：统计许可编组失败 | Failed to tally permit groups: " + e
                );
            }
        }

        public static void CallAid_Finalizer()
        {
            // 聚合为单条消息，避免逐组刷屏
            if (contextActive && contextLines.Count > 0)
            {
                Messages.Message(
                    "AX_MAS_Msg_GroupHeader".Translate(contextLines.Count).Resolve()
                        + "\n"
                        + string.Join("\n", contextLines),
                    MessageTypeDefOf.PositiveEvent,
                    historical: true
                );
            }
            contextActive = false;
            contextFaction = null;
            contextTotalPoints = 0f;
            contextGroupIndex = 0;
            contextLines.Clear();
        }

        // 呼叫期间收集明细行，结束时聚合为单条消息。
        public static bool NotifyGroupScaled_Prefix(string line)
        {
            if (!contextActive)
            {
                return true; // 非米莉拉呼叫：走本体默认的单条短消息
            }
            contextLines.Add(line);
            return false; // 跳过本体默认消息
        }

        public static void AdjustGroupTarget_Postfix(IncidentParms parms, ref float __result)
        {
            if (!contextActive || parms == null || parms.faction != contextFaction)
            {
                return;
            }
            try
            {
                float groupValue = parms.pawnKind != null
                    ? parms.pawnKind.combatPower * parms.pawnCount
                    : parms.points;
                if (groupValue <= 0f)
                {
                    return;
                }
                int index = contextGroupIndex++;
                float share = groupValue / contextTotalPoints;

                // 跟随模式按份额分摊使总量 = X×百分比；固定系数模式求和本就正确。
                if (AX_MAS_Mod.Instance != null
                    && AX_MAS_Mod.Instance.Settings.followThreatScale)
                {
                    __result *= share;
                }

                // 各组落点螺旋错开，消除组间同格折跃
                if (parms.target is Map map && parms.spawnCenter.IsValid)                {
                    parms.spawnCenter = GroupCenter(parms.spawnCenter, map, index);
                }

                if (AX_MAS_Mod.DebugLogging)
                {
                    Log.Message(
                        $"[Military Aid Scaling] Ariandel 兼容：组 {index + 1} 份额 {share:P0}，目标点数 {__result:0}，落点 ({parms.spawnCenter.x},{parms.spawnCenter.z}) | group {index + 1} share {share:P0}, target {__result:0} points, spawn ({parms.spawnCenter.x},{parms.spawnCenter.z})"
                    );
                }
            }
            catch (Exception e)
            {
                Log.Error(
                    "[Military Aid Scaling] Ariandel 兼容：分组分摊失败 | Failed to split group share: " + e
                );
            }
        }

        private static IntVec3 GroupCenter(IntVec3 center, Map map, int index)
        {
            if (index <= 0)
            {
                return center;
            }
            int ring = (index - 1) / 8 + 1;
            int dir = (index - 1) % 8;
            int dist = ring * 14;
            // 贴边钳制（留出编队半径 6 + 余量）；极端贴边时仍可能相邻
            int x = Mathf.Clamp(center.x + DirX[dir] * dist, 8, map.Size.x - 9);
            int z = Mathf.Clamp(center.z + DirZ[dir] * dist, 8, map.Size.z - 9);
            return new IntVec3(x, 0, z);
        }
    }

    [StaticConstructorOnStartup]
    public static class AX_MAS_Compat_Ariandel_Init
    {
        static AX_MAS_Compat_Ariandel_Init()
        {
            try
            {
                Harmony harmony = new Harmony("Astralux.MilitaryAidScaling.Compat.Ariandel");
                harmony.Patch(
                    AccessTools.Method(
                        typeof(RoyalTitlePermitWorker_CallAid_Group), "CallAid"),
                    prefix: new HarmonyMethod(
                        typeof(AX_MAS_Compat_Ariandel), nameof(AX_MAS_Compat_Ariandel.CallAid_Prefix)),
                    finalizer: new HarmonyMethod(
                        typeof(AX_MAS_Compat_Ariandel), nameof(AX_MAS_Compat_Ariandel.CallAid_Finalizer))
                );
                harmony.Patch(
                    AccessTools.Method(
                        typeof(AX_MAS_Mod), nameof(AX_MAS_Mod.AdjustGroupTarget)),
                    postfix: new HarmonyMethod(
                        typeof(AX_MAS_Compat_Ariandel), nameof(AX_MAS_Compat_Ariandel.AdjustGroupTarget_Postfix))
                );
                harmony.Patch(
                    AccessTools.Method(
                        typeof(AX_MAS_Mod), nameof(AX_MAS_Mod.NotifyGroupScaled)),
                    prefix: new HarmonyMethod(
                        typeof(AX_MAS_Compat_Ariandel), nameof(AX_MAS_Compat_Ariandel.NotifyGroupScaled_Prefix))
                );
                Log.Message(
                    "[Military Aid Scaling] AriandelLibrary 兼容补丁已应用 | AriandelLibrary compat applied"
                );
            }
            catch (Exception e)
            {
                Log.Error(
                    "[Military Aid Scaling] AriandelLibrary 兼容补丁应用失败 | Failed to apply AriandelLibrary compat: " + e
                );
            }
        }
    }
}
