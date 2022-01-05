﻿

USE [master]
GO

CREATE DATABASE [HADB001] COLLATE Latin1_General_CI_AS_KS_WS
GO

ALTER DATABASE [HADB001] MODIFY FILE
( NAME = N'HADB001', FILEGROWTH = 65536KB )
GO

ALTER DATABASE [HADB001] MODIFY FILE
( NAME = N'HADB001_log', FILEGROWTH = 65536KB )
GO

ALTER DATABASE [HADB001] SET COMPATIBILITY_LEVEL = 140
GO
IF (1 = FULLTEXTSERVICEPROPERTY('IsFullTextInstalled'))
begin
EXEC [HADB001].[dbo].[sp_fulltext_database] @action = 'enable'
end
GO
ALTER DATABASE [HADB001] SET ANSI_NULL_DEFAULT OFF 
GO
ALTER DATABASE [HADB001] SET ANSI_NULLS OFF 
GO
ALTER DATABASE [HADB001] SET ANSI_PADDING OFF 
GO
ALTER DATABASE [HADB001] SET ANSI_WARNINGS OFF 
GO
ALTER DATABASE [HADB001] SET ARITHABORT OFF 
GO
ALTER DATABASE [HADB001] SET AUTO_CLOSE OFF 
GO
ALTER DATABASE [HADB001] SET AUTO_SHRINK ON 
GO
ALTER DATABASE [HADB001] SET AUTO_UPDATE_STATISTICS ON 
GO
ALTER DATABASE [HADB001] SET CURSOR_CLOSE_ON_COMMIT OFF 
GO
ALTER DATABASE [HADB001] SET CURSOR_DEFAULT  GLOBAL 
GO
ALTER DATABASE [HADB001] SET CONCAT_NULL_YIELDS_NULL OFF 
GO
ALTER DATABASE [HADB001] SET NUMERIC_ROUNDABORT OFF 
GO
ALTER DATABASE [HADB001] SET QUOTED_IDENTIFIER OFF 
GO
ALTER DATABASE [HADB001] SET RECURSIVE_TRIGGERS OFF 
GO
ALTER DATABASE [HADB001] SET  DISABLE_BROKER 
GO
ALTER DATABASE [HADB001] SET AUTO_UPDATE_STATISTICS_ASYNC OFF 
GO
ALTER DATABASE [HADB001] SET DATE_CORRELATION_OPTIMIZATION OFF 
GO
ALTER DATABASE [HADB001] SET TRUSTWORTHY OFF 
GO
ALTER DATABASE [HADB001] SET ALLOW_SNAPSHOT_ISOLATION ON 
GO
ALTER DATABASE [HADB001] SET PARAMETERIZATION SIMPLE 
GO
ALTER DATABASE [HADB001] SET READ_COMMITTED_SNAPSHOT OFF 
GO
ALTER DATABASE [HADB001] SET HONOR_BROKER_PRIORITY OFF 
GO
ALTER DATABASE [HADB001] SET RECOVERY SIMPLE 
GO
ALTER DATABASE [HADB001] SET  MULTI_USER 
GO
ALTER DATABASE [HADB001] SET PAGE_VERIFY CHECKSUM  
GO
ALTER DATABASE [HADB001] SET DB_CHAINING OFF 
GO
ALTER DATABASE [HADB001] SET FILESTREAM( NON_TRANSACTED_ACCESS = OFF ) 
GO
ALTER DATABASE [HADB001] SET TARGET_RECOVERY_TIME = 60 SECONDS 
GO
ALTER DATABASE [HADB001] SET DELAYED_DURABILITY = DISABLED 
GO
EXEC sys.sp_db_vardecimal_storage_format N'HADB001', N'ON'
GO
ALTER DATABASE [HADB001] SET QUERY_STORE = ON
GO
/*ALTER DATABASE [HADB001]
SET AUTOMATIC_TUNING ( FORCE_LAST_GOOD_PLAN = ON ); */
USE [HADB001]
GO

USE [master]
GO
ALTER DATABASE [HADB001] SET  READ_WRITE 
GO

USE [master]
GO

