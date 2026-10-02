# RPC audit — 2026-10-02

Read-only probes from the application host. No signing or broadcasts.

30 configured endpoints checked: 29 answered successfully; ZERO returned
`Node is not available`. Availability is a point-in-time result.

All 25 available EVM networks also passed through the application's Nethereum
`TreasuryRpcBalances.Read` implementation, checking chain ID and native balances
at an explicit block. ERC-20 `balanceOf` succeeded on 23 networks. Monad and
Neura testnets passed native reads and generic `eth_call`; no deployed ERC-20
fixture was used on those two testnets. Aptos ledger and all three Solana
`getHealth` endpoints answered successfully.

| Network | Chain ID | Live result |
|---|---:|---|
| Ethereum | 1 | Native and ERC-20 reads passed |
| Arbitrum | 42161 | Native and ERC-20 reads passed |
| Base | 8453 | Native and ERC-20 reads passed |
| Celo | 42220 | Native and ERC-20 reads passed |
| Blast | 81457 | Native and ERC-20 reads passed |
| Fantom | 250 | Native and ERC-20 reads passed; replaced expired TLS endpoint |
| Linea | 59144 | Native and ERC-20 reads passed |
| Manta | 169 | Native and ERC-20 reads passed |
| Optimism | 10 | Native and ERC-20 reads passed |
| Scroll | 534352 | Native and ERC-20 reads passed |
| Soneium | 1868 | Native and ERC-20 reads passed |
| Taiko | 167000 | Native and ERC-20 reads passed |
| Unichain | 130 | Native and ERC-20 reads passed; corrected ID from 1301 |
| zkSync Era | 324 | Native and ERC-20 reads passed |
| Zora | 7777777 | Native and ERC-20 reads passed; replaced expired TLS endpoint |
| Avalanche | 43114 | Native and ERC-20 reads passed |
| BSC | 56 | Native and ERC-20 reads passed |
| Gravity Alpha | 1625 | Native and ERC-20 reads passed; uses Alpha RPC, not L1 ID 127001 |
| Gnosis | 100 | Native and ERC-20 reads passed |
| opBNB | 204 | Native and ERC-20 reads passed |
| Polygon | 137 | Native and ERC-20 reads passed |
| Mantle | 5000 | Native and ERC-20 reads passed |
| Sepolia | 11155111 | Native and ERC-20 reads passed; replaced rate-limited endpoint |
| Monad testnet | 10143 | Native reads passed; corrected ID from 41454 |
| Neura testnet | 267 | Native reads passed; corrected ID from 999999 |
| ZERO | 543210 | Unavailable; RPC lookup reports the reason, preserves previous data |

Reproduce: `python tests/rpc-audit.py`, then run the BalanceChecks executable
with `--rpc-audit-live bin/TokenValidation/rpc-audit.json`.
The Python audit exits nonzero on unexpected failures and keeps ZERO visible
as an expected unavailable endpoint. Raw reports are saved under ignored `bin/`.
