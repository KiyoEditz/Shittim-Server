# Shittim Server

A private server for Blue Archive's Steam release, written in C# on ASP.NET Core (.NET 10). Progress lives in a local SQLite database, and it's far enough along that the game is just playable: log in, pull, clear stages, decorate the cafe.

Questions, bugs, support, or anything else: https://discord.gg/GANwPn9xX6

## Features

- Play without touching the official servers
- Pull on gacha banners with the real rates or custom rates, or set up whatever banner you want from the Control Center
- Replay the koyuki incident
- See hidden game notices
- Replay any old event and minigame
- Clear campaign stages: normal, hard, extra, sweeps, and strategy maps with a working enemy phase
- Decorate the cafe, save and load presets, and get rotating visitors to invite
- Claim daily/weekly missions, achievements and attendance rewards
- Craft, open item boxes and select tickets, and spend in the shops (AP, eligma, secret stones)
- Read the story at your own pace, or unlock all of it with one button
- Give yourself any student, item or currency through the admin panel, and send yourself mail
- Run as many accounts as you like from one install


## Installation

Grab Shittim Control Center from the [releases page](https://github.com/Neoexm/Shittim-Server/releases). It handles the whole setup: downloads the server, installs the .NET 10 SDK and mitmproxy if they're missing, and trusts the proxy's CA certificate.

The Control Center acts as the admin panel. Accounts, inventory, mail, gacha, events, and all other features can be found there. The console also keeps itself, as well as the server, fully up to date.


## How to Start & Stop Properly

### 🚀 Starting the Game (Correct Order)
Always start the private server **before** launching the game:

1. **Open Steam**: Make sure Steam is running and logged in (offline mode is fine, as the game requires Steam SDK initialization).
2. **Open Shittim Control Center**.
3. **Start the Server**: Click **Start Server** (or **Start Offline** if playing in offline mode).
4. **Wait for Server & Proxy to be Ready**:
   - Check the status at the bottom: wait until both **Server: Running** and **Proxy: Running** are displayed.
   - The server will automatically patch the client gateway RSA public key inside `global-metadata.dat` and initialize `mitmproxy` routing.
5. **Launch Blue Archive from Steam**:
   - The game will unpack resources, connect to the local server, and take you straight to the title screen. Click **"CLICK TO START"** to enter the lobby.

### 🛑 Stopping the Game (Correct Order)
Always close the game **before** stopping the server:

1. **Exit Blue Archive First**: Close the game normally or press `Alt + F4` so it returns to the desktop.
   > **Note**: While the game is running, Unity IL2CPP keeps `global-metadata.dat` locked in memory. Stopping the server while the game is still open can cause the restore operation to fail (`file in use`).
2. **Stop the Server via Control Center**: Click the red **■ Stop** button in the bottom-right corner of Shittim Control Center.
   - This performs a clean shutdown:
     - Automatically restores `global-metadata.dat` back to the official Nexon gateway public key.
     - Restores the Windows `hosts` file (removes redirect entries if offline mode was used).
     - Terminates `mitmproxy` and the server process safely.
3. **Verify Status**: Wait until the status changes to **Server: Stopped** and **Proxy: Stopped** before closing the Control Center.


## Troubleshooting

### 1. Stuck on "Unpacking game resources"
- **Cause**: The game's `global-metadata.dat` was left in a patched state while the server is stopped (e.g. after a sudden crash, forced shutdown, or closing the server before exiting the game), or the server cannot find `GatewayPrivateKey.pem`.
- **How to Fix**:
  1. Stop the server in Control Center (if running).
  2. Open Steam Library ➔ Right-click **Blue Archive** ➔ **Properties** ➔ **Installed Files** ➔ Click **Verify integrity of game files**.
  3. Steam will automatically re-download the clean official `global-metadata.dat`.
  4. Ensure `GatewayPrivateKey.pem` exists in `Shittim-Server/Config/` (verify in Control Center Environment Readiness).
  5. Start the server in Control Center first, wait until `Running`, then launch the game.

### 2. Notice Popup Error (140001) at Title Screen
- **Cause**: An authentication response mismatch during Toy SDK / IAS web token ticket issuance.
- **How to Fix**:
  - Ensure your server build is up-to-date. The server must provide integer `errorCode` / `code` (numeric `0`, not string `"0"`) and include both `access_token` and `expires_in` in `IssueTicketByWebToken`.
  - Rebuild the server via `dotnet build -c Debug` or click **Rebuild** in Control Center Updates page.

### 3. Missing CA Certificate or Proxy Redirects
- Check the **Environment readiness** tab in Shittim Control Center. If the CA certificate or mitmproxy shows a warning, click **Install missing** to re-trust the certificate.


## Disclaimer

For educational and research purposes only. Not affiliated with Nexon.

