# Checks that never wait for a sign-in

Nothing changes for the people using MyCRMRecords. The automatic checks of each change now fetch with their own token and never wait for a sign-in on the build server, and a new test makes sure every page and script goes into the release.
