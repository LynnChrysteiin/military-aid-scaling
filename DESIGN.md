# Military Aid Scaling（军事支援规模缩放）设计文档

> 目标游戏版本：RimWorld **1.6**（本地 Steam 安装，反编译验证）
> 类型：XML + C#（Harmony）
> 状态：设计稿，等待审阅后实施

---

## 0. 一句话定位

**让手动呼叫的友军支援规模等比匹配当前难度的袭击规模（threatScale），在超高袭击规模下可选压缩支援人数避免卡顿，并与 Elite Raid / Unlimited Threat Scale 等社区 mod 正确兼容。**

---

## 1. 社区调研结论（是否重复造轮子？）

### 1.1 最接近的竞品：[CNEIO] Just Help For Me（id 3584588840）

国人作者 CNEIO 出品，1.6 版本，约 5 万订阅，需 Harmony。它重做了一整套支援系统：

- 按**当前地图上的实时威胁**计算支援规模（且刻意设计为**永远略弱于**敌方威胁）；
- 新增"支援点数"全局货币系统、支援可解散、支援可指定落点、支援可跨图行军等大量新机制。

**与本 mod 的定位差异：**

| 维度 | Just Help For Me | 本 mod（Military Aid Scaling） |
|---|---|---|
| 缩放依据 | 地图实时威胁（永远弱于敌方） | **难度设置里的袭击规模 threatScale**（追求"公平"，与敌方同系数） |
| 系统侵入性 | 全新支援系统，重写呼叫流程 | **保留原版呼叫流程**，只改规模数值，行为不变 |
| 人数压缩 | 无（按威胁算出多少人就来多少人） | 可选的支援侧人数上限+补偿（防超高规模卡顿） |
| 超高袭击规模（>500%）兼容 | 未明确 | 核心设计目标（与 Unlimited Threat Scale 协同） |

**结论：有重叠但不重复。** "按难度袭击规模等比缩放原版支援"这个切入点目前是空白，且本 mod 走轻量路线（不改流程、只改数值），与它面向的人群不同。建议在创意工坊描述中注明与 Just Help For Me 的定位差异，二者不建议同时启用（都会放大支援规模，叠加会导致支援过量）。

### 1.2 压缩类：Elite Raid 精英化袭击（id 3492954974，本地已安装，已反编译其 DLL）

- 作者 trigger，自述约 30% 代码来自 Compressed Raids（ryouta 系，Continued 版 packageId `vk.compressedraid.latestcontinued`）；
- 机制：7 级精英 + 词条强化 + 承伤系数；`袭击人数 = 原人数 / 压缩率`（默认压缩率 3，最大袭击人数默认 20，超过 20 人才生效）；
- 关键代码事实（反编译确认）：
  - 压缩 hook 挂在 `PawnGroupMakerUtility.ChoosePawnGenOptionsByPoints` 的 Finalizer 和 `PawnGroupKindWorker` 上，即**人群生成层**，友军/敌军共用此路径；
  - `EliteRaidMod.AllowCompress(IncidentParms)`：敌对派系 → 恒压缩；**友军支援 → 仅当用户开启 `allowRaidFriendlyValue` 设置才压缩**；
  - `About.xml` 声明与 `unlimitedthreatscale.1trickpwnyta`（Unlimited Threat Scale）、`brok.nolimitraidpoint`、`vk.compressedraid.latestcontinued` **互不兼容**。

### 1.3 解锁上限类：Unlimited Threat Scale（id 2562732435）

- 作者 1trickPwnyta，解锁自定义难度中 500% 的威胁规模滑条上限（理论上无上限）；姊妹 mod Visible Raid Points（id 2562730174，本地已安装）显示袭击点数计算过程。
- 代码事实：`threatScale` 在游戏代码中就是一个 `float`，**没有任何代码层上限**，500%/1000% 纯粹是 UI 滑条限制；袭击点数计算中它直接作为乘数（见 §2.2）。
- 它与 Elite Raid 互不兼容是**双方 patch 冲突的自我声明**（都动袭击规模/人数相关逻辑），不是引擎限制。

### 1.4 总结论

用户设想的组合——"**支援按 threatScale 等比缩放 + 吃得下 >500% 的超高规模 + 支援侧人数压缩 + 与 Elite Raid 共存**"——目前没有现成 mod 覆盖，值得做。

---

## 2. 原版机制分析（本地 1.6 反编译，Assembly-CSharp.dll）

### 2.1 手动呼叫友军支援共四条路径，前三条为原版（点数/人数全部**固定值**）

| 路径 | 入口代码 | 点数来源 |
|---|---|---|
| 通讯台请求立即军事援助 | `RimWorld.FactionDialogMaker.CallForAid()`（私有静态，:499-510） | `DiplomacyTuning.RequestedMilitaryAidPointsRange` = **固定 800~1000 随机** |
| 帝国爵位许可（Royalty） | `RimWorld.RoyalTitlePermitWorker_CallAid.CallAid()`（:88-115） | `def.royalAid.points`（XML 固定值）；或 `pawnKindDef + pawnCount` 固定人数模式 |
| 灵能/能力随机盟军支援 | `RimWorld.CompAbilityEffect_RandomAlliedMilitaryAid.Apply()`（:10-24） | `Props.points`（XML 固定值） |
| **米莉安帝国**（Milira Imperium，社区 mod） | `AriandelLibrary.RoyalTitlePermitWorker_CallAid_Group.CallDefaultAid()/CallExtraAid()` | **全部 `pawnKindDef + pawnCount` 固定人数模式**（12 个战斗许可无一例外） |

前三者最终调用 `IncidentDefOf.RaidFriendly.Worker.TryExecute(parms)`；米莉安走自定义事件 `AL_Incident_RaidFriendly`（自定义 worker `IncidentWorker_Raid_Ariandel : IncidentWorker_Raid`，自定义策略"边缘跃迁-不撤退"），但**同样经过 `IncidentWorker.TryExecute` 这个家族公共入口**（详见 §2.5）。

### 2.1.1 两种生成模式的关键差异（对压缩设计至关重要）

`RaidStrategyWorker.SpawnThreats()`（:120-）：

- **pawnKind 模式**（`parms.pawnKind != null`）：直接生成 `parms.pawnCount` 个指定兵种，**完全不看 points**——原版帝国小队、米莉安支援均属此类；
- **points 模式**：走 `ChoosePawnGenOptionsByPoints()` 贪心循环（§2.4），通讯台/灵能/讲述者自发支援属此类。

### 2.2 敌方袭击的点数才吃袭击规模系数

`StorytellerUtility.DefaultThreatPointsNow()`（:131-188）：

```csharp
return Mathf.Clamp(num4 * num5 * Find.Storyteller.difficulty.threatScale
    * Find.Storyteller.def.pointsFactorFromDaysPassed.Evaluate(...),
    GlobalPointsMin(), 10000f);
```

- 财富/人口推导值 × **threatScale** × 时间因子，钳制在 [最小值, **10000**]；
- 讲述者**自发**的友军支援（`ResolveRaidPoints`：points≤0 时回退到 `DefaultThreatPointsNow`）因此**已经**随袭击规模缩放——**用户抱怨的"支援固定"只发生在手动呼叫路径**，这与用户的观察完全吻合。

#### 2.2.1 `difficulty.threatScale` 是"当前袭击规模"的唯一真值来源（v0.3 关键结论）

三个规模类 mod 对这个字段的处理方式（反编译确认）：

| 来源 | 行为 |
|---|---|
| 原版 UI | 自定义难度滑条写入（上限 500%，"极端难度"解锁 1000%） |
| Unlimited Threat Scale | 解除滑条 UI 上限，写入值本身仍是这个字段 |
| **Elite Raid** | 其"袭击倍率"滑条（50%~100000%）在**游戏加载、每个 StorytellerTick、mod 初始化**三个时机**强制覆写** `difficulty.threatScale`（`Game_RaidScale_Patch.cs:22-27`、`Storyteller_RaidScale_Patch.cs:11-16`、`EliteRaidMod.cs:237-240`）——这就是"原版叙事者设置它不用"的原因，也是它声明与 Unlimited Threat Scale 互斥的原因（两者争夺同一字段） |

**推论**：本 mod 的规模取值**只读 `Find.Storyteller.difficulty.threatScale` 一个字段**，即自动跟随原版滑条 / Unlimited Threat Scale / Elite Raid 三种来源的任意取值（含 >500%），无需感知任何 mod 是否在场。

### 2.3 关键判别标记（本设计的技术基石）

三处原版手动呼叫点都会设置：

```csharp
incidentParms.raidArrivalModeForQuickMilitaryAid = true;
```

全代码库反编译检索确认：**该标记仅在这三个手动呼叫点被赋值，其余全是读取**（用于选择快速空投舱抵达模式）。它等价于"这是一次手动呼叫的支援"的官方标记。

- 用它判别 → 一次性 hook 覆盖原版三条路径（含未来新增 XML 定义的许可/能力，只要走原版 worker）；
- 不带此标记的自发友军袭击已被 `DefaultThreatPointsNow` 缩放过，**不会**被二次放大。

### 2.3.1 第二判别标记：`parms.pawnKind != null`（覆盖米莉安等社区 mod）

米莉安帝国的支援**不设置** quickMilitaryAid 标记，但其固定人数模式必然设置 `parms.pawnKind`。反编译确认：讲述者自发友军袭击（`DefaultParmsNow` 新建 parms）**从不设置 pawnKind**，原版事件中该字段仅由"手动呼叫固定人数支援"路径赋值。因此 `pawnKind != null && 派系非敌对` 是第二个安全的"手动支援"判别器，与 quickMilitaryAid 标记取并集即可把米莉安（默认组 + extraGroups，各自独立 TryExecute）全部纳入缩放范围。

### 2.4 人数与点数的关系

1.6 中不存在旧的 `Difficulty.raidCombatPowerToPawnCountCurve`；点数→人数由 `PawnGroupMakerUtility.ChoosePawnGenOptionsByPoints()`（:225-）的贪心循环决定：用点数预算从派系 `pawnGroupMaker` 的选项里反复"购买"小人，直到预算耗尽。即**人数 ≈ points / 选项平均战力**。这既是超高 threatScale 下支援人数爆炸的原因（800~1000 点 × 10 倍规模 ≈ 万点级 ≈ 200+ 部落兵），也是压缩的人口。注意此循环仅作用于 points 模式；pawnKind 模式直接按 `pawnCount` 生成（§2.1.1）。

### 2.5 米莉安帝国（Milira Imperium）支援机制调查结论

本地安装版本反编译调查（用户明确要求兼容）：

- **不是继承原版 Royalty 支援**。其许可用自写 worker `AriandelLibrary.RoyalTitlePermitWorker_CallAid_Group`（继承链 `RoyalTitlePermitWorker_SilverCost → RoyalTitlePermitWorker_Targeted → RoyalTitlePermitWorker`），自实现了瞄准落点、白银/荣誉双货币扣费、冷却逻辑；
- 发射**自定义事件** `AL_Incident_RaidFriendly`（`workerClass = AriandelLibrary.IncidentWorker_Raid_Ariandel`，继承原版 `IncidentWorker_Raid`，category = AllyAssistance），使用自定义袭击策略 `AL_ImmediateAttack_EdgeWarp_NoRetreat`（边缘跃迁进场、不撤退）；
- 12 个战斗支援许可**全部**为 `pawnKindDef + pawnCount` 固定人数模式（如 `Milira_SpaceMarine×8`），另可通过 `AL_CallAid_Group_Extension.extraGroups` 追加副组（每个副组是**独立的一次 TryExecute 调用**，`sendLetter=false`）；
- **不设置** `raidArrivalModeForQuickMilitaryAid` 标记 → 仅靠标记判别会漏掉它；但它每次调用都经过 `IncidentWorker.TryExecute` 公共入口且 `parms.pawnKind != null`（§2.3.1 的第二判别器可覆盖）；
- pawnKind 模式**不看点数**（§2.1.1）→ Elite Raid 挂在 `ChoosePawnGenOptionsByPoints` 上的主压缩链**管不到**米莉安支援；但 Elite Raid 在 `RaidStrategyWorker.SpawnThreats` 上还有一层针对 pawnKind 模式的前缀补丁（§6.1）；
- **装备与 hediff 的生成语义**（回答"复制还是重随"）：pawnKind 模式下 `RaidStrategyWorker.SpawnThreats`（:120-）对**每个**名额单独构造 `PawnGenerationRequest` 并调用一次 `PawnGenerator.GeneratePawn`——即每个援军都是**同模板独立随机生成**（装备按兵种预算独立随机、技能/特质/年龄独立随机，生体锁等按概率独立判定），**不存在复制粘贴同一个体**；米莉安 worker 在 `PostProcessSpawnedPawns` 中也是**逐个** pawn 应用其扩展 hediff 列表。因此缩放 pawnCount 只是"多来几个各自随机的同兵种"，与原版大规模袭击的语义一致。

**结论：无需为米莉安写任何专项兼容代码**——把模块 A 的判别条件从"仅 quickMilitaryAid 标记"扩为"标记 ∥ pawnKind 模式"，米莉安支援（含每个 extraGroup）即自动按 `pawnCount × 有效规模` 缩放；其他基于 AriandelLibrary 或同模式的自定义派系 mod 也顺带全覆盖。

---

## 3. 设计目标与非目标

### 目标

1. **G1 等比缩放**：手动支援规模 × threatScale（100%~任意值，配合 Unlimited Threat Scale 无上限）——支援与敌方保持同一系数；
2. **G2 三路径全覆盖**：通讯台 / 爵位许可（points 与 pawnCount 两种模式）/ 灵能能力；
3. **G3 不误伤**：讲述者自发友军袭击、其他正向事件不受影响；
4. **G4 规模可控且尊重他 mod**：规模完全由百分比因子掌控，**无自研人数封顶**；points 模式天花板动态镜像"敌方当前真实点数上限"（原版 10000；Elite Raid 在场时为其 maxRaidPoint，含解锁上限场景，v0.6）；**对其他 mod 的友军处理只读镜像、零干预**（其"只压缩不强化"等行为视为刻意设计，v0.6）；强化相关完全甩手给同时启用的其他 mod（v0.4）；
5. **G5 兼容共存**：与 Elite Raid、Unlimited Threat Scale、Visible Raid Points 明确兼容或正确让位。

### 非目标（v1 不做）

- 不改呼叫流程（好感度消耗 -25、1 天冷却、对话选项均保持原版）；
- 不做 Just Help For Me 式的新系统（支援货币、指定落点、跨图行军）；
- 不做敌方袭击的缩放/压缩（那是 Unlimited Threat Scale 和 Elite Raid 的领域）；
- 不支持 1.5 及以下（本地只有 1.6 可验证；1.5 的 `FactionDialogUtility`→`FactionDialogMaker` 重构前后 hook 点不同，留待后续按需适配）。

---

## 4. 核心方案

### 4.1 模块 A：规模乘算（核心，一期必做，v0.3 按用户方案改为百分比乘算）

**不再做"缩放后钳制到绝对人数"，改为直接以当前袭击规模的百分比计算支援规模**：

```
[HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
Prefix(IncidentWorker __instance, IncidentParms parms)
  ├─ if (!(__instance is IncidentWorker_Raid)) return;               // 袭击家族（含 RaidFriendly / Raid_Ariandel 等自定义子类）
  ├─ if (parms.target is not Map) return;
  ├─ if (parms.faction == null || parms.faction == Faction.OfPlayer
  │      || parms.faction.HostileTo(Faction.OfPlayer)) return;        // 只处理盟友支援
  ├─ bool manual = parms.raidArrivalModeForQuickMilitaryAid          // 原版三路径标记
  │              || parms.pawnKind != null;                           // 固定人数支援（帝国小队/米莉安/extraGroups）
  ├─ if (!manual) return;                                             // 自发支援已含 threatScale，绝不二次缩放
  ├─ float scale = Find.Storyteller.difficulty.threatScale           // 唯一真值来源（§2.2.1），Elite Raid 在场时即其袭击倍率
  │              × Settings.aidPercent;                               // 支援规模百分比（默认 100%；例：500%×10%=50%）
  ├─ points 模式（parms.pawnKind == null）:
  │     parms.points = Min(parms.points * scale, CurrentEnemyPointsCeiling())
  │        // 动态镜像"敌方当前真实点数天花板"：Elite Raid 在场 → 其 maxRaidPoint
  │        //（它已用 Transpiler 把原版 10000 上限解锁为 1e7，再由该设置封顶）；
  │        // 不在场 → 原版 10000。只读不写，反射失败回退 10000（§4.2）
  └─ pawnCount 模式（parms.pawnKind != null）:
        parms.pawnCount = Max(1, Round(parms.pawnCount * scale)) // 无任何自研封顶，也不干预其他 mod 对它的事后处理
```

设计细节：

- patch 挂在**基类入口 `IncidentWorker.TryExecute`** 并以 `__instance is IncidentWorker_Raid` + 友军派系 + 手动标记过滤，而不是挂在 `TryExecuteWorker` 上——后者是虚方法继承，直接 patch 会命中基类实现体、波及全部袭击类事件；入口 `TryExecute` 为非虚公共方法，配合实例过滤最干净；
- 敌对袭击 worker（`IncidentWorker_RaidEnemy` 等）被"派系非敌对"条件天然排除；
- Prefix 在事件执行**前**改 `parms`，后续人数生成、抵达模式、信件文本全部自然采用新值，零额外改动；
- **百分比与 Elite Raid 的关系是"乘算"而非"两次门控取最小"**：Elite Raid 把它的袭击倍率写进 `threatScale`，我们在此基础上乘自己的百分比，一算到位，互不覆盖；Elite Raid 的 `maxRaidPoint` 只钳 `DefaultThreatPointsNow`（自发袭击/任务），碰不到手动支援的固定点数，不构成第二道门；
- points 模式天花板为**动态镜像**（非固定值、非用户旋钮）：始终等于"敌方袭击当前的真实点数天花板"——原版为 10000；Elite Raid 在场时其 Transpiler 已把原版常量解锁为 1e7、实际由其 `maxRaidPoint`（public static int，滑条 5000~400 万，默认 10000）封顶，故镜像该值（v0.6 依用户指正修订）。支援永不超过敌方可能达到的上限，公平对齐由此成立；pawnKind 模式无自研封顶；
- `minPoints`：threatScale < 100% 时默认**同样对称缩小**（公平原则的对称性），但提供设置"低于 100% 不缩小"；
- pawnCount 模式取整采用 `Rand.RangeInclusive(ceil, floor)` 保留原版随机风味；
- 米莉安 extraGroups 是多次独立 TryExecute，各自缩放后自然求和，无需聚合处理；
- 好感消耗与冷却不动（非目标），但在设置里留一个"高级选项：高规模支援提高好感消耗"的二期坑位。

### 4.2 天花板动态镜像与尊重原则（v0.6：撤协同预钳）

**设计原则（两条）**：

1. **规模完全由百分比因子掌控**（§4.1），不提供任何自研人数封顶——用户已能通过因子直接决定规模（用户决策，v0.5）；
2. **尊重其他 mod 的设计**（用户决策，v0.6）：本 mod 对其他 mod 只做**只读镜像**，不做任何行为改写——不预钳、不干预、不"修复"它们对友军支援的任何处理。Elite Raid 关闭友军压缩时对 pawnKind 支援"只截断不强化"，**视为其刻意设计，本 mod 尊重并照单全收**（v0.5 的协同预钳方案撤销）。

**唯一保留的自动化行为——points 天花板动态镜像**：呼叫时只读反射获取当前敌方真实点数天花板并用于 points 钳制：

- Elite Raid 激活 → 读取其 `public static int maxRaidPoint`（`EliteRaidMod.cs:49`；其 `StorytellerUtility_DefaultThreatPointsNow_Patch` Transpiler 已把原版 10000 常量替换为 1e7，随后由 `Patch_DefaultThreatPointsNow` Postfix 以 maxRaidPoint 封顶——敌方真实天花板即此值，可高达 400 万）；
- 未激活 / 反射失败（版本变动）→ 回退原版 10000；
- 该镜像的意义：支援点数与敌方"可能达到的最大规模"始终同顶，公平在任意 mod 组合下成立（含"解锁上限后支援也能跟着上万点"的场景，用户指正）。

Compressed Raid Continued 未在本地安装，不做专门适配（同为 L2 压缩类，镜像逻辑天然不冲突）。

### 4.3 设置（原生 ModSettings 设置页，二期可选接 MCM）

| 设置 | 默认 | 说明 |
|---|---|---|
| 启用缩放 | 开 | 总开关 |
| 缩放模式 | 跟随当前袭击规模 | 跟随 `difficulty.threatScale`（自动含 Elite Raid / Unlimited Threat Scale）/ 自定义固定系数 |
| 支援规模百分比 | 100% | **用户方案**：支援规模 = 当前袭击规模 × 此百分比（1%~500%；例：500%×10%=50%） |
| 低规模是否缩小 | 是 | threatScale<100% 时的对称行为 |

---

## 5. 工程结构

```
MilitaryAidScaling/
├── About/
│   ├── About.xml                # packageId: Astralux.MilitaryAidScaling
│   └── Preview.png              #（占位）
├── Languages/
│   ├── English/Keyed/Keys.xml
│   └── ChineseSimplified/Keyed/Keys.xml
├── Source/
│   ├── MilitaryAidScaling.csproj   # net472，引用本地 Managed DLL
│   ├── Mod.cs                        # Mod 入口 + ModSettings
│   └── Patch_ScaleAid.cs             # 唯一 patch：百分比缩放 + points 天花板动态镜像（无 hediff/无行为干预）
└── Assemblies/                  # 编译输出 MilitaryAidScaling.dll
```

（v0.4：移除 Defs/HediffDefs 与 DefOf——不做自研强化后，本 mod **零自定义 Def**，纯 Harmony 单 patch + 设置页。）

- **编译**：macOS + `dotnet build`（net472 目标 + 本地 `RimWorldMac.app/Contents/Resources/Data/Managed` 引用，技能已验证该路径存在；Harmony 引用创意工坊 `brrainz.harmony` 的 `0Harmony.dll`）；
- **加载顺序**：`loadAfter: brrainz.harmony`；`loadAfter: trigger.eliteRaid`、`unlimitedthreatscale.1trickpwnyta`（保证让位检测和设置读取顺序）；**不声明任何 incompatibleWith**；
- **依赖**：Harmony（必需）；其余全部软依赖。

---

## 6. 兼容性矩阵

### 6.0 兼容性的实现方式：机制层优先，而非逐 mod if（v0.6.1）

本 mod 的兼容性**不靠**在 C# 里挨个 `if (某个mod)`。实现分三层，逐 mod 代码只出现在最末层且仅一处：

| 层 | 做法 | 维护成本 |
|---|---|---|
| **① 机制层（零 mod 感知）** | 兼容性来自挂点选择——hook 全部选在**引擎的公共汇聚点**上：`IncidentWorker.TryExecute` 是一切袭击类事件（含任何 mod 自定义 worker）的必经入口；`difficulty.threatScale` 是一切规模类 mod（原版滑条/UTS/Elite Raid）的最终汇聚字段；`quickMilitaryAid 标记 ∥ pawnKind 模式` 是引擎自带的语义判别。**选对了缝，未知 mod 自动兼容**——米莉安全覆盖即是免费获得：全工程没有一行"if Milira" | 零：新 mod 无需适配即被覆盖 |
| **② 声明层（零 C#）** | 加载顺序/依赖/建议全部写在 `About.xml`（`loadAfter` 等），由游戏加载器执行 | 零：改 XML 即可 |
| **③ 点对点探测层（唯一一处）** | 仅当需要**读取**"当前 mod 环境的信息"而引擎无查询接口时才探测。当前只有 Elite Raid 的 `maxRaidPoint`（动态天花板镜像）。实现为**能力探测+回退**：`AccessTools.TypeByName("EliteRaid.EliteRaidMod")` 找不到（未装/改名/升级）→ 静默回退原版 10000，**永不构成硬依赖或崩溃点** | 极低：探测失败自动降级 |

与"逐 mod if 链"的本质区别：if 链是**白名单式行为分叉**（漏一个 mod 就行为错、版本一变就坏）；本设计是**机制式覆盖 + 可选信息探测**——行为分叉为零（尊重原则，§4.2），探测只用于读取环境参数且失败即回退。若未来出现第二个解锁点数上限的 mod（如 No Limit Raid Points），策略是**泛化探测优先**（按优先级探测已知改写者列表，仍全部带回退），而非为每个 mod 写行为分支。§9-4 若采纳"Just Help For Me 同开提示"，也只是一行 `ModLister.HasActiveModWithName(...)` 的**信息性**提示，不参与任何行为逻辑。

**引擎原生条件加载机制工具箱**（用户提示补充，已逐项代码/实例证实）——"挨个 if"之外，引擎提供四种声明式条件加载，按粒度选用：

| 机制 | 粒度 | 证据 | 本 mod 何时使用 |
|---|---|---|---|
| **版本文件夹**（`1.6/` 等，免声明自动加载） | 整文件夹 × 游戏版本 | `ModContentPack.InitLoadFolders`（:260-）：无 loadFolders 声明时自动加载版本号文件夹；Elite Raid / 米莉安均此结构 | **现在就用**：mod 内容放 `1.6/` 下 |
| **`<loadFolders>` + `IfModActive` / `IfModActiveAll` / `IfModNotActive`**（逗号列表，按 `<v1.6>` 分组，另有版本回退） | 整文件夹 × 指定 mod 是否激活 | `Verse/ModLoadFolders.cs:53-73`（1.6 原生支持三属性）；`InitLoadFolders` 优先按声明加载 | **现在不需要**（无条件内容）；将来若做"编译期引用 Elite Raid 类型的兼容补丁"，放入 `<li IfModActive="trigger.eliteRaid">Compat/EliteRaid</li>` 独立程序集——届时以声明式替代运行时探测 |
| **`MayRequire="<packageId>"`** | 单个 Def / XML 字段 | 引擎 `XmlInheritance` / `DirectXmlCrossRefLoader` 支持；米莉安 `1.6/Mods/<id>/` 的兼容 Def 全靠它（实测 `MayRequire="Ludeon.RimWorld.Odyssey"` 等） | **现在不需要**（零自定义 Def） |
| **`PatchOperationFindMod`** / C# `ModLister` 运行时守卫 | Patch 级 / 代码级 | 生态标准做法 | Patch：一期无 XML patch；C#：仅 `maxRaidPoint` 探测（层③） |

**当前结论**：本 mod 条件性内容为零（零 Def、零条件 Patch、单 DLL），四种机制里只用到"版本文件夹"；唯一跨 mod 代码（`maxRaidPoint` 只读探测）用运行时反射+回退比 loadFolders 更简单且更稳（探测自门控，不依赖 About 声明与额外构建产物）。**升级路径已定**：一旦未来兼容需要编译期依赖（直接 patch 对方方法/类型），即切换为 `IfModActive` 挂独立兼容程序集——声明式、零 if 链。

### 6.1 与 Elite Raid 的分层共存（用户重点关切）

Elite Raid 实际有**两层**压缩 hook（反编译确认）：

| 层 | 挂点 | 覆盖对象 | 友军门控 |
|---|---|---|---|
| L2 选项层 | `PawnGroupMakerUtility.ChoosePawnGenOptionsByPoints` Finalizer + `PawnGroupKindWorker.GeneratePawns` Transpiler | points 模式人群（通讯台/灵能/**手动+自发皆含**/普通敌袭） | `AllowCompress(PawnGroupMakerParms)`：非敌对派系需 `allowRaidFriendlyValue`（**默认开**，v0.5 修正：代码字段与存档默认均为 true） |
| L3 生成层 | `RaidStrategyWorker.SpawnThreats` Prefix | **pawnKind 模式人群**（帝国小队/米莉安支援/机械集群） | **人数截断无条件**（深挖定论，见下）；逐 pawn 精英强化有 `AllowCompress(Pawn)` 友军门控（需 `allowRaidFriendlyValue`） |

**L3 深挖定论（全 367 行控制流逐行确认）**：前缀入口条件依次为——玩家主地图、`modEnabled`、`parms.pawnKind != null`、`parms.faction != null`、`raidArrivalMode.Worker != null`、兵种允许（机械/虫群/实体开关）、`pawnCount > maxRaidEnemy`（默认 20）。通过后：

1. `parms.pawnCount = Min(maxRaidEnemy, pawnCount)` —— **截断无条件执行，全程无任何敌友判断、不看友军压缩开关**（`RaidStrategyWorkerPatch.cs:123-127`）；
2. 补丁自行重新生成名单：每个小人独立生成，`AllowCompress(Pawn)` 不通过（如友军 + 开关关闭）**只会跳过精英强化 hediff，小人仍被 `list.Add` 加入到场名单**（else 分支，:201-227）——友军不会被吞，正常到场；
3. `parms.raidArrivalMode.Worker.Arrive(list, parms); __result = list; return false` —— 补丁接管原版生成（:255-259 附近）。

**顺带发现 Elite Raid 该路径的三个自身瑕疵**（非本 mod 问题，作为实测预期记录）：① Path B 生成循环内每生成一人无条件发一条屏幕消息"捕获到当前生成的敌人阵营X"+日志（该版本 DLL 固有行为，我方支援超 20 人时会逐人刷屏，可当作截断生效的诊断标志）；② 循环内 `num2++` 误增目标计数，实际生成数 ≈ pawnCount − 初始目标数（压缩比设计值弱）；③ 构造生成请求时生体锁 apparel/weapons 概率字段写反。另：米莉安的 Milian 机械体小队受其"允许机械体"开关影响——该开关关闭时 `flag=false` 前缀提前放行原版，完全不截断。

**关于"Elite Raid 的友军压缩是否只管自发支援"（用户问题三）**：不是。Elite Raid 的判别依据是**派系敌对性**，它既不知道也不区分手动/自发——`allowRaidFriendlyValue` 开启时，手动呼叫的 points 模式支援（通讯台/灵能）与讲述者自发支援**同样会被它压缩**。

**友军压缩开/关两种配置下的完整图景（v0.5 修正默认值；v0.6 起本 mod 零干预）**：

- **开启（默认）**：敌友对称——我方 pawnKind 支援放大后超过其 20 阈值 → 被其截断**并**精英强化，与敌方同待遇；points 模式支援同样被其压缩+强化；
- **关闭（用户手动）**：points 模式支援它不碰（本 mod 缩放结果原样生效，仅受动态天花板镜像钳制）；pawnKind 模式支援的 L3 人数截断仍生效但无强化——**本 mod 视其为该 mod 的刻意设计，尊重不干预**（v0.6 撤销了 v0.5 的协同预钳方案），期望行为照 §6.1 定论描述呈现。

本 mod 的挂点**仅在 L0**（`IncidentWorker.TryExecute` 入口，百分比缩放 + points 天花板动态镜像；对 Elite Raid 仅只读其 `maxRaidPoint` 一个字段），与 Elite Raid 的 L2/L3 **天然错层**，且规模轴自动跟随（§2.2.1，其袭击倍率直接写入 `threatScale`）。

**执行顺序（回答"谁先谁后"）**：同一次支援呼叫中，先 L0（本 mod 把固定点数/人数放大到目标规模）→ 再 L2/L3（Elite Raid 按它的阈值与参数处理放大后的结果）。即"本 mod 先放大、Elite Raid 后压缩"是同一条流水线上的两站；Elite Raid 压缩的正是本 mod 放大后的数字。注意其压缩有触发阈值（生成人数超过约 20 才介入），低规模支援两者都不会动。

两者的分工与冲突处理：

- 敌方：Elite Raid 独占压缩+强化（它的参数），本 mod 不碰；
- 友方：本 mod 在 L0 完成缩放与 points 天花板镜像（**只管规模，不管强化、不干预他对友军的任何处理**——用户决策 v0.4/v0.6）；其后 Elite Raid 按其自身设置对支援做任何它设计好的事，本 mod 全程尊重。不存在双重强化问题（本 mod 无强化层）。

### 6.2 矩阵总表

| mod | 关系 | 依据/策略 |
|---|---|---|
| **Unlimited Threat Scale** | ✅ 协同 | 本 mod 只读 `difficulty.threatScale`（float 无上限），它负责解除 UI 上限制；二者不改同一方法 |
| **Visible Raid Points** | ✅ 无冲突 | 只读显示类；建议本 mod 在设置页自显"当前规模与预估支援点数" |
| **Elite Raid 精英化袭击** | ✅ 分层共存 | 见 §6.1；它压敌方用它的参数，本 mod 缩友军用本 mod 百分比；**规模轴自动跟随**（其袭击倍率直接写入 `difficulty.threatScale`，本 mod 零适配读取）；**注意它声明与 Unlimited Threat Scale 互斥，那是它俩之间的事，与本 mod 无关** |
| **Compressed Raid Continued** | ⚠️ 类似 Elite Raid | 同为 L2 压缩类；本 mod 错层设计同样成立，不再读取其内部设置，仅在描述中建议关注双重压缩 |
| **Milira Imperium / Ariandel Library** | ✅ 自动覆盖 | §2.5：pawnKind 判别器覆盖其全部 12 个支援许可（含 extraGroups）；其自定义 worker/策略/扣费流程零侵入 |
| **[CNEIO] Just Help For Me** | ⚠️ 功能叠加 | 两者都会放大支援；文档建议二选一；不做硬互斥（它重写了呼叫流程，其自建支援大概率不走本 mod 判别器，模块 A 对其无效而无害——实施时实测确认） |
| **Vanilla Royalty / 灵能** | ✅ | pawnCount/points 双模式专为其设计 |
| **讲述者自发友军袭击** | ✅ 不受影响 | 无 quickMilitaryAid 标记亦无 pawnKind，且已被原版 threatScale 缩放 |

---

## 7. 测试计划

1. **基础**：500% 规模新档，通讯台呼叫 → 支援人数/装备应明显随规模增长；25% 规模 → 相应缩小；
1b. **百分比**：500% 规模 + 百分比 10% → 支援规模应为 50% 档（点数模式点数减半、pawnCount 模式人数减半）；百分比 100% → 纯公平跟随；
1c. **Elite Raid 规模跟随**：同开 Elite Raid 并把其"袭击倍率"拉到自定义值（如 300%），通讯台/米莉安支援应按 300% × 百分比缩放（验证 `threatScale` 覆写链路）；
2. **四路径 + 米莉安**：帝国爵位"呼叫战士小队"（pawnCount 模式）与"呼叫佣兵"（points 模式）分别验证；灵能能力验证；**米莉安帝国各爵位支援（含带 extraGroups 的许可，如 ImperialGuard III = VarangianGuard×4 + OldHunter×4）在高规模下人数应 ×scale 且逐组生效，装备逐个体独立随机**；
3. **不误伤**：观察讲述者自发友军袭击点数（配合 Visible Raid Points）不被二次放大；敌方袭击规模不变；
4. **超高规模与动态天花板**：不开 Elite Raid 时 Unlimited Threat Scale 拉到 1000%/10000%，验证 points 模式钳在原版 10000；开 Elite Raid 且其 maxRaidPoint 调高（如 100 万）时，支援点数应可随之超过 10000（动态镜像生效）、pawnKind 模式人数完全跟随因子；
5. **无自研强化确认**：任何配置下支援单位不出现本 mod 添加的 hediff/属性改动（v0.4 决策）；
6. **Elite Raid 共存（本 mod 零干预验证）**：与 Elite Raid 同开 →
   - 其友军压缩**开启（默认）**：支援与敌方对称获得其压缩+精英强化；验证不崩溃、强化正常套在友军身上（本 mod 无强化层，无叠加问题）；
   - 其友军压缩**关闭**：本 mod 缩放结果原样交给其 L3 处理——人数被其截断且无强化，确认本 mod 未做任何预钳/干预（尊重其设计，v0.6），可观察到其逐人屏幕消息（其固有行为）；
   - 米莉安 Milian 机械体小队：其"允许机械体"开关两种状态下截断行为差异；
7. **回归**：不开本 mod 的任何功能（全默认关闭缩放）时行为与原版一致。

---

## 8. 路线图

- **一期**：模块 A（核心缩放）+ 设置页 + 双语 + 兼容让位检测 + 工程与发布脚本；
- **二期**：模块 B（人数压缩 + 精锐补偿 hediff）、MCM 接入、好感消耗随规模调整选项；
- **三期（视需求）**：1.5 适配（hook 点改名适配）、按派系差异系数（如部落支援打折）、支援装备质量随规模提升。

---

## 9. 风险与开放问题（需要拍板）

1. **命名（已定，v0.6.3）**：英文名 *Military Aid Scaling*、中文名 *军事支援规模缩放*、packageId `Astralux.MilitaryAidScaling`（对齐用户 PascalCase 包名习惯）——原名 Fair Friendly Support 因"实际对玩家净增强、名不副实"被用户否决；备选曾考虑 *Aid Scales With Raids*（更口语化）与 *Scaled Reinforcements*（更短），如需更换是全局搜索替换级别的小改动；
2. **低规模对称缩小**：threatScale<100% 时支援跟着变小，部分玩家可能只想"增强不减弱"——默认对称，设置可关，是否合适？
3. **模块拆分**：当前方案已极简（单 patch + 4 项设置），无分期必要——一期即全部；
4. **Just Help For Me 的关系**：只在描述中说明定位差异，还是加启动提示（检测到同时启用时警告）？

---

## 修订记录

- **v0.6.3**（2026-09-19）：**mod 更名**（用户决策）：弃用 Fair Friendly Support / 公平支援（实际对玩家净增强，"公平"名不副实），定名 **Military Aid Scaling / 军事支援规模缩放**，packageId `Astralux.MilitaryAidScaling`，类前缀 `AX_MAS_`；设计文档与实施指南已全局同步，G1 目标措辞由"公平缩放"改为"等比缩放"。
- **v0.6.2**（2026-09-19）：应用户提示补充 §6.0"引擎原生条件加载机制工具箱"——代码证实 1.6 的 `<loadFolders>` 支持 `IfModActive`/`IfModActiveAll`/`IfModNotActive`（`Verse/ModLoadFolders.cs`）与版本文件夹免声明自动加载（`ModContentPack.InitLoadFolders`）；确认米莉安兼容 Def 实际靠 `MayRequire` 门控。明确本 mod 当前仅用版本文件夹，`maxRaidPoint` 探测保持运行时反射（更简更稳），并定下"编译期依赖→IfModActive 独立兼容程序集"的升级路径。
- **v0.6.1**（2026-09-19）：应用户架构追问，新增 §6.0"兼容性的实现方式"——明确三层结构（机制层公共汇聚点零 mod 感知 / About.xml 声明层 / 点对点探测层仅 `maxRaidPoint` 一处且带回退），并给出未来新增解锁上限 mod 时的泛化探测策略。
- **v0.6**（2026-09-18）：根据用户第六轮指正修订——① **points 天花板改为动态镜像**：反编译证实 Elite Raid 用 Transpiler 把原版 `DefaultThreatPointsNow` 的 10000 上限常量改写为 1e7（`StorytellerUtility_DefaultThreatPointsNow_Patch.cs`），再由其 `maxRaidPoint`（滑条 5000~400 万）封顶——敌方真实天花板随其设置可远超 10000，支援钳制必须镜像该动态值而非硬编码 10000（§4.1/§4.2）；② **撤销 v0.5 协同预钳，确立尊重原则**：其他 mod（如 Elite Raid 关闭友军压缩时"只截断不强化"）的行为视为刻意设计，本 mod 对其只读镜像（仅 `maxRaidPoint` 一个字段）、零行为干预（§4.2/§6.1）；设置项精简为 4 项，§9 同步收敛。
- **v0.5**（2026-09-18）：根据用户第五轮追问修订——① **移除自研人数封顶（"兜底"概念删除）**：规模完全由百分比因子掌控，唯一数值钳为 points 模式镜像上限（公平对齐，非用户旋钮）（§3/§4.1/§4.2/§4.3）；② 提出协同预钳方案解决"绑死"问题（**v0.6 已撤销**）；③ **修正默认值认知**：Elite Raid 友军压缩默认为**开**（`EliteRaidSettings.cs:86` 存档默认 true），默认配置下敌友本就对称压缩+强化（§6.1）。
- **v0.4**（2026-09-18）：根据用户第四轮反馈修订——① **彻底移除自研强化/补偿**（"精锐先锋"hediff、L1 挂点、精锐补偿设置全部删除），强化完全甩手给同时启用的其他 mod，工程结构精简为零自定义 Def 的单 patch mod（§3/§4.2/§4.3/§5）；② **L3 人数截断深挖定论**：通读 Elite Raid `RaidStrategyWorkerPatch` 全部 367 行，确认 pawnKind 模式人数截断**无条件**（不看敌友、不看友军压缩开关）、友军小人不被丢弃（不强化也照样到场）、补丁以 `return false` 接管原版；顺带记录其三个自身瑕疵（逐人屏幕消息刷屏、num2 计数错误、生体锁概率写反）与 Milian 机械体受"允许机械体"开关影响的事实（§6.1）。
- **v0.3**（2026-09-18）：根据用户第三轮追问修订——① **规模公式改为百分比乘算**（用户方案）：支援规模 = 当前袭击规模 × 百分比；反编译确认 Elite Raid 的"袭击倍率"是**直接覆写 `difficulty.threatScale`**（`Game_RaidScale_Patch`/`Storyteller_RaidScale_Patch`），故只读该字段即自动跟随原版/UTS/Elite Raid 三种规模来源，与 Elite Raid 是乘算关系而非两次门控取 min（§2.2.1/§4.1）；② 模块 B 降级为纯性能兜底（§4.2）；③ 澄清 Elite Raid"友军压缩"按派系敌对性判别、不分手动/自发（§6.1）；④ 补充米莉安援军装备/hediff 逐个体独立随机生成的语义说明（§2.5）。
- **v0.2**（2026-09-18）：根据用户两个追问修订——① 模块 B 改为**本 mod 独立压缩参数**并与 Elite Raid 错层共存（§4.2/§6.1）；② 增加米莉安帝国支援机制调查（§2.5），模块 A 判别器扩展 `pawnKind 模式`，自动覆盖米莉安 12 个支援许可及 extraGroups（§2.3.1/§4.1）。
- **v0.1**（2026-09-18）：初稿。

---

## 附录：关键代码证据索引（本地反编译输出，/tmp/rwdecomp/）

**原版**（/tmp/rwdecomp/full/，Assembly-CSharp.dll @ 1.6）：

- `RimWorld/FactionDialogMaker.cs:499-510` — 通讯台 CallForAid：固定 800~1000 点 + quickMilitaryAid 标记；
- `RimWorld/RoyalTitlePermitWorker_CallAid.cs:88-115` — 爵位许可：royalAid.points / pawnKind+pawnCount 双模式 + 标记；
- `RimWorld/CompAbilityEffect_RandomAlliedMilitaryAid.cs:10-24` — 灵能能力：Props.points + 标记；
- `RimWorld/DiplomacyTuning.cs:144` — `RequestedMilitaryAidPointsRange = (800, 1000)`；
- `RimWorld/StorytellerUtility.cs:188` — 袭击点数 × threatScale，钳制 [min, 10000]；
- `RimWorld/IncidentWorker_RaidFriendly.cs:65-71` — 自发支援回退 DefaultThreatPointsNow（已含 threatScale）；
- `RimWorld/RaidStrategyWorker.cs:120-` — pawnKind 模式按 pawnCount 直生成、无视 points；
- `RimWorld/PawnGroupMakerUtility.cs:225-` — points 模式点数→人数的贪心生成循环。

**Elite Raid 精英化袭击**（/tmp/rwdecomp/eliteraid/，RimwoldEliteRaidProject.dll）：

- `EliteRaidMod.cs:127-186` — `AllowCompress(Pawn)`：非玩家派系 + 机械/虫群门控 + **非敌对需 allowRaidFriendlyValue**；
- `EliteRaidMod.cs:188-203` — `AllowCompress(IncidentParms)`：同上友军门控；
- `EliteRaidMod.cs:53/237-240/542-544` — `raidScale` 滑条（0.5~1000）在加载/tick/初始化时**覆写** `difficulty.threatScale`；
- `PawnGroupMakerUtility_Patch.cs` — L2 挂点：ChoosePawnGenOptionsByPoints Finalizer；
- `RaidStrategyWorkerPatch.cs`（全 367 行通读）— L3 挂点：`SpawnThreats` Prefix；**:123-127 人数截断无条件**（入口条件后直接 `pawnCount = Min(maxRaidEnemy, pawnCount)`，无敌友判断）；**:201-227** 生成循环中 `AllowCompress(Pawn)` 不通过的小人仍 `list.Add` 到场（else 分支）；**:255-259** `Arrive(list, parms); __result = list; ... return false` 接管原版；另见三个自身瑕疵（逐人 `Messages.Message` 刷屏、循环内 `num2++` 误增目标计数、生体锁概率字段互换）；
- `Game_RaidScale_Patch.cs:22-27` / `Storyteller_RaidScale_Patch.cs:11-16` — threatScale 覆写的两个时机；
- `StorytellerUtility_DefaultThreatPointsNow_Patch.cs` — **Transpiler 把原版 10000 点上限常量替换为 1e7（解锁上限的直接证据）**；
- `Patch_DefaultThreatPointsNow.cs` — Postfix 以 `maxRaidPoint` 封顶（敌方真实天花板）；`EliteRaidMod.cs:49` `public static int maxRaidPoint = 10000`、`:539-541` 滑条范围 5000~4,000,000；
- `About.xml` — 与 Unlimited Threat Scale / No Limit Raid Points / Compressed Raid Continued 互斥声明。

**Milira Imperium / Ariandel Library**（/tmp/rwdecomp/ariandel/，AriandelLibrary.dll + 模组 XML）：

- `AriandelLibrary/RoyalTitlePermitWorker_CallAid_Group.cs:165-215` — CallDefaultAid：新建 IncidentParms（pawnKind+pawnCount 或 royalAid.points）、**无 quickMilitaryAid 标记**、raidStrategy=AL_ImmediateAttack_EdgeWarp_NoRetreat、调用 AL_Incident_RaidFriendly.Worker.TryExecute；
- 同文件 `:217-260` — CallExtraAid：extraGroups 每组独立 TryExecute（sendLetter=false）；
- `Defs/IncidentDefs/IncidentDef_Map.xml:6-14` — AL_Incident_RaidFriendly：workerClass=AriandelLibrary.IncidentWorker_Raid_Ariandel，category=AllyAssistance；
- `AriandelLibrary/IncidentWorker_Raid_Ariandel.cs` — 继承原版 IncidentWorker_Raid；PostProcessSpawnedPawns 先调 base 再挂扩展 hediff（本 mod 补偿挂点天然可达）；
- `Milira Imperium /1.6/Defs/RoyalTitles/RoyalPermits_MiliraImperium.xml` — 12 个战斗支援许可全部 pawnKindDef+pawnCount 模式。
