﻿USE [HADB001]
GO


/****** Object:  Table [dbo].[HADB_CONFIG]    Script Date: 2021/12/05 19:47:27 ******/
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
/****** Object:  Table [dbo].[HADB_DATA]    Script Date: 2021/12/05 19:47:27 ******/
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
	[DATA_ARCHIVE] [bit] NOT NULL,
	[DATA_DELETED] [bit] NOT NULL,
	[DATA_SNAPSHOT_NO] [bigint] NOT NULL,
	[DATA_VALUE] [nvarchar](max) NOT NULL,
	[DATA_KEY1] [nvarchar](350) NOT NULL,
	[DATA_KEY2] [nvarchar](350) NOT NULL,
	[DATA_KEY3] [nvarchar](350) NOT NULL,
	[DATA_KEY4] [nvarchar](350) NOT NULL,
	[DATA_KEY5] [nvarchar](350) NOT NULL,
	[DATA_LABEL1] [nvarchar](350) NOT NULL,
	[DATA_LABEL2] [nvarchar](350) NOT NULL,
	[DATA_LABEL3] [nvarchar](350) NOT NULL,
	[DATA_LABEL4] [nvarchar](350) NOT NULL,
	[DATA_LABEL5] [nvarchar](350) NOT NULL,
	[DATA_CREATE_DT] [datetimeoffset](7) NOT NULL,
	[DATA_UPDATE_DT] [datetimeoffset](7) NOT NULL,
	[DATA_DELETE_DT] [datetimeoffset](7) NOT NULL,
	[DATA_LAZY_COUNT1] [bigint] NOT NULL,
	[DATA_LAZY_COUNT2] [bigint] NOT NULL,
	[DATA_EXT1] [nvarchar](max) NOT NULL,
	[DATA_EXT2] [nvarchar](max) NOT NULL,
	[DATA_FT1] [nvarchar](max) NOT NULL,
	[DATA_FT2] [nvarchar](max) NOT NULL,
	[DATA_UID_ORIGINAL] [nvarchar](350) NOT NULL,
 CONSTRAINT [PK_DATA] PRIMARY KEY CLUSTERED 
(
	[DATA_UID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[HADB_KV]    Script Date: 2021/12/05 19:47:27 ******/
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
/****** Object:  Table [dbo].[HADB_LOG]    Script Date: 2021/12/05 19:47:27 ******/
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
	[LOG_LABEL5] [nvarchar](350) NOT NULL,
	[LOG_VALUE] [nvarchar](max) NOT NULL,
	[LOG_EXT1] [nvarchar](max) NOT NULL,
	[LOG_EXT2] [nvarchar](max) NOT NULL,
	[LOG_FT1] [nvarchar](max) NOT NULL,
	[LOG_FT2] [nvarchar](max) NOT NULL,
 CONSTRAINT [PK_HADB_LOG] PRIMARY KEY CLUSTERED 
(
	[LOG_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[HADB_SNAPSHOT]    Script Date: 2021/12/05 19:47:27 ******/
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
/****** Object:  Index [SYSTEM_NAME]    Script Date: 2021/12/05 19:47:27 ******/
CREATE NONCLUSTERED INDEX [SYSTEM_NAME] ON [dbo].[HADB_CONFIG]
(
	[CONFIG_SYSTEMNAME] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [DATA_KEY1]    Script Date: 2021/12/05 19:47:27 ******/
CREATE UNIQUE NONCLUSTERED INDEX [DATA_KEY1] ON [dbo].[HADB_DATA]
(
	[DATA_KEY1] ASC,
	[DATA_SYSTEMNAME] ASC,
	[DATA_NAMESPACE] ASC,
	[DATA_TYPE] ASC
)
WHERE ([DATA_ARCHIVE]=(0) AND [DATA_DELETED]=(0) AND [DATA_KEY1]<>'')
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [DATA_KEY2]    Script Date: 2021/12/05 19:47:27 ******/
CREATE UNIQUE NONCLUSTERED INDEX [DATA_KEY2] ON [dbo].[HADB_DATA]
(
	[DATA_KEY2] ASC,
	[DATA_SYSTEMNAME] ASC,
	[DATA_NAMESPACE] ASC,
	[DATA_TYPE] ASC
)
WHERE ([DATA_ARCHIVE]=(0) AND [DATA_DELETED]=(0) AND [DATA_KEY2]<>'')
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [DATA_KEY3]    Script Date: 2021/12/05 19:47:27 ******/
CREATE UNIQUE NONCLUSTERED INDEX [DATA_KEY3] ON [dbo].[HADB_DATA]
(
	[DATA_KEY3] ASC,
	[DATA_SYSTEMNAME] ASC,
	[DATA_NAMESPACE] ASC,
	[DATA_TYPE] ASC
)
WHERE ([DATA_ARCHIVE]=(0) AND [DATA_DELETED]=(0) AND [DATA_KEY3]<>'')
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [DATA_KEY4]    Script Date: 2021/12/05 19:47:27 ******/
CREATE UNIQUE NONCLUSTERED INDEX [DATA_KEY4] ON [dbo].[HADB_DATA]
(
	[DATA_KEY4] ASC,
	[DATA_SYSTEMNAME] ASC,
	[DATA_NAMESPACE] ASC,
	[DATA_TYPE] ASC
)
WHERE ([DATA_ARCHIVE]=(0) AND [DATA_DELETED]=(0) AND [DATA_KEY4]<>'')
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [DATA_KEY5]    Script Date: 2021/12/05 19:47:27 ******/
CREATE UNIQUE NONCLUSTERED INDEX [DATA_KEY5] ON [dbo].[HADB_DATA]
(
	[DATA_KEY5] ASC,
	[DATA_SYSTEMNAME] ASC,
	[DATA_NAMESPACE] ASC,
	[DATA_TYPE] ASC
)
WHERE ([DATA_ARCHIVE]=(0) AND [DATA_DELETED]=(0) AND [DATA_KEY5]<>'')
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [DATA_LABEL1]    Script Date: 2021/12/05 19:47:27 ******/
CREATE NONCLUSTERED INDEX [DATA_LABEL1] ON [dbo].[HADB_DATA]
(
	[DATA_LABEL1] ASC
)
WHERE ([DATA_LABEL1]<>'' AND [DATA_DELETED]=(0) AND [DATA_ARCHIVE]=(0))
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [DATA_LABEL2]    Script Date: 2021/12/05 19:47:27 ******/
CREATE NONCLUSTERED INDEX [DATA_LABEL2] ON [dbo].[HADB_DATA]
(
	[DATA_LABEL2] ASC
)
WHERE ([DATA_LABEL2]<>'' AND [DATA_DELETED]=(0) AND [DATA_ARCHIVE]=(0))
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [DATA_LABEL3]    Script Date: 2021/12/05 19:47:27 ******/
CREATE NONCLUSTERED INDEX [DATA_LABEL3] ON [dbo].[HADB_DATA]
(
	[DATA_LABEL3] ASC
)
WHERE ([DATA_LABEL3]<>'' AND [DATA_DELETED]=(0) AND [DATA_ARCHIVE]=(0))
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [DATA_LABEL4]    Script Date: 2021/12/05 19:47:27 ******/
CREATE NONCLUSTERED INDEX [DATA_LABEL4] ON [dbo].[HADB_DATA]
(
	[DATA_LABEL4] ASC
)
WHERE ([DATA_LABEL4]<>'' AND [DATA_DELETED]=(0) AND [DATA_ARCHIVE]=(0))
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
/****** Object:  Index [DATA_LABEL5]    Script Date: 2021/12/05 19:47:27 ******/
CREATE NONCLUSTERED INDEX [DATA_LABEL5] ON [dbo].[HADB_DATA]
(
	[DATA_LABEL5] ASC
)
WHERE ([DATA_LABEL5]<>'' AND [DATA_DELETED]=(0) AND [DATA_ARCHIVE]=(0))
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
/****** Object:  Index [DATA_SNAP_NO]    Script Date: 2021/12/05 19:47:27 ******/
CREATE NONCLUSTERED INDEX [DATA_SNAP_NO] ON [dbo].[HADB_DATA]
(
	[DATA_SNAPSHOT_NO] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [DATA_SYSTEMNAME]    Script Date: 2021/12/05 19:47:27 ******/
CREATE NONCLUSTERED INDEX [DATA_SYSTEMNAME] ON [dbo].[HADB_DATA]
(
	[DATA_SYSTEMNAME] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [DATA_UID_ORIGINAL]    Script Date: 2021/12/05 19:47:27 ******/
CREATE NONCLUSTERED INDEX [DATA_UID_ORIGINAL] ON [dbo].[HADB_DATA]
(
	[DATA_UID_ORIGINAL] ASC
)
WHERE ([DATA_ARCHIVE]=(1))
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
/****** Object:  Index [DATA_UPDATE_DT]    Script Date: 2021/12/05 19:47:27 ******/
CREATE NONCLUSTERED INDEX [DATA_UPDATE_DT] ON [dbo].[HADB_DATA]
(
	[DATA_CREATE_DT] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
/****** Object:  Index [DATA_VER]    Script Date: 2021/12/05 19:47:27 ******/
CREATE NONCLUSTERED INDEX [DATA_VER] ON [dbo].[HADB_DATA]
(
	[DATA_VER] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [KV_KEY]    Script Date: 2021/12/05 19:47:27 ******/
CREATE NONCLUSTERED INDEX [KV_KEY] ON [dbo].[HADB_KV]
(
	[KV_KEY] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [KV_SYSTEM_NAME]    Script Date: 2021/12/05 19:47:27 ******/
CREATE NONCLUSTERED INDEX [KV_SYSTEM_NAME] ON [dbo].[HADB_KV]
(
	[KV_SYSTEM_NAME] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [LOG_LABEL1]    Script Date: 2021/12/05 19:47:27 ******/
CREATE NONCLUSTERED INDEX [LOG_LABEL1] ON [dbo].[HADB_LOG]
(
	[LOG_LABEL1] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [LOG_LABEL2]    Script Date: 2021/12/05 19:47:27 ******/
CREATE NONCLUSTERED INDEX [LOG_LABEL2] ON [dbo].[HADB_LOG]
(
	[LOG_LABEL2] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [LOG_LABEL3]    Script Date: 2021/12/05 19:47:27 ******/
CREATE NONCLUSTERED INDEX [LOG_LABEL3] ON [dbo].[HADB_LOG]
(
	[LOG_LABEL3] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [LOG_LABEL4]    Script Date: 2021/12/05 19:47:27 ******/
CREATE NONCLUSTERED INDEX [LOG_LABEL4] ON [dbo].[HADB_LOG]
(
	[LOG_LABEL4] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [LOG_LABEL5]    Script Date: 2021/12/05 19:47:27 ******/
CREATE NONCLUSTERED INDEX [LOG_LABEL5] ON [dbo].[HADB_LOG]
(
	[LOG_LABEL5] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [LOG_UID]    Script Date: 2021/12/05 19:47:27 ******/
CREATE UNIQUE NONCLUSTERED INDEX [LOG_UID] ON [dbo].[HADB_LOG]
(
	[LOG_UID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO


CREATE NONCLUSTERED INDEX [LOG_SYSTEM_NAME] ON [dbo].[HADB_LOG]
(
	[LOG_SYSTEM_NAME] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF)
GO



/****** Object:  Index [SNAP_DT]    Script Date: 2021/12/05 19:47:27 ******/
CREATE NONCLUSTERED INDEX [SNAP_DT] ON [dbo].[HADB_SNAPSHOT]
(
	[SNAPSHOT_DT] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
/****** Object:  Index [SNAP_NO]    Script Date: 2021/12/05 19:47:27 ******/
CREATE NONCLUSTERED INDEX [SNAP_NO] ON [dbo].[HADB_SNAPSHOT]
(
	[SNAPSHOT_NO] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO




CREATE NONCLUSTERED INDEX [SNAP_SYSTEM_NAME] ON [dbo].[HADB_SNAPSHOT]
(
	[SNAPSHOT_SYSTEM_NAME] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF)

GO





/****** Object:  Table [dbo].[HADB_STAT]    Script Date: 2021/12/30 15:55:43 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[HADB_STAT](
	[STAT_UID] [nvarchar](350) NOT NULL,
	[STAT_SYSTEMNAME] [nvarchar](350) NOT NULL,
	[STAT_SNAPSHOT_NO] [bigint] NOT NULL,
	[STAT_DT] [datetimeoffset](7) NOT NULL,
	[STAT_GENERATOR] [nvarchar](350) NOT NULL,
	[STAT_VALUE] [nvarchar](max) NOT NULL,
	[STAT_EXT1] [nvarchar](max) NOT NULL,
	[STAT_EXT2] [nvarchar](max) NOT NULL,
 CONSTRAINT [PK_HADB_STAT] PRIMARY KEY CLUSTERED 
(
	[STAT_UID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Index [STAT_DT]    Script Date: 2021/12/30 15:55:43 ******/
CREATE NONCLUSTERED INDEX [STAT_DT] ON [dbo].[HADB_STAT]
(
	[STAT_DT] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
/****** Object:  Index [STAT_SNAPSHOT_NO]    Script Date: 2021/12/30 15:55:43 ******/
CREATE NONCLUSTERED INDEX [STAT_SNAPSHOT_NO] ON [dbo].[HADB_STAT]
(
	[STAT_SNAPSHOT_NO] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [STAT_SYSTEMNAME]    Script Date: 2021/12/30 15:55:43 ******/
CREATE NONCLUSTERED INDEX [STAT_SYSTEMNAME] ON [dbo].[HADB_STAT]
(
	[STAT_SYSTEMNAME] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO








USE [HADB001]
GO
/****** Object:  Table [dbo].[HADB_QUICK]    Script Date: 2022/01/15 9:28:48 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[HADB_QUICK](
	[QUICK_UID] [nvarchar](350) NOT NULL,
	[QUICK_SYSTEMNAME] [nvarchar](350) NOT NULL,
	[QUICK_TYPE] [nvarchar](350) NOT NULL,
	[QUICK_NAMESPACE] [nvarchar](350) NOT NULL,
	[QUICK_DELETED] [bit] NOT NULL,
	[QUICK_KEY] [nvarchar](350) NOT NULL,
	[QUICK_VALUE] [nvarchar](max) NOT NULL,
	[QUICK_CREATE_DT] [datetimeoffset](7) NOT NULL,
	[QUICK_UPDATE_DT] [datetimeoffset](7) NOT NULL,
	[QUICK_DELETE_DT] [datetimeoffset](7) NOT NULL,
	[QUICK_SNAPSHOT_NO] [bigint] NOT NULL,
	[QUICK_UPDATE_COUNT] [bigint] NOT NULL,
 CONSTRAINT [PK_HADB_QUICK] PRIMARY KEY CLUSTERED 
(
	[QUICK_UID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [QUICK_KEY]    Script Date: 2022/01/15 9:28:49 ******/
CREATE UNIQUE NONCLUSTERED INDEX [QUICK_KEY] ON [dbo].[HADB_QUICK]
(
	[QUICK_KEY] ASC,
	[QUICK_SYSTEMNAME] ASC,
	[QUICK_NAMESPACE] ASC,
	[QUICK_TYPE] ASC
)
WHERE ([QUICK_DELETED]=(0))
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [QUICK_SYSTEMNAME]    Script Date: 2022/01/15 9:28:49 ******/
CREATE NONCLUSTERED INDEX [QUICK_SYSTEMNAME] ON [dbo].[HADB_QUICK]
(
	[QUICK_SYSTEMNAME] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO








/* 圧縮 */

USE [HADB001]
ALTER TABLE [dbo].[HADB_DATA] REBUILD PARTITION = ALL
WITH 
(DATA_COMPRESSION = PAGE
)
GO




USE [HADB001]
ALTER TABLE [dbo].[HADB_LOG] REBUILD PARTITION = ALL
WITH 
(DATA_COMPRESSION = PAGE
)
GO





USE [HADB001]
ALTER TABLE [dbo].[HADB_SNAPSHOT] REBUILD PARTITION = ALL
WITH 
(DATA_COMPRESSION = PAGE
)
GO



USE [HADB001]
ALTER TABLE [dbo].[HADB_STAT] REBUILD PARTITION = ALL
WITH 
(DATA_COMPRESSION = PAGE
)
GO




USE [HADB001]
ALTER TABLE [dbo].[HADB_SNAPSHOT] REBUILD PARTITION = ALL
WITH 
(DATA_COMPRESSION = PAGE
)
GO






USE [HADB001]
ALTER TABLE [dbo].[HADB_QUICK] REBUILD PARTITION = ALL
WITH 
(DATA_COMPRESSION = PAGE
)
GO

















/* ロックエスカレーション無効化 */
/*
ALTER TABLE HADB_CONFIG
SET ( LOCK_ESCALATION = DISABLE )

ALTER TABLE HADB_DATA
SET ( LOCK_ESCALATION = DISABLE )

ALTER TABLE HADB_KV
SET ( LOCK_ESCALATION = DISABLE )

ALTER TABLE HADB_LOG
SET ( LOCK_ESCALATION = DISABLE )

ALTER TABLE HADB_SNAPSHOT
SET ( LOCK_ESCALATION = DISABLE )

ALTER TABLE HADB_STAT
SET ( LOCK_ESCALATION = DISABLE )

ALTER TABLE HADB_QUICK
SET ( LOCK_ESCALATION = DISABLE )
*/



/* ロックエスカレーションを AUTO に設定 (パーティション分割している場合に効果的) */
ALTER TABLE HADB_CONFIG
SET ( LOCK_ESCALATION = AUTO )

ALTER TABLE HADB_DATA
SET ( LOCK_ESCALATION = AUTO )

ALTER TABLE HADB_KV
SET ( LOCK_ESCALATION = AUTO )

ALTER TABLE HADB_LOG
SET ( LOCK_ESCALATION = AUTO )

ALTER TABLE HADB_SNAPSHOT
SET ( LOCK_ESCALATION = AUTO )

ALTER TABLE HADB_STAT
SET ( LOCK_ESCALATION = AUTO )

ALTER TABLE HADB_QUICK
SET ( LOCK_ESCALATION = AUTO )





USE [master]
GO


