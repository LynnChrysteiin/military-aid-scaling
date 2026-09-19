using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MilitaryAidScaling.Core
{
    public class AX_MAS_ModSettings : ModSettings
    {
        public bool enabled = true;            // 总开关
        public bool followThreatScale = true;  // true=跟随当前袭击规模；false=固定系数
        public float aidPercent = 1f;          // 跟随模式：百分比（1 = 100%）
        public float customFactor = 1f;        // 固定系数模式：直接给系数
        public bool scaleBelowOne = true;      // 低于 100% 时是否对称缩小
        public bool debugLogging = false;      // 调试日志

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref followThreatScale, "followThreatScale", true);
            Scribe_Values.Look(ref aidPercent, "aidPercent", 1f);
            Scribe_Values.Look(ref customFactor, "customFactor", 1f);
            Scribe_Values.Look(ref scaleBelowOne, "scaleBelowOne", true);
            Scribe_Values.Look(ref debugLogging, "debugLogging", false);
            base.ExposeData();
        }
    }

    public class AX_MAS_Mod : Mod
    {
        public static AX_MAS_Mod Instance { get; private set; }
        public AX_MAS_ModSettings Settings { get; private set; }

        public static bool Enabled => Instance != null && Instance.Settings.enabled;
        public static bool DebugLogging => Instance != null && Instance.Settings.debugLogging;

        public AX_MAS_Mod(ModContentPack content)
            : base(content)
        {
            Instance = this;
            Settings = GetSettings<AX_MAS_ModSettings>();
        }

        public override string SettingsCategory()
        {
            return "Military Aid Scaling";
        }

        // 有效支援规模。
        // 跟随模式 = difficulty.threatScale × 百分比。threatScale 是唯一真值来源：
        // 原版难度滑条、Unlimited Threat Scale（解锁滑条上限）、Elite Raid 的袭击倍率
        // （它直接覆写该字段）最终全部汇聚在这一个字段里——因此这里无需感知任何
        // mod 是否在场，天然全兼容。
        // 固定系数模式 = customFactor，不与威胁规模联动。
        public static float EffectiveScale()
        {
            if (Instance == null)
            {
                return 1f;
            }
            AX_MAS_ModSettings s = Instance.Settings;
            float scale = s.followThreatScale
                ? Find.Storyteller.difficulty.threatScale * s.aidPercent
                : s.customFactor;
            if (!s.scaleBelowOne && scale < 1f)
            {
                scale = 1f; // "只增不减"选项：低于 100% 时按原版规模
            }
            return scale;
        }

        // 敌方当前真实点数天花板，用于 points 模式钳制（保证支援永不超过敌方
        // 可能达到的规模上限）。
        // 原版：DefaultThreatPointsNow 里硬编码的 10000。
        // Elite Raid 在场：它用 Transpiler 把该常量解锁为 1e7，再用自己的
        // maxRaidPoint 设置（滑条 5000~400 万）封顶——故镜像该值。
        // 只读探测 + 回退：mod 未装/改名/升级导致探测失败时自动退回 10000，
        // 绝不构成硬依赖，也不会报错打扰玩家。
        public static float CurrentEnemyPointsCeiling()
        {
            try
            {
                Type eliteRaidMod = AccessTools.TypeByName("EliteRaid.EliteRaidMod");
                if (eliteRaidMod != null)
                {
                    object value = AccessTools.Field(eliteRaidMod, "maxRaidPoint")?.GetValue(null);
                    if (value is int maxRaidPoint)
                    {
                        return Mathf.Max(1, maxRaidPoint);
                    }
                }
            }
            catch
            {
                // 探测失败，走原版回退
            }
            return 10000f;
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.GapLine(12f);
            Text.Font = GameFont.Medium;
            listing.Label("AX_MAS_Settings_Header".Translate());
            Text.Font = GameFont.Small;
            listing.GapLine(12f);

            listing.CheckboxLabeled(
                "AX_MAS_Settings_Enabled".Translate(),
                ref Settings.enabled,
                "AX_MAS_Settings_Enabled_Tip".Translate()
            );
            listing.CheckboxLabeled(
                "AX_MAS_Settings_FollowThreatScale".Translate(),
                ref Settings.followThreatScale,
                "AX_MAS_Settings_FollowThreatScale_Tip".Translate()
            );

            if (Settings.followThreatScale)
            {
                Rect percentRect = listing.GetRect(24f);
                Settings.aidPercent = Widgets.HorizontalSlider(
                    percentRect, Settings.aidPercent, 0.01f, 5f, false,
                    "AX_MAS_Settings_AidPercent".Translate(Mathf.RoundToInt(Settings.aidPercent * 100f)),
                    "1%", "500%"
                );
            }
            else
            {
                Rect factorRect = listing.GetRect(24f);
                Settings.customFactor = Widgets.HorizontalSlider(
                    factorRect, Settings.customFactor, 0.01f, 100f, false,
                    "AX_MAS_Settings_CustomFactor".Translate(Settings.customFactor.ToString("0.00")),
                    "0.01", "100"
                );
            }

            listing.CheckboxLabeled(
                "AX_MAS_Settings_ScaleBelowOne".Translate(),
                ref Settings.scaleBelowOne,
                "AX_MAS_Settings_ScaleBelowOne_Tip".Translate()
            );
            listing.CheckboxLabeled(
                "AX_MAS_Settings_DebugLogging".Translate(),
                ref Settings.debugLogging,
                "AX_MAS_Settings_DebugLogging_Tip".Translate()
            );

            listing.Gap(6f);
            // 实时预览：当前威胁规模 → 本次呼叫将使用的有效支援规模
            string threatScaleText = "?";
            if (Find.Storyteller != null && Find.Storyteller.difficulty != null)
            {
                threatScaleText = Find.Storyteller.difficulty.threatScale.ToStringPercent();
            }
            listing.Label("AX_MAS_Settings_Preview".Translate(threatScaleText, EffectiveScale().ToStringPercent()));

            listing.End();
            base.DoSettingsWindowContents(inRect);
        }
    }
}