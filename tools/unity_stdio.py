import asyncio
import json
import os
import sys
from datetime import timedelta
from pathlib import Path

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client
from anyio import BrokenResourceError

response_written = False
tool_failed = False


async def main():
    global response_written, tool_failed
    root = Path(__file__).resolve().parents[1]
    request = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8-sig"))
    parameters = StdioServerParameters(
        command="powershell.exe",
        args=["-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
              str(root / "simulation/unity-mcp.ps1")],
        env={**os.environ, "UNITY_MCP_DISABLE_TELEMETRY": "1"},
    )
    with (root / "simulation/Reports/unity-stdio-client.log").open("a", encoding="utf-8") as log:
        async with stdio_client(parameters, errlog=log) as (read, write):
            async with ClientSession(read, write, read_timeout_seconds=timedelta(seconds=600)) as session:
                await session.initialize()
                if request.get("schema"):
                    result = next(tool for tool in (await session.list_tools()).tools if tool.name == request["schema"])
                elif request.get("resource"):
                    result = await session.read_resource(request["resource"])
                else:
                    result = await session.call_tool(request["tool"], request.get("arguments", {}))
                output = json.dumps(result.model_dump(mode="json"), indent=2)
                if len(sys.argv) > 2:
                    Path(sys.argv[2]).write_text(output, encoding="utf-8")
                print(output)
                response_written = True
                payload = result.model_dump(mode="json")
                tool_failed = bool(payload.get('isError')) or (payload.get('structuredContent') or {}).get('success') is False


try:
    asyncio.run(main())
except* BrokenResourceError:
    # Some server versions emit a tool-list notification while stdio is closing.
    # Only suppress this teardown race after a complete response was persisted.
    if not response_written:
        raise
if tool_failed:
    sys.exit(1)
