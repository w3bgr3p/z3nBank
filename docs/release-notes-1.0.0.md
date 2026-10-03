# z3nBank 1.0.0

## Updated installer and fixes

This update replaces the original 1.0.0 installer with the latest fixes.

### Treasury and balances

- Use RPC balances and token decimals for amounts; APIs supply token discovery and prices.
- Refresh both native and token balances after confirmed swaps, including positive balances without prices.
- Update only selected accounts when a selection exists; otherwise update all accounts.
- Select swap networks explicitly and show the account, chain and token scope before confirmation.
- Execute the confirmed token contracts and explain skipped positions and zero live balances in logs.
- Correct chain IDs, RPC aliases and unavailable providers, and resolve RPC settings by chain ID.
- Explain balance update failures, preserve previous data and stop on shared database outages.
- Persist protected database connection settings for subsequent launches.

### DeFi withdrawals

- Fix confirmation handling so rejected executions remain visible and startup errors reach shared logs.
- Allow operations on already scanned accounts while other accounts continue scanning.
- Show position previews on hover and open account actions on click.
- Support validated Rabby transaction actions with checked destinations, recipients and ABI parameters.
- Expand built-in adapters for lending, vaults, LP positions, staking, rewards and vesting.
- Cover Aave V2/V3, Compound V2/V3, LayerBank, Blackwing, Hana, Seamless, Beefy and Gearbox paths.
- Add Balancer, Curve, QuickSwap, SushiSwap, Uniswap V3, PancakeSwap V3, LFJ, Hop and Pendle exits.
- Add Stargate, SyncSwap, GMX, Lido, Stader, Sonne, Cygnus, Human Passport, EYWA, Sablier and Shell paths.
- Support Merkl and protocol reward claims where verified contract data is available.
- Persist pending withdrawal requests and reconcile them when the account is refreshed.
- Provide request, claim and GMX cancellation actions where the protocol requires separate stages.
- Add account-specific refresh after operations without requiring a complete portfolio rescan.
- Include prerequisite approvals, keeper payments and L1 costs in withdrawal previews.
- Check live balances, ownership, debt, liquidity and estimated fees before execution.

## Included in 1.0.0

- Consistent Treasury and DeFi heatmaps, protocol/token summaries and chain distribution.
- Multiple token selection from the Treasury sidebar and account selection through clickable IDs.
- Free Rabby discovery without a paid API subscription or API key.
- Saved DeFi scan results and bounded, cancellable scans that continue after account errors.
- Batch operations for selected accounts and supported positions.
- Configurable gas price increase, uneconomic-operation guards and stoppable queues.
- Shared log drawer with compact entries, centered dialogs and clearer disabled-action explanations.
- Visible database login errors and Caps Lock warnings for password and PIN fields.

## Installation and operational limits

Download **z3nBank_Setup_1.0.0.exe** for Windows x64. Installation is per user and does not require elevation.
Microsoft Edge WebView2 Runtime is required. SQLite additionally requires the SQLite3 ODBC driver;
PostgreSQL is also supported. **SHA256SUMS.txt** contains the installer checksum.

Position discovery and USD prices remain estimates. RPC amounts are checked for supported operations.
Withdrawal availability depends on protocol rules, maturity, liquidity, rewards funding and available gas.
Some positions require a request followed by a later claim; pending requests never execute automatically.
Manta withdrawal validation remains limited by public RPC rate limits. Support does not guarantee that every
position can be withdrawn immediately. Stopping a queue does not cancel transactions already broadcast.

Validation included automated DeFi, Rabby action, persistence and UI checks, plus read-only live contract
checks. Validation did not sign or broadcast transactions.
