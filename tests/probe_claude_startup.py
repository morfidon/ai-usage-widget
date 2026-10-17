"""Read-only CLI initialization probe: no user message and no inference call."""
import json
import pathlib
import subprocess
import time
import re

credential_path = pathlib.Path.home() / '.claude' / '.credentials.json'
def expired():
    value = json.loads(credential_path.read_text())['claudeAiOauth']
    return value.get('expiresAt', 0) < time.time() * 1000

print('expired_before', expired())
command = [str(pathlib.Path.home() / '.local' / 'bin' / 'claude.exe'),
           '--print', '--input-format', 'stream-json', '--output-format', 'stream-json', '--verbose',
           '--no-session-persistence', '--safe-mode', '--strict-mcp-config', '--max-budget-usd', '0.000001']
process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                           text=True, creationflags=subprocess.CREATE_NO_WINDOW)
try:
    request = {'type': 'control_request', 'request_id': 'widget_auth_probe', 'request': {'subtype': 'initialize'}}
    output, error = process.communicate(json.dumps(request) + '\n', timeout=25)
    safe_error = re.sub(r'(sk-[A-Za-z0-9_-]+|eyJ[A-Za-z0-9_.-]+|Bearer\s+\S+)', '[REDACTED]', error)
    print('startup_error', safe_error[:1200])
    for line in output.splitlines():
        try:
            value = json.loads(line)
        except ValueError:
            continue
        print('message_type', value.get('type'), 'response_subtype', value.get('response', {}).get('subtype'))
    print('exit_code', process.returncode)
finally:
    if process.poll() is None:
        process.kill()
        process.wait()
print('expired_after', expired())
