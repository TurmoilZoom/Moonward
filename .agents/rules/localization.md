# 本地化

- 应用 UI：`Starward.Language/Lang.*.resx`（Crowdin）。**禁止硬编码用户可见字符串**。
- Core 领域文案（抽卡类型、自助查询类型等）：`Starward.Core/Localization/CoreLang.*.resx`，**增改规则与 Lang 相同**。
- API 错误展示文案走 `MiHoYoApiErrorFeedbackFactory`，不在异常/页面硬编码。
- 已维护语言以仓库内 `Lang.*.resx` / `CoreLang.*.resx` 文件名为准（含默认 `Lang.resx`、zh-CN/HK/TW、de/es/it/ja/ko/ru/th/vi 等）。
- **`dotnet build` 不重生 Designer**；`Lang.Designer.cs` / `CoreLang.Designer.cs` 须手改。

**新增词条**

1. 默认/英文 resx  
2. `zh-CN`  
3. 对应 `*.Designer.cs` 属性  
4. **补全其余全部已维护语言的同名键**（不会的语言可用英文占位，键不可缺）

**修改词条**

1. 更新源语言（通常 `Lang.resx` + `zh-CN`）  
2. **逐语言核对**该键译文是否仍匹配新语义（含 `{0}` 等占位符）  
3. 过时/错误译文一并改；勿只改英/中

文档翻译：`docs/文件名.<语言-地区>.md`。
