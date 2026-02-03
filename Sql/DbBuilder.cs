
using NBitcoin;
using Nethereum.Web3.Accounts;
using z3n;
using z3nSafe;
public static class DBuilder
{
    
    #region Import
    private static readonly Dictionary<string, List<string>> DbStructureTemplate = new()
    {
        ["_addresses"] = new() { "id", "evm", "sol", "sui" },
        ["_deposits"] = new() { "id", "okx_evm", "bitget_evm", "binance_evm" },
        ["_treasury"] = new() { "id" },
        ["_wallets"] = new() { "id", "secp256k1", "base58", "bip39" }
    };
    public static string GetAddressFromPrivateKey(string privateKey)
    {
        // Убираем 0x если есть
        privateKey = privateKey.Replace("0x", "");
            
        var account = new Account(privateKey);
        return account.Address;
    }
    public static void ImportDbStructure(Db db)
    {

        foreach (var tableEntry in DbStructureTemplate)
        {
            string tableName = tableEntry.Key;
            List<string> columns = tableEntry.Value;
            db.PrepareTable(columns, tableName, log: true, prune: false, rearrange: true);
        }
    }
    public static async Task ImportWalletsAsync(Db db, List<string> evmWallets, List<string> solKeys = null, string pin = null)
    {
        var tableName = "_wallets";
        
        if (evmWallets != null && evmWallets.Count > 0)
        {
            db.AddRange("_wallets", evmWallets.Count);
            db.AddRange("_addresses", evmWallets.Count);
            db.AddRange("_treasury", evmWallets.Count);
            
            for (int id = 1; id <= evmWallets.Count; id++)
            {
                string item = evmWallets[id - 1]?.Trim();
                if (string.IsNullOrEmpty(item)) continue;

                string privateKey;
                bool isMnemonic = item.Split(' ').Length > 1;

                if (isMnemonic)
                {
                    string encodedSeed = SAFU.Encode(item, pin.FromBase64(), id.ToString());
                    db.Upd($"bip39 = '{encodedSeed}'", tableName, where: $"id = {id}");
                    
                    var mnemonicObj = new Mnemonic(item);
                    var hdRoot = mnemonicObj.DeriveExtKey();
                    var derivationPath = new NBitcoin.KeyPath("m/44'/60'/0'/0/0");
                    privateKey = hdRoot.Derive(derivationPath).PrivateKey.ToHex();
                }
                else
                {
                    privateKey = item.Replace("0x", "");
                }

                string encodedPrivateKey = SAFU.Encode(privateKey, pin.FromBase64(), id.ToString());
                string address = GetAddressFromPrivateKey(privateKey);
                
                db.Upd($"secp256k1 = '{encodedPrivateKey}'", tableName, id: id);
                db.Upd($"evm = '{address}'", "_addresses", id: id);
            }
        }

        // Import SOL
        if (solKeys != null && solKeys.Count > 0)
        {

            db.AddRange(tableName,  solKeys.Count);
            for (int id = 1; id <= solKeys.Count; id++)
            {
                string item = solKeys[id - 1]?.Trim();
                if (string.IsNullOrEmpty(item)) 
                    continue;
                
                string encodedSolKey = SAFU.Encode(item, pin.FromBase64(), id.ToString());
                db.Upd($"base58 = '{encodedSolKey}'", tableName, id: id);
            }
        }
    }
    public static void ImportDepositAddresses(Db db, string chain, string cex, List<string> addresses)
    {
        string table = "_deposits";
        
        if (string.IsNullOrEmpty(chain) || string.IsNullOrEmpty(cex) || addresses == null || addresses.Count == 0)
        {
            throw new ArgumentException("Chain, CEX, and addresses cannot be empty");
        }

        string CHAIN = chain.ToLower();
        string CEX = cex.ToLower();
        string columnName = $"{CEX}_{CHAIN}";

        db.AddColumn(columnName, table);
        db.AddRange(table, addresses.Count);

        int lineCount = 0;

        for (int acc0 = 1; acc0 <= addresses.Count; acc0++)
        {
            string line = addresses[acc0 - 1]?.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                Console.WriteLine($"Warning: Line {acc0} is empty");
                continue;
            }

            try
            {
                db.Upd($"{columnName} = '{line}'", table, where: $"id = {acc0}");
                lineCount++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing line {acc0}: {ex.Message}");
                continue;
            }
        }

        Console.WriteLine($"[{lineCount}] addresses added to [{table}].{columnName}");
    }
    
    #endregion


    
    
    
}

