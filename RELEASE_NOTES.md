# z3nBank 1.0.0

## Treasury

- Redesigned the dashboard with consistent themes, readable heatmap cells and compact controls.
- Select multiple tokens from the right sidebar to highlight balances and select their accounts.
- Toggle account selection by clicking its ID; exclude individual wallets from token swaps.
- Persist complete balance snapshots and improve token amount and USD calculations.

## DeFi

- Discover positions through the free Rabby API without a subscription or API key.
- View an account-by-chain heatmap, protocol summaries and chain distribution in a dedicated tab.
- Save scan progress, positions and errors in the connected database and restore them after restart.
- Continue scanning after account errors, with bounded timeouts and cancellable scans.
- Preview and batch withdrawals for supported protocols and selected accounts.
- Add verified withdrawal adapters for selected Aave V3, Compound V3, LayerBank, Stargate,
  LFJ, Blackwing, SynFutures V3 and SyncSwap positions.
- Estimate Scroll LAB.s rewards using free LI.FI prices and check unlocked rewards separately.

## Transactions and usability

- Reject swaps when estimated fees exceed expected output and block uneconomic withdrawals.
- Configure a gas price increase in percent and stop pending operation queues.
- Improve receipt tracking, RPC aliases, native gas checks and L2 fee estimates.
- Keep transaction hashes when a broadcast outcome is uncertain and stop the queue.
- Send DeFi operations to the shared log drawer; use single-line log entries and centered dialogs.
- Keep database login errors in the form without clearing entered settings.
- Show Caps Lock warnings in database password and wallet PIN fields.

## Installation and limitations

Download **z3nBank_Setup_1.0.0.exe** for Windows x64. Installation is per user and does not require elevation.
Microsoft Edge WebView2 Runtime is required. SQLite additionally requires the SQLite3 ODBC driver;
PostgreSQL is also supported.

DeFi discovery uses DeBank estimates through Rabby. Displayed values do not guarantee a withdrawable balance.
Withdrawal support is limited to verified adapters; locked positions, vesting, liquidity restrictions and fees
can prevent an exit. Stopping a queue does not cancel transactions already broadcast on-chain.
