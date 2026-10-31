using System.Collections.Generic;
using Events;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace BetterFG.Utilities
{
    internal static class GameSoundBanks
    {
        internal static List<string> For(IEnumerable<string> eventKeys)
        {
            var banks = new List<string>();
            foreach (var key in eventKeys)
            {
                var needed = AudioManager.FMODData.GetEventBanks(key);
                if (needed == null || needed.BankNames == null)
                {
                    Plugin.Log.LogWarning($"nothing lists a bank for {key}, it might stay silent");
                    continue;
                }
                foreach (var name in needed.BankNames)
                    if (!string.IsNullOrEmpty(name) && !banks.Contains(name)) banks.Add(name);
            }
            return banks;
        }

        internal static void Load(List<string> banks) => Send(SoundBankMod.Load, banks);

        internal static void Unload(List<string> banks) => Send(SoundBankMod.Unload, banks);

        static void Send(SoundBankMod mod, List<string> banks)
        {
            if (banks.Count == 0) return;
            var names = new Il2CppStringArray(banks.Count);
            for (int i = 0; i < banks.Count; i++) names[i] = banks[i];
            Broadcaster.Instance.Broadcast(new ModifySoundBanksEvent(mod, names, false));
        }
    }
}
