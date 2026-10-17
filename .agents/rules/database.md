# 数据库（SQLite + Dapper）

`Features/Database/DatabaseService.cs`：`StarwardDatabase.db`；`KVT`、`Setting` + 各游戏业务表。

- 迁移：`DatabaseSqls` 中每个 `Sql_vN` 一次迁移，按 `PRAGMA USER_VERSION` 跳过已执行。
- **改 schema 只追加新 `Sql_vN` 并更新 `USER_VERSION`**，禁止改已发布迁移。
- 自定义类型：`DapperSqlMapper`（如 `GameBiz` 存字符串）。
