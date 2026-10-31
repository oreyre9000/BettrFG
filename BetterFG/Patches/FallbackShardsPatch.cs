/*
using System;
using FallGuys.Player.Protocol.Client.Wallet;
using FGClient;
using FGClient.CatapultServices;

namespace BetterFG.Patches
{
    internal static class FallbackShardsPatch
    {
        internal static void OnRewardScreenPrepared(RewardScreenViewModel vm)
        {
            if (!vm._seasonSequence.ShouldShowMaxFameReached) return;

            var before = vm.PreviousServicesDataSnapshot?.Wallet;
            if (before == null) { Plugin.Log.LogWarning("fallback shards: no pre-show wallet snapshot, nothing to diff against"); return; }
            var dto = vm.CompletedEpisodeDto;
            var cur = vm._currencySequence;
            int rate = cur.ShardsToCrownsConversionRate;
            int items = CompleteEpisodeDtoExtensions.GetCrownsShardsEarnedQuantity(dto);
            int expected = before.CrownShards + rate * before.Crowns + items
                + rate * CompleteEpisodeDtoExtensions.GetCrownsEarnedFromEpisodeQuantity(dto);

            UniRx.ObservableExtensions.Subscribe(CatapultServices.Instance.PlayerWalletService.TryCast<PlayerWalletService>().GetWallet(),
                new Action<Catapult.Protocol.Result<WalletDto>>(r =>
                {
                    if (!r.IsSuccess) { Plugin.Log.LogWarning("fallback shards: wallet fetch came back failed, showing nothing"); return; }
                    int extra = r.Body.CrownShards + rate * r.Body.Crowns - expected;
                    Plugin.Log.LogInfo($"fallback shards {extra} (wallet {r.Body.CrownShards}/{r.Body.Crowns}, before {before.CrownShards}/{before.Crowns}, show items {items})");
                    if (extra <= 0 || vm == null) return;
                    cur._estimatedCrownShardsEarned += extra;
                    vm._crownShardsSequence._estimatedCrownShardsEarned += extra;
                    if (cur._crownShardsEarned > 0) cur.CrownShardsEarned = cur._estimatedCrownShardsEarned;
                }),
                new Action<Il2CppSystem.Exception>(e => Plugin.Log.LogWarning($"fallback shards: wallet fetch died, {e.Message}")),
                new Action(() => { }));
        }
    }
}
*/
