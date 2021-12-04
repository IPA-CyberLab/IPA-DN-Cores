﻿USE [HADB001]
GO


/****** Object:  Table [dbo].[HADB_CONFIG]    Script Date: 2021/12/04 21:03:32 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[HADB_CONFIG](
	[CONFIG_ID] [bigint] IDENTITY(1,1) NOT NULL,
	[CONFIG_SYSTEMNAME] [nvarchar](350) NOT NULL,
	[CONFIG_NAME] [nvarchar](350) NOT NULL,
	[CONFIG_VALUE] [nvarchar](max) NOT NULL,
	[CONFIG_EXT] [nvarchar](max) NOT NULL,
 CONSTRAINT [PK_CONFIG] PRIMARY KEY CLUSTERED 
(
	[CONFIG_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[HADB_DATA]    Script Date: 2021/12/04 21:03:32 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[HADB_DATA](
	[DATA_UID] [nvarchar](350) NOT NULL,
	[DATA_SYSTEMNAME] [nvarchar](350) NOT NULL,
	[DATA_TYPE] [nvarchar](350) NOT NULL,
	[DATA_NAMESPACE] [nvarchar](350) NOT NULL,
	[DATA_VER] [bigint] NOT NULL,
	[DATA_DELETED] [bit] NOT NULL,
	[DATA_ARCHIVE_AGE] [bigint] NOT NULL,
	[DATA_SNAPSHOT_NO] [bigint] NOT NULL,
	[DATA_VALUE] [nvarchar](max) NOT NULL,
	[DATA_KEY1] [nvarchar](350) NOT NULL,
	[DATA_KEY2] [nvarchar](350) NOT NULL,
	[DATA_KEY3] [nvarchar](350) NOT NULL,
	[DATA_KEY4] [nvarchar](350) NOT NULL,
	[DATA_LABEL1] [nvarchar](350) NOT NULL,
	[DATA_LABEL2] [nvarchar](350) NOT NULL,
	[DATA_LABEL3] [nvarchar](350) NOT NULL,
	[DATA_LABEL4] [nvarchar](350) NOT NULL,
	[DATA_CREATE_DT] [datetimeoffset](7) NOT NULL,
	[DATA_UPDATE_DT] [datetimeoffset](7) NOT NULL,
	[DATA_DELETE_DT] [datetimeoffset](7) NOT NULL,
	[DATA_LAZY_COUNT1] [bigint] NOT NULL,
	[DATA_LAZY_COUNT2] [bigint] NOT NULL,
	[DATA_EXT1] [nvarchar](max) NOT NULL,
	[DATA_EXT2] [nvarchar](max) NOT NULL,
 CONSTRAINT [PK_DATA] PRIMARY KEY CLUSTERED 
(
	[DATA_UID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[HADB_KV]    Script Date: 2021/12/04 21:03:32 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[HADB_KV](
	[KV_ID] [bigint] IDENTITY(1,1) NOT NULL,
	[KV_SYSTEM_NAME] [nvarchar](350) NOT NULL,
	[KV_KEY] [nvarchar](350) NOT NULL,
	[KV_VALUE] [nvarchar](350) NOT NULL,
	[KV_DELETED] [bit] NOT NULL,
	[KV_CREATE_DT] [datetimeoffset](7) NOT NULL,
	[KV_UPDATE_DT] [datetimeoffset](7) NOT NULL,
 CONSTRAINT [PK_HADV_KEY] PRIMARY KEY CLUSTERED 
(
	[KV_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[HADB_LOG]    Script Date: 2021/12/04 21:03:32 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[HADB_LOG](
	[LOG_ID] [bigint] IDENTITY(1,1) NOT NULL,
	[LOG_UID] [nvarchar](350) NOT NULL,
	[LOG_SYSTEM_NAME] [nvarchar](350) NOT NULL,
	[LOG_TYPE] [nvarchar](350) NOT NULL,
	[LOG_NAMESPACE] [nvarchar](350) NOT NULL,
	[LOG_DT] [datetimeoffset](7) NOT NULL,
	[LOG_SNAP_NO] [bigint] NOT NULL,
	[LOG_DELETED] [bit] NOT NULL,
	[LOG_LABEL1] [nvarchar](350) NOT NULL,
	[LOG_LABEL2] [nvarchar](350) NOT NULL,
	[LOG_LABEL3] [nvarchar](350) NOT NULL,
	[LOG_LABEL4] [nvarchar](350) NOT NULL,
	[LOG_VALUE] [nvarchar](max) NOT NULL,
	[LOG_EXT1] [nvarchar](max) NOT NULL,
	[LOG_EXT2] [nvarchar](max) NOT NULL,
 CONSTRAINT [PK_HADB_LOG] PRIMARY KEY CLUSTERED 
(
	[LOG_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[HADB_SNAPSHOT]    Script Date: 2021/12/04 21:03:32 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[HADB_SNAPSHOT](
	[SNAPSHOT_UID] [nvarchar](350) NOT NULL,
	[SNAPSHOT_SYSTEM_NAME] [nvarchar](350) NOT NULL,
	[SNAPSHOT_NO] [bigint] NOT NULL,
	[SNAPSHOT_DT] [datetimeoffset](7) NOT NULL,
	[SNAPSHOT_DESCRIPTION] [nvarchar](max) NOT NULL,
	[SNAPSHOT_EXT1] [nvarchar](max) NOT NULL,
	[SNAPSHOT_EXT2] [nvarchar](max) NOT NULL,
 CONSTRAINT [PK_HADB_SNAP] PRIMARY KEY CLUSTERED 
(
	[SNAPSHOT_UID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [DATA_KEY1]    Script Date: 2021/12/04 21:03:32 ******/
CREATE NONCLUSTERED INDEX [DATA_KEY1] ON [dbo].[HADB_DATA]
(
	[DATA_KEY1] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [DATA_KEY2]    Script Date: 2021/12/04 21:03:32 ******/
CREATE NONCLUSTERED INDEX [DATA_KEY2] ON [dbo].[HADB_DATA]
(
	[DATA_KEY2] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [DATA_KEY3]    Script Date: 2021/12/04 21:03:32 ******/
CREATE NONCLUSTERED INDEX [DATA_KEY3] ON [dbo].[HADB_DATA]
(
	[DATA_KEY3] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [DATA_KEY4]    Script Date: 2021/12/04 21:03:32 ******/
CREATE NONCLUSTERED INDEX [DATA_KEY4] ON [dbo].[HADB_DATA]
(
	[DATA_KEY4] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
/****** Object:  Index [DATA_SNAP_NO]    Script Date: 2021/12/04 21:03:32 ******/
CREATE NONCLUSTERED INDEX [DATA_SNAP_NO] ON [dbo].[HADB_DATA]
(
	[DATA_SNAPSHOT_NO] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
/****** Object:  Index [DATA_UPDATE_DT]    Script Date: 2021/12/04 21:03:32 ******/
CREATE NONCLUSTERED INDEX [DATA_UPDATE_DT] ON [dbo].[HADB_DATA]
(
	[DATA_CREATE_DT] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
/****** Object:  Index [DATA_VER]    Script Date: 2021/12/04 21:03:32 ******/
CREATE NONCLUSTERED INDEX [DATA_VER] ON [dbo].[HADB_DATA]
(
	[DATA_VER] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [KV_KEY]    Script Date: 2021/12/04 21:03:32 ******/
CREATE NONCLUSTERED INDEX [KV_KEY] ON [dbo].[HADB_KV]
(
	[KV_KEY] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [LOG_LABEL1]    Script Date: 2021/12/04 21:03:32 ******/
CREATE NONCLUSTERED INDEX [LOG_LABEL1] ON [dbo].[HADB_LOG]
(
	[LOG_LABEL1] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [LOG_LABEL2]    Script Date: 2021/12/04 21:03:32 ******/
CREATE NONCLUSTERED INDEX [LOG_LABEL2] ON [dbo].[HADB_LOG]
(
	[LOG_LABEL2] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [LOG_LABEL3]    Script Date: 2021/12/04 21:03:32 ******/
CREATE NONCLUSTERED INDEX [LOG_LABEL3] ON [dbo].[HADB_LOG]
(
	[LOG_LABEL3] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [LOG_LABEL4]    Script Date: 2021/12/04 21:03:32 ******/
CREATE NONCLUSTERED INDEX [LOG_LABEL4] ON [dbo].[HADB_LOG]
(
	[LOG_LABEL4] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [LOG_UID]    Script Date: 2021/12/04 21:03:32 ******/
CREATE UNIQUE NONCLUSTERED INDEX [LOG_UID] ON [dbo].[HADB_LOG]
(
	[LOG_UID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
/****** Object:  Index [SNAP_DT]    Script Date: 2021/12/04 21:03:32 ******/
CREATE NONCLUSTERED INDEX [SNAP_DT] ON [dbo].[HADB_SNAPSHOT]
(
	[SNAPSHOT_DT] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
/****** Object:  Index [SNAP_NO]    Script Date: 2021/12/04 21:03:32 ******/
CREATE NONCLUSTERED INDEX [SNAP_NO] ON [dbo].[HADB_SNAPSHOT]
(
	[SNAPSHOT_NO] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO



/****** Object:  Index [SYSTEM_NAME]    Script Date: 2021/12/04 23:33:44 ******/
CREATE NONCLUSTERED INDEX [SYSTEM_NAME] ON [dbo].[HADB_CONFIG]
(
	[CONFIG_SYSTEMNAME] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO


/****** Object:  Index [DATA_SYSTEMNAME]    Script Date: 2021/12/04 23:34:26 ******/
CREATE NONCLUSTERED INDEX [DATA_SYSTEMNAME] ON [dbo].[HADB_DATA]
(
	[DATA_SYSTEMNAME] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO





