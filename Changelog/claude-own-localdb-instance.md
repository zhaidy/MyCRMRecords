# Builds no longer disturb each other's database tests

Nothing changes on the website. In the build system, each app's database tests now use a SQL Server LocalDB instance of their own instead of one shared by all the apps, so a database restart in one app's build can no longer make another app's tests fail with a "pipe has been ended" error.
