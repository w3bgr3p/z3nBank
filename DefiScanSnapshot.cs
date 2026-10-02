namespace z3nSafe;

public sealed record DefiScanAccount(int Id, string Address, string Status);
public sealed record DefiScanSnapshot(int Version, int Processed, int Total, DateTimeOffset? UpdatedAt,
    bool Cancelled, bool NeedsRescan, DefiPosition[] Positions, DefiScanAccount[] Accounts, object[] Errors);
