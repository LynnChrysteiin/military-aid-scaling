using RimWorld;
using UnityEngine;
using Verse;

namespace MilitaryAidScaling.Core
{
    public class AX_MAS_ModSettings : ModSettings
    {
        public bool enabled = true;            // 总开关
        public bool followThreatScale = true;  // true=跟随当前袭击规模；false=固定系数
        public float aidPercent = 0.2f;          // 跟随模式：百分比（1 = 100%）
        public float customFactor = 1f;        // 固定系数模式：直接给系数
        public bool floorAtVanillaMin = true;  // 下限钳制到原版最低值（防极小规模支援）
        public bool debugLogging = false;      // 调试日志

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref followThreatScale, "followThreatScale", true);
            Scribe_Values.Look(ref aidPercent, "aidPercent", 0.2f);
            Scribe_Values.Look(ref customFactor, "customFactor", 1f);
            Scribe_Values.Look(ref floorAtVanillaMin, "floorAtVanillaMin", true);
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

        // 跟随模式：目标点数 = DefaultThreatPointsNow × 百分比。该函数的钳制
        // 上限随环境（原版 10000 / Elite Raid 解锁后的 maxRaidPoint），取值即
        // 天然跟随解锁后的上限，本 mod 不做主动钳制。
        public static float TargetAidPoints(float basePoints, IIncidentTarget target)
        {
            if (Instance == null)
            {
                return basePoints;
            }
            AX_MAS_ModSettings s = Instance.Settings;
            if (!s.followThreatScale)
            {
                return basePoints * s.customFactor;
            }
            Map map = target as Map;
            if (map == null || Find.Storyteller == null)
            {
                return basePoints * s.aidPercent;
            }
            return StorytellerUtility.DefaultThreatPointsNow(map) * s.aidPercent;
        }

        // 当前动态袭击点数，设置页预览用；无地图时返回 -1。
        public static float CurrentRaidPoints()
        {
            Map map = Find.CurrentMap;
            if (map == null || Find.Storyteller == null)
            {
                return -1f;
            }
            return StorytellerUtility.DefaultThreatPointsNow(map);
        }

        // 原版通讯台支援点数下界，取自原版常量。
        public static float VanillaMinAidPoints =>
            DiplomacyTuning.RequestedMilitaryAidPointsRange.min;

        // 兼容接缝：默认原样返回，供 IfModActive 加载的兼容程序集 Postfix，
        // 处理"一次呼叫 = 多组独立 TryExecute"的份额分摊与落点错开。
        public static float AdjustGroupTarget(IncidentParms parms, float target)
        {
            return target;
        }

        // 缩放通知缝：默认逐条弹短消息；兼容层可 Prefix 返回 false 聚合为单条。
        public static void NotifyGroupScaled(string line)
        {
            Messages.Message(line, MessageTypeDefOf.PositiveEvent, historical: true);
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // 原版设置对话框固定 900×700 且不做屏幕钳制，高 UI 缩放下底部
            // 会被屏幕切掉；钳回屏幕内。
            foreach (Window window in Find.WindowStack.Windows)
            {
                if (!(window is Dialog_ModSettings))
                {
                    continue;
                }
                float width = Mathf.Min(window.windowRect.width, UI.screenWidth - 8f);
                float height = Mathf.Min(window.windowRect.height, UI.screenHeight - 8f);
                if (width != window.windowRect.width || height != window.windowRect.height)
                {
                    window.windowRect = new Rect(
                        ((float)UI.screenWidth - width) / 2f,
                        ((float)UI.screenHeight - height) / 2f,
                        width,
                        height
                    );
                }
                break;
            }

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
                    percentRect, Settings.aidPercent, 0.01f, 2f, false,
                    "AX_MAS_Settings_AidPercent".Translate(Mathf.RoundToInt(Settings.aidPercent * 100f)),
                    "1%", "200%"
                );
            }
            else
            {
                Rect factorRect = listing.GetRect(24f);
                Settings.customFactor = Widgets.HorizontalSlider(
                    factorRect, Settings.customFactor, 0.01f, 50f, false,
                    "AX_MAS_Settings_CustomFactor".Translate(Settings.customFactor.ToString("0.00")),
                    "0.01", "50"
                );
            }

            listing.CheckboxLabeled(
                "AX_MAS_Settings_FloorAtVanillaMin".Translate(),
                ref Settings.floorAtVanillaMin,
                "AX_MAS_Settings_FloorAtVanillaMin_Tip".Translate()
            );
            listing.CheckboxLabeled(
                "AX_MAS_Settings_DebugLogging".Translate(),
                ref Settings.debugLogging,
                "AX_MAS_Settings_DebugLogging_Tip".Translate()
            );

            listing.Gap(6f);
            // 实时预览（含下限钳制）
            if (Settings.followThreatScale)
            {
                float raidPoints = CurrentRaidPoints();
                string raidText = raidPoints < 0f
                    ? "?"
                    : Mathf.RoundToInt(raidPoints).ToString();
                float targetPoints = raidPoints < 0f
                    ? -1f
                    : raidPoints * Settings.aidPercent;
                if (targetPoints >= 0f && Settings.floorAtVanillaMin
                    && targetPoints < VanillaMinAidPoints)
                {
                    targetPoints = VanillaMinAidPoints;
                }
                string targetText = targetPoints < 0f
                    ? "?"
                    : Mathf.RoundToInt(targetPoints).ToString();
                listing.Label("AX_MAS_Settings_Preview".Translate(raidText, targetText));
            }
            else
            {
                listing.Label("AX_MAS_Settings_PreviewFixed"
                    .Translate(Settings.customFactor.ToString("0.00")));
            }

            listing.End();
            base.DoSettingsWindowContents(inRect);
        }
    }
}