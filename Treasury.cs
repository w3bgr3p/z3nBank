namespace z3nSafe;

public  class Treasury
{
    private readonly List<Token> _tokens;

    public Treasury()
    {
        _tokens = new List<Token>();
    }
    public IReadOnlyList<Token> Tokens => _tokens;
    public class Token
    {
        public string Symbol { get; set; }
        public string AmountRaw { get; set; } 
        public int Decimals { get; set; }
        public string PriceUSDString { get; set; }
        public int ChainId { get; set; }
        public string Address { get; set; }
        public decimal ValueUSD { get; set; }
        
        public decimal GetAmountDecimal() => 
            decimal.TryParse(AmountRaw, out var val) ? val : 0m;
    }

    public void AddToken(Token newToken)
    {
        
        var existingToken = _tokens.FirstOrDefault(t => 
            t.ChainId == newToken.ChainId && 
            t.Address.Equals(newToken.Address, StringComparison.OrdinalIgnoreCase));

        if (existingToken != null)
        {
            
            var totalAmount = existingToken.GetAmountDecimal() + newToken.GetAmountDecimal();
            existingToken.AmountRaw = totalAmount.ToString("F0"); 
            
            existingToken.ValueUSD += newToken.ValueUSD;
            
            existingToken.PriceUSDString = newToken.PriceUSDString;
        }
        else
        {
            _tokens.Add(newToken);
        }
    }
}