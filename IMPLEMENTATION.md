# Military Aid Scaling（军事支援规模缩放）实施指南

> 配套设计文档：[DESIGN.md](./DESIGN.md)（v0.6.2 定稿）。本指南是**手敲用操作手册**：创建什么文件、写什么代码、每段干什么。
> 风格与工程约定完全对齐你的 `milira-remielle` / `astralux-library`：net48 + Local.props、根部 LoadFolders.xml、`AX_缩写_` 前缀、块作用域命名空间、Allman 大括号、LangVersion 8.0。
> 以下所有 API 签名均已用本地 1.6 反编译逐一核实，照抄即可编译。

---

## 0. 成品结构（共 9 个文件）

```
military-aid-scaling/                  ← mod 根目录（建议建在本工作区下；工作区目录名不用改）
├── About/
│   └── About.xml                      ① mod 元数据
├── LoadFolders.xml                    ② 条件加载声明（引擎自动从根目录读取）
├── 1.6/                               ← 版本文件夹，内容随 1.6 加载
│   ├── Assemblies/                    ←（编译输出，不用手建文件）
│   └── Languages/
│       ├── English/Keyed/MilitaryAidScaling.xml        ③ 英文键
│       └── ChineseSimplified (简体中文)/Keyed/MilitaryAidScaling.xml ④ 中文键
└── Source/
    ├── Local.props                    ⑤ 本机路径（可直接抄你现有那份）
    ├── MilitaryAidScaling.csproj     ⑥ 编译工程
    └── MilitaryAidScaling/
        ├── Core/
        │   ├── AX_MAS_Startup.cs      ⑦ Harmony 引导
        │   └── AX_MAS_Mod.cs          ⑧ Mod 入口 + 设置 + 两个核心静态方法
        └── AidScaling/
            └── IncidentWorker_TryExecute_ScaleAid_Patch.cs ⑨ 唯一补丁（核心逻辑）
```

命名约定沿用你的习惯：包名 `Astralux.MilitaryAidScaling`、程序集/命名空间 `MilitaryAidScaling`、类前缀 `AX_MAS_`（对应你的 `AX_RaM_`）、Keyed 键前缀 `AX_MAS_Settings_`。**本 mod 不引用 AstraluxLibrary**——按设计它只依赖 Harmony，保持零额外依赖。

---

## ① `About/About.xml`

```xml
<?xml version="1.0" encoding="utf-8"?>
<ModMetaData>
  <name>Military Aid Scaling</name>
  <author>Astralux</author>
  <supportedVersions>
    <li>1.6</li>
  </supportedVersions>
  <description>手动呼叫的友军支援规模等比匹配当前难度的袭击规模：支援规模 = 当前袭击规模 × 自定义百分比。自动覆盖通讯台、帝国爵位许可、灵能支援，以及米莉安帝国等使用固定人数支援的派系 mod；500% 以上规模（Unlimited Threat Scale / Elite Raid 袭击倍率）自动跟随。强化与压缩完全交由同时启用的其他 mod（如 Elite Raid）按其自身设计处理，本 mod 只管规模缩放、不做任何强化。</description>
  <packageId>Astralux.MilitaryAidScaling</packageId>
  <modDependencies>
    <li>
      <packageId>brrainz.harmony</packageId>
      <displayName>Harmony</displayName>
      <steamWorkshopUrl>https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077</steamWorkshopUrl>
    </li>
  </modDependencies>
  <loadAfter>
    <li>brrainz.harmony</li>
    <li>trigger.eliteRaid</li>
    <li>unlimitedthreatscale.1trickpwnyta</li>
    <li>visibleraidpoints.1trickpwnyta</li>
  </loadAfter>
</ModMetaData>
```

**说明**：唯一硬依赖是 Harmony。`loadAfter` 列出 Elite Raid / Unlimited Threat Scale 只是保证加载顺序稳定（我们不 patch 它们任何方法，顺序其实无关紧要，声明是无害的礼貌）。**刻意不写任何 `incompatibleWith`**。

---

## ② `LoadFolders.xml`（mod 根目录）

```xml
<?xml version="1.0" encoding="UTF-8"?>
<loadFolders>
	<v1.6>
		<li>1.6</li>
	</v1.6>
</loadFolders>
```

**说明**：与你的 milira-remielle 同款机制——引擎会自动从 mod 根目录读取此文件（`ModMetaData.cs:626`，大小写不敏感），效果等同写在 About.xml 内。这里只声明 `1.6` 版本文件夹。将来要加"某 mod 在场才加载"的兼容内容时，仿照你 milira-remielle 里的写法加 `<li IfModActive="trigger.eliteRaid">Compat/EliteRaid</li>` 即可（`IfModActive` / `IfModActiveAll` / `IfModNotActive` 均为 1.6 原生支持，逗号分隔可写多个包名）。

---

## ③ `1.6/Languages/English/Keyed/MilitaryAidScaling.xml`

```xml
<?xml version="1.0" encoding="utf-8"?>
<LanguageData>
  <AX_MAS_Settings_Header>Military Aid Scaling</AX_MAS_Settings_Header>
  <AX_MAS_Settings_Enabled>Enable scaling</AX_MAS_Settings_Enabled>
  <AX_MAS_Settings_Enabled_Tip>Scale manually called allied military aid with the current raid scale (threat scale).</AX_MAS_Settings_Enabled_Tip>
  <AX_MAS_Settings_FollowThreatScale>Follow current threat scale</AX_MAS_Settings_FollowThreatScale>
  <AX_MAS_Settings_FollowThreatScale_Tip>On: aid scale = threat scale × percent. Off: use a fixed factor instead.</AX_MAS_Settings_FollowThreatScale_Tip>
  <AX_MAS_Settings_AidPercent>Support scale percent: {0}%</AX_MAS_Settings_AidPercent>
  <AX_MAS_Settings_CustomFactor>Custom factor: {0}</AX_MAS_Settings_CustomFactor>
  <AX_MAS_Settings_ScaleBelowOne>Also shrink when scale below 100%</AX_MAS_Settings_ScaleBelowOne>
  <AX_MAS_Settings_ScaleBelowOne_Tip>Off: aid never gets smaller than vanilla, only bigger.</AX_MAS_Settings_ScaleBelowOne_Tip>
  <AX_MAS_Settings_DebugLogging>Debug logging</AX_MAS_Settings_DebugLogging>
  <AX_MAS_Settings_DebugLogging_Tip>Print aid scaling details to the log on every call.</AX_MAS_Settings_DebugLogging_Tip>
  <AX_MAS_Settings_Preview>Current threat scale: {0} → effective aid scale: {1}</AX_MAS_Settings_Preview>
</LanguageData>
```

## ④ `1.6/Languages/ChineseSimplified (简体中文)/Keyed/MilitaryAidScaling.xml`

```xml
<?xml version="1.0" encoding="utf-8"?>
<LanguageData>
  <AX_MAS_Settings_Header>军事支援规模缩放</AX_MAS_Settings_Header>
  <AX_MAS_Settings_Enabled>启用支援缩放</AX_MAS_Settings_Enabled>
  <AX_MAS_Settings_Enabled_Tip>手动呼叫的友军支援规模将匹配当前难度的袭击规模。</AX_MAS_Settings_Enabled_Tip>
  <AX_MAS_Settings_FollowThreatScale>跟随当前袭击规模</AX_MAS_Settings_FollowThreatScale>
  <AX_MAS_Settings_FollowThreatScale_Tip>开启：支援规模 = 当前袭击规模 × 百分比；关闭：使用固定系数。</AX_MAS_Settings_FollowThreatScale_Tip>
  <AX_MAS_Settings_AidPercent>支援规模百分比：{0}%</AX_MAS_Settings_AidPercent>
  <AX_MAS_Settings_CustomFactor>自定义固定系数：{0}</AX_MAS_Settings_CustomFactor>
  <AX_MAS_Settings_ScaleBelowOne>规模低于 100% 时也对称缩小</AX_MAS_Settings_ScaleBelowOne>
  <AX_MAS_Settings_ScaleBelowOne_Tip>关闭后支援只会比原版大、不会比原版小。</AX_MAS_Settings_ScaleBelowOne_Tip>
  <AX_MAS_Settings_DebugLogging>调试日志</AX_MAS_Settings_DebugLogging>
  <AX_MAS_Settings_DebugLogging_Tip>每次呼叫支援时在日志中打印缩放明细。</AX_MAS_Settings_DebugLogging_Tip>
  <AX_MAS_Settings_Preview>当前袭击规模：{0} → 本次支援有效规模：{1}</AX_MAS_Settings_Preview>
</LanguageData>
```

**说明**：Keyed 键名大小写敏感，C# 里 `"AX_MAS_Settings_Enabled".Translate()` 必须与 XML 完全一致。文件夹名 `ChineseSimplified (简体中文)` 带空格和括号——照你的现有工程原样即可，引擎支持。`{0}` 是占位符，由代码传参填充。

---

## ⑤ `Source/Local.props`

**直接复制你 milira-remielle 里那份，内容一字不改**（同一台机器，路径完全相同）：

```xml
<Project>
  <PropertyGroup>
    <RimWorldManagedDir>/Users/astralux/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Contents/Resources/Data/Managed</RimWorldManagedDir>
    <HarmonyAssembliesDir>/Users/astralux/Library/Application Support/Steam/steamapps/workshop/content/294100/2009463077/Current/Assemblies</HarmonyAssembliesDir>
    <FrameworkPathOverride Condition="Exists('/Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.8-api')">/Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.8-api</FrameworkPathOverride>
  </PropertyGroup>
</Project>
```

**说明**：`FrameworkPathOverride` 指向 Homebrew Mono 的 4.8-api 目录，这就是 macOS 上免装 Windows .NET Framework 编译 net48 的关键（你现有工程已验证此路可行）。注意此文件是机器相关的，若将来建 git 仓库应加进 `.gitignore`（你的 remielle 仓库就是这么处理的）。

---

## ⑥ `Source/MilitaryAidScaling.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <Import
    Project="Local.props"
    Condition="Exists('Local.props')" />

  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>

    <AssemblyName>MilitaryAidScaling</AssemblyName>
    <RootNamespace>MilitaryAidScaling</RootNamespace>

    <LangVersion>8.0</LangVersion>
    <Nullable>disable</Nullable>

    <OutputPath>../1.6/Assemblies/</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="Assembly-CSharp">
      <HintPath>$(RimWorldManagedDir)/Assembly-CSharp.dll</HintPath>
      <Private>false</Private>
    </Reference>

    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(RimWorldManagedDir)/UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>

    <Reference Include="0Harmony">
      <HintPath>$(HarmonyAssembliesDir)/0Harmony.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

</Project>
```

**说明**：与你 RemielleAsMilira.csproj 的三点差异——只引用 3 个 DLL（本 mod 不依赖任何库 mod）、程序集名不同、其余逐行同构。`Private=false` 确保游戏/Unity/Harmony 的 DLL **不会**被复制进 `Assemblies/`（这是 mod 发布红线）。`LangVersion 8.0` 意味着**不能用** C# 9+ 语法（没有 `is not`、没有目标类型 new、没有文件级命名空间）——下面给的代码已全部按 8.0 写好。

---

## ⑦ `Source/MilitaryAidScaling/Core/AX_MAS_Startup.cs`

```csharp
using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MilitaryAidScaling.Core
{
    [StaticConstructorOnStartup]
    public static class AX_MAS_Startup
    {
        static AX_MAS_Startup()
        {
            Harmony harmony = new Harmony("Astralux.MilitaryAidScaling");
            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
            {
                try
                {
                    if (type.GetCustomAttribute<HarmonyPatch>() != null)
                    {
                        harmony.CreateClassProcessor(type).Patch();
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"[Military Aid Scaling] Harmony patch failed: {type.Name}\n{e}");
                }
            }
            Log.Message("[Military Aid Scaling] Harmony patches applied successfully.");
        }
    }
}
```

**干什么用**：RimWorld **不会自动应用** `[HarmonyPatch]` 标注的类（反编译确认游戏代码里没有任何自动 PatchAll 逻辑），必须有这个引导类：`[StaticConstructorOnStartup]` 让游戏在启动后调用它的静态构造函数，再逐类型手动应用补丁。逐类型 try/catch 的好处是单个补丁失败不影响其他补丁、且日志能报出具体是哪个类失败——这是你 remielle 工程同款写法，直接沿用。

---

## ⑧ `Source/MilitaryAidScaling/Core/AX_MAS_Mod.cs`

```csharp
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
```

**逐段干什么**：

- `AX_MAS_ModSettings`：六个设置项及其存档读写（`Scribe_Values.Look` 的第三个参数是默认值，老存档缺字段时不炸）。字段命名与存档键同名，风格同你的 `AX_RaM_ModSettings`。
- `AX_MAS_Mod`：Mod 入口。游戏会自动实例化程序集里每个 `Mod` 子类，无需注册。`Instance`/`Settings` 单例 + `Enabled`/`DebugLogging` 静态便捷属性，补丁里直接 `AX_MAS_Mod.Enabled` 读取——同你 remielle 的 `AX_RaM_Mod.EnchainerEnabled` 模式。
- `EffectiveScale()`：**缩放公式的唯一实现**，设计文档 §4.1 的代码化。注意它只读 `Find.Storyteller.difficulty.threatScale` 一个字段——这是设计核心（DESIGN §2.2.1：三种规模来源全汇聚于此）。
- `CurrentEnemyPointsCeiling()`：**动态天花板镜像**，设计 §4.2 的代码化。`AccessTools.TypeByName` 找不到类型返回 null（mod 未启用时），整个方法永远安全返回一个数值。
- `DoSettingsWindowContents`：设置页 UI。滑条重载签名 `(Rect, float value, float min, float max, bool middleAlignment, string label, string leftLabel, string rightLabel)` 已对 1.6 反编译核实。

---

## ⑨ `Source/MilitaryAidScaling/AidScaling/IncidentWorker_TryExecute_ScaleAid_Patch.cs`

```csharp
using System;
using MilitaryAidScaling.Core;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MilitaryAidScaling.AidScaling
{
    // 唯一的核心补丁：挂在一切袭击类事件的公共入口 IncidentWorker.TryExecute，
    // 在事件执行前把"手动呼叫的友军支援"按 有效规模 缩放。
    //
    // 为什么选这个入口：
    //  - 原版三条呼叫路径（通讯台 FactionDialogMaker / 爵位许可 RoyalTitlePermitWorker_CallAid
    //    / 灵能 CompAbilityEffect_RandomAlliedMilitaryAid）最终都调用它；
    //  - 米莉安帝国等 mod 的自定义 worker（IncidentWorker_Raid_Ariandel 等继承
    //    IncidentWorker_Raid 的子类）也经过它——所以不需要任何针对性兼容代码；
    //  - Prefix 在事件执行前改 parms，后续人数生成/抵达方式/信件全部自然采用新值。
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
                //  - pawnKind != null：固定人数支援模式（帝国小队/米莉安），讲述者自发
                //    支援从不设置此字段。
                // 讲述者自发支援两者皆无——它已经过 DefaultThreatPointsNow 缩放，
                // 这里必须放行，否则会二次放大。
                bool manualCall = parms.raidArrivalModeForQuickMilitaryAid || parms.pawnKind != null;
                if (!manualCall)
                {
                    return;
                }

                float scale = AX_MAS_Mod.EffectiveScale();
                if (scale <= 0f)
                {
                    return;
                }

                if (parms.pawnKind != null)
                {
                    // pawnCount 模式：人数 × 有效规模。在上下取整之间随机，
                    // 保留原版支援"800~1000 点随机"的风味。不设上限——人数完全由
                    // 玩家的百分比掌控；下游 mod（Elite Raid 等）照常做它自己的事。
                    float scaled = parms.pawnCount * scale;
                    int lo = Mathf.Max(1, Mathf.CeilToInt(scaled));
                    int hi = Mathf.Max(lo, Mathf.FloorToInt(scaled));
                    int before = parms.pawnCount;
                    parms.pawnCount = Rand.RangeInclusive(lo, hi);
                    if (AX_MAS_Mod.DebugLogging)
                    {
                        Log.Message($"[Military Aid Scaling] {faction.Name} {parms.pawnKind.defName}: pawnCount {before} -> {parms.pawnCount} (scale {scale})");
                    }
                }
                else
                {
                    // points 模式：点数 × 有效规模，钳到敌方当前真实点数天花板，
                    // 保证支援不会超过敌方可能达到的最大规模（原版 10000；
                    // Elite Raid 在场时为其 maxRaidPoint，可被解锁到远超 10000）。
                    float ceiling = AX_MAS_Mod.CurrentEnemyPointsCeiling();
                    float before = parms.points;
                    parms.points = Mathf.Min(before * scale, ceiling);
                    if (AX_MAS_Mod.DebugLogging)
                    {
                        Log.Message($"[Military Aid Scaling] {faction.Name}: points {before} -> {parms.points} (scale {scale}, ceiling {ceiling})");
                    }
                }
            }
            catch (Exception e)
            {
                // 任何异常都不能影响原事件执行（支援必须照常到场）。
                Log.Error("[Military Aid Scaling] Failed to scale aid: " + e);
            }
        }
    }
}
```

**逐段干什么**：整段就是设计文档 §4.1 伪代码的成品化。六个守卫从宽到窄过滤（总开关 → 袭击家族 → 地图事件 → 友军 → 手动判别 → 规模有效），保证**零误伤**：讲述者自发支援、敌方袭击、其他正向事件全部原样放行。最后的 try/catch 是保险丝——就算未来某个 mod 传来了结构怪异的 parms，最坏结果也只是本次不缩放，支援照常出发。

---

## 编译

```bash
cd /Users/astralux/projs/rimmoddev-fair-friendly-support/military-aid-scaling/Source
dotnet build -c Release
```

预期产物：`../1.6/Assemblies/MilitaryAidScaling.dll`（且该目录下**只有**这一个文件——若出现 Assembly-CSharp.dll / 0Harmony.dll 说明 `Private=false` 漏写了）。

## 安装（开发期用符号链接，改完重编译即生效）

```bash
ln -s /Users/astralux/projs/rimmoddev-fair-friendly-support/military-aid-scaling \
      "/Users/astralux/Library/Application Support/Steam/steamapps/common/RimWorld/Mods/MilitaryAidScaling"
```

游戏内：模组列表勾选 Harmony（在前）+ Military Aid Scaling（任意其后位置）。

## 冒烟测试（开调试日志后）

1. **基础**：自定义难度 500% → 通讯台呼叫盟友支援 → 日志应出现 `points 8xx -> 4xxx (scale 5)`，到场支援明显变多；
2. **百分比**：百分比拉到 10% → 同样呼叫 → `scale 0.5`（500% × 10%），支援约为原版一半；
3. **pawnCount 模式**：帝国爵位"呼叫战士小队"或米莉安支援 → 日志 `pawnCount 8 -> 40`；米莉安带副组的许可应看到多条日志逐组缩放；
4. **不误伤**：等讲述者自发派盟友来帮忙（或观察敌袭）→ **不出现**本 mod 日志；
5. **动态天花板**：同开 Elite Raid 且其 maxRaidPoint 调高 → points 模式日志里 `ceiling` 应显示其值而非 10000；
6. **回归**：总开关关闭 → 一切与原版一致。

## 手敲坑清单（最容易翻车的七处）

1. **Prefix 的参数名必须叫 `parms`**——Harmony 按参数名匹配原方法，写成 `e`/`args` 会直接 patch 失败（启动日志报错）；
2. **必须有 ⑦ 引导类**——RimWorld 不自动 PatchAll，漏掉它补丁永远不生效且无任何报错（这是最隐蔽的坑）；
3. **LangVersion 8.0 语法边界**——`is not`、`x is null`、目标类型 new 都是 C# 9+，用了编不过；
4. **`Private=false` 三处都要写**——漏了会把游戏 DLL 拷进 mod，玩家加载即报错；
5. **Keyed 键名大小写敏感**——C# 字符串与 XML 标签必须逐字符一致；
6. **Local.props 别进版本库**——里面是你本机绝对路径；
7. **语言文件夹名**——`ChineseSimplified (简体中文)` 中的空格和全角括号照抄，少一个字符语言就 fallback 到英文。

---

## 与设计文档的对应关系

| 代码位置 | 设计文档 |
|---|---|
| `EffectiveScale()` | §4.1 缩放公式（v0.3 百分比方案） |
| `CurrentEnemyPointsCeiling()` | §4.2 动态天花板镜像（v0.6） |
| `manualCall` 判别 | §2.3 / §2.3.1 双判别器（米莉安覆盖） |
| 守卫链与 try/catch | §3 G3 不误伤、尊重原则 |
| ⑦ 引导类 | §6.0 层③之外的常规 C# 侧机制 |
| LoadFolders.xml | §6.0 条件加载工具箱（版本文件夹） |
