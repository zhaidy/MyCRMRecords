# Builds no longer waste time on pull requests that are already merged

Nothing changes on the website. In the build system, a pull request check that waited in the queue now stops at once when its pull request has been merged or closed by hand, or has a newer commit, instead of building and testing it first. That leaves the build computer free sooner for the changes that still need testing, so merges and releases wait less.
