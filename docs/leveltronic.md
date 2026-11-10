# Leveltronic instrument — integration reference

How LevelApp talks to the **Leveltronic** precision electronic level (firmware repo
`InclinationMeterFirmware`, product string `InclinationMeter`, STM32G0B1 + RN4871), what the app does
with it, and how it is tested. The plugin architecture in general is in
[`architecture.md`](architecture.md) §12; this page is the device-specific companion.

**State:** implemented in v0.20.0 – v0.20.4, **not yet run against real hardware** (see §11).

References (kept in the other repositories):

| What | Where |
|---|---|
| Protocol (framing, verbs, validation, subscriptions, service mode, transports) | firmware `docs/api-v3-spec.md` |
| Every resource, opcode and payload layout | firmware `docs/api-reference.md` (generated from `tools/api_spec.py`) |
| Reference host library | firmware `PythonTestCode/apiv3.py`, `apiv3_defs.py` |
| Reference client with the same feature set | phone app `LevelAppMobile` (`lib/devices/leveltronic/`) |

Only **firmware 0.11 and later (API v3)** is supported. v3 was a deliberate one-time break from v2; a v2
firmware is refused on connect ("This instrument speaks API v2 … needs API v3").

---

## 1. Transports

| | USB | BLE |
|---|---|---|
| Link | Custom HID, VID `0x04D8` / PID `0xF08F` | RN4871 Transparent UART |
| Framing | fixed 64-byte reports; a packet starts at a report boundary and occupies `ceil((6+LEN)/64)` reports, last zero-padded | raw byte stream, reassembled by `LEN` |
| Discovery | `UsbHidDeviceScanner` (VID/PID) | `BleDeviceScanner` by **name prefix** `Leveltronic` — the module does not advertise the service UUID |
| GATT | — | service `49535343-FE7D-4AE5-8FA9-9FAFD205E455`, write `…8841-43F4-A8D4-ECBE34729BB3`, notify `…1E4D-4BD9-BA61-23C647249616` |
| Code | `UsbHidLeveltronicLink` + `HidReportReassembler` | `BleLeveltronicLink` + `PacketReassembler` (+ `BleConnectionManager`) |

BLE is expected to be the common choice. Both links implement `ILeveltronicLink` (framed packets in/out,
`ConnectionChanged`); everything above is transport-agnostic.

## 2. Protocol in one page

```
[OPCODE 2B LE][LEN 2B LE][PAYLOAD 0..LEN][CRC16 2B LE]      LEN <= 122, total <= 128
OPCODE = [VERB:4][CATEGORY:4][RESOURCE:8]
CRC16  = CRC-16/CCITT-FALSE over opcode + length + payload
```

* Every response echoes the request opcode and starts its payload with a status byte
  (`0x00 OK` … `0x0B SERVICE_MODE_REQUIRED`); data follows only on OK.
* SUBSCRIBE is acknowledged with a status byte; pushes follow under the SUBSCRIBE opcode as
  `[OK][issue_seq][page][data]`. Interval topics take `u32 interval_ms`; event topics (LIVE,
  PHASOR_STREAM, Procedures) push when there is something new.
* Categories: System 0, Commands 1, Calibrations 2, Settings 3, Measurements 4, Topics 5, Debug log 6,
  Diagnostics 7, Bulk 8, Procedures 9. Every state-changing resource has an id ≥ 0x40, so a stale v2
  client can never trigger an action by accident.
* **Service mode** gates all Calibrations writes and the maintenance commands (ZERO_CAL, FACTORY_DEFAULTS,
  REBOOT_DFU, …) with status `0x0B`. It can only be switched on **at the instrument** (SETTINGS → Service
  mode, right knob twice); the app can observe it (STATUS `service_mode`) and end it (SERVICE_END). It ends
  by itself after 10 minutes without any request, so the app sends a cheap request every 30 s while a
  calibration surface is open (`LeveltronicSession.KeepServiceModeAlive`).

Resources the app uses (ids in `LeveltronicApi`, rows in `ValueCatalog`):

| Area | Resources |
|---|---|
| System | IDENTITY `0x00` (50 B: fw, `api_version`, `max_payload`, product, serial, build), STATE `0x01`, HEALTH `0x02`, RTC `0x03` |
| Topics | LIVE `0x02` (tilt S1/S2/Diff in mm/m + flags), STATUS `0x01`, ENV `0x00`, SIGNAL_DIAG `0x05`, PHASOR_STREAM `0x04` |
| Procedures | ZERO_CAL `0x00`, PRECISION `0x01` (status + result, subscribable) |
| Commands | POWER_OFF `0x40`, REBOOT `0x41`, REBOOT_DFU `0x42`*, FORCE_CHARGE `0x43`, END_CHARGING `0x44`, CHARGE_INHIBIT `0x45`, TILT_DEMOD `0x50`, ZERO_CAL `0x51`*, PRECISION `0x52`, FACTORY_DEFAULTS `0x60`*, CLEAR_COUNTERS `0x61`, SERVICE_END `0x62`, TEST_BEEP `0x70` (* service mode) |
| Settings | auto power-off, charge/battery thresholds, encoder, task periods (`0x40`–`0x61`) |
| Calibrations | S1/S2 k, zero, phase, invert (`0x40`–`0x47`), battery divider, clock trim, temperature sensors — all service-gated |

## 3. Layers in code

```
LevelApp.Instruments.Leveltronic  (headless, tested)             LevelApp.Instruments.Leveltronic.UI  (WinUI)
  ApiV3Codec / PacketReassembler                                   LeveltronicPlugin (DI singleton)
  ILeveltronicLink  ← Usb/Ble links                                LeveltronicManagementView  (tabs)
  LeveltronicDeviceClient  (requests + subscriptions)              CalibrationHubView + WizardPageView
  InstrumentApi  (typed operations)                                Flip/Phase/GainCalViewModel → WizardPage
  LiveFeed · PrecisionRun
  LeveltronicSession / SessionManager (leases)
  LeveltronicProvider : IInstrumentProvider, ILiveReadingSource
  LeveltronicFirmwareUpdater : IFirmwareUpdater
  Flip/Phase/GainCalWizard · CalibrationMath · Preferences
```

* **Client.** Requests are keyed by the echoed opcode; a second request for an opcode that is still
  waiting fails the older one ("superseded") so a late reply is never mistaken for the newer one's;
  default timeout 3 s. One device subscription is shared between all listeners of an opcode and ended
  (UNSUBSCRIBE) when the last leaves. A link loss fails pending requests and raises `Closed`.
* **Session / manager.** See `architecture.md` §12 "Shared session". `LeveltronicSession.OpenAsync`
  connects the link, verifies IDENTITY (`IncompatibleFirmwareException` for API ≠ 3,
  `UnsupportedDeviceException` for another product) and starts the `LiveFeed` (LIVE + STATUS + ENV merged
  into an `InstrumentSnapshot`; restarts the tilt demodulation once if a previous host stopped it).
* **Time.** Everything with a clock (timeouts, 1 s precision-run ticks, 8 s phase captures, 30 s
  keep-alive, BLE backoff) takes a `TimeProvider`, which is how the tests run in simulated time.

## 4. Readings and units

The instrument reports tilt in **mm/m** (S1, S2, Diff = S1 − S2; flags: valid, sensor 1/2 doubtful =
vibration). The app side works in **µm/m**, the unit of the guided measurement's reading box, so
`LeveltronicProvider` multiplies by 1000.

* **Channel** — S1, S2 or Diff, chosen in the measurement panel, remembered per serial. Default Diff.
* **Live value** — from the LIVE topic (~4 Hz); "unsteady" while the selected channel is doubtful.
* **Take reading / `MeasureAsync`** — Commands PRECISION: one value from the first clean 2 s window within
  5 s, tracked through the Procedures PRECISION subscription (stale `done` ignored, 1 s fallback poll,
  9 s timeout). No clean window → `InstrumentMeasurementException` ("Keep the table still and measure
  again").
* **Zero Diff** — an **app-side** offset (the firmware has no such function; its zero is a per-sensor
  calibration and moving it would change S1 and S2 too): a precision measurement of Diff, stored per serial,
  subtracted from Diff for the live value, Take reading and `GetReadingAsync`. S1 and S2 are untouched. The
  panel shows "Zeroed at x µm/m".

## 5. Management view (Instruments → Leveltronic)

Connect / Disconnect on a shared session, a service-mode card (with Leave), and tabs:

| Tab | Content |
|---|---|
| Info | battery, temperatures, humidity/pressure, clock vs PC (Set to PC time), identity/serial/firmware/build/API, activity flags, health (uptime, reset cause, EEPROM self-test, subsystem flags, dropped frames, last fault); RTC polled 1 s and HEALTH 5 s while visible |
| Settings | catalog rows with range validation, per-row Read / Write; optional resources hidden when the firmware answers UNKNOWN_RESOURCE |
| Actions | power off, reboot, force / end charge, inhibit / allow charging, test beep, clear counters, leave service mode, reboot to DFU, factory defaults (the destructive ones confirm) |
| Calibration values | all calibration rows; reading is always allowed, writing needs service mode |
| Diagnostics | SIGNAL_DIAG (subscribed only while the tab shows): RMS, peak-to-peak, phase per converter channel, theoretical tilt |

`SERVICE_MODE_REQUIRED` anywhere becomes the instruction to switch service mode on at the instrument.

## 6. Calibration wizards (Instruments → Leveltronic → Calibration)

Run **phase → gain → zero** (each assumes the one before). All three behave as on the phone app: sensor
choice S1 / S2 / both, step headers, "hold still" progress, before/after tables, verdict cards, and a
service-mode card that continues by itself once service mode is switched on.

| Wizard | What it does | Math (`CalibrationMath`) |
|---|---|---|
| **Phase** | needs no neutral measurement: capture the PHASOR_STREAM (wire order S2, B, A, S1; 8 s, >= 60 batches) with a clear **positive** and a clear **negative** tilt, or with just one of them (the operator chooses `PhaseMode`: `+ and -` / `+ only` / `- only`). With u = S/(A-B) a positive tilt points at the sensor's signal phase, a negative one at phase + 180 deg. Both tilts: the direction of u+ - u- (the zero offset cancels, most accurate); one tilt: the direction of u itself (turned 180 deg for the negative one; flagged "one tilt only"). Full +-180 deg; independent of the stored phase and the tilt size. An inverted sensor (INVERT = 1) gets phase + 180 deg so the tilt reads positive. Writes 0.01 deg | `EstimatePhaseFromTilts`, `WrapDegrees`, `PhaseEstimate.Rotated` (Welford stats, uncertainty, SNR, confidence) |
| **Gain** | precision measurement without and with a shim of known height under an edge of known length; `kNew = kOld·|Δreading| / tilt`; saving **only writes k** (nothing is measured again); shim/edge values remembered per serial | `EstimateGain`, `ShimTilt` |
| **Zero (flip)** | Commands ZERO_CAL step 1 → operator turns 180° → step 2; the firmware stores the zero; before/after shown, repeat runs compared | — (firmware) |

The wizards are UI-free state machines in the headless project (`FlipCalWizard`, `PhaseCalWizard`,
`GainCalWizard`): an immutable state record, a `StateChanged` event, and a generation counter that discards
late async results of a cancelled or restarted run. The `.UI` view-models turn each state into a
`WizardPage` (title, step text, instructions, progress, tables, verdicts, actions) that one generic
`WizardPageView` renders.

The phase range is the firmware's +-180 deg (-18000..18000 x 0.01 deg), as on the phone app. Starting the phase or gain wizard needs service mode (the button stays disabled until the service-mode card reports it).

## 7. Preferences

`%LOCALAPPDATA%\LevelApp\leveltronic.json` (best effort — unreadable or unwritable just means values are
not remembered), keyed by instrument serial: Zero Diff offset (mm/m), selected channel, gain-wizard shim
height and edge length per sensor. Interface `ILeveltronicPreferences`; `JsonLeveltronicPreferences` and
`InMemoryLeveltronicPreferences`.

## 8. Firmware update

`LeveltronicFirmwareUpdater` (USB only): REBOOT_DFU (service mode) → `DfuConnectionDetector` (VID `0x0483` /
PID `0xDF11`) → `DfuSession`. **Caveat:** the firmware's reboot-to-DFU programs the `nBOOT0` option byte, so
the device stays in the ROM bootloader on every boot until reflashed with `nBOOT0 = 1` restored
(`dfu_flash.ps1` / `STM32_Programmer_CLI -ob nBOOT_SEL=1 nBOOT0=1`); `DfuSession` does not touch option
bytes. There is no update feed (`CheckForUpdateAsync` → `null`) and nothing sets `FirmwareImageProvider`, so
the standard dialog reports "up to date"; the Reboot-to-DFU action in the management view carries the
warning.

## 9. Tests

`LevelApp.Tests/Leveltronic/` — 133 tests; the test project references only the headless project.

* `SimulatedLeveltronicLink` is a C# port of the phone app's simulated instrument: API v3 at frame level,
  sensor physics (excitation phasors, k / zero / phase / invert), the flip and precision state machines,
  service-mode gating with its 10-minute timeout, settings ranges and cross-field rules, topic subscriptions.
  Its stored calibration is deliberately off the "true" values so the wizards have something to correct.
* `FakeTimeProvider` drives timers; `TestKit.AdvanceUntilAsync` advances in small steps and lets
  continuations run.
* Codec vectors are cross-checked against `PythonTestCode/apiv3.py`; calibration math and wizard tests are
  ported case-for-case from the phone app's `calibration_math_test.dart` and `wizards_test.dart`.
* Not covered by automated tests: the WinUI views, the BLE/HID transports against real hardware.

## 10. Changing the API / adding a device

* **Firmware API changed** — regenerate from `tools/api_spec.py`, then update `LeveltronicApi` (ids),
  `LeveltronicModels` (decoders), `ValueCatalog` (rows) and the simulator; the codec tests will flag drift.
* **New device** — follow the headless + `.UI` split (architecture.md §12). A device that reports live
  readings implements `ILiveReadingSource` on its provider and gets the measurement panel for free; one
  with a calibration routine returns an `ICalibrationWorkflow`.

## 11. Hardware checklist (not yet done)

1. Instruments → Leveltronic → **Add Device** → Bluetooth finds `Leveltronic-…`; USB finds VID `04D8`/PID `F08F`.
2. Management view **Connect** over BLE and over USB: identity/serial/firmware correct, battery and
   temperatures update, RTC set to PC time.
3. New project with the Leveltronic → the measurement panel connects; live S1 / S2 / Diff match the phone;
   **Take reading** matches the phone's precision measurement; Zero Diff behaves.
4. Settings read/write (e.g. auto power-off); service-mode message when writing a calibration without it.
5. With service mode on at the instrument: phase → gain → zero wizards against a known shim / flip,
   compared with the phone app on the same unit.
6. Check the Diagnostics channel labels (firmware reference: S2, B, A, S1; the phone shows A, B, S1, S2).
7. BLE: walk out of range and back — the connection bar should show Connecting and recover.
