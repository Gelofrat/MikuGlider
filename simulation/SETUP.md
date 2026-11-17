# Unity model inspection and artwork

The lab runs the mod's current pose garments, hair and hand corrections on locally extracted Mycopunk rigs and clips. It uses small game adapters rather than compiling the game's code. Game resources, recordings, generated source copies, third-party tools and Unity caches are not distributed.

## Setup

Use Unity **6000.4.12f1**, Python **3.11+**, and an AssetRipper export from your own game installation. Run these commands from the repository root:

```powershell
git clone https://github.com/CoplayDev/unity-mcp.git tools/vendor/unity-mcp
git -C tools/vendor/unity-mcp checkout 81542a7e86292b5c4c21b1782c5af23b567f45ce
python -m venv tools/unity-mcp-env
tools/unity-mcp-env/Scripts/python.exe -m pip install ./tools/vendor/unity-mcp/Server
python simulation/sync.py --game-assets 'C:/path/to/ExportedProject/Assets' --captures 'C:/path/to/Mycopunk/BepInEx/MikuGliderDiagnostics/captures'
```

The project manifest references this local MCP checkout. Open `simulation/Project` in Unity, then choose **Miku Physics Lab → Create inspection scene**. The project-local `MikuMcpStartup` starts the stdio bridge after compilation. `simulation/unity-mcp.ps1` opens the editor if needed and starts the MCP server; `start-mcp.ps1 -Unity '.../Unity.exe'` supports another editor installation path.

After code changes, use `python simulation/sync.py --code-only`. After asset changes, use `--reuse-catalog` to update models while retaining the extracted animation and weapon catalog. The full initial sync is required before either shortcut. Replay import skips incomplete skeleton definitions from destroyed or replaced characters.

The local protocol clients are `tools/unity_stdio.py` (run with the MCP environment's Python) and `tools/blender_session.py` (requires Blender MCP listening on localhost:9876). They execute only the supplied requests or scripts; no credentials are required. Blender MCP is a separate installed add-on.

## Render the nine promotional views

1. Sync current models and code, then refresh Unity and wait for compilation.
2. Through Unity MCP `execute_code`, run `return Horse.MikuPhysicsLab.Editor.LabArtworkExport.ExportAll("C:/path/to/MikuGlider/scratch/art-poses");`.
3. Review the nine PNG files in that folder before copying them into `arts/`.

The exporter renders the visible runtime meshes directly in Unity with the lab's albedo shader and 2× supersampling. It also writes baked geometry snapshots for inspection, a report with image hashes, and projected framing bounds. No second skinning or ray-traced interpretation of overlapping garment surfaces is involved. Idle and running views are 1000 × 1200, flight views are 1600 × 900. The renderer does not write icon files. `arts/render-manifest.json` records the input hashes and the final image hashes.

These are model renders, not gameplay screenshots or proof that every live animation transition passes. The regression entry point for the mod is `tests/Run.ps1`; Unity editor checks live in `Package/Editor/` and recorded hand tests require local captures.
