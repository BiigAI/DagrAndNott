# DagrNott_CustomDayCycle

> **DagrNott_CustomDayCycle is a lightweight mod designed to customize and balance Valheim's day, night, dawn, and dusk cycle pacing.**

### About the Name: *Dagr & Nótt*
In Norse mythology, **Dagr** (the personification of Day) and his mother **Nótt** (the personification of Night) ride their celestial chariots across the heavens to drive the progression of time. In this mod, *Dagr & Nótt* empowers you to control the speed of the cosmos, fine-tuning the duration of daylight, nightfall, dawn, and dusk.

---

## Features
- **Granular Phase Multipliers**: Customize the speed and duration of Dawn, Day, Dusk, and Night independently.
- **Extended Balanced Daytime**: Defaults to a balanced ~60-minute cycle with extended daylight (~30m) and immersive night (~20m).
- **Synchronized Client Visuals**: Synchronizes visual cycle pacing across all clients via Jotunn without altering underlying network or water simulation clocks.

---

### Installation Type
- **Location:** Must be installed on both the Server and the Client.
- **Enforcement:** Client versions must match the server version.

### Manual Install
1. Ensure BepInEx and Jotunn are installed.
2. Extract the downloaded `.zip` archive.
3. Copy `DagrNott_CustomDayCycle.dll` into your `Valheim/BepInEx/plugins/` folder.
4. Launch the game once to generate the default configuration file.

---

## Configuration
The configuration file is automatically created at `BepInEx/config/com.bigai.dagrnott_customdaycycle.cfg` after running the game once.

| Section | Setting | Default | Description |
| :--- | :--- | :--- | :--- |
| `DayCycle` | `DawnMultiplier` | `0.90` | Visual time speed multiplier during Dawn (~5.0 mins). Lower values make Dawn last longer. |
| `DayCycle` | `DayMultiplier` | `0.50` | Visual time speed multiplier during Day (~30.0 mins). Lower values make Day last longer. |
| `DayCycle` | `DuskMultiplier` | `0.90` | Visual time speed multiplier during Dusk (~5.0 mins). Lower values make Dusk last longer. |
| `DayCycle` | `NightMultiplier` | `0.30` | Visual time speed multiplier during Night (~20.0 mins). Lower values make Night last longer. |
| `Logging` | `LogPhaseTransitions` | `true` | Log phase transition events to the server console. |

---

## Controls & Commands
- **Keybinds:** None.
- **Admin Commands:** *(Chat or Console; requires admin permissions in `adminlist.txt`)*
  - `/dn` *(aliases `/dn status`, `/dagrnott`, `/dagrandnott`)*: Displays current day phase, speed multiplier, day progress %, and cycle configuration.

---

## Compatibility & Safe Removal
- **Multiplayer:** Must be installed on both server and clients with Jotunn.
- **Save Integrity:** Safe to add or remove mid-playthrough. DagrNott_CustomDayCycle does not alter underlying world save files or network physics.

### AI Disclosure 

I made this mod using AI. Most of the code in this mod was AI generated. If you have an issue with this, I completely understand and urge you to not use this mod. This mod ("DagrNott_CustomDayCycle") is meant as a lightweight mod for small servers that don't need all the bells and whistles of a more complex mod.
