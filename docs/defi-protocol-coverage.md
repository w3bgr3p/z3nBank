# DeFi withdrawal coverage

Discovery uses Rabby's free public API; balances, ownership and withdrawal limits are checked against RPC.
Protocol actions use fresh simulation and exact token quantities before confirmation and again before signing.
No paid API key is required. Deployment support is explicit; this is not a promise to execute every contract
ever deployed under a protocol's name.

| Protocols / observed deployments | Exit route |
| --- | --- |
| Aave V2 Polygon; Aave V3 Ethereum, Base, Polygon, Metis, Gnosis, Scroll | Withdraw supplied reserves; block active debt; separate verified incentive claims |
| Hana Finance Taiko; Seamless Base | Verified Aave fork withdrawals; separate SEAM/esSEAM claims and vesting |
| Compound V2 Ethereum | Live cToken redemption, debt guard and COMP claims |
| Compound V3 Arbitrum, Base, Polygon, Scroll | Live base/collateral withdrawal; verified reward controllers |
| LayerBank Scroll / Manta | Lending redemption; accrued LAB.s claim into vesting and unlocked withdrawal; mature Manta locks |
| Blackwing Arbitrum / BSC | Verified vault implementation, live shares, underlying and lock checks |
| SynFutures V3 Blast | Free Gate deposits; does not close trading margin |
| Hana Network Arbitrum, Base, Polygon, Optimism | Live native deposits and contract withdrawal permission |
| Beefy Optimism / Linea | Verified Vault V7 share withdrawal; underlying identity and actual simulated output |
| Gearbox Ethereum | Verified v2 diesel-token redemption; free official Merkle GEAR claim |
| Balancer V2 Ethereum | Verified Rabby Vault exit action, exact approvals and simulated underlying outputs |
| Curve Ethereum / Polygon | Live LP removal with protected minima; verified sETH pool can exit into ETH only |
| QuickSwap Polygon; SushiSwap Ethereum / Polygon | Verified factory pairs, exact LP approval and dual-token removal |
| Uniswap V3 Arbitrum; PancakeSwap V3 BSC | NFT ownership, live liquidity, atomic removal/collection and farm rewards |
| LFJ Arbitrum | sJOE withdrawal/rewards; verified bin LP burn into both tokens |
| Hop Polygon USDT | Unstake/claim, exact LP approval and removal into canonical USDT |
| Pendle V2 Arbitrum | Expired PT/LP exit into underlying tokens; separate reward claim |
| Stargate Ethereum/BSC/Arbitrum/Optimism locks; Base/Optimism/Linea LP | Mature escrow withdrawal and verified local pool redemption |
| SyncSwap zkSync Era | Verified classic LP burn, both output minima and exact approval |
| GMX v1 Arbitrum | Live GLP redemption through a liquid output token; separate ETH rewards |
| GMX V2 Arbitrum | GM request with protected minima and keeper fee; pending request check and separately confirmed cancellation |
| Lido Ethereum | stETH request, withdrawal NFT finalization and claim |
| Stader Polygon | MaticX request, bonding period and mature request claim |
| Sonne Optimism | Stake burn/cooldown, mature claim and separate rewards |
| Cygnus Base | Verified wcgUSD unwrap into cgUSD |
| Human Passport Ethereum | Mature GTC round stake withdrawal; repeat for additional rounds |
| EYWA / Sablier Arbitrum | Verified vesting release / owned stream withdrawal |
| Shell V3 Arbitrum | Expired SHELL lock withdrawal |
| Merkl Ethereum, Arbitrum, Base, Polygon, Optimism | Current public proof, claimed amount and distributor checks |

## Confirmation and queues

Preview lists the wallet, outputs, total fee and prerequisites. An asynchronous request is explicitly labelled
as a future claim: approval, request and keeper payments are included, but later claim fees must be checked
again when the protocol makes funds available. Requests are saved with the database and wallet identity.
Refresh checks their state; an RPC failure preserves the saved request. Restarting never signs or broadcasts.
GMX cancellation returns GM tokens, rather than underlying coins, and obeys the protocol cancellation delay.

LP routes enforce on-chain minima where the verified contract supports them. Legacy Beefy, Gearbox v2 and
LFJ bin burns lack a minimum-output parameter; their notice states this and execution rechecks simulation.
Batch selection retains separate reserves, reward assets, NFT IDs and request/claim phases.

## Conditions that prevent execution

An adapter cannot override a contract's lock, paused transfer, missing liquidity, insufficient reward funding,
active collateral debt, failed simulation or missing wallet gas. These conditions produce a specific explanation.
Fees at or above the received value block execution, including required prerequisite fees. Unknown prices do
not become zero-priced withdrawals. Provider amounts alone never establish what can be withdrawn.

The observed portfolio was checked with unsigned RPC calls and transaction simulations. This validates
transaction preparation, not actual mining or later keeper execution; no validation run signs or broadcasts.
Protocol state and public RPC availability change, so checks are repeated on each user operation.
