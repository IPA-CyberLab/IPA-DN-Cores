

USE [master]
GO
CREATE LOGIN [sql_hadb001_reader] WITH PASSWORD=N'sql_hadb_reader_default_password', DEFAULT_DATABASE=[master], CHECK_EXPIRATION=OFF, CHECK_POLICY=OFF
GO
USE [HADB001]
GO
CREATE USER [sql_hadb001_reader] FOR LOGIN [sql_hadb001_reader]
GO
USE [HADB001]
GO
ALTER ROLE [db_datareader] ADD MEMBER [sql_hadb001_reader]
GO
USE [HADB001]
GO
ALTER ROLE [db_owner] ADD MEMBER [sql_hadb001_reader]
GO


USE [master]
GO
CREATE LOGIN [sql_hadb001_writer] WITH PASSWORD=N'sql_hadb_writer_default_password', DEFAULT_DATABASE=[master], CHECK_EXPIRATION=OFF, CHECK_POLICY=OFF
GO
USE [HADB001]
GO
CREATE USER [sql_hadb001_writer] FOR LOGIN [sql_hadb001_writer]
GO
USE [HADB001]
GO
ALTER ROLE [db_datareader] ADD MEMBER [sql_hadb001_writer]
GO
USE [HADB001]
GO
ALTER ROLE [db_datawriter] ADD MEMBER [sql_hadb001_writer]
GO
USE [HADB001]
GO
ALTER ROLE [db_owner] ADD MEMBER [sql_hadb001_writer]
GO

