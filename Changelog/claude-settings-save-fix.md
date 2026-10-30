# Settings save even when the file is kept open on the server

When something on the server keeps the settings file open (an editor, a backup, a virus scan), saving on the Settings
page writes the new settings into the file itself instead of failing with "being used by another process". If even that
is impossible, the message names the file so it can be closed.
