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
        public static Dictionary<string, string> nameReplace = new Dictionary<string, string> // Used for dynamically generating KeyCode nicknames
        {
            ["Alpha"] = "",
            ["Keypad"] = "Num",
            ["Plus"] = "+",
            ["Minus"] = "-",
            ["Divide"] = "/",
            ["Multiply"] = "*",
            ["Period"] = ".",
            ["Equals"] = "=",
            ["Left"] = "Lf",
            ["Right"] = "Rt",
            ["Windows"] = "Win",
            ["Control"] = "Ctrl"
        };
        public static Dictionary<string, KeyCode> nickname = new Dictionary<string, KeyCode> // Stores a list of nicknames for the KeyCodes
        {
            [";"] = KeyCode.Semicolon,
            ["'"] = KeyCode.Quote,
            ["/"] = KeyCode.Slash,
            ["\\"] = KeyCode.Backslash,
            ["Ins"] = KeyCode.Insert,
            ["Del"] = KeyCode.Delete,
            ["PgDn"] = KeyCode.PageDown,
            ["PgUp"] = KeyCode.PageUp,
            ["`"] = KeyCode.BackQuote,
            ["Left"] = KeyCode.LeftArrow,
            ["Right"] = KeyCode.RightArrow,
            ["Up"] = KeyCode.UpArrow,
            ["Down"] = KeyCode.DownArrow,
            ["("] = KeyCode.LeftParen,
            ["["] = KeyCode.LeftBracket,
            ["{"] = KeyCode.LeftCurlyBracket,
            [")"] = KeyCode.RightParen,
            ["]"] = KeyCode.RightBracket,
            ["}"] = KeyCode.RightCurlyBracket
        };

        public override void PreLoad()
        {
            foreach (KeyCode k in System.Enum.GetValues(typeof(KeyCode)))
            {
                if (nickname.ContainsValue(k))
                    continue;
                bool flag = false;
                string name = k.ToString();
                foreach (var pair in nameReplace)
                    if (name.Contains(pair.Key))
                    {
                        name = name.Replace(pair.Key, pair.Value);
                        flag = true;
                    }
                if (flag)
                    nickname.Add(name, k);
            }
            HarmonyInstance.PatchAll();
            Console.RegisterCommand(new ChangeSlotsCommand());
            Console.RegisterCommand(new SetSlotBindCommand());
        }
        /*public override void PostLoad()
        {
            string str = "Key nicknames:";
            foreach (var name in nickname)
                str += "\n • KeyCode." + name.Value + " = \"" + name.Key + "\"";
            Log(str);
        }*/
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
        public static bool TryParseKeyCode(this string value, out KeyCode key) => Main.nickname.TryGetValue(value, out key) || KeyCode.TryParse(value, out key);
        public static string GetName(this KeyCode key)
        {
            foreach (var pair in Main.nickname)
                if (pair.Value == key)
                    return pair.Key;
            return key.ToString();
        }
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
                Main.Insert(ref ___lastSlotIds, ___lastSlotIds[0], 0);
            }
            Main.InsertRange(ref ___lastSlotCounts, new int[extraSlots], 0);
            Main.InsertRange(ref ___lastSlotMaxAmmos, new int[extraSlots], 0);
            var keyText = new List<XlateKeyText>();
            for (int i = 0; i < Main.defaultSlots; i++)
                keyText.Add(__instance.slots[i].keyBinding.GetComponentInChildren<XlateKeyText>());
            Main.InsertRange(ref __instance.slots, sU, 0);
            for (int i = 0; i < __instance.slots.Length; i++)
            {
                var key = __instance.slots[i].keyBinding.GetComponentInChildren<XlateKeyText>();
                if (i < keyText.Count)
                    CopySettings(key, keyText[i]);
                else if (Patch_XlateKeyText_OnKeysChanged.custom.ContainsKey(key))
                    Patch_XlateKeyText_OnKeysChanged.custom[key] = i;
                else
                    Patch_XlateKeyText_OnKeysChanged.custom.Add(key, i);
            }
            
        }
        public static void CopySettings(XlateKeyText target, XlateKeyText source)
        {
            target.key = source.key;
            target.inputKey = source.inputKey;
            target.bundlePath = source.bundlePath;
            Traverse.Create(target).Field("bundle").SetValue(Traverse.Create(target).Field("bundle").GetValue());
        }
    }

    [HarmonyPatch(typeof(XlateKeyText), "OnKeysChanged")]
    class Patch_XlateKeyText_OnKeysChanged
    {
        public static Dictionary<XlateKeyText, int> custom = new Dictionary<XlateKeyText, int>();
        public static bool Prefix(XlateKeyText __instance, Text ___text, TMPro.TMP_Text ___meshText)
        {
            if (!custom.ContainsKey(__instance))
                return true;
            if (___text)
                ___text.text = Config.GetCustomBind(custom[__instance]);
            if (___meshText)
                ___meshText.text = Config.GetCustomBind(custom[__instance]);
            return false;
        }
        public static void Update(int slot)
        {
            var text = Config.GetCustomBind(slot);
            foreach (var pair in custom)
                if (pair.Key && pair.Value == slot)
                {
                    var traverse = Traverse.Create(pair.Key);
                    var t = traverse.Field("text").GetValue<Text>();
                    if (t)
                        t.text = text;
                    var tt = traverse.Field("meshText").GetValue<TMPro.TMP_Text>();
                    if (tt)
                        tt.text = text;
                }
        }
    }

    [HarmonyPatch(typeof(WeaponVacuum), "UpdateSlotForInputs")]
    class Patch_WeaponVacuum_UpdateSlotForInputs
    {
        public static void Postfix(WeaponVacuum __instance, PlayerState ___player, Animator ___vacAnimator, int ___animSwitchSlotsId)
        {
            foreach (var pair in Config.GetBindData())
                if (Input.GetKeyDown(pair.Value) && ___player.Ammo.SetAmmoSlot(pair.Key))
                {
                    Traverse.Create(__instance).Method("PlayTransientAudio", __instance.vacAmmoSelectCue, false);
                    ___vacAnimator.SetTrigger(___animSwitchSlotsId);
                }
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

    class SetSlotBindCommand : ConsoleCommand
    {
        public override string Usage => "bindslot <slot index> [key]";
        public override string ID => "bindslot";
        public override string Description => "sets the selected vac slot";
        public override bool Execute(string[] args)
        {
            if (args.Length < 1)
            {
                Main.Log("Not enough arguments");
                return false;
            }
            if (!int.TryParse(args[0], out int v))
            {
                Main.LogError(args[0] + " failed to parse as a number");
                return true;
            }
            if (v < 0)
            {
                Main.LogError("Value cannot be less than 0");
                return true;
            }
            if (args.Length >= 2)
            {
                if (!args[1].TryParseKeyCode(out KeyCode k))
                {
                    Main.LogError(args[1] + " failed to parse as a key");
                    return true;
                }
                Config.SetBind(v, k);
                Traverse.Create<Console>().Field("commands").GetValue<Dictionary<string, ConsoleCommand>>().Values.DoIf(
                    (c) => c is SRML.Console.Commands.ConfigCommand,
                    (c) => c.Execute(new string[] {
                    "extravacslot",
                    "interal config",
                    "CONFIG",
                    "binds",
                    Config.binds
                    })
                );
                Patch_XlateKeyText_OnKeysChanged.Update(v);
                Main.LogSuccess("Bind has been updated");
            } else
                Main.Log("Bind for slot " + v + " is " + Config.GetCustomBind(v));
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
        public static string binds = "";
        public static void SetBind(int slot, KeyCode key)
        {
            if (key == KeyCode.None)
            {
                RemoveBind(slot);
                return;
            }
            var data = GetBindData();
            if (data.ContainsKey(slot))
                data[slot] = key;
            else
                data.Add(slot, key);
            SetBindData(data);
        }
        public static KeyCode GetBind(int slot)
        {
            var data = GetBindData();
            if (data.ContainsKey(slot))
                return data[slot];
            else
                return KeyCode.None;
        }
        public static void RemoveBind(int slot)
        {
            var data = GetBindData();
            if (data.ContainsKey(slot))
                data.Remove(slot);
            SetBindData(data);
        }
        public static Dictionary<int, KeyCode> GetBindData()
        {
            var d = new Dictionary<int, KeyCode>();
            if (binds == "")
                return d;
            var pairs = binds.Split(';');
            foreach (var pair in pairs)
            {
                if (pair == "")
                    continue;
                var data = pair.Split('=');
                d.Add(int.Parse(data[0]), (KeyCode)int.Parse(data[1]));
            }
            return d;
        }
        public static void SetBindData(Dictionary<int, KeyCode> dictionary)
        {
            binds = "";
            foreach (var pair in dictionary)
                binds += (binds == "" ? "" : ";") + pair.Key + "=" + (int)pair.Value;
        }
        public static string GetCustomBind(int slot)
        {
            var data = GetBindData();
            if (data.ContainsKey(slot))
                return data[slot].GetName();
            return "?";
        }
    }
}