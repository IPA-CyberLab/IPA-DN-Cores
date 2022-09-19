﻿USE [HADB001]
GO

BEGIN TRANSACTION


/*****************************/
USE [HADB001]
GO

CREATE PARTITION FUNCTION [HADB_DATA_PART_FUNC1](nvarchar(350)) AS RANGE RIGHT FOR VALUES (N'B', N'C', N'D', N'E', N'F', N'G', N'H', N'I', N'J', N'K', N'L', N'M', N'N', N'O', N'P', N'Q', N'R', N'S', N'T', N'U', N'V', N'W', N'X', N'Y', N'Z')


CREATE PARTITION SCHEME [HADB_DATA_PART_SCHEME1] AS PARTITION [HADB_DATA_PART_FUNC1] TO ([HADB001_FG_A], [HADB001_FG_B], [HADB001_FG_C], [HADB001_FG_D], [HADB001_FG_E], [HADB001_FG_F], [HADB001_FG_G], [HADB001_FG_H], [HADB001_FG_I], [HADB001_FG_J], [HADB001_FG_K], [HADB001_FG_L], [HADB001_FG_M], [HADB001_FG_N], [HADB001_FG_O], [HADB001_FG_P], [HADB001_FG_Q], [HADB001_FG_R], [HADB001_FG_S], [HADB001_FG_T], [HADB001_FG_U], [HADB001_FG_V], [HADB001_FG_W], [HADB001_FG_X], [HADB001_FG_Y], [HADB001_FG_Z])






ALTER TABLE [dbo].[HADB_DATA] DROP CONSTRAINT [PK_DATA] WITH ( ONLINE = OFF )


SET ANSI_PADDING ON

ALTER TABLE [dbo].[HADB_DATA] ADD  CONSTRAINT [PK_DATA] PRIMARY KEY CLUSTERED 
(
	[DATA_UID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF, DATA_COMPRESSION = PAGE) ON [HADB_DATA_PART_SCHEME1]([DATA_UID])








SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [DATA_LABEL1] ON [dbo].[HADB_DATA]
(
	[DATA_LABEL1] ASC
)
WHERE ([DATA_LABEL1]<>'' AND [DATA_DELETED]=(0) AND [DATA_ARCHIVE]=(0))
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = ON, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [HADB_DATA_PART_SCHEME1]([DATA_UID])




SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [DATA_LABEL2] ON [dbo].[HADB_DATA]
(
	[DATA_LABEL2] ASC
)
WHERE ([DATA_LABEL2]<>'' AND [DATA_DELETED]=(0) AND [DATA_ARCHIVE]=(0))
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = ON, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [HADB_DATA_PART_SCHEME1]([DATA_UID])




SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [DATA_LABEL3] ON [dbo].[HADB_DATA]
(
	[DATA_LABEL3] ASC
)
WHERE ([DATA_LABEL3]<>'' AND [DATA_DELETED]=(0) AND [DATA_ARCHIVE]=(0))
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = ON, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [HADB_DATA_PART_SCHEME1]([DATA_UID])




SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [DATA_LABEL4] ON [dbo].[HADB_DATA]
(
	[DATA_LABEL4] ASC
)
WHERE ([DATA_LABEL4]<>'' AND [DATA_DELETED]=(0) AND [DATA_ARCHIVE]=(0))
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = ON, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [HADB_DATA_PART_SCHEME1]([DATA_UID])




SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [DATA_LABEL5] ON [dbo].[HADB_DATA]
(
	[DATA_LABEL5] ASC
)
WHERE ([DATA_LABEL5]<>'' AND [DATA_DELETED]=(0) AND [DATA_ARCHIVE]=(0))
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = ON, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [HADB_DATA_PART_SCHEME1]([DATA_UID])




CREATE NONCLUSTERED INDEX [DATA_SNAP_NO] ON [dbo].[HADB_DATA]
(
	[DATA_SNAPSHOT_NO] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = ON, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [HADB_DATA_PART_SCHEME1]([DATA_UID])




SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [DATA_SYSTEMNAME] ON [dbo].[HADB_DATA]
(
	[DATA_SYSTEMNAME] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = ON, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [HADB_DATA_PART_SCHEME1]([DATA_UID])




SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [DATA_UID_ORIGINAL] ON [dbo].[HADB_DATA]
(
	[DATA_UID_ORIGINAL] ASC
)
WHERE ([DATA_ARCHIVE]=(1))
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = ON, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [HADB_DATA_PART_SCHEME1]([DATA_UID])




CREATE NONCLUSTERED INDEX [DATA_UPDATE_DT] ON [dbo].[HADB_DATA]
(
	[DATA_UPDATE_DT] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = ON, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [HADB_DATA_PART_SCHEME1]([DATA_UID])




CREATE NONCLUSTERED INDEX [DATA_VER] ON [dbo].[HADB_DATA]
(
	[DATA_VER] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = ON, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [HADB_DATA_PART_SCHEME1]([DATA_UID])





/*****************************/
USE [HADB001]
GO


CREATE PARTITION FUNCTION [HADB_QUICK_PART_FUNC1](nvarchar(350)) AS RANGE RIGHT FOR VALUES (N'B', N'C', N'D', N'E', N'F', N'G', N'H', N'I', N'J', N'K', N'L', N'M', N'N', N'O', N'P', N'Q', N'R', N'S', N'T', N'U', N'V', N'W', N'X', N'Y', N'Z')


CREATE PARTITION SCHEME [HADB_QUICK_PART_SCHEME1] AS PARTITION [HADB_QUICK_PART_FUNC1] TO([HADB001_FG_A], [HADB001_FG_B], [HADB001_FG_C], [HADB001_FG_D], [HADB001_FG_E], [HADB001_FG_F], [HADB001_FG_G], [HADB001_FG_H], [HADB001_FG_I], [HADB001_FG_J], [HADB001_FG_K], [HADB001_FG_L], [HADB001_FG_M], [HADB001_FG_N], [HADB001_FG_O], [HADB001_FG_P], [HADB001_FG_Q], [HADB001_FG_R], [HADB001_FG_S], [HADB001_FG_T], [HADB001_FG_U], [HADB001_FG_V], [HADB001_FG_W], [HADB001_FG_X], [HADB001_FG_Y], [HADB001_FG_Z])






ALTER TABLE [dbo].[HADB_QUICK] DROP CONSTRAINT [PK_HADB_QUICK] WITH ( ONLINE = OFF )


SET ANSI_PADDING ON

ALTER TABLE [dbo].[HADB_QUICK] ADD  CONSTRAINT [PK_HADB_QUICK] PRIMARY KEY CLUSTERED 
(
	[QUICK_UID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF, DATA_COMPRESSION = PAGE) ON [HADB_QUICK_PART_SCHEME1]([QUICK_UID])








SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [QUICK_SYSTEMNAME] ON [dbo].[HADB_QUICK]
(
	[QUICK_SYSTEMNAME] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = ON, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [HADB_QUICK_PART_SCHEME1]([QUICK_UID])















/**************** デッドロック多発防止 *****************/
/* これは、ライブラリ側で LightLock を用いて Update するようにしたので、ひとまず解決
   できるだけ、DATA_UID 主キーインデックスはパーティション分割されたほうがよい */

/*USE [HADB001]
GO
ALTER TABLE [dbo].[HADB_DATA] DROP CONSTRAINT [PK_DATA] WITH ( ONLINE = OFF )
GO


ALTER TABLE dbo.HADB_DATA ADD CONSTRAINT
	PK_HADB_DATA PRIMARY KEY NONCLUSTERED 
	(
	DATA_UID
	) WITH( STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]

GO

*/





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








COMMIT TRANSACTION


USE [master]
GO



