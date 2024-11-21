# Claude with Keystroke History
A Windows application which (i) logs your keystrokes, and (ii) messages Claude an inputted question combined with your recent keystroke history.

## Warning
Be careful with keystroke logging - it can capture sensitive info like passwords. Ensure that logging is turned off (Ctrl+Shift+L) before typing sensitive information.

## What it Does
- Records keystrokes when activated
- Encrypts logged data for privacy
- Uses Anthropic's Claude model to answer questions
- Shows you API cost usage with every message
- Stores those conversations in JSON file

## How to Use
1. Get an Anthropic API key
2. Add your key to config.json
3. Run the program
4. Use hotkeys:
   - Ctrl+Shift+L: Start/Stop logging
   - Ctrl+Shift+K: Ask Claude a question, combined with your recent keystroke history
5. Config.json currently includes Claude Haiku/Sonnet 3.5 but you can add other models.  

## Quick Setup
1. Clone this repo
2. Open in Visual Studio
3. Create config.json with your API key
4. Build and run

## Notes
- Lives in your system tray (bottom right of screen)
- Green icon = logging on
- Red icon = logging off
- All data is encrypted locally
- Right-click tray icon to exit
- Conversations with Claude are saved in conversation.json
