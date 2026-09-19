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
                    Log.Error(
                        $"[Military Aid Scaling] Harmony patch 应用失败: {type.Name} | Harmony patch failed to apply: {type.Name}: {e}"
                        );
                }
            }
            Log.Message(
                "[Military Aid Scaling] 所有 Harmony patch 成功应用 | All Harmony patches applied successfully"
            );
        }
    }
}