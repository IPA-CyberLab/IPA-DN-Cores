﻿
-- HADB_DATA

USE [HADB001]
GO
ALTER INDEX [DATA_KEY1] ON [dbo].[HADB_DATA] REBUILD PARTITION = ALL WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
GO
USE [HADB001]
GO
ALTER INDEX [DATA_KEY1] ON [dbo].[HADB_DATA] REORGANIZE  WITH ( LOB_COMPACTION = ON )
GO





USE [HADB001]
GO
ALTER INDEX [DATA_KEY2] ON [dbo].[HADB_DATA] REBUILD PARTITION = ALL WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
GO
USE [HADB001]
GO
ALTER INDEX [DATA_KEY2] ON [dbo].[HADB_DATA] REORGANIZE  WITH ( LOB_COMPACTION = ON )
GO





USE [HADB001]
GO
ALTER INDEX [DATA_KEY3] ON [dbo].[HADB_DATA] REBUILD PARTITION = ALL WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
GO
USE [HADB001]
GO
ALTER INDEX [DATA_KEY3] ON [dbo].[HADB_DATA] REORGANIZE  WITH ( LOB_COMPACTION = ON )
GO





USE [HADB001]
GO
ALTER INDEX [DATA_KEY4] ON [dbo].[HADB_DATA] REBUILD PARTITION = ALL WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
GO
USE [HADB001]
GO
ALTER INDEX [DATA_KEY4] ON [dbo].[HADB_DATA] REORGANIZE  WITH ( LOB_COMPACTION = ON )
GO





USE [HADB001]
GO
ALTER INDEX [DATA_KEY5] ON [dbo].[HADB_DATA] REBUILD PARTITION = ALL WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
GO
USE [HADB001]
GO
ALTER INDEX [DATA_KEY5] ON [dbo].[HADB_DATA] REORGANIZE  WITH ( LOB_COMPACTION = ON )
GO





USE [HADB001]
GO
ALTER INDEX [DATA_LABEL1] ON [dbo].[HADB_DATA] REBUILD PARTITION = ALL WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
GO
USE [HADB001]
GO
ALTER INDEX [DATA_LABEL1] ON [dbo].[HADB_DATA] REORGANIZE  WITH ( LOB_COMPACTION = ON )
GO





USE [HADB001]
GO
ALTER INDEX [DATA_LABEL2] ON [dbo].[HADB_DATA] REBUILD PARTITION = ALL WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
GO
USE [HADB001]
GO
ALTER INDEX [DATA_LABEL2] ON [dbo].[HADB_DATA] REORGANIZE  WITH ( LOB_COMPACTION = ON )
GO





USE [HADB001]
GO
ALTER INDEX [DATA_LABEL3] ON [dbo].[HADB_DATA] REBUILD PARTITION = ALL WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
GO
USE [HADB001]
GO
ALTER INDEX [DATA_LABEL3] ON [dbo].[HADB_DATA] REORGANIZE  WITH ( LOB_COMPACTION = ON )
GO





USE [HADB001]
GO
ALTER INDEX [DATA_LABEL4] ON [dbo].[HADB_DATA] REBUILD PARTITION = ALL WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
GO
USE [HADB001]
GO
ALTER INDEX [DATA_LABEL4] ON [dbo].[HADB_DATA] REORGANIZE  WITH ( LOB_COMPACTION = ON )
GO





USE [HADB001]
GO
ALTER INDEX [DATA_LABEL5] ON [dbo].[HADB_DATA] REBUILD PARTITION = ALL WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
GO
USE [HADB001]
GO
ALTER INDEX [DATA_LABEL5] ON [dbo].[HADB_DATA] REORGANIZE  WITH ( LOB_COMPACTION = ON )
GO





USE [HADB001]
GO
ALTER INDEX [DATA_SNAP_NO] ON [dbo].[HADB_DATA] REBUILD PARTITION = ALL WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
GO
USE [HADB001]
GO
ALTER INDEX [DATA_SNAP_NO] ON [dbo].[HADB_DATA] REORGANIZE  WITH ( LOB_COMPACTION = ON )
GO





USE [HADB001]
GO
ALTER INDEX [DATA_SYSTEMNAME] ON [dbo].[HADB_DATA] REBUILD PARTITION = ALL WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
GO
USE [HADB001]
GO
ALTER INDEX [DATA_SYSTEMNAME] ON [dbo].[HADB_DATA] REORGANIZE  WITH ( LOB_COMPACTION = ON )
GO





USE [HADB001]
GO
ALTER INDEX [DATA_UID_ORIGINAL] ON [dbo].[HADB_DATA] REBUILD PARTITION = ALL WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
GO
USE [HADB001]
GO
ALTER INDEX [DATA_UID_ORIGINAL] ON [dbo].[HADB_DATA] REORGANIZE  WITH ( LOB_COMPACTION = ON )
GO





USE [HADB001]
GO
ALTER INDEX [DATA_UPDATE_DT] ON [dbo].[HADB_DATA] REBUILD PARTITION = ALL WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
GO
USE [HADB001]
GO
ALTER INDEX [DATA_UPDATE_DT] ON [dbo].[HADB_DATA] REORGANIZE  WITH ( LOB_COMPACTION = ON )
GO





USE [HADB001]
GO
ALTER INDEX [DATA_VER] ON [dbo].[HADB_DATA] REBUILD PARTITION = ALL WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
GO
USE [HADB001]
GO
ALTER INDEX [DATA_VER] ON [dbo].[HADB_DATA] REORGANIZE  WITH ( LOB_COMPACTION = ON )
GO




USE [HADB001]
GO
ALTER INDEX [PK_DATA] ON [dbo].[HADB_DATA] REBUILD PARTITION = ALL WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, DATA_COMPRESSION = PAGE)
GO
USE [HADB001]
GO
ALTER INDEX [PK_DATA] ON [dbo].[HADB_DATA] REORGANIZE  WITH ( LOB_COMPACTION = ON )
GO











-- HADB_LOG

USE [HADB001]
GO
ALTER INDEX [LOG_LABEL1] ON [dbo].[HADB_LOG] REBUILD PARTITION = ALL WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
GO
USE [HADB001]
GO
ALTER INDEX [LOG_LABEL1] ON [dbo].[HADB_LOG] REORGANIZE  WITH ( LOB_COMPACTION = ON )
GO




USE [HADB001]
GO
ALTER INDEX [LOG_LABEL2] ON [dbo].[HADB_LOG] REBUILD PARTITION = ALL WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
GO
USE [HADB001]
GO
ALTER INDEX [LOG_LABEL2] ON [dbo].[HADB_LOG] REORGANIZE  WITH ( LOB_COMPACTION = ON )
GO




USE [HADB001]
GO
ALTER INDEX [LOG_LABEL3] ON [dbo].[HADB_LOG] REBUILD PARTITION = ALL WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
GO
USE [HADB001]
GO
ALTER INDEX [LOG_LABEL3] ON [dbo].[HADB_LOG] REORGANIZE  WITH ( LOB_COMPACTION = ON )
GO




USE [HADB001]
GO
ALTER INDEX [LOG_LABEL4] ON [dbo].[HADB_LOG] REBUILD PARTITION = ALL WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
GO
USE [HADB001]
GO
ALTER INDEX [LOG_LABEL4] ON [dbo].[HADB_LOG] REORGANIZE  WITH ( LOB_COMPACTION = ON )
GO




USE [HADB001]
GO
ALTER INDEX [LOG_LABEL5] ON [dbo].[HADB_LOG] REBUILD PARTITION = ALL WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
GO
USE [HADB001]
GO
ALTER INDEX [LOG_LABEL5] ON [dbo].[HADB_LOG] REORGANIZE  WITH ( LOB_COMPACTION = ON )
GO




USE [HADB001]
GO
ALTER INDEX [LOG_SYSTEM_NAME] ON [dbo].[HADB_LOG] REBUILD PARTITION = ALL WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
GO
USE [HADB001]
GO
ALTER INDEX [LOG_SYSTEM_NAME] ON [dbo].[HADB_LOG] REORGANIZE  WITH ( LOB_COMPACTION = ON )
GO




USE [HADB001]
GO
ALTER INDEX [LOG_UID] ON [dbo].[HADB_LOG] REBUILD PARTITION = ALL WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
GO
USE [HADB001]
GO
ALTER INDEX [LOG_UID] ON [dbo].[HADB_LOG] REORGANIZE  WITH ( LOB_COMPACTION = ON )
GO




USE [HADB001]
GO
ALTER INDEX [PK_HADB_LOG] ON [dbo].[HADB_LOG] REBUILD PARTITION = ALL WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, DATA_COMPRESSION = PAGE)
GO
USE [HADB001]
GO
ALTER INDEX [PK_HADB_LOG] ON [dbo].[HADB_LOG] REORGANIZE  WITH ( LOB_COMPACTION = ON )
GO



