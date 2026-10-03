# 🏦 z3nBank
[RU]()

**Multi-chain cryptocurrency treasury management dashboard with GitHub-style heatmap visualization**

z3nBank is a Windows desktop application for managing and visualizing multi-chain crypto portfolios. It displays saved balance snapshots, supports explicit balance updates, cross-chain bridging, and token swapping.

Successful database connection settings are saved automatically in `%LOCALAPPDATA%/z3nBank/database.config` and restored on startup. Windows DPAPI protects the configuration, including the PostgreSQL password, for the current Windows user. A failed saved connection opens the configuration dialog with the previous parameters and a readable error. Database Settings pre-fills the saved parameters; leave the password blank to keep it for the same connection, or enter a replacement. Failed changes preserve the previous working connection and saved settings. Treasury RPC selection uses numeric chain IDs rather than provider display names.

See [RPC audit](docs/RPC_AUDIT.md) for the latest endpoint checks and the repeatable read-only audit. Unichain uses mainnet ID 130. ZERO's public RPC is unavailable; the application reports this explicitly rather than submitting requests to a broken endpoint.

Confirmed token swaps execute the exact contracts listed in the preview, even if LI.FI subsequently omits them. Each contract's amount is read from RPC before requesting a quote. Zero on-chain balances refresh the outdated Treasury snapshot without sending a transaction. Skip reasons (zero balance, minimum value, cost guard) appear in shared logs and the result dialog; a run with no swaps shows a warning rather than green success.

Relay output token prices are derived from `currencyOut.amountUsd` and the scaled quoted output amount. Post-swap logs include the saved native amount, price and USD estimate. Positive on-chain balances remain visible even without price data; the tooltip labels their price as unknown.

Database connection errors remain in the startup/settings dialog without clearing entered fields.
PostgreSQL authentication, missing databases and connectivity failures have readable messages;
failed settings changes preserve the existing working connection. Password and wallet PIN fields show
a Caps Lock indicator, including its current Windows state when the field gains focus.

RPC transaction handling: the app signs locally and logs the transaction hash before broadcasting.
A lost broadcast response is checked against that exact hash without resending. If the result remains unknown,
the queue stops and keeps the hash for on-chain inspection. A confirmed revert remains a failed transaction.
The app checks native funds for transaction value plus estimated gas, including the gas-limit buffer;
valuable ERC-20 tokens cannot pay native gas. BSC uses official `bsc-dataseed.bnbchain.org`, with
`bsc-dataseed1.bnbchain.org` for fallback receipt checks. Read availability does not guarantee broadcast availability.

### DeFi positions and withdrawals

Open **DeFi** in the header and select **Scan accounts**. No API key, subscription or paid API is required.
The scanner calls Rabby's public `/v1/user/complex_protocol_list` endpoint, also used in the
[official Rabby API client](https://github.com/RabbyHub/rabby-api/blob/main/src/index.ts).
Scanning uses Max ID and all provider-supported networks, independently of the treasury chain filter.
Transient 429/503 responses have bounded retries within a 30-second budget per account. Account errors,
including access/rate-limit errors and request timeouts, remain visible and the scan continues to the next wallet.
Only an explicit stop cancels the batch; errored accounts keep unknown balances rather than reporting zero.
There is no paid fallback. Public endpoint availability and limits may change.
Rabby uses DeBank data: this is a free discovery source, not independent validation. Withdrawal limits are checked directly via RPC.

The **DeFi** tab fills the workspace with an account-by-network heatmap. Cells show estimated net USD;
loans are subtracted and unknown prices or unscanned accounts are marked `?`. Click a network cell for that
account's positions in the network, or its total for all networks. The Protocol selector filters both totals and details.
Details show deposited, staked, locked, reward, LP and loan positions. Amounts and USD values are provider estimates,
not proof of a withdrawable balance. LP legs share a group ID visible in the row tooltip.
DeFi values are not added to the wallet total: receipt tokens may already be included there.
The right sidebar follows Treasury: protocol totals, unique account counts, distribution bars, chain totals and
portfolio summary. Click protocol cards to select multiple protocols; click again to deselect or use **Clear protocols**.
Debt is subtracted and unknown prices stay marked `?`. Batch withdrawal requires exactly one protocol.
Results, account progress, scan time and errors are checkpointed in the connected database's `_defi_snapshot`
table after each account and restored when opening DeFi after a restart. Interrupted scans retain partial results;
pending/error accounts remain unknown. Saved positions are matched to current wallet addresses and never shared
across databases. Restarting does not resume scans, withdrawal queues or confirmation plans. A transaction marks
the affected account as requiring a new scan before another withdrawal; other scanned accounts remain usable.
Single-account withdrawal checks and execution can run while the scanner processes other accounts. Pending,
failed or stale accounts remain blocked with a visible reason. Protocol batches still wait for scanning to finish.
Fresh RPC checks still precede every exit.

STG locks can now be withdrawn from verified Stargate escrows on Ethereum, BSC, Optimism and Arbitrum once
the lock expires. The adapter checks the lock against the latest block timestamp, verifies the STG token and
simulates `withdraw()` before confirmation. An unexpired lock shows its unlock time. Optimism reserves L1
data and operator fees via its official gas oracle. See [Stargate VotingEscrow](https://github.com/stargate-protocol/stargate-dao/blob/main/contracts/VotingEscrow.sol)
and [Optimism fee documentation](https://docs.optimism.io/op-stack/transactions/fees).

LFJ sJOE on Arbitrum also supports a separate USDC.e reward claim using `withdraw(0)`; JOE remains staked.
The pending reward, funding, simulation and cost are checked before signing. A normal JOE withdrawal also
harvests rewards. Compound V3 supports the verified Polygon USDC.e market in addition to Scroll; both `:yield`
and `:lending` provider suffixes normalize to the underlying market address. Polygon now uses the keyless
PublicNode RPC because the former `polygon-rpc.com` endpoint rejects unauthenticated requests.

PancakeSwap V3, Curve, Merkl and Hana positions are no longer incorrectly offered ERC-4626 withdrawal checks.
Their details explain the required NFT exit, pool exit, reward proof or lending adapter. Stargate LP/farm exits
and Compound reward claims also remain unsupported; STG escrow withdrawal and supplied Compound assets
have separate verified paths.

**Check withdrawal** attempts a direct synchronous [ERC-4626](https://eips.ethereum.org/EIPS/eip-4626) withdrawal.
Currently this supports Ethereum, BSC, Gnosis, Polygon, Avalanche and Blast. The contract must expose the matching underlying asset
and a positive `maxWithdraw(owner)`. The app simulates the exact withdrawal, checks gas and fresh asset prices,
shows a preview, and requires explicit confirmation. It withdraws the displayed underlying amount to the same wallet;
it does not swap the result to native currency. The selected Gas +% applies. The fee must be below the output value.
Before signing, limits, simulation and fee are checked again; a fee increase above 10% requires a new preview.
Stop withdrawal stops further work/receipt tracking, but cannot undo a broadcast transaction.

For **Withdraw selected**, choose one Protocol and click account IDs, **Select visible**, or a range
of accounts. Selected IDs are highlighted; clicking again deselects the account. You can enter a range
such as `1-100, 105` with **Select batch**. The preview checks up to 200 protocol positions, groups duplicate vault
legs, and lists ready withdrawals, amounts, fees and reasons for skipping unsupported or uneconomic positions.
Confirming starts a sequential queue restricted to that exact protocol and account batch. Each position is checked
again before signing. **Stop scan / check** cancels scanning or batch preview; **Stop withdrawal** stops the queue.
An unknown transaction outcome halts the queue and preserves transaction information for inspection.
Scan progress/results, preview checks, skipped positions, withdrawal errors and queue stops are sent to the
shared **Logs** drawer with the account ID. DeFi has no separate log panel. Position and confirmation dialogs
are centered in the viewport. Scan again after broadcasting before another batch.

**SynFutures V3 on Blast:** the adapter withdraws free WETH/USDB deposits from the official Gate contract
`0x6A372dBc1968f4a07cf2ce352f410962A972c257`, using live `reserveOf(token, wallet)` and `withdraw(bytes32)`
as specified in the [official Oyster SDK](https://github.com/SynFutures/oyster-sdk).
It does not close leveraged positions or remove LP ranges. WETH returns as WETH, USDB as USDB.
Single withdrawals and protocol batches use the same adapter and execution checks.
Blast previews query `GasPriceOracle.getL1Fee` on the unsigned RLP transaction and reserve an extra 25% for
L1 data costs; total fees and required native funds include this amount. The oracle already includes signature overhead.
No key or signature is used during preview; a missing L1 estimate blocks execution.

**Aave V3:** verified Ethereum and Polygon Pool markets use `withdraw(asset, amount, wallet)` rather than ERC-4626.
The preview reads account debt and simulates the full withdrawal to discover the live supplied amount, then freezes
an exact underlying amount. Accounts with active debt are blocked to protect collateral. Batches keep separate
underlying assets within the same market; balances, simulation and fees are checked again before signing.

**LFJ sJOE on Arbitrum:** the verified staking contract uses `joe()`, `getUserInfo(wallet, token)` and `withdraw(amount)`.
Live stake and token identity are verified; normal withdrawal also triggers the contract's reward distribution.
The fee comparison conservatively values the JOE output alone. Arbitrum's `eth_estimateGas` includes L1 posting gas,
so it is counted once in the total fee. Aliases `arb`, `era`, `op`, `eth`, `matic`, `avax` and `xdai` resolve to configured RPCs.

**Blackwing BSC launch vault:** the adapter verifies the proxy implementation, reads live vault shares and the
deposit lock, and uses `withdraw(asset, vaultShares)`. Underlying amounts are read from the contract, independently
of the cached portfolio. No approval is needed. Owner-disabled withdrawals and unavailable deployed liquidity
block the simulation. Implementation upgrades block automatic withdrawal until verified again.
Source: [verified launch vault](https://sourcify.dev/server/v2/contract/56/0xc6ade8a68026d582ab37b879d188caf7e405dd09?fields=abi,sources).

**SyncSwap zkSync Era classic LP:** the adapter verifies pool type, master, vault, factory registration and both
underlying tokens. Router `burnLiquidity` returns both ERC20 assets to the same wallet (WETH stays wrapped), with
0.5% minimum-output protection. Both legs are displayed in single/batch previews; a pool is withdrawn once per account.
If allowance is missing, preview includes an exact-share approval and a conservative 3,000,000-gas withdrawal
reserve before the normal 10% gas buffer. The withdrawal is not yet simulated in that case: outputs follow the
verified pool's fee/dilution formula. Approval spends gas; after its receipt the withdrawal is simulated and priced
again, and changed minimum outputs or excessive total fees stop execution. Cancellation or unknown receipts halt the queue.
Era's gas estimate includes execution and pubdata; regular EOA legacy transactions use the existing signer.
Sources: [router and pool contracts](https://github.com/syncswap/core-contracts),
[official deployments](https://docs.syncswap.xyz/api-documentation/resources/smart-contract),
[Era transaction types](https://docs.zksync.io/zksync-protocol/era-vm/transactions/transaction-lifecycle).

**Scroll lending:** `scrl` resolves to the configured Scroll RPC. Rabby native token IDs become the zero address;
Compound's exact `:lending` suffix is removed from valid contract addresses. Group identities and displayed amounts stay intact.
The verified Aave V3 Scroll Pool and Compound V3 USDC Comet support supplied-asset withdrawals. Both block
accounts with active debt and reject withdrawals whose fees exceed the received value, including tiny leftovers.
LayerBank's verified Scroll Core uses `redeemToken(market, shares)` for ETH/USDC supplies, with live shares,
underlying/core identity checks, debt protection and read-only simulation. An empty market reports unavailable
protocol liquidity rather than a generic contract error. Scroll LAB.s rewards with missing provider prices use a cached
free LI.FI token price, shown as an estimate. Failure to obtain a price preserves the amount and unknown valuation.
The verified LayerBank reward controller supports withdrawal of unlocked rewards only. Checks read the live unlocked
balance; they never start vesting or accept an early-exit penalty. Accrued rewards that require claiming into vesting
remain a separate flow. Reward withdrawals retain the same fresh simulation and fee/output guards as deposits.
Scroll fees query the official L1 oracle with a full-size RLP signature reserve (no signing), plus a 25% L1 fee buffer.
Sources: [LayerBank contracts/deployments](https://github.com/layerbank-foundation/v2-contracts),
[Aave Scroll address book](https://github.com/aave-dao/aave-address-book/blob/main/src/AaveV3Scroll.sol),
[Compound Scroll deployment](https://github.com/compound-finance/comet/blob/main/deployments/scroll/usdc/roots.json),
[Scroll fee oracle](https://docs.scroll.io/en/developers/transaction-fees-on-scroll/).

Single-withdrawal confirmation uses a centered application dialog matching the other DeFi popups, with wallet,
output amounts, network fee and L1 fee displayed separately. Small fee amounts retain up to eight decimal places.
Confirmation remains open until the server accepts execution. Missing PINs, expired plans and busy-operation
errors are shown inside the confirmation and logged to the shared drawer. Confirm is disabled while submitting
to prevent duplicate requests; after acceptance the normal operation status and logs show progress.
Cancel, Escape or clicking outside the dialog before submitting does not execute the prepared withdrawal.

Unverified Blackwing vaults, SyncSwap stable/staked pools and Balancer liquidity still require dedicated adapters.
Click **Adapter unavailable · details** for the reason; these positions are not sent to ERC-4626 methods.

This is not universal unstaking. LP exits, staking farms, collateral withdrawal, claim rewards and asynchronous withdrawal queues
need separate adapters. Other L2 automatic withdrawals remain blocked until their additional fees are supported.
[Enso withdrawal routes](https://docs.enso.build/pages/use-cases/deposits/withdrawal) are a possible further adapter,
but are not implemented in this version. [Lido](https://docs.lido.fi/contracts/withdrawal-queue-erc721/) requires separate request/claim stages.
Coverage of discovery does not imply coverage of execution. The scanner uses the
[Rabby complex protocol API](https://github.com/RabbyHub/rabby-api/blob/main/src/index.ts).

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)
![Platform](https://img.shields.io/badge/platform-Windows-lightgrey.svg)

---

## ✨ Features

### 📊 Portfolio Visualization
- **GitHub-style Heatmap**: Visual representation of account balances across chains
- **Real-time Statistics**: Track total value, active accounts, and chain distribution
- **Token Analytics**: View top tokens and portfolio composition
- **Chain Distribution**: Analyze asset allocation across different blockchains

### 🔄 DeFi Operations
- **Cross-chain Bridging**: Bridge native tokens between chains using Relay or LiFi
- **Token Swapping**: Swap all tokens to native currency on selected chains
- **Batch Operations**: Execute operations across multiple accounts simultaneously
- **Threshold Management**: Set minimum value thresholds for transactions
- **Stablecoin Filtering**: Option to exclude stablecoins from operations

### 💾 Data Management
- **Multi-database Support**: SQLite and PostgreSQL compatible
- **Balance Updates**: Fetch and update balances for all accounts
- **Wallet Import**: Bulk import wallet addresses
- **Activity Logging**: Comprehensive logging system with filtering

### 🎨 User Interface
- **Dark Theme**: GitHub-inspired dark mode interface
- **Responsive Layout**: Three-panel design (heatmap, logs, statistics)
- **Interactive Tooltips**: Detailed information on hover
- **Multi-select Filters**: Filter by chains, log levels, and more
- **Keyboard Shortcuts**: Quick access to features (Ctrl+H for help)

---

## 🏗️ Architecture

z3nBank is built as a hybrid desktop application:

```
┌─────────────────────────────────────┐
│      Windows Forms Container        │
│  ┌───────────────────────────────┐  │
│  │      WebView2 Control         │  │
│  │  ┌─────────────────────────┐  │  │
│  │  │   Frontend (HTML/JS)    │  │  │
│  │  │   - Heatmap UI          │  │  │
│  │  │   - Interactive Charts  │  │  │
│  │  │   - Real-time Updates   │  │  │
│  │  └─────────────────────────┘  │  │
│  └───────────────────────────────┘  │
│                                     │
│  ┌───────────────────────────────┐  │
│  │   ASP.NET Core Backend        │  │
│  │   - REST API (dynamic port)  │  │
│  │   - TreasuryController        │  │
│  │   - Database Service          │  │
│  │   - Logging Service           │  │
│  └───────────────────────────────┘  │
└─────────────────────────────────────┘
```

### Technology Stack

**Backend:**
- .NET 8.0 / C#
- ASP.NET Core Web API
- WebView2 (.NET)
- Entity Framework Core (optional)

**Frontend:**
- HTML5 / CSS3
- Vanilla JavaScript
- SweetAlert2 for dialogs
- Custom heatmap visualization

**Database:**
- SQLite (default)
- PostgreSQL (optional)

---

## 🚀 Getting Started

### Prerequisites

- Windows 10/11 (64-bit)
- [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)

### Installation

1. **Download the latest release**
   ```
   Download z3nBank.zip from the releases page
   ```

2. **Extract the archive**
   ```
   Extract all files to a folder (e.g., C:\z3nBank)
   ```

3. **Project Structure**
   ```
   z3nBank/
   ├── z3nBank.exe           # Main executable
   ├── icon.ico              # Application icon
   ├── wwwroot/              # Web assets
   │   ├── index.html
   │   ├── app.js
   │   ├── styles.css
   │   ├── logs.js
   │   └── ui-components.js
   └── [.NET runtime files]
   ```

4. **Launch the application**
   ```
   Double-click z3nBank.exe
   ```

### Initial Setup

On first launch, you'll be prompted to configure the database:

**Option 1: SQLite (Recommended for beginners)**
- Select "SQLite" from the dropdown
- Enter database filename (e.g., `treasury.db`)
- The file will be created in the application directory

**Option 2: PostgreSQL**
- Select "PostgreSQL" from the dropdown
- Enter connection details:
    - Host: `localhost` (or remote server)
    - Port: `5432`
    - Database: `your_database_name`
    - Username: `your_username`
    - Password: `your_password`

---

## 📖 Usage Guide

### Setting up Your PIN

Before performing DeFi operations, set your wallet PIN:

1. Press `Ctrl + P` or use the PIN dialog
2. Enter your wallet encryption PIN
3. This PIN will be used for all swap and bridge operations

### Importing Wallets

1. Click the "Import Wallets" button
2. Paste wallet addresses (one per line)
3. Run **Update Balances** to fetch a new balance snapshot

### Viewing Your Portfolio

The **heatmap** displays:
- Each row = one wallet account
- Each column = one blockchain network
- Color intensity = balance value (darker green = higher value)

Hover over any cell to see:
- Wallet address
- Chain name
- Token breakdown
- Total USD value

### Refreshing Balances

**Manual Refresh:**
- Click the 🔄 Refresh button
- Enter max account ID to scan
- **Refresh** reloads the saved database snapshot; **Update Balances** uses LI.FI only for discovery and price metadata. Native balances, ERC-20 balances and ERC-20 `decimals()` are read through RPC at a specific block. Every discovered or previously known network includes its native token, even when wallet discovery omits it. Previously known contracts are checked even if the indexer omits them. Dollar prices remain estimates from LI.FI.
  for selected Treasury accounts. With no selected accounts, it updates the entire current Max ID range.
  The confirmation shows the scope and freezes the selected account IDs for that run.

**Auto-refresh:**
- Click "Auto: OFF" to toggle automatic updates
- Reloads the database view every 5 seconds; this does not fetch new blockchain balances

Successful balance updates replace the entire wallet snapshot. Known token contracts are retained for future RPC checks, including zero balances, dust and tokens without a USD price. Treasury displays positive on-chain balances; the minimum USD setting controls swap eligibility, not snapshot storage. Failed RPC checks keep the previous wallet snapshot and appear in the update status. The public LI.FI API allows 10 requests per minute; discovery requests are paced and HTTP 429 responses are retried after waiting.

After each confirmed Treasury swap, native and known ERC-20 amounts are read from RPC and saved for the affected chain only. The UI reloads when that snapshot changes, including swaps started from a row button. RPC network and block height are checked against the confirmed transaction; a failed refresh keeps the previous snapshot and logs a separate error without reporting the confirmed swap as failed. No indexed balance response overwrites this post-swap refresh.

The update report includes failed account IDs, the failing stage, the error code and an explanation.
Shared database failures (including PostgreSQL recovery, SQLSTATE `57P03`) stop the run instead of repeating
the same failure for every wallet. Accounts not attempted are counted separately; individual wallet API errors
still allow the next account to run. When PostgreSQL is recovering, wait for it to become ready and retry;
the PostgreSQL server log explains why recovery started.

USD values are estimates from token amounts and API prices, before fees and slippage. Cached USD totals are recalculated on read. Legacy DeBank records use a different amount format and must be refreshed before they can be included. Tokens flagged as denied or malicious by the provider are excluded; unverified tokens may still be returned.

### Filtering by Chains

1. Click the "All Chains" dropdown
2. Check/uncheck specific chains
3. Heatmap updates to show only selected chains

### DeFi Operations

#### Swap Tokens to Native

Converts all tokens to native currency on selected chains (e.g., ETH on Ethereum, MATIC on Polygon)

1. Select account ID (click on row)
2. Choose chains to process
3. Select bridge service (Relay or LiFi)
4. Set threshold (minimum value to swap)
5. Toggle "Exclude Stables" if desired
6. Click "Swap to Native"

#### Bridge to One Chain

Consolidates all native assets to a single destination chain

1. Select account ID
2. Choose source chains
3. Select destination chain
4. Select bridge service (Relay or LiFi)
5. Set threshold
6. Click "Bridge to Chain"

### Viewing Logs

The **Logs Panel** shows:
- Real-time operation status
- Success/error messages
- Transaction hashes
- Timestamp and log level

**Filters:**
- **Level**: ERROR, WARNING, INFO, SUCCESS
- **Limit**: Number of recent logs to display

**Controls:**
- 🗑️ Clear all logs from server

---

## 🎯 API Endpoints

The embedded API server binds to `http://127.0.0.1:0`. Windows selects a free port; the application prints and opens the actual selected URL.

### Database Configuration

```http
GET  /api/treasury/db-status
POST /api/treasury/db-config
```

### Treasury Data

```http
GET /api/treasury/data?maxId=100&chains=Ethereum,Polygon
GET /api/treasury/stats?maxId=100
GET /api/treasury/account/{id}
GET /api/treasury/chains
```

### Operations

```http
POST /api/treasury/update?maxId=100&minValue=0.001
GET  /api/treasury/update-status
POST /api/treasury/swap-chains
POST /api/treasury/bridge-chains
POST /api/treasury/import-wallets
POST /api/treasury/pin
```

### Logging

```http
GET  /api/treasury/logs?limit=50&level=ERROR
POST /api/treasury/log
POST /api/treasury/clear
```

---

## ⚙️ Configuration

### Database Schema

**Tables:**
- `_addresses`: Wallet addresses with IDs
- `_treasury`: Token balances per chain (JSON columns)

**Example `_treasury` structure:**
```json
{
  "Ethereum": [
    {
      "Symbol": "USDC",
      "Amount": "1000000000",
      "Decimals": 6,
      "PriceUSD": "1.00",
      "ChainId": 1,
      "Address": "0xA0b86...",
      "ValueUSD": 1000.00
    }
  ]
}
```

### Environment Variables (Optional)

```env
WEBVIEW2_USER_DATA_FOLDER=%LOCALAPPDATA%\z3nBank\WebView2
```

---

## 🔐 Security Considerations

⚠️ **Important Security Notes:**

1. **Private Key Encryption**:
    - Private keys and mnemonics are stored **encrypted** in the database
    - Encryption uses AES-256-CBC with HMAC-SHA256 authentication
    - Encryption key is derived from: **PIN + Hardware ID + Account ID**
    - Uses PBKDF2 with 100,000 iterations for key derivation
    - Each key is unique per account and hardware

2. **Hardware-Bound Security**:
    - Encryption keys are tied to specific hardware (CPU, motherboard, disk serial)
    - Database cannot be decrypted on different hardware
    - Provides additional protection against database theft

3. **PIN Security**:
    - PIN is stored in memory only during runtime
    - PIN is required to decrypt private keys
    - Use a strong, unique PIN (not reused elsewhere)

4. **Local Server**:
    - API runs on localhost (127.0.0.1) - not exposed to internet
    - No remote access to your wallet data

5. **Database Security**:
    - Use strong PostgreSQL passwords if using remote database
    - Restrict database access to localhost when possible
    - Consider encrypting database file at filesystem level for additional protection

6. **HTTPS**: Consider using HTTPS in production deployments

⚠️ **CRITICAL**: If you lose your PIN or move the database to different hardware, you will NOT be able to decrypt your private keys!

---

## 🛠️ Development

### Building from Source

```bash
# Clone repository
git clone https://github.com/yourusername/z3nBank.git
cd z3nBank

# Restore dependencies
dotnet restore

# Build project
dotnet build -c Release

# Run application
dotnet run
```

### Project Structure

```
z3nSafe/
├── MainForm.cs          # WinForms main window & WebView2 host
├── Program.cs           # Application entry point
├── Controllers/
│   └── TreasuryController.cs    # API endpoints
├── Services/
│   ├── DbConnectionService.cs   # Database management
│   └── LogService.cs            # Logging system
└── wwwroot/             # Frontend assets
    ├── index.html       # Main UI
    ├── app.js           # Core logic & API calls
    ├── logs.js          # Logging UI
    ├── ui-components.js # UI helpers
    └── styles.css       # GitHub-style theme
```

### Adding New Chains

To add support for a new blockchain:

1. Update database schema to include new chain column
2. Update `TreasuryController.GetChains()` if needed
3. Implement balance fetching logic in `HeatmapGenerator`
4. Add chain metadata (name, logo, colors) to frontend

---

## 🤝 Contributing

Contributions are welcome! Please follow these guidelines:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

### Development Guidelines

- Follow C# coding conventions
- Use meaningful variable names
- Add XML documentation for public APIs
- Test on Windows 10 and Windows 11
- Update README for new features

---

## 📝 Changelog

### Version 1.0.0 (Current)
- ✅ Multi-chain portfolio visualization
- ✅ GitHub-style heatmap interface
- ✅ SQLite and PostgreSQL support
- ✅ Cross-chain bridging (Relay, LiFi)
- ✅ Token swapping functionality
- ✅ Real-time logging system
- ✅ Bulk wallet import
- ✅ Auto-refresh capability

---

## ⚠️ Critical Backup Warning

**ALWAYS keep backup copies of your original mnemonics and private keys outside the application!**

- The database encryption is hardware-bound
- If hardware fails or you lose your PIN, you CANNOT recover keys from the database
- Write down mnemonics on paper or use a reliable offline storage solution
- Store backups in multiple secure locations
- Test your backups regularly

**The application is a portfolio management tool, NOT a primary wallet backup solution.**

---

## 🐛 Known Issues

- WebView2 requires Edge Runtime to be installed
- Large portfolios (1000+ accounts) may experience slow rendering
- PostgreSQL connection requires network access configuration

---

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

## 🙏 Acknowledgments

- **UI Inspiration**: GitHub contribution graph
- **Bridge Providers**: Relay, LiFi
- **Icons**: Unicode emoji set
- **Themes**: GitHub Dark theme color palette

---

## 🎓 Frequently Asked Questions

**Q: Where are my private keys stored?**
A: Private keys are stored **encrypted** in the database using AES-256 encryption. The encryption key is derived from your PIN + your computer's hardware ID + account ID. This means the database cannot be decrypted on different hardware or without your PIN.

**Q: Is it safe to enter my PIN?**
A: Your PIN is used to derive the encryption key for your private keys. It's stored only in memory during runtime and is never written to disk. Use a strong, unique PIN.

**Q: Can I move my database to another computer?**
A: No. The encryption is hardware-bound. If you move the database to different hardware, you will not be able to decrypt your private keys. Always backup your original mnemonics/private keys separately.

**Q: What happens if I forget my PIN?**
A: You will lose access to the encrypted private keys in the database. This is why it's critical to keep backup copies of your original mnemonics/private keys outside the application.

**Q: Which blockchain networks are supported?**
A: Balance updates use the EVM chains returned by LI.FI. The current dashboard does not fetch Solana wallet balances.

**Q: Can I use this on macOS or Linux?**
A: Currently only Windows is supported. Porting to other OS would require adapting the hardware ID detection and possibly switching to Avalonia or Electron.

**Q: What are the bridge/swap fees?**
A: Fees depend on the selected protocol (Relay/LiFi) and current gas prices on the network.

---

## 📞 Support

- **Issues**: [GitHub Issues](https://github.com/yourusername/z3nBank/issues)
- **Discussions**: [GitHub Discussions](https://github.com/yourusername/z3nBank/discussions)
- **Email**: support@yourproject.com

---

## ⚡ Quick Tips

### Selected token operations

- Click tokens in the right panel to select multiple symbols and highlight their cells. A second click deselects
  only that token; **Clear token selection** resets all highlights. The panel includes all tokens, sorted by value.
- Selecting a token automatically selects every account holding it. Click account IDs to exclude accounts
  from the batch swap or include them again. Token presence highlights remain visible for excluded accounts.
  Adding another token preserves manual exclusions; removing a token keeps accounts holding any remaining selected token.
  Clearing the token selection also clears its account selection and exclusions.
  Selected IDs are highlighted. **Clear accounts** resets this selection, and no selection disables the batch swap.
- **Swap selected → native** previews the combined contract selection only for selected accounts and networks.
  Click network headers in Treasury to select swap networks; click again to deselect. Selected headers and
  columns are highlighted, and the swap scope shows account IDs and networks. With no tokens selected in the
  sidebar, the action swaps all eligible tokens in that scope; otherwise it swaps only the selected symbols.
  With no explicit swap network selected, the action uses all networks visible through the current chain filter.
  Row swap buttons affect only their own account and use the same network scope.
  The server restricts both preview and execution to the selected account IDs.
  DeFi uses the same Treasury heatmap levels: below $1, $1–10, $10–100 and $100+; debt stays marked separately.
- **Swap token → native** previews the exact contracts, networks and accounts before confirmation.
- The queue obeys **Max ID**, selected networks and **Min. USD**, uses the selected Relay/LiFi service,
  and swaps each listed contract's full live balance to the native token in the same network.
- Symbols can refer to different contracts: review the contract addresses in the confirmation.
- Native tokens are skipped. **Exclude Stables** applies to the existing swap-all action;
  explicitly selecting a stablecoin allows swapping that selected token.
- Set the wallet PIN first. Execution reads the on-chain balance and reports swapped, failed and skipped positions.
- Successful swaps refresh the account snapshot; refresh failures are reported separately.
- All swaps to native compare total estimated fees (network gas, required approval and route fees)
  against the quoted minimum output value. Expensive swaps and quotes with incomplete cost data are skipped before execution.
- **Logs** opens a bottom overlay without reserving table space. Click outside it, press Escape or click Close to hide it.
- **Stop swaps** cancels active account swaps and the selected-token queue, including receipt waits and retry delays.
- **Gas +%** adds the selected percentage to the current RPC gas price for approvals, swaps and bridges.
  For example, 0.06 Gwei with +2% becomes 0.0612 Gwei. The saved default remains the previous +20%; 0 uses the network price.
  The execution preview freezes this setting, and the fee guard uses the same percentage.
  Broadcast transactions cannot be undone; their hashes remain in Logs for on-chain verification.
- Receipt checks switch to a BSC fallback RPC on access errors. Three consecutive RPC failures halt the queue,
  report the underlying error and transaction hash, and require checking the transaction before restarting.

- **Keyboard Shortcut**: Press `Ctrl + H` for help
- **Fast Navigation**: Click on any heatmap cell to see details
- **Batch Operations**: Select multiple chains for bulk processing
- **Theme**: Interface uses GitHub's dark theme for reduced eye strain
- **Performance**: Use chain filters to reduce data load

---

<p align="center">
  Made with ❤️ by w3bgr3p
</p>

<p align="center">
  <sub>Manage your crypto portfolio like a boss 🚀</sub>
</p>
