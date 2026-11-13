# RDF Plugin for vatSys

Radio Direction Finder for vatSys. When a pilot transmits on a frequency you are receiving (RX), the plugin draws a circle around that aircraft on the ASD. If the aircraft is outside the displayed area, it draws a line from the display centre towards it and shows the callsign at the display edge. The VSCS (voice) window also gets a **LAST TX** line showing the last stations received and their frequencies.

Ported from the EuroScope plugin [chembergj/RDF](https://github.com/chembergj/RDF). vatSys has its own AFV client, so the plugin reads `Audio.VSCSFrequencies[].ReceivingCallsigns` directly. No AFV Standalone Client or bridge is needed.

## Features

| Situation | Display |
| --- | --- |
| One pilot transmitting, aircraft on screen | Ring around the track (white by default, size adjustable) |
| Aircraft outside the displayed area | Line from the display centre towards the aircraft, arrow head, callsign at the edge |
| Simultaneous transmissions | All transmitting aircraft get a ring in the "simultaneous" colour (red by default); off-screen callsigns are stacked along the edge instead of overlapping |
| After the transmission ended | Optional: hold a recall key (off by default) to show the last transmitting aircraft again |
| VSCS window | LAST TX line at the bottom: last received callsign(s) + frequency (`000.000`), all simultaneous speakers in one line; a station still talking is backed with the receiving colour |

- Works on every ASD window and on the ASMGCS (ground) display
- Optional: recolour the track and label of transmitting aircraft (`SelectASDTrackColour`)
- Optional: keep the last transmission highlighted for N seconds
- Audio Settings: a **VHF effects** toggle to turn off the simulated VHF radio sound on received voice
- Optional label item `RDF`: add an item with `Type="RDF"` to the profile's `Labels.xml` to show a bordered `RX` while the aircraft is transmitting

### LAST TX line

```
LAST TX  QFA1 124.400  VOZ2 128.000  JST3 124.400
```

A single line of text at the bottom of the VSCS window, next to the page scroll bar. The frequency buttons and pages are not changed, so it stays tidy however many frequencies are set up.

It follows the AFV standalone client's "Last Received" / "Last Freq" logic. When a station starts transmitting on an RX frequency, it becomes the last station and the frequency it was heard on is recorded. The value stays until another station transmits.

Stations that start while another station is still talking (simultaneous transmissions) are added to the same entry, in the order they started, each with the frequency it was heard on. A station that starts after silence begins a new entry. A station that is still talking is backed with the VSCS receiving colour, like its frequency button.

When the page scroll bar is shown there is less room: the title shortens to `TX`, the first speaker is always shown and the others are counted as `+N`. The line can be turned off with **Settings → RDF → Show LAST TX in VSCS**.

## Installation

The plugin is a single file, `vatSys.RDFPlugin.dll` (Harmony is embedded). Put it in either location:

1. **Main directory** (all profiles):
   `<vatSys>\bin\Plugins\vatSys.RDFPlugin.dll`
2. **Profile directory** (that profile only):
   `Documents\vatSys Files\Profiles\<Profile>\Plugins\vatSys.RDFPlugin.dll`

vatSys scans both `Plugins` folders recursively, so a subfolder such as `Plugins\RDF\` also works. If the DLL is in both places, vatSys loads it once, and the profile copy wins.

Restart vatSys. `RDF` appears in the plugin list in the *About* window.

## Usage and settings

Main menu **Settings → RDF**:

- **Enabled**: turn the ASD highlighting on or off
- **Show LAST TX in VSCS**: show or hide the LAST TX line (also in the settings window, saved as `ShowLastTx`)
- **Ring size (N px)**: pick the ring radius from presets (20-100 px); the current value is ticked
- **Settings...**: colours, ring size (any value 5-500 px), line width, off-screen indicator (line from centre / edge pointer / none), recall key (with CLEAR) and recall hold delay, persistence, track recolouring, LAST TX line

The settings window is a native vatSys window (vatSys `BaseForm` and controls, profile colours).

### VHF effects

The plugin adds a toggle to the **Emulation** box of the vatSys **Audio Settings** window, in the empty space left of the VHF preset label (`VHFEmulation2`). Ticked (default) is normal vatSys behaviour. Unticked turns off the AFV client's radio simulation for received voice: the VHF band-pass filter, white noise and crackle. The AFV client has one switch for this, so the HF noise of **Realistic HF** is turned off too. The setting is saved as `VhfEffects` and applied again whenever vatSys restarts its audio.

### Test mode (hidden)

**Test on selected track** simulates the selected aircraft transmitting for 5 seconds, so the ring, off-screen line and LAST TX line can be checked without a network connection. The LAST TX line then shows `---.---` as the frequency.

The menu item is hidden by default. To show it, create `RDFPlugin.json` next to the DLL (or in `Documents\vatSys Files`) and restart vatSys:

```json
{ "EnableTest": true }
```

The key is case-insensitive and the value must be the JSON boolean `true`. Delete the file, or set the value to `false`, to hide the item again.

Settings are stored in `RDFPlugin.xml` next to the DLL. If that folder is not writable, they go to `Documents\vatSys Files\RDFPlugin.xml`. The file can be edited by hand; colours accept `#RRGGBB` or the EuroScope RDF `R:G:B` format:

```xml
<RDF>
  <Enabled>true</Enabled>
  <Colour>#FFFFFF</Colour>
  <ConcurrentColour>#FF0000</ConcurrentColour>
  <Radius>40</Radius>
  <LineWidth>2</LineWidth>
  <Offscreen>LineFromCentre</Offscreen>   <!-- None | LineFromCentre | EdgePointer -->
  <OffscreenLabel>true</OffscreenLabel>
  <HoldKey>None</HoldKey>                 <!-- System.Windows.Forms.Keys name, e.g. MButton; None = recall off -->
  <RecallDelayMs>400</RecallDelayMs>      <!-- hold time before the recall shows, 0-5000 ms -->
  <PersistSeconds>0</PersistSeconds>
  <HighlightTrack>false</HighlightTrack>
  <TrackColour>#FFFFFF</TrackColour>
  <ShowLastTx>true</ShowLastTx>
</RDF>
```

> The recall (show the last transmitting aircraft again, like the middle mouse button in the EuroScope RDF plugin) is off by default. To use it, click **HOLD TO RECALL** in the settings and press a key or mouse button; **CLEAR** turns it off again. The key has to be held for the **recall hold delay** (400 ms by default), so short presses, such as normal middle-clicks on vatSys tracks and label items, do not bring back old rings. If you pick the middle button and also pan with it in the Drag and Zoom plugin, a long pan still triggers the recall.

## How it works

- The vatSys plugin SDK only offers label items, track colours and menu items; there is no API for drawing on the ASD. The plugin uses a [Harmony](https://github.com/pardeike/Harmony) postfix on `ASDControlDX.PaintTracksLabels` to draw the RDF graphics inside vatSys' own Direct2D frame, so nothing flickers and other windows cover it normally.
- If a future vatSys version changes these internals, the plugin reports an error and the ASD highlight falls back to track recolouring.
- The LAST TX line is an ordinary WinForms control the plugin adds to each VSCS window (vatSys font and colours); no patching is involved. The window is found through `MMI.VSCSWindow`, because vatSys shows it with `SetWindowPlacement` when a saved position exists and it then never appears in `Application.OpenForms`.
- The VHF effects toggle uses a Harmony postfix on the `AudioSettingsWindow` constructor (to add the control) and on `AFV.StartClient` (to set the AFV client's `BypassEffects` on every new soundcard).
- Receiving callsigns are read from the VSCS frequencies every 50 ms. Only RX-enabled frequencies count.

## Building

Requires a .NET SDK that can build `net472`:

```bash
dotnet build RDFPlugin/RDFPlugin.csproj -c Release
```

Output: `RDFPlugin/bin/Release/vatSys.RDFPlugin.dll`. If vatSys is not installed in `G:\vatSys\vatSys`, add `-p:VatSysDir=<vatSys install directory>`.

## Credits

- Original EuroScope RDF plugin: Claus Hemberg Joergensen - https://github.com/chembergj/RDF
- LAST TX logic modelled on the AFV standalone client - https://github.com/lpxlpx7/afv-recovered-source
- vatSys SDK: https://virtualairtrafficsystem.com/docs/sdk/

## License

GPL-3.0, see [LICENSE](LICENSE) (same licence as the original EuroScope RDF plugin). Bundled third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
