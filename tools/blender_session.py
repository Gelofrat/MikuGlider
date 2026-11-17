import json
import socket
import sys
import os
from pathlib import Path

code = Path(sys.argv[1]).read_text(encoding='utf-8') if len(sys.argv) > 1 else sys.stdin.read()
with socket.create_connection(('127.0.0.1', 9876), 8) as connection:
    connection.settimeout(float(os.environ.get('BLENDER_MCP_TIMEOUT', '120')))
    connection.sendall(json.dumps({'type': 'execute_code', 'params': {'code': code}}).encode())
    received = bytearray()
    while True:
        chunk = connection.recv(1048576)
        if not chunk:
            raise RuntimeError('Blender disconnected before returning a result')
        received.extend(chunk)
        try:
            result = json.loads(received)
            break
        except json.JSONDecodeError:
            continue
    print(json.dumps(result, ensure_ascii=True))
    if result.get('status') != 'success':
        sys.exit(1)
