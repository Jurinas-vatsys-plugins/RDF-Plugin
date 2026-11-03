# RDF Plugin for vatSys

Radio Direction Finder for vatSys. When a pilot transmits on a frequency you are receiving (RX), the plugin draws a circle around that aircraft on the ASD. If the aircraft is outside the displayed area, it draws a line from the display centre towards it and shows the callsign at the display edge. The VSCS (voice) window also gets a **LAST TX** tile showing the last station received and its frequency.

Ported from the EuroScope plugin [chembergj/RDF](https://github.com/chembergj/RDF). vatSys has its own AFV client, so the plugin reads `Audio.VSCSFrequencies[].ReceivingCallsigns` directly. No AFV Standalone Client or bridge is needed.

## Features

| Situation | Display |
| --- | --- |
| One pilot transmitting, aircraft on screen | Ring around the track (white by default, size adjustable) |
| Aircraft outside the displayed area | Line from the display centre towards the aircraft, arrow head, callsign at the edge |
| Simultaneous transmissions | All transmitting aircraft use the "simultaneous" colour (red by default) |
| After the transmission ended | Hold the recall key (middle mouse button by default) to show the last transmitting aircraft again |
| VSCS window | LAST TX tile: last received callsign + frequency (`000.000`); lit in the receiving colour while that station is still talking |

- Works on every ASD window and on the ASMGCS (ground) display
- Optional: recolour the track and label of transmitting aircraft (`SelectASDTrackColour`)
- Optional: keep the last transmission highlighted for N seconds
- Optional label item `RDF`: add an item with `Type="RDF"` to the profile's `Labels.xml` to show a bordered `RX` while the aircraft is transmitting

### LAST TX tile

The tile follows the AFV standalone client's "Last Received" / "Last Freq" logic. When a station starts transmitting on an RX frequency, it becomes the last station and the frequency it was heard on is recorded. The value stays until another station transmits. If several stations start at the same time, the tile shows the first callsign and `+N`.

The tile takes the next free slot after the last frequency button. If that column is full, it opens a new column and the Setup/Coord/Text/Group buttons and pages move one column to the right. It can be turned off with **Settings → RDF → Show LAST TX in VSCS**.

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
- **Show LAST TX in VSCS**: show or hide the VSCS tile (also in the settings window, saved as `ShowLastTx`)
- **Ring size (N px)**: pick the ring radius from presets (20-100 px); the current value is ticked
- **Settings...**: colours, ring size (any value 5-500 px), line width, off-screen indicator (line from centre / edge pointer / none), recall key, persistence, track recolouring, LAST TX tile

The settings window is a native vatSys window (vatSys `BaseForm` and controls, profile colours).

### Test mode (hidden)

**Test on selected track** simulates the selected aircraft transmitting for 5 seconds, so the ring, off-screen line and LAST TX tile can be checked without a network connection. The LAST TX tile then shows `---.---` as the frequency.

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
  <HoldKey>MButton</HoldKey>              <!-- System.Windows.Forms.Keys name, None to disable -->
  <PersistSeconds>0</PersistSeconds>
  <HighlightTrack>false</HighlightTrack>
  <TrackColour>#FFFFFF</TrackColour>
  <ShowLastTx>true</ShowLastTx>
</RDF>
```

> If you use the Drag and Zoom plugin with middle-button panning, panning also triggers the recall. Change the recall key in the RDF settings if that bothers you.

## How it works

- The vatSys plugin SDK only offers label items, track colours and menu items; there is no API for drawing on the ASD or the VSCS. The plugin uses [Harmony](https://github.com/pardeike/Harmony) postfixes:
  - `ASDControlDX.PaintTracksLabels`: draws the RDF graphics inside vatSys' own Direct2D frame, so nothing flickers and other windows cover it normally.
  - `VSCSControlDX.PaintFreqButtons`: draws the LAST TX tile with vatSys' own button styling and shifts the following columns when needed.
- If a future vatSys version changes these internals, the plugin reports an error. The ASD highlight then falls back to track recolouring; the LAST TX tile is skipped.
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
