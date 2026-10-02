using System.Numerics;
using Nethereum.RLP;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;

namespace z3nSafe;

public static class DefiFees
{
    public const string BlastOracle = "0x420000000000000000000000000000000000000F";
    public const string ScrollOracle = "0x5300000000000000000000000000000000000002";
    private const string Abi = "[{\"type\":\"function\",\"name\":\"getL1Fee\",\"inputs\":[{\"name\":\"data\",\"type\":\"bytes\"}],\"outputs\":[{\"type\":\"uint256\"}],\"stateMutability\":\"view\"}]";
    public static byte[] Unsigned(TransactionInput tx, BigInteger chainId, BigInteger nonce, BigInteger gas, BigInteger price, bool signatureReserve = false)
    {
        byte[] Number(BigInteger value) => value == 0 ? Array.Empty<byte>() : value.ToByteArray(isUnsigned: true, isBigEndian: true);
        var fields = new[] { Number(nonce), Number(price), Number(gas), Convert.FromHexString(tx.To[2..]),
            Number(tx.Value?.Value ?? 0), Convert.FromHexString(tx.Data[2..]), Number(signatureReserve ? chainId * 2 + 36 : chainId),
            signatureReserve ? Enumerable.Repeat((byte)255, 32).ToArray() : Array.Empty<byte>(),
            signatureReserve ? Enumerable.Repeat((byte)255, 32).ToArray() : Array.Empty<byte>() };
        return RLP.EncodeList(fields.Select(RLP.EncodeElement).ToArray());
    }
    public static async Task<BigInteger> L1Fee(Web3 web3, int chainId, TransactionInput tx, BigInteger gas, BigInteger price)
    {
        // Arbitrum eth_estimateGas includes the L1 posting component in its gas units.
        // zkSync Era estimates charge execution and pubdata together, including for legacy EOA transactions.
        if (chainId is 1 or 56 or 100 or 137 or 43114 or 42161 or 324) return 0;
        if (chainId is not (10 or 81457 or 534352)) throw new InvalidOperationException("Missing network data fee adapter");
        var nonce = await SwapExecution.Read(web3.Eth.Transactions.GetTransactionCount.SendRequestAsync(tx.From, BlockParameter.CreatePending()));
        if (chainId == 10)
        {
            // OP's Fjord upper bound includes signature bytes and avoids compression underestimates.
            // getOperatorFee follows the active Isthmus/Jovian rules; missing reads fail closed.
            var oracle = web3.Eth.GetContract(DefiBlackwing.Abi(("getL1FeeUpperBound", ["uint256"], ["uint256"]),
                ("getOperatorFee", ["uint256"], ["uint256"])), BlastOracle);
            var dataFee = await SwapExecution.Read(oracle.GetFunction("getL1FeeUpperBound").CallAsync<BigInteger>(
                new BigInteger(Unsigned(tx, chainId, nonce.Value, gas, price).Length)));
            var operatorFee = await SwapExecution.Read(oracle.GetFunction("getOperatorFee").CallAsync<BigInteger>(gas));
            if (dataFee <= 0 || operatorFee < 0) throw new InvalidOperationException("Optimism network fees unavailable; withdrawal blocked");
            return ((dataFee + operatorFee) * 125 + 99) / 100;
        }
        // Blast's oracle adds worst-case signature bytes to the unsigned RLP. No key or signature is used here.
        // Scroll requires full RLP size, including v/r/s: reserve two 32-byte non-zero fields without signing.
        var fee = await SwapExecution.Read(web3.Eth.GetContract(Abi, chainId == 534352 ? ScrollOracle : BlastOracle).GetFunction("getL1Fee")
            .CallAsync<BigInteger>(Unsigned(tx, chainId, nonce.Value, gas, price, signatureReserve: chainId == 534352)));
        if (fee <= 0) throw new InvalidOperationException("L1 data fee unavailable; withdrawal blocked");
        return (fee * 125 + 99) / 100; // Data-fee reserve; execution obtains a fresh estimate before signing.
    }
}
