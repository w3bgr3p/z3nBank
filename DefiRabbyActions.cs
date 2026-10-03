using System.Numerics;
using System.Text.RegularExpressions;
using System.Net.Http.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nethereum.Web3;
using Nethereum.RPC.Eth.DTOs;

namespace z3nSafe;

public sealed class RabbyWithdrawAction
{
    [JsonProperty("type")] public string Type { get; set; } = "";
    [JsonProperty("contract_id")] public string Contract { get; set; } = "";
    [JsonProperty("func")] public string Function { get; set; } = "";
    [JsonProperty("str_params"), JsonConverter(typeof(RabbyParametersConverter))] public string[]? Parameters { get; set; }
    [JsonProperty("need_approve")] public JObject? Approval { get; set; }
}

// Rabby's DappActions builds calldata from func/str_params. No signing occurs here.
public static class DefiRabbyActions
{
    private static readonly HashSet<string> Blocked = new(StringComparer.OrdinalIgnoreCase)
        { "approve", "increaseAllowance", "transferOwnership", "transferOwner", "signalTransfer", "transfer", "transferFrom", "safeTransferFrom", "setApprovalForAll", "permit", "multicall", "execute", "delegatecall" };

    public static RabbyWithdrawAction Select(DefiPosition p)
    {
        if (p.HasProxy) throw new InvalidDataException("Rabby hides automatic actions for proxy-held positions; use the proxy protocol's exit");
        var candidates = p.WithdrawActions.Where(a => a.Type == (p.Type is "reward" or "vesting" ? "claim" : "withdraw")).ToArray();
        var exact = candidates.Where(a => a.Parameters?.Contains(p.AssetAddress, StringComparer.OrdinalIgnoreCase) == true).ToArray();
        if (exact.Length == 1) return exact[0];
        if (candidates.Length != 1) throw new InvalidDataException("No unique Rabby withdrawal/claim action for this asset; queue or multi-step exits need separate handling");
        return candidates[0];
    }

    public static TransactionInput Build(DefiPosition p)
    {
        var action = Select(p);
        if (!DefiVault.AddressValid(action.Contract) || !DefiActionTrust.Destination(p, action))
            throw new InvalidDataException("Rabby action destination does not match the position contract");
        if (action.Approval?["to"] is { } spender && !DefiActionTrust.Approval(p.Chain, spender.ToString()))
            throw new InvalidDataException("Rabby approval spender is not in the verified action route list");
        var (name, types) = Signature(action.Function);
        var parameters = action.Parameters ?? (types.Length == 0 ? [] : throw new InvalidDataException("Rabby action is missing exact str_params"));
        if (parameters.Length != types.Length) throw new InvalidDataException("Rabby action parameter count mismatch");
        return DefiActionAbi.Build(p.Wallet, action.Contract, action.Function, parameters);
    }

    public static string? Unavailable(DefiPosition p)
    {
        try { Build(p); return null; }
        catch (Exception ex) { return ex.Message; }
    }

    public static (string Name, string[] Types) Signature(string signature)
    {
        var parsed = DefiActionAbi.Signature(signature);
        if (Blocked.Contains(parsed.Name)) throw new InvalidDataException("Unsupported or unsafe Rabby action signature");
        return parsed;
    }

    public static object Parameter(string type, string value)
    {
        if (type.EndsWith("[]")) return JArray.Parse(value).Select(v => Parameter(type[..^2], v.ToString())).ToArray();
        if (type == "address" && DefiVault.AddressValid(value)) return value;
        if (Regex.IsMatch(type, @"^u?int\d*$")) {
            var number = BigInteger.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            var bits = int.TryParse(Regex.Match(type, @"\d+").Value, out var width) ? width : 256;
            var unsigned = type.StartsWith('u');
            if (bits < 8 || bits > 256 || bits % 8 != 0) throw new InvalidDataException("Invalid Rabby integer type width");
            if (number < (unsigned ? 0 : -(BigInteger.One << (bits - 1))) || number >= (BigInteger.One << (unsigned ? bits : bits - 1)))
                throw new InvalidDataException("Rabby integer parameter is out of range");
            return number;
        }
        if (type == "bool" && bool.TryParse(value, out var boolean)) return boolean;
        if (type == "string") return value;
        if (Regex.IsMatch(type, @"^bytes(?:[1-9]|[12]\d|3[0-2])?$")) {
            var bytes = Convert.FromHexString(value.StartsWith("0x") ? value[2..] : value);
            if (type != "bytes" && bytes.Length != int.Parse(type[5..])) throw new InvalidDataException("Wrong Rabby bytes parameter length");
            return bytes;
        }
        throw new InvalidDataException("Unsupported Rabby parameter type: " + type);
    }

    public static List<(string Asset, BigInteger Amount)> Outputs(JObject response, string chain)
    {
        if ((bool?)response["pre_exec"]?["success"] != true || (bool?)response["balance_change"]?["success"] != true)
            throw new InvalidOperationException("Rabby withdrawal simulation failed: " + (response["pre_exec"]?["error"] ?? response["balance_change"]?["error"]));
        if ((response["balance_change"]?["send_nft_list"] as JArray)?.Count > 0)
            throw new InvalidDataException("Rabby action spends NFTs; a verified NFT exit adapter is required");
        var received = response["balance_change"]?["receive_token_list"] as JArray ?? throw new InvalidDataException("Rabby simulation has no output token list");
        var outputs = new List<(string Asset, BigInteger Amount)>();
        foreach (var token in received) {
            if ((string?)token["chain"] != chain) throw new InvalidDataException("Rabby output network does not match the position");
            var asset = DefiPositionsClient.AssetId((string?)token["id"], chain);
            if (!DefiVault.AddressValid(asset) || !BigInteger.TryParse((string?)token["raw_amount"], out var amount) || amount <= 0)
                throw new InvalidDataException("Rabby output is missing exact raw_amount or a valid asset");
            outputs.Add((asset!, amount));
        }
        if (outputs.Count == 0) throw new InvalidOperationException("Rabby action produces no tokens now; a queued exit or empty position cannot be withdrawn immediately");
        return outputs;
    }

    public static async Task<DefiVault.Quote> Prepare(Web3 web3, int chainId, DefiPosition p, decimal gasPercent,
        string? exact = null, HttpClient? prices = null, HttpClient? simulationClient = null)
    {
        if ((await SwapExecution.Read(web3.Eth.ChainId.SendRequestAsync())).Value != chainId || Rpc.ChainId(p.Chain) != chainId)
            throw new InvalidDataException("Rabby action RPC chain mismatch");
        if (p.DebtUsd > 0 && p.Type is not ("reward" or "vesting")) throw new InvalidOperationException("This position has active debt. Automatic withdrawal is blocked to protect collateral.");
        var tx = Build(p);
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Convert.FromHexString(tx.Data[2..])));
        if (exact != null && exact != fingerprint) throw new InvalidDataException("Rabby action parameters changed; preview again");
        // Address arguments may identify the owner/receiver or token, never another EOA.
        var action = Select(p); var (_, types) = Signature(action.Function);
        for (var i = 0; i < types.Length; i++) {
            foreach (var address in DefiActionAbi.Addresses(types[i], action.Parameters![i])) {
            if (address.Equals(p.Wallet, StringComparison.OrdinalIgnoreCase) || TokenSelection.IsNative(address)) continue;
            var code = await SwapExecution.Read(web3.Eth.GetCode.SendRequestAsync(address));
            if (string.IsNullOrWhiteSpace(code) || code == "0x") throw new InvalidDataException("Rabby action contains a different wallet recipient");
            }
        }
        return await PrepareBuilt(web3, chainId, p, gasPercent, tx, fingerprint, prices, simulationClient, action);
    }

    // Only protocol adapters that verify their contracts, recipients and live inputs call this path.
    internal static async Task<DefiVault.Quote> PrepareBuilt(Web3 web3, int chainId, DefiPosition p, decimal gasPercent,
        TransactionInput tx, string fingerprint, HttpClient? prices = null, HttpClient? simulationClient = null,
        RabbyWithdrawAction? action = null, IReadOnlyList<(string Asset, BigInteger Amount)>? futureOutputs = null,
        IReadOnlyList<DefiVault.Approval>? preparedSteps = null, IReadOnlyDictionary<string, JObject>? verifiedPrices = null)
    {
        if ((await SwapExecution.Read(web3.Eth.ChainId.SendRequestAsync())).Value != chainId || Rpc.ChainId(p.Chain) != chainId)
            throw new InvalidDataException("Withdrawal RPC chain mismatch");
        if (p.HasProxy || p.DebtUsd > 0 && p.Type is not ("reward" or "vesting")) throw new InvalidOperationException("Proxy ownership or active debt prevents automatic withdrawal");
        var approvals = preparedSteps?.ToList() ?? (action == null ? new List<DefiVault.Approval>() : await DefiActionApprovals.Prepare(web3, chainId, p, action, gasPercent));
        if (approvals.Count == 0) await SwapExecution.Read(web3.Eth.Transactions.Call.SendRequestAsync(tx));
        var nonce = await SwapExecution.Read(web3.Eth.Transactions.GetTransactionCount.SendRequestAsync(p.Wallet, BlockParameter.CreatePending()));
        using var owned = simulationClient == null ? new HttpClient { Timeout = TimeSpan.FromSeconds(30) } : null;
        var http = simulationClient ?? owned!;
        var pending = approvals.Select((a, i) => new { chainId, from = p.Wallet, to = a.Destination, data = a.Data, value = "0x0",
            nonce = new Nethereum.Hex.HexTypes.HexBigInteger(nonce.Value + i).HexValue,
            gas = new Nethereum.Hex.HexTypes.HexBigInteger(BigInteger.Parse(a.Gas)).HexValue,
            gasPrice = new Nethereum.Hex.HexTypes.HexBigInteger(BigInteger.Parse(a.GasPrice)).HexValue }).ToArray();
        var body = new { tx = new { chainId, from = p.Wallet, to = tx.To, data = tx.Data, value = tx.Value?.HexValue ?? "0x0",
            nonce = new Nethereum.Hex.HexTypes.HexBigInteger(nonce.Value + approvals.Count).HexValue,
            gas = "0x1000000", gasPrice = "0x0" }, user_addr = p.Wallet, origin = "z3nBank", update_nonce = approvals.Count == 0, pending_tx_list = pending };
        using var response = await http.PostAsJsonAsync("https://api.rabby.io/v1/wallet/pre_exec_tx", body, SwapExecution.Token);
        response.EnsureSuccessStatusCode();
        var simulation = (JObject)DefiPositionsClient.ParseJson(await response.Content.ReadAsStringAsync(SwapExecution.Token));
        if ((bool?)simulation["pre_exec"]?["success"] != true)
            throw new InvalidOperationException("Withdrawal simulation failed: " + simulation["pre_exec"]?["error"]);
        var outputs = futureOutputs?.ToList() ?? Outputs(simulation, p.Chain);
        BigInteger? reserved = null;
        if (approvals.Count > 0) {
            if ((bool?)simulation["gas"]?["success"] != true || !BigInteger.TryParse((string?)simulation["gas"]?["gas_limit"], out var estimate) || estimate <= 0)
                throw new InvalidOperationException("Rabby did not estimate the withdrawal after its approval sequence");
            reserved = estimate;
        }
        var quote = await DefiVault.PrepareTransaction(web3, chainId, outputs[0].Asset, outputs[0].Amount, tx, gasPercent, prices, outputs,
            reservedGas: reserved, approvals: approvals, verifiedPrices: verifiedPrices);
        return quote with { InputAmountRaw = fingerprint };
    }
}
