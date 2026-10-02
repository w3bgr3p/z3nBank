using System.Numerics;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.JsonRpc.Client;
using z3nSafe;

internal static class BroadcastChecks
{
    public static async Task Run(Action<string, bool> check)
    {
        const string raw = "1234";
        var hash = TransactionBroadcast.Hash(raw);
        check("Transaction hash uses Ethereum Keccak, not SHA256", TransactionBroadcast.Hash("0x") == "0xc5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470");
        var sends = 0; var lookups = 0;
        var confirmed = await TransactionBroadcast.SubmitAsync(raw, _ => { sends++; throw new TimeoutException("RPC timeout after 20000 milliseconds"); },
            requested => { lookups++; check("Recovery looks up the exact precomputed hash", requested == hash); return Task.FromResult(new TransactionReceipt { Status = new HexBigInteger(1) }); }, null);
        check("Accepted transaction survives lost RPC reply without a resend", confirmed == hash && sends == 1 && lookups == 1);
        var reverted = await TransactionBroadcast.SubmitAsync(raw, _ => throw new TimeoutException(),
            _ => Task.FromResult(new TransactionReceipt { Status = new HexBigInteger(0) }), null);
        check("Recovered revert is returned for receipt failure checking", reverted == hash);
        sends = 0; var stopped = false;
        try { await TransactionBroadcast.SubmitAsync(raw, _ => { sends++; throw new TimeoutException(); },
            _ => Task.FromResult<TransactionReceipt>(null!), null); }
        catch (ReceiptUnavailableException ex) { stopped = ex.Message.Contains(hash) && ex.Message.Contains("Queue stopped"); }
        check("Unknown broadcast halts queue and retains hash instead of resending", stopped && sends == 1);
        var rejected = false; lookups = 0;
        try { await TransactionBroadcast.SubmitAsync(raw, _ => throw new RpcResponseException(new RpcError(-32000, "insufficient funds")),
            _ => { lookups++; return Task.FromResult<TransactionReceipt>(null!); }, null); }
        catch (InvalidOperationException ex) { rejected = ex.Message.Contains("eth_sendRawTransaction rejected") && ex.Message.Contains("insufficient funds"); }
        check("Explicit node rejection retains its reason and does not poll receipts", rejected && lookups == 0);
        var normalized = false;
        await TransactionBroadcast.SubmitAsync(raw, data => { normalized = data == "0x1234"; return Task.FromResult(hash); },
            _ => throw new Exception("No receipt lookup on success"), null);
        check("Raw transactions carry required 0x prefix", normalized);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); sends = 0;
        try { await SwapExecution.Run(cancellation.Token, async () => await TransactionBroadcast.SubmitAsync(raw,
            _ => { sends++; return Task.FromResult(hash); }, _ => Task.FromResult<TransactionReceipt>(null!), null)); }
        catch (OperationCanceledException) { }
        check("Stop before submission never broadcasts", sends == 0);
        var balanceBlocked = false;
        try { TransactionGas.RequireBalance(BigInteger.Parse("3840926937994"), 0, 760355, 60000000); }
        catch (InvalidOperationException ex) { balanceBlocked = ex.Message.Contains("native balance"); }
        check("Actual account 28 gas budget cannot fund a FLOCK swap", balanceBlocked);
        TransactionGas.RequireBalance(1000, 100, 9, 100);
        check("Exact native transaction value plus gas budget is accepted", true);
    }
}
