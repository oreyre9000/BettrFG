using System.Linq;
using System.Reflection;
using System.Threading;
using BetterFG.Tweaks;
using HarmonyLib;
using Hazel;
using Mediatonic.Networking;

namespace BetterFG.Patches
{
    // patches the Hazel transport's send + receive entry points to tally bytes/sec for the
    [Utilities.BfgPatchGate("tweak.show_server_info", true, roundOnly: true)]
    [HarmonyPatch]
    internal static class NetByteCounterSendPatch
    {
        [HarmonyTargetMethod]
        public static MethodBase Find()
        {
            // HazelNetworkTransport.Send(int hostId, int connId, int channelId, byte[] buffer, int size, out byte error)
            return typeof(HazelNetworkTransport).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .First(m => m.Name == "Send" && m.GetParameters().Length == 6);
        }

        [HarmonyPostfix]
        public static void Postfix(int size)
        {
            if (size > 0) Interlocked.Add(ref ShowServerInfoTweak.BytesOut, size);
        }
    }

    [Utilities.BfgPatchGate("tweak.show_server_info", true, roundOnly: true)]
    [HarmonyPatch(typeof(HazelNetworkTransport), "OnDataReceived", new[] { typeof(DataReceivedEventArgs) })]
    internal static class NetByteCounterRecvPatch
    {
        [HarmonyPostfix]
        public static void Postfix(DataReceivedEventArgs eventArgs)
        {
            var msg = eventArgs?.Message;
            if (msg != null)
            {
                int len = msg.Length;
                if (len > 0) Interlocked.Add(ref ShowServerInfoTweak.BytesIn, len);
            }
        }
    }
}
