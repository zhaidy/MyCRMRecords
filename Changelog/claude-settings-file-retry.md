# Saving settings no longer fails with "being used by another process"

Saving on the Settings page waits a moment and tries again when the settings file is briefly open elsewhere (the old
and new website side by side during an update, a virus scan or a backup), instead of failing; reading the settings
never stops a save any more.
