Microsoft SQL Database Restore Utility by Rafael Ubaldo Sr.

9/18/2017 Built using Visual Studio 2017 Community Edition. You can also use Microsoft Code too!

Simple no nonsense application that quickly restores a full database backup file to a Microsoft SQL Server Database.
The restore covers any combination of restoring a local or remote backup to a local or remote database server.
The database backup file is assumed to be a single full database backup file - next version will cover combining multiple files!

To use, the backup folder(s) need to have a file shares. SQL Server (local or remote) must have 
sufficient rights to read the shares. In most cases, using Windows Login did helps over SQL Server Login.

To start the restore, enter the connection information then drag and drop the backup file on to this application.
Connection information and other settings is saved and restored on next startup.

If the restore application (this application) and SQL Server both have sufficient rights, the restore SQL command script appears.
It's recommended that you only view and not alter the script! Next version won't show the script up front.

The Original database name appears with and option to change the target database (sweet!). Note the script updates as the target changes.

You have extra options to Check how many sessions are currently using the current database about to be restored,
a handy Kill all session button (my favorite) which kills all sessions (only for the database!), which is
needed to restore the database and, the Drop button that drops the database about to be full restored from the database server.

Please note as time permits, I will be fixing some of the async and other exception handling issues and mapped drive support.
This is only the first pass that I did in two days.

