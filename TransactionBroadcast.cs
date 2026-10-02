using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Nethereum.Util;
using z3n;

namespace z3nSafe;

public static class TransactionBroadcast
{
    public static async Task<string> SendAsync(Web3 web3, TransactionInput transaction, int chainId, Logger? log)
    {
        SwapExecution.Check();
        // Signing may read chain/fee metadata, but never broadcasts. Know the hash before submitting.
        var raw = await SwapExecution.Read(web3.Eth.TransactionManager.SignTransactionAsync(transaction));
        return await SubmitAsync(raw,
            signed => web3.Eth.Transactions.SendRawTransaction.SendRequestAsync(signed),
            hash => (ReceiptWaiter.Fallback(chainId, hash) ??
                (() => web3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(hash)))(), log);
    }
    public static string Hash(string raw)
    {
        var bytes = Convert.FromHexString(raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? raw[2..] : raw);
        return "0x" + Convert.ToHexString(new Sha3Keccack().CalculateHash(bytes)).ToLowerInvariant();
    }
    public static async Task<string> SubmitAsync(string raw, Func<string, Task<string>> send,
        Func<string, Task<TransactionReceipt>> receipt, Logger? log)
    {
        if (!raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) raw = "0x" + raw;
        var hash = Hash(raw);
        log?.Send($"Prepared TX: {hash} | submitting eth_sendRawTransaction", "INFO");
        SwapExecution.Check();
        try
        {
            // Never automatically resend or regenerate a transaction after an uncertain broadcast.
            var returned = await send(raw);
            if (!string.Equals(returned, hash, StringComparison.OrdinalIgnoreCase))
                throw new ReceiptUnavailableException($"RPC returned an unexpected transaction hash. Expected TX: {hash}; returned: {returned}");
            log?.Send($"Broadcast TX: {hash}", "INFO");
            return hash;
        }
        catch (Nethereum.JsonRpc.Client.RpcResponseException ex) when
            (!ex.Message.Contains("already known", StringComparison.OrdinalIgnoreCase) &&
             !ex.Message.Contains("nonce too low", StringComparison.OrdinalIgnoreCase))
        { throw new InvalidOperationException($"eth_sendRawTransaction rejected TX: {hash} | {SwapExecution.ErrorDetails(ex)}", ex); }
        catch (ReceiptUnavailableException) { throw; }
        catch (Exception ex)
        {
            log?.Send($"eth_sendRawTransaction response unavailable | TX: {hash} | {SwapExecution.ErrorDetails(ex)}. Checking on-chain; no resend.", "WARNING");
            try
            {
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    var found = await SwapExecution.Read(receipt(hash));
                    if (found != null)
                    {
                        log?.Send($"Broadcast recovered from receipt | TX: {hash} | status={found.Status?.Value}", "INFO");
                        return hash;
                    }
                    if (attempt < 2) await SwapExecution.Delay(1000);
                }
            }
            catch (Exception lookupError)
            { throw new ReceiptUnavailableException($"Broadcast outcome unknown. TX: {hash}. Queue stopped; no resend. {SwapExecution.ErrorDetails(lookupError)}", ex); }
            throw new ReceiptUnavailableException($"Broadcast outcome unknown. TX: {hash}. Queue stopped; no resend. {SwapExecution.ErrorDetails(ex)}", ex);
        }
    }
}
