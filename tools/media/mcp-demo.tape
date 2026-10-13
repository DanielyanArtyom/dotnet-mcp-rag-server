# Records docs/media/mcp-demo.gif: Claude Code using Lumen's MCP tools as the Acme tenant.
# Prerequisites: tools/media/demo-server.sh running on :5219, and $DEMO pointing at a directory that holds
# the .mcp.json generated from tools/media/mcp-demo/.mcp.json.template. Run from the repository root:
#   mkdir -p ~/lumen-demo && sed "s#__REPO__#$PWD#" tools/media/mcp-demo/.mcp.json.template > ~/lumen-demo/.mcp.json
#   DEMO=~/lumen-demo vhs tools/media/mcp-demo.tape
# The directory must already be trusted in Claude Code (start claude there once and accept the prompt).
# env -i keeps a parent Claude Code session's environment out of the recording.
Output docs/media/mcp-demo.gif
Require claude

Set Shell "bash"
Set FontSize 15
Set Width 1000
Set Height 640
Set Padding 16
Set Theme "Catppuccin Mocha"
Set TypingSpeed 35ms
Set Framerate 12
Set PlaybackSpeed 1.5

Hide
Type `cd "$DEMO" && clear && env -i HOME="$HOME" USER="$USER" PATH="$PATH" TERM="$TERM" LANG=en_US.UTF-8 claude --mcp-config .mcp.json --strict-mcp-config --setting-sources project --tools "" --allowedTools "mcp__lumen__*"`
Enter
Sleep 8s
Show

Type "/mcp"
Sleep 500ms
Enter
Sleep 3s
Escape
Sleep 1s

Type "How many shareholders of record did Apple have as of October 17, 2025?"
Enter
Sleep 20s

Type "Search the filings: what was JPMorgan Chase's CET1 capital ratio at June 30, 2026?"
Enter
Sleep 22s
Sleep 6s
