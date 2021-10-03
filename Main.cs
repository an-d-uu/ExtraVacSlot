using HarmonyLib;
using SRML;
using SRML.Console;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using MonomiPark.SlimeRancher.DataModel;
using MonomiPark.SlimeRancher.Persist;
using SRML.Config.Attributes;

namespace ExtraVacSlot
{
    public class Main : ModEntryPoint
    {
        internal static Assembly modAssembly = Assembly.GetExecutingAssembly();
        internal static string modName = $"{modAssembly.GetName().Name}";
        internal static string modDir = $"{System.Environment.CurrentDirectory}\\SRML\\Mods\\{modName}";
        public static readonly int defaultSlots = Traverse.Create<AmmoSlotUI>().Field("MAX_SLOTS").GetValue<int>();

        public override void PreLoad()
        {
            HarmonyInstance.PatchAll();
            Console.RegisterCommand(new ChangeSlotsCommand());
            Console.RegisterCommand(new SelectSlotCommand());
        }
        public static void Log(string message) => Console.Log($"[{modName}]: " + message);
        public static void LogError(string message) => Console.LogError($"[{modName}]: " + message);
        public static void LogWarning(string message) => Console.LogWarning($"[{modName}]: " + message);
        public static void LogSuccess(string message) => Console.LogSuccess($"[{modName}]: " + message);
        public static void Insert<T>(ref T[] array, T value, int index)
        {
            var list = array.ToList();
            list.Insert(index, value);
            array = list.ToArray();
        }
        public static void InsertRange<T>(ref T[] array, T[] value, int index)
        {
            var list = array.ToList();
            list.InsertRange(index, value);
            array = list.ToArray();
        }
    }

    static class ExtentionMethods {
        public static Ammo.Slot Clone(this Ammo.Slot slot) => new Ammo.Slot(slot.id, slot.count) { emotions = slot.emotions };
        public static AmmoDataV02 Clone(this AmmoDataV02 ammo) => new AmmoDataV02() { count = ammo.count, id = ammo.id, emotionData = ammo.emotionData };
        public static List<T> ToList<T>(this T[] array) => new List<T>(array);
    }

    [HarmonyPatch(typeof(PlayerState), "Reset")]
    class Patch_PlayerState_Reset
    {
        public static bool called = false;
        public static void Prefix() => called = true;
    }

    [HarmonyPatch(typeof(Ammo), MethodType.Constructor, new System.Type[] { typeof(HashSet<Identifiable.Id>), typeof(int), typeof(int), typeof(System.Predicate<Identifiable.Id>[]), typeof(System.Func<Identifiable.Id, int, int>) })]
    class Patch_Ammo_ctor
    {
        public static void Prefix(ref int numSlots, ref int usableSlots, ref System.Predicate<Identifiable.Id>[] slotPreds)
        {
            if (Patch_PlayerState_Reset.called)
            {
                Patch_PlayerState_Reset.called = false;
                for (int i = 0; i < Config.slots - numSlots; i++)
                {
                    Main.Insert(ref slotPreds, slotPreds[0], 0);
                };
                usableSlots += Config.slots - numSlots;
                numSlots = Config.slots;
                Main.Log("Slot count changed");
            }
        }
    }

    [HarmonyPatch(typeof(MonomiPark.SlimeRancher.SavedGame), "AmmoDataToSlots", new System.Type[] {typeof(Dictionary<PlayerState.AmmoMode, List<AmmoDataV02>>) })]
    class Patch_SavedGame_AmmoDataToSlots
    {
        public static void Prefix(Dictionary<PlayerState.AmmoMode, List<AmmoDataV02>> ammo)
        {
            var data = ammo[PlayerState.AmmoMode.DEFAULT];
            for (int i = data.Count; i > Config.slots; i--)
                data.RemoveAt(Config.slots - 1);
            for (int i = data.Count; i < Config.slots; i++)
                data.Insert(data.Count - 1, new AmmoDataV02() { id = Identifiable.Id.NONE, count = 0, emotionData = new SlimeEmotionDataV02() });
        }
    }

    [HarmonyPatch(typeof(PlayerModel), "ApplyUpgrade")]
    class Patch_PlayerModel_ApplyUpgrade
    {
        public static bool Prefix(PlayerModel __instance, PlayerState.Upgrade upgrade)
        {
            if (upgrade == PlayerState.Upgrade.LIQUID_SLOT)
            {
                var ammo = __instance.ammoDict[PlayerState.AmmoMode.DEFAULT];
                ammo.IncreaseUsableSlots(ammo.slots.Length);
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(AmmoSlotUI), "Awake")]
    class Patch_AmmoSlotUI_Awake
    {
        public static void Prefix(AmmoSlotUI __instance, ref int[] ___lastSlotCounts, ref int[] ___lastSlotMaxAmmos, ref Identifiable.Id[] ___lastSlotIds)
        {
            var extraSlots = Config.slots - ___lastSlotCounts.Length;
            var sU = new AmmoSlotUI.Slot[extraSlots];
            for (int i = extraSlots - 1; i >= 0; i--)
            {
                var newSlot = GameObject.Instantiate(__instance.transform.Find("Ammo Slot 1").gameObject, __instance.transform, false);
                newSlot.name = "Ammo Slot ?";
                newSlot.transform.SetSiblingIndex(0);
                sU[i] =  new AmmoSlotUI.Slot()
                {
                    anim = newSlot.GetComponent<Animator>(),
                    back = newSlot.transform.Find("Ammo Slot").Find("Behind").GetComponent<Image>(),
                    bar = newSlot.transform.Find("Ammo Slot").GetComponent<StatusBar>(),
                    front = newSlot.transform.Find("Ammo Slot").Find("Frame").GetComponent<Image>(),
                    icon = newSlot.transform.Find("Icon").GetComponent<Image>(),
                    keyBinding = newSlot.transform.Find("Keybinding").gameObject,
                    label = newSlot.transform.Find("Label").GetComponent<TMPro.TMP_Text>()
                };
                Patch_XlateKeyText_OnKeysChanged.custom.Add(sU[i].keyBinding.GetComponentInChildren<XlateKeyText>());
                Main.Insert(ref ___lastSlotIds, ___lastSlotIds[0], 0);
            }
            Main.InsertRange(ref ___lastSlotCounts, new int[extraSlots], 0);
            Main.InsertRange(ref ___lastSlotMaxAmmos, new int[extraSlots], 0);
            Main.InsertRange(ref __instance.slots, sU, 0);
        }
    }

    [HarmonyPatch(typeof(XlateKeyText), "OnKeysChanged")]
    class Patch_XlateKeyText_OnKeysChanged
    {
        public static List<XlateKeyText> custom = new List<XlateKeyText>();
        public static bool Prefix(XlateKeyText __instance, Text ___text, TMPro.TMP_Text ___meshText)
        {
            if (!custom.Contains(__instance))
                return true;
            if (___text)
                ___text.text = "?";
            if (___meshText)
                ___meshText.text = "?";
            return false;
        }
    }

    [HarmonyPatch(typeof(WeaponVacuum), "UpdateSlotForInputs")]
    class Patch_WeaponVacuum_UpdateSlotForInputs
    {
        public static bool calling = false;
        public static void Prefix() => calling = true;
        public static void Postfix() => calling = false;
    }

    [HarmonyPatch(typeof(Ammo), "SetAmmoSlot")]
    class Patch_Ammo_SetAmmoSlot
    {
        public static void Prefix(ref int idx)
        {
            if (Patch_WeaponVacuum_UpdateSlotForInputs.calling)
                idx += Config.slots - Main.defaultSlots;
        }
    }

    class ChangeSlotsCommand : ConsoleCommand
    {
        public override string Usage => "extraslots [count]";
        public override string ID => "extraslots";
        public override string Description => "gets or sets the number of extra slots";
        public override bool Execute(string[] args)
        {
            if (args.Length < 1)
            {
                Main.Log("Slot count is " + Config.slots + " (" + Main.defaultSlots + " default + " + Config.extraSlots + " extra)" );
                return true;
            }
            if (!Levels.isMainMenu())
            {
                Main.LogError("This command can only be used on the main menu");
                return true;
            }
            if (!int.TryParse(args[0], out int v))
            {
                Main.LogError(args[0] + " failed to parse as a number");
                return false;
            }
            if (v < 0)
            {
                Main.LogError("Value cannot be less than 0");
                return true;
            }
            Traverse.Create<Console>().Field("commands").GetValue<Dictionary<string, ConsoleCommand>>().Values.DoIf(
                (c) => c is SRML.Console.Commands.ConfigCommand,
                (c) => c.Execute(new string[] {
                    "extravacslot",
                    "interal config",
                    "CONFIG",
                    "extraSlots",
                    v.ToString()
                })
            );
            Main.LogSuccess("Slot count has been changed to " + Config.slots);
            return true;
        }
    }

    class SelectSlotCommand : ConsoleCommand
    {
        public override string Usage => "selectslot <slot index>";
        public override string ID => "selectslot";
        public override string Description => "sets the selected vac slot";
        public override bool Execute(string[] args)
        {
            if (!SRSingleton<SceneContext>.Instance || !SRSingleton<SceneContext>.Instance.PlayerState)
            {
                Main.LogError("No player found");
                return true;
            }
            if (args.Length < 1)
            {
                Main.Log("Not enough arguments");
                return false;
            }
            if (!int.TryParse(args[0], out int v))
            {
                Main.LogError(args[0] + " failed to parse as a number");
                return false;
            }
            if (v < 0)
            {
                Main.LogError("Value cannot be less than 0");
                return true;
            }
            if (v >= Config.slots)
            {
                Main.LogError("Value cannot be more than the highest slot index");
                return true;
            }
            var ammo = SRSingleton<SceneContext>.Instance.PlayerState.Ammo;
            var vac = SRSingleton<SceneContext>.Instance.Player.GetComponentInChildren<WeaponVacuum>();
            var tVac = Traverse.Create(vac);
            if (ammo.SetAmmoSlot(v)) {
                tVac.Method("PlayTransientAudio", vac.vacAmmoSelectCue, false);
                tVac.Field("vacAnimator").GetValue<Animator>().SetTrigger(tVac.Field("animSwitchSlotsId").GetValue<int>());
            }
            return true;
        }
    }

    [ConfigFile("interal config")]
    public static class Config
    {
        static Config()
        {
        }
        public static int extraSlots = 3;
        public static int slots => Main.defaultSlots + extraSlots;
    }
}