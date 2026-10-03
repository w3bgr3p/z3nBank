# z3nBank 1.0.0

The first public release of z3nBank, a Windows desktop app for managing EVM wallets and DeFi positions.

## Features

- Multi-account Treasury with balance heatmaps, token summaries and chain distribution.
- Token discovery through APIs, with wallet amounts checked directly on-chain.
- Select accounts, tokens and networks for batch swaps to native assets and cross-chain bridges.
- Free DeFi position discovery through Rabby, with protocol summaries and saved scan results.
- Preview and execute supported withdrawals, unstaking, LP exits and reward claims.
- Track pending withdrawal requests, claim available funds and cancel eligible GMX requests.
- Configure gas increases, review estimated fees and stop operation queues.
- Shared transaction logs, protected database settings and wallet PIN controls.
- PostgreSQL and SQLite storage.

## Installation

Download **z3nBank_Setup_1.0.0.exe** for Windows x64. Installation does not require administrator rights.
Microsoft Edge WebView2 Runtime is required; SQLite also requires the SQLite3 ODBC driver.
Verify the installer using **SHA256SUMS.txt**.

DeFi availability depends on supported contracts, lock periods, liquidity, gas and RPC access.
Prices are estimates. Stopping a queue does not cancel transactions already sent.
