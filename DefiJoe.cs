using System.Numerics;
using Nethereum.Web3;

namespace z3nSafe;

public static class DefiJoe
{
    public const string Staking = "0x43646a8e839b2f2766392c1bf8f60f6e587b6960";
    public const string Joe = "0x371c7ec6d8039ff7933a2aa28eb827ffe1f52f07";
    public const string Reward = "0xff970a61a04b1ca14834a43f5de4533ebddb5cc8";
    private const string Abi = "[{\"type\":\"function\",\"name\":\"joe\",\"inputs\":[],\"outputs\":[{\"type\":\"address\"}],\"stateMutability\":\"view\"},{\"type\":\"function\",\"name\":\"getUserInfo\",\"inputs\":[{\"type\":\"address\"},{\"type\":\"address\"}],\"outputs\":[{\"type\":\"uint256\"},{\"type\":\"uint256\"}],\"stateMutability\":\"view\"},{\"type\":\"function\",\"name\":\"withdraw\",\"inputs\":[{\"type\":\"uint256\"}],\"outputs\":[],\"stateMutability\":\"nonpayable\"}]";
    public static bool IsSupported(DefiPosition p) => p.Protocol == "LFJ" && Rpc.Normalize(p.Chain) == "arbitrum" &&
        string.Equals(p.VaultAddress, Staking, StringComparison.OrdinalIgnoreCase) &&
        (p.Type == "staked" && string.Equals(p.AssetAddress, Joe, StringComparison.OrdinalIgnoreCase) ||
            p.Type == "reward" && string.Equals(p.AssetAddress, Reward, StringComparison.OrdinalIgnoreCase));
    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gasPercent,
        string? exactAmount = null, HttpClient? priceClient = null)
    {
        if (!IsSupported(p) || chainId != 42161 || (await SwapExecution.Read(web3.Eth.ChainId.SendRequestAsync())).Value != 42161)
            throw new InvalidOperationException("LFJ staking position or RPC chain does not match");
        var contract = web3.Eth.GetContract(Abi, Staking);
        var joe = await SwapExecution.Read(contract.GetFunction("joe").CallAsync<string>());
        if (!string.Equals(joe, Joe, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("LFJ staking underlying token does not match JOE");
        if (p.Type == "reward")
        {
            // StableJoeStaking.withdraw(0) harvests rewards without reducing the JOE stake.
            var rewards = web3.Eth.GetContract(DefiBlackwing.Abi(("isRewardToken", ["address"], ["bool"]),
                ("pendingReward", ["address", "address"], ["uint256"])), Staking);
            if (!await SwapExecution.Read(rewards.GetFunction("isRewardToken").CallAsync<bool>(Reward)))
                throw new InvalidOperationException("LFJ reward token is no longer enabled");
            var pending = await SwapExecution.Read(rewards.GetFunction("pendingReward").CallAsync<BigInteger>(p.Wallet, Reward));
            var token = web3.Eth.GetContract(DefiBlackwing.Abi(("balanceOf", ["address"], ["uint256"])), Reward);
            var funded = await SwapExecution.Read(token.GetFunction("balanceOf").CallAsync<BigInteger>(Staking));
            var output = exactAmount == null ? pending : BigInteger.Parse(exactAmount);
            if (output <= 0 || output > pending || output > funded) throw new InvalidOperationException("LFJ reward is empty, decreased or not funded");
            return await DefiVault.PrepareTransaction(web3, chainId, Reward, output,
                contract.GetFunction("withdraw").CreateTransactionInput(p.Wallet, BigInteger.Zero), gasPercent, priceClient);
        }
        var userCall = contract.GetFunction("getUserInfo").CreateCallInput(p.Wallet, Joe); userCall.From = p.Wallet;
        var info = await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(userCall));
        var available = DefiLending.Word(info, 0);
        var amount = exactAmount == null ? available : BigInteger.Parse(exactAmount);
        if (amount <= 0 || available < amount) throw new InvalidOperationException("No LFJ staked JOE available or balance decreased");
        var tx = contract.GetFunction("withdraw").CreateTransactionInput(p.Wallet, amount);
        return await DefiVault.PrepareTransaction(web3, chainId, Joe, amount, tx, gasPercent, priceClient);
    }
}
