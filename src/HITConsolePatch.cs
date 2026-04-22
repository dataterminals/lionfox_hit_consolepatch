using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Common;

namespace lionfox_hit_consolepatch
{
    public class HITConsolePatchMod : ModSystem
    {
        const string HarmonyId = "lionfox.hit_consolepatch";
        Harmony? harmony;

        public override void Start(ICoreAPI api)
        {
            var hitAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "HIT");

            if (hitAssembly == null)
            {
                api.Logger.Warning("[lionfox_hit_consolepatch] HIT assembly not found — skipping patch.");
                return;
            }

            var watcherType = hitAssembly.GetType("Ele.HIT.PlayerToolWatcher");
            if (watcherType == null)
            {
                api.Logger.Warning("[lionfox_hit_consolepatch] Ele.HIT.PlayerToolWatcher not found — skipping patch.");
                return;
            }

            var method = watcherType.GetMethod(
                "UpdateInventory",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (method == null)
            {
                api.Logger.Warning("[lionfox_hit_consolepatch] UpdateInventory not found — skipping patch.");
                return;
            }

            harmony = new Harmony(HarmonyId);
            harmony.Patch(method, transpiler: new HarmonyMethod(typeof(HITConsolePatchMod), nameof(StripConsoleWriteLines)));
            api.Logger.Notification("[lionfox_hit_consolepatch] Patched HIT PlayerToolWatcher.UpdateInventory — console spam removed.");
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(HarmonyId);
        }

        // Replaces every Console.WriteLine(string) call in UpdateInventory — along
        // with the instructions that build its argument — with nops. Labels on
        // removed instructions are forwarded so branch targets stay intact.
        static IEnumerable<CodeInstruction> StripConsoleWriteLines(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var writeLine = typeof(Console).GetMethod("WriteLine", new[] { typeof(string) });
            int removed = 0;

            for (int i = codes.Count - 1; i >= 0; i--)
            {
                if (!codes[i].Calls(writeLine))
                    continue;

                // Walk backwards to find the first instruction of the argument expression.
                // depth = how many stack values still need their origin found.
                // Simple push opcodes (ldstr, ldloc*, ldarg*) decrease depth by 1.
                // Parameterless instance callvirts (e.g. ToString, get_Itemstack) consume
                // one value and produce one — net 0 — so depth is unchanged.
                int depth = 1;
                int start = i;
                while (start > 0 && depth > 0)
                {
                    start--;
                    var op = codes[start].opcode;
                    if (op == OpCodes.Ldstr  || op == OpCodes.Ldnull  ||
                        op == OpCodes.Ldloc  || op == OpCodes.Ldloc_0 ||
                        op == OpCodes.Ldloc_1|| op == OpCodes.Ldloc_2 ||
                        op == OpCodes.Ldloc_3|| op == OpCodes.Ldloc_S ||
                        op == OpCodes.Ldarg  || op == OpCodes.Ldarg_0 ||
                        op == OpCodes.Ldarg_1|| op == OpCodes.Ldarg_2 ||
                        op == OpCodes.Ldarg_3|| op == OpCodes.Ldarg_S)
                    {
                        depth--;
                    }
                }

                // Replace [start..i] with nops, preserving any branch labels.
                for (int j = start; j <= i; j++)
                {
                    var nop = new CodeInstruction(OpCodes.Nop);
                    nop.labels.AddRange(codes[j].labels);
                    codes[j] = nop;
                    removed++;
                }
            }

            return codes;
        }
    }
}
