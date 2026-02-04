# 🏦 z3nBank
[RU]()

**Multi-chain cryptocurrency treasury management dashboard with GitHub-style heatmap visualization**

z3nBank is a powerful Windows desktop application for managing and visualizing multi-chain crypto portfolios. It provides real-time balance tracking, cross-chain bridging, token swapping, and an intuitive heatmap interface inspired by GitHub's contribution graph.

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
│  │   - REST API (Port 5000)      │  │
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
3. The system will automatically fetch balances

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
- System fetches latest balances from blockchain

**Auto-refresh:**
- Click "Auto: OFF" to toggle automatic updates
- Refreshes every 30 seconds when enabled

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

The embedded API server runs on `http://127.0.0.1:5000`

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
POST /api/treasury/update-balances
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
ASPNETCORE_URLS=http://127.0.0.1:5000
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
A: The application supports all EVM-compatible networks configured in your database, plus Solana (SOL) wallets.

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

- **Keyboard Shortcut**: Press `Ctrl + H` for help
- **Fast Navigation**: Click on any heatmap cell to see details
- **Batch Operations**: Select multiple chains for bulk processing
- **Theme**: Interface uses GitHub's dark theme for reduced eye strain
- **Performance**: Use chain filters to reduce data load

---

<p align="center">
  Made with ❤️ by the z3nBank Team
</p>

<p align="center">
  <sub>Manage your crypto portfolio like a boss 🚀</sub>
</p>