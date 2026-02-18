# VFDProxy

A Windows COM port proxy that sits between a CNC sender (such as [Candle](https://github.com/Denvi/Candle)) and a GRBL controller, intercepting spindle commands and routing them to a **Huanyang VFD** over RS-485 while forwarding all motion G-code to GRBL.

## Why?

GRBL's PWM spindle output is insufficient for driving a Huanyang Variable Frequency Drive (VFD). The VFD requires its own serial protocol over RS-485. VFDProxy solves this by transparently splitting the G-code stream:

- **Motion commands** (G0, G1, arcs, feed rates, coordinates) are forwarded to the GRBL controller over USB/serial.
- **Spindle commands** (M3, M4, M5, S-word) are intercepted and translated into the Huanyang VFD's Modbus-like RS-485 protocol.
- **Mixed lines** (e.g. `G1 X10 F500 M3 S12000`) are split: motion goes to GRBL, spindle goes to VFD.

The CNC sender sees a normal GRBL device and requires no modification.

## Architecture

```
                        Candle (CNC Sender)
                              |
                     Virtual COM pair (com0com)
                              |
                    +------- VFDProxy --------+
                    |    GCodeParser           |
                    |    ProxyEngine           |
                    +-------+--------+---------+
                            |        |
                 USB/Serial |        | RS-485
                            |        |
                    GRBL Controller  Huanyang VFD
                    (motion control) (spindle control)
```

### Communication Flow

1. Candle connects to one end of a [com0com](https://com0com.sourceforge.net/) virtual COM pair.
2. VFDProxy reads G-code lines from the other end of the pair.
3. Each line is parsed and routed:
   - **ForwardToGrbl** -- motion commands sent to GRBL with character-counting buffer flow control.
   - **InterceptSpindle** -- spindle commands translated to VFD protocol and queued for sequential execution.
   - **InterceptToolChange** -- M6/T-word commands acknowledged and discarded.
   - **InterceptPause** -- M0/M1 acknowledged (configurable).
   - **InterceptCoolant** -- M7/M8/M9 optionally stripped.
   - **PassThrough** -- empty/comment lines acknowledged immediately.
4. GRBL responses (`ok`, `error:`, alarms) are forwarded back through the virtual COM pair to Candle.
5. VFD status (running, direction, frequency, fault) is polled every 2 seconds.

### Threading Model

| Thread | Purpose |
|--------|---------|
| Proxy loop | Reads lines from virtual COM, parses, dispatches |
| VFD loop | Drains VFD command channel sequentially (decouples RS-485 latency from GRBL pipeline) |
| Poll timer | Reads VFD status every 2 seconds |
| GRBL read loop | Reads GRBL responses via BaseStream for cancellation support |

## Features

- **G-code routing** -- transparent split of spindle vs. motion commands
- **GRBL buffer accounting** -- character-counting protocol prevents RX buffer overflow (127-byte limit)
- **Huanyang VFD protocol** -- CRC-16/Modbus framing, RS-485 direction control via RTS toggle
- **Manual spindle control** -- set RPM, direction (CW/CCW), and stop from the UI
- **Emergency stop** -- immediate VFD halt with port teardown
- **Auto-stop on disconnect** -- spindle stops when proxy shuts down
- **Real-time VFD monitoring** -- running state, direction, frequency, and fault status
- **Configurable job behavior** -- strip spindle, tool changes, pauses, coolant commands
- **Dark-themed WPF UI** -- color-coded log (sent/received/error/warning/debug), status indicators
- **Persistent configuration** -- saved as JSON in `%AppData%\VFDProxy\config.json` with atomic writes

## Prerequisites

- **Windows 10/11** (x64)
- **.NET 8.0 Runtime** (or use the self-contained publish)
- **[com0com](https://com0com.sourceforge.net/)** -- virtual null-modem COM port driver to create the port pair
- **USB-to-RS-485 adapter** -- for the Huanyang VFD connection
- **GRBL controller** connected via USB/serial

## Setup

1. **Install com0com** and create a virtual COM pair (e.g. `COM20` <-> `COM21`).
2. **Connect hardware:**
   - GRBL controller via USB (e.g. `COM3`)
   - RS-485 adapter to VFD (e.g. `COM5`)
3. **Launch VFDProxy** and configure:
   - Virtual COM Candle port = `COM20` (the side Candle connects to)
   - Virtual COM Proxy port = `COM21` (the side VFDProxy reads from)
   - GRBL port and baud rate (default 115200)
   - VFD port, baud rate (default 9600), and slave address (default 1)
4. **Configure VFD parameters:**
   - Max RPM, Min/Max Hz, pole pairs for your motor
   - M4 direction behavior (CW or CCW)
5. **Point Candle** at the virtual COM port (`COM20`) and run jobs normally.

## Configuration

Settings are persisted to `%AppData%\VFDProxy\config.json`.

| Setting | Default | Description |
|---------|---------|-------------|
| `VirtualPortCandle` | -- | COM port Candle connects to |
| `VirtualPortProxy` | -- | COM port VFDProxy reads from |
| `GrblPort` | -- | GRBL controller COM port |
| `GrblBaud` | 115200 | GRBL baud rate |
| `VfdPort` | -- | VFD RS-485 COM port |
| `VfdBaud` | 9600 | VFD baud rate |
| `VfdSlaveAddr` | 1 | Modbus slave address |
| `PolePairs` | 1 | Motor pole pairs (1 = 2-pole motor) |
| `MaxRpm` | 24000 | Maximum spindle RPM |
| `MinHz` | 5.0 | Minimum VFD frequency (Hz) |
| `MaxHz` | 400.0 | Maximum VFD frequency (Hz) |
| `M4IsCcw` | false | Whether M4 runs counter-clockwise |
| `StripSpindleCommands` | true | Intercept M3/M4/M5/S commands |
| `StripToolChanges` | true | Discard M6/T-word commands |
| `TreatM0M1AsPause` | true | Acknowledge M0/M1 without pausing |
| `StripCoolantCommands` | false | Discard M7/M8/M9 commands |
| `AutoStopOnDisconnect` | true | Stop spindle when proxy disconnects |

### RPM to Frequency Conversion

```
Frequency (Hz) = RPM * PolePairs / 60
```

For a 2-pole motor (1 pole pair) at 24000 RPM: `24000 * 1 / 60 = 400 Hz`.

## Huanyang VFD Protocol

VFDProxy implements the Huanyang-specific Modbus-like protocol:

```
Frame: [SlaveAddr] [FuncCode] [DataLen] [Data...] [CRC_Lo] [CRC_Hi]
CRC:   CRC-16/Modbus (polynomial 0xA001, init 0xFFFF)
```

| Function | Code | Data | Purpose |
|----------|------|------|---------|
| Control | 0x01 | 0x01 | Run clockwise |
| Control | 0x01 | 0x02 | Run counter-clockwise |
| Control | 0x01 | 0x08 | Stop |
| Read | 0x03 | 0x00 0x01 | Read status register |
| Write | 0x05 | Freq_Hi Freq_Lo | Set frequency (units of 0.01 Hz) |

RS-485 direction is controlled via RTS: `true` = transmit, `false` = receive, with a 7 ms drain delay after TX.

## Building

```bash
dotnet build VFDProxy.sln
```

### Publish as self-contained single-file executable:

```bash
dotnet publish VFDProxy/VFDProxy.csproj -c Release
```

Output: a single `VFDProxy.exe` for Windows x64 (no .NET runtime required on target machine).

## Project Structure

```
VFDProxy/
  Models/          Configuration, parsed G-code, state enums, log entries
  Parsing/         Single-pass G-code parser with routing logic
  Drivers/         Serial port drivers (VirtualCom, GRBL, VFD)
  Engine/          Central ProxyEngine orchestrator
  Services/        Config persistence, COM port enumeration
  ViewModels/      MVVM view models for WPF UI
  Views/           WPF windows and value converters
  Infrastructure/  Relay commands, dispatcher service
```

## License

See LICENSE file for details.
