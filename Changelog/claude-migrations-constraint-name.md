# Switching to SQL Server works in a database shared with MyCEV

*Save and switch* on the Database tab stopped with "There is already an object named 'PK_CvMigrations' in the database" when MyCRMRecords' SQL Server database was the one MyCEV uses: the table that records the data migrations had a constraint named like MyCEV's. It now has its own name, so MyCRMRecords can share the database.
