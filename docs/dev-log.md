# 新员工入职工具套件 - 开发日志

## 2026-08-11

### 任务来源
- 提示词文件：`../new-hire-tools-prompt.md`
- 目标：为 corp1.local / corp2.local 双域环境开发
  1. 工具一：初始密码计算器（单文件 exe，C# WinForms）
  2. 工具二：NAS 映射脚本 `Map-NAS-Domain.ps1`

### 环境检查
- [x] C# 编译器：`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`（.NET Framework 4.x，Windows 自带）——可用
- [x] PowerShell：系统自带 Windows PowerShell——可用
- [x] 无需安装任何额外环境

### 关键设计决策
- **Seed**：内置固定字符串，两个工具源码中保持一致（见各自源码注释，不在日志中明文记录以外的位置传播）。
- **密码算法**（两工具严格一致）：
  1. 拼接：`员工号_2026`（如 `J10065_2026`）
  2. `HMAC-SHA256(Seed, 拼接串)` → Base64 → 取前 8 位
  3. 复杂度修复（基于原始 8 位判断缺失类别，确定性替换，保证两语言实现结果一致）：
     - 缺大写 → 第 3 位替换为 `'A' + digest[8] % 26`
     - 缺数字 → 第 5 位替换为 `'0' + digest[9] % 10`
     - 缺符号 → 第 7 位替换为 `"!@#$%^&*"[digest[10] % 8]`
     - 缺小写 → 第 1 位替换为 `'a' + digest[11] % 26`（提示词未规定此情况的固定位，取第 1 位作为扩展，两实现保持一致）
  - 替换字符取自 HMAC 摘要字节，确保确定性、可复现。
- **员工号校验**：正则 `^[A-Za-z]+[0-9]+$`（字母前缀 + 数字）。
- **C# 语法**：编译器为 C# 5，避免字符串插值等新语法。

### 进度记录
- [x] 创建本目录与开发日志
- [x] 拆分算法文件 `PasswordGenerator.cs`（exe 与一致性测试共用同一源码，避免两份实现漂移）
- [x] 编写 WinForms 界面 `PasswordCalculator.cs`，用 csc.exe 编译为单文件 `PasswordCalculator.exe`
      - 编译命令（注意：Git Bash 中需用 `-参数` 形式，`/参数` 会被 MSYS 误转成路径）：
        `csc.exe -target:winexe -utf8output -codepage:65001 -nologo -reference:System.dll,System.Drawing.dll,System.Windows.Forms.dll -out:PasswordCalculator.exe PasswordCalculator.cs PasswordGenerator.cs`
      - 产物仅 7.5 KB，纯 .NET Framework 4.x，双击即用
- [x] 编写 `Map-NAS-Domain.ps1`（单文件、无外部配置、中文提示、`-员工号` 参数）
      - 已转为 UTF-8 with BOM（Windows PowerShell 5.1 无 BOM 会把脚本按 GBK 误读，导致中文乱码甚至解析错误）
      - 已通过 PowerShell AST 语法检查（SYNTAX OK）
      - 期间发现并修复一处笔误：NAS 配置数组中 `}` 误写为 `)`

### 测试记录
- **一致性测试**（`tests/Test-Consistency.ps1`，函数体直接从正式脚本中提取，非复制粘贴）：
  - 44 组员工号（提示词示例 8 个 + 边界/随机 36 个，含大小写混合、长短不一），
    C# 与 PowerShell 输出 **44/44 完全一致**
  - 测试期间发现并修复两个测试脚本自身的问题（非算法问题）：
    1. `[char]'A'..[char]'Z'` 实际生成 65..90 的数字员工号 → 改用 `65..90 | [char]`
    2. PowerShell 哈希表键默认不区分大小写，`j10065` 覆盖了 `J10065` 的结果 → 改用 Ordinal 字典
- **冒烟测试**：`PasswordCalculator.exe` 可正常启动运行（进程存活验证通过）

### 交付物清单
| 文件 | 说明 |
|---|---|
| `PasswordCalculator.exe` | 工具一：初始密码计算器，双击即用 |
| `PasswordCalculator.cs` | 工具一 WinForms 界面源码 |
| `PasswordGenerator.cs` | 共用密码算法源码（exe 与测试共用） |
| `Map-NAS-Domain.ps1` | 工具二：NAS 自动映射脚本 |
| `README.md` | 使用说明 |
| `tests/` | 开发期一致性测试与辅助脚本（非交付物，仅供回归验证） |

### 说明 / 待现场确认事项
- NAS 连接探测、`net view` 共享解析、实际映射等流程依赖真实域与 NAS 环境，
  本机无法验证，需在入职现场实测；
- `net view` 解析已兼容中文（磁盘/打印）与英文（Disk/Print）系统输出；
- 盘符分配范围 Z: 至 E:，自动跳过已占用盘符；
- 密码不出现在控制台、日志与任何文件中，脚本退出前主动清除密码变量。

---

## 2026-08-11（第二轮：exe 打包需求）

### 需求
- 用户反馈直接打开 ps1 不方便，要求也提供 exe 单文件形式。

### 方案
- 未引入第三方工具（ps2exe 等），用系统自带 csc.exe 编译 C# 控制台外壳
  `MapNasWrapper.cs`：
  - `Map-NAS-Domain.ps1` 作为嵌入资源打包进 `Map-NAS-Domain.exe`；
  - 运行时释放到 `%TEMP%\Map-NAS-Domain-<随机>.ps1`，
    调用系统 Windows PowerShell（`-NoProfile -ExecutionPolicy Bypass -File`）执行；
  - 命令行参数原样透传（如 `Map-NAS-Domain.exe -员工号 J10065`）；
  - 退出后删除临时文件；无参数（双击）运行时末尾等待回车，避免窗口一闪而过。
- 编译命令：
  `csc.exe -target:exe -utf8output -codepage:65001 -nologo -resource:Map-NAS-Domain.ps1 -out:Map-NAS-Domain.exe MapNasWrapper.cs`

### 端到端测试发现并修复的真实缺陷
- **问题**：首次 exe 联调时脚本在 `net use /delete` 处异常中断。
  根因：脚本设置了 `$ErrorActionPreference = "Stop"`，net.exe 向 stderr 的输出
  经 `2>&1` 合并后被 PowerShell 当作 NativeCommandError 终止性错误抛出。
- **修复**：
  1. `$ErrorActionPreference` 改为 `"Continue"`（脚本本来就靠 `$LASTEXITCODE`
     判断原生命令成败）；
  2. 所有 net/cmdkey 调用的 `2>&1` 改为 `2>$null`（失败属预期分支，静默处理）。
- 修复后重新语法检查、重新打包 exe。

### 测试结果
- 端到端运行 `Map-NAS-Domain.exe`（本机无域环境）：
  两个 NAS 均正确提示"无该域权限，跳过"，最终输出
  "该员工无任何 NAS 访问权限，脚本退出"，退出码 1 —— 符合预期错误路径；
- 参数透传验证：`Map-NAS-Domain.exe -员工号 "bad!!"` 正确提示
  "员工号格式不正确"，退出码 1；
- 临时文件验证：运行结束后 `%TEMP%` 无 Map-NAS-Domain-*.ps1 残留。

### 交付物更新
- 新增 `Map-NAS-Domain.exe`（工具二的 exe 单文件形式，双击即用）
- 新增 `MapNasWrapper.cs`（外壳源码）
- 注意：`Map-NAS-Domain.ps1` 如有修改，必须重新执行上述编译命令重新打包 exe。

---

## 2026-08-11（第三轮：三合一工具箱）

### 需求
- 将两个工具合并为一个软件，分 3 个 Tab：
  1. "NAS懒人映射"（原自动映射功能 + 新增一键清除所有 NAS 映射）
  2. "初始密码查询"（原密码计算器）
  3. "自定义映射"（手动输入共享地址/用户名/密码映射 + 一键清除所有映射）
- 界面美观：Win11 原生设计风格融合 macOS 审美
- 界面不得出现 "(CORP1 / corp2 双域)" 和 "（如 J10065）" 字样

### 实现
- 新增 `NewHireToolbox.cs` + 复用 `PasswordGenerator.cs` → 编译为单文件
  `NewHireToolbox.exe`（28 KB，纯 .NET Framework）：
  `csc.exe -target:winexe -utf8output -codepage:65001 -nologo -reference:System.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Management.dll -out:NewHireToolbox.exe NewHireToolbox.cs PasswordGenerator.cs`
- 界面：未使用原生 TabControl（样式陈旧），改为自绘圆角分段控件
  （选中项 Win11 强调蓝 #0067C0 填充）+ 圆角卡片面板（白底细边）
  + 大留白布局；按钮自绘常态/悬停/按下/禁用四态，圆角裁剪 Region。
- 网络操作封装 `NasOperations`：net use / cmdkey / net view 进程调用，
  已映射驱动器通过 WMI `Win32_MappedLogicalDisk` 枚举；
  所有耗时操作走后台线程（Task.Factory.StartNew），UI 不卡死。
- 一键清除所有映射：WMI 枚举当前用户全部网络驱动器 → 逐一
  `net use X: /delete /y`，弹确认框防误触，Tab1/Tab3 共用该逻辑。
- 匿名类型 + dynamic 初版方案改为显式 `NasTarget` 类，
  避免额外引用 Microsoft.CSharp.dll。
- NAS 映射走真实网络，本机只能验证错误路径与界面；密码页逻辑与
  原计算器共用同一算法文件。

### 测试
- 自动化截图烟测（`tests/_smoke-shot.ps1`：启动 exe → SetWindowPos 置顶 →
  模拟鼠标点击三个 Tab → 截图）：三个 Tab 布局、配色、禁用态均正常；
  截图存于 `tests/shot-tab1/2/3.png`。
- 禁用字符串检查：源码中无 "CORP1 / corp2"、"J10065" 字样。
- 密码算法回归：44/44 与 PowerShell 实现一致（算法文件未改动，按构建即一致）。

### 交付物更新
- 新增 `NewHireToolbox.exe`（三合一主交付物）、`NewHireToolbox.cs`（源码）
- 原有 PasswordCalculator.exe / Map-NAS-Domain.exe / Map-NAS-Domain.ps1 保留可用

### 迭代：自定义映射流程修正
- 用户反馈：原"自定义映射"要求输入完整共享路径不合理。
- 改为与"NAS懒人映射"一致的探测式流程：只需输入 **服务器地址（IP 或主机名）
  + 用户名 + 密码** → 点击"探测共享" → 连接并保存凭据 → 自动列出该服务器
  全部共享 → 勾选后"映射选中共享"（Z: 向下分配，使用已保存凭据）；
  "一键清除所有映射"保留。
- 已重新编译 `NewHireToolbox.exe` 并截图验证新布局。

### 迭代：共享探测缺漏修复 + 自定义盘符
- **缺漏根因**：原 `GetShares` 依赖类型列关键字匹配（`^(\S+)\s{2,}(Disk|Print|磁盘|打印)`），
  共享名含空格时 `\S+` 无法匹配导致整条丢弃；且排除所有 `$` 结尾共享。
- **修复**：改用经现场多次检验的 `CORP1-NAS懒人映射/map-nas.ps1` 同款解析：
  以虚线分隔行定位共享列表区域，按"两个及以上空格"分割取首列，
  不依赖类型列关键字；过滤规则一致（空 / IPC$ / 以 `$ \ /` 开头）。
  样例验证：`HR Admin`、`My Share` 等带空格共享名均可正确抓取（tests/_parse-test.ps1）。
  注：Tab1 与 Tab3 共用 `GetShares`，两处同时受益。
- **自定义盘符**：Tab3 新增"起始盘符"输入框——留空从 Z: 自动向下分配；
  填写 D-Z 单个字母则从该盘符开始向下分配（自动跳过已占用盘符）。
  `NextFreeLetter` 增加起始盘符参数，下限由 E: 扩展到 D:。
- 已重新编译 `NewHireToolbox.exe`，截图验证 Tab3 新布局正常。

### 迭代：盘符分配改为拖动排序
- 用户反馈自定义盘符输入效果不好，撤销"起始盘符"输入框。
- 改为：Tab3 共享列表支持 **上下拖动调整顺序**（自绘 MouseDown/MouseMove/
  MouseUp 拖拽，超过系统拖拽阈值才进入拖动，拖动会保持各项勾选状态），
  映射时按列表顺序依次分配 Z:、Y:、X:……（仍自动跳过已占用盘符）。
- CheckedListBox 的 CheckOnClick 会在拖动起点误触发勾选切换，
  已在 MouseUp 中恢复拖动前记录的勾选状态。
- 已重新编译 `NewHireToolbox.exe`，截图验证新布局正常；
  拖动交互需真实环境人工验证。

### 迭代：探测流程模块化
- 用户提问 Tab1 与 Tab3 的探测是否一致：底层函数本就共用
  （`ProbeNas`/`SaveCredential`/`GetShares`），结果一致，
  但"连接 → 保存凭据 → 列共享"的编排代码在两个 Click 事件里重复。
- 已收拢为 `NasOperations.ProbeServer(server, user, password)`，
  返回 `ProbeResult{ Connected, CredentialSaved, Shares }`，
  Tab1（循环两个内置 NAS）与 Tab3（单服务器、手动凭据）统一调用。
- 重新编译 + 截图烟测 + 密码一致性回归（44/44）均通过。

### 迭代：映射流程模块化（相同功能全部收拢）
- 两个 Tab 的"映射选中共享"流程（收集勾选项 → WMI 已用盘符 →
  Z: 向下分配 → 逐个 net use → 汇报成功/失败）原本重复，
  已抽为窗体级共用方法 `MapSharesAsync(uncList, log, onComplete)`，
  Tab1/Tab3 仅各自负责收集勾选的 UNC 和按钮状态切换。
- 其余共用部分此前已模块化：`BtnClear_Click`（一键清除，两个按钮共用一个
  事件处理器）、`Log`/`MakeLabel`/`MakeInput`/`MakeLog`（UI 构造与日志）、
  `NasOperations`（net use / cmdkey / net view / WMI 封装）。
- 重新编译 + 三 Tab 截图烟测通过。

### 迭代：整体复盘重构（消除补丁堆砌）
通读全部代码后整体重写 `NewHireToolbox.cs`，修复以下一体性问题：
- **新增共用控件 `ShareListBox`**（CheckedListBox 子类）：内置条目数据
  （`ShareEntry{ Unc, Display }`）、拖动排序、`SetEntries`/`GetSelectedUncs`，
  取代 Tab1 的 `ShareItem`+平行列表与 Tab3 的 string 平行列表+手写拖动
  两套不一致实现；Tab1 顺带获得与 Tab3 一致的拖动排序能力。
  勾选状态记录在 base.OnMouseDown 之前，杜绝拖动误触发勾选切换的时序问题。
- **删除死代码**：Tab1 探测中收集却从未输出的 `skipped` 列表 ——
  改为探测结束时统一汇报"跳过：xxx（无该域权限/无可用共享）"。
- **删除 `AcceptButtonFix` 扩展类 hack**：构造函数直接 `this.AcceptButton=`。
- **简化签名**：`NextFreeLetter` 收回恒为 'Z' 的起始盘符参数；
  `MapShare` 收回恒为 null 的凭据参数（凭据一律先存凭据管理器）。
- **控件构造辅助**：新增 `MakePrimaryButton` / `MakeClearButton`（Tag 指向
  所属日志框），消除两个 Tab 重复的按钮样式代码；删除无意义的
  `RoundedButton.CardBack()` 与未使用的 `Theme.TitleFont`；
  删除 `_customServer` 等冗余字段（UNC 统一由 ShareListBox 条目承载）。
- 更新文件头结构说明与 Tab3 的过时描述。
- 验证：重编译零警告；三 Tab 截图烟测通过（Tab3 选中态经放大确认）；
  密码一致性 44/44；共享解析样例测试通过。
  注：重构编译时发现一个残留的 NewHireToolbox.exe 运行实例占用输出文件，
  已终止后重编译。

---

## 2026-08-11（收尾：文件夹清理）

经用户确认（A 方案），删除已被三合一工具箱取代的旧独立工具及其配套测试：
- `PasswordCalculator.exe` / `PasswordCalculator.cs`（被工具箱 Tab2 取代）
- `Map-NAS-Domain.exe` / `MapNasWrapper.cs` / `Map-NAS-Domain.ps1`（被 Tab1 取代）
- `tests/Test-Consistency.ps1` / `TestConsole.cs` / `TestConsole.exe`
  （一致性测试依赖 ps1 作为 PowerShell 侧算法来源，随 ps1 一并删除；
  今后只维护 `PasswordGenerator.cs` 一份 C# 算法实现）
- `tests/_convert-and-check.ps1`（仅服务于已删除的 ps1）
- `tests/screenshot.png`（旧版截图，已被 shot-tab*.png 取代）

清理后文件夹结构：
```
new-hire-tools/
├── NewHireToolbox.exe     主交付物
├── NewHireToolbox.cs      工具箱源码
├── PasswordGenerator.cs   密码算法
├── README.md              使用说明
├── 开发日志.md            过程记录
└── tests/                 回归工具（_smoke-shot.ps1 / _parse-test.ps1 / 截图）
```
README 已同步移除独立工具相关章节与编译命令。

---

## 2026-08-11（第四轮：前端美化，结构化 UI 设计）

按"布局 / 主题 / 动效"三层方法整体升级界面：

### 布局设计
- 8px 间距网格（Theme.SpaceXS/S/M/L/XL = 4/8/16/24/32），卡片内边距统一 24；
- 三个功能页统一节奏：页标题 + 副标题 → 表单区 → 列表区 → 操作区 → 日志区；
- 输入框统一高 30、按钮统一高 32（Theme.InputHeight / ButtonHeight）。

### 主题设计
- Theme 集中管理全部设计 token：配色（macOS 浅灰底 #F5F5F7、白卡片、
  半透明细边框 BorderSoft、Win11 强调蓝 #0067C0、悬停灰、危险红）、
  字阶（页标题 12.5 Bold / 正文 9.5 / 说明 8.5 / 等宽 / 密码 16 Bold）、
  圆角（卡片 12 / 按钮与输入框 6 / 分段栏全圆角）；
- 柔和投影：RoundHelper.DrawShadow 用多层低透明度圆角矩形叠加模拟，
  卡片经 ShadowPanel 容器获得投影，分段栏自绘投影。

### 动效设计
- RoundedButton：悬停/按下颜色 Timer 插值渐变（约 150ms 收敛）；
- SegmentedTabBar：全新自绘分段导航，选中胶囊 ease-out cubic 滑动（170ms），
  未选中项悬停有浅灰高亮，自绘文字与命中检测（取代原 RoundedButton 拼装）；
- 页面切换滑入微交互（14px ease-out，140ms）；
- InputBox：全新圆角输入框控件，聚焦时边框强调蓝加粗高亮；
- 密码生成成功时文字颜色淡入（350ms）。

### 修复的问题
- 一键清除按钮初始渲染成蓝底（RoundedButton 显示颜色在构造函数中初始化，
  Outline 属性后设未生效）→ OnHandleCreated 时对齐一次显示颜色；
- Tab2/Tab3 切换后整页灰屏：三个 ShadowPanel 同位置重叠且 BackColor=
  Transparent，Panel 套 Panel 的透明渲染在双缓冲 Form 下失败 →
  改为切换宿主 ShadowPanel 的 Visible（不再重叠），ShadowPanel 改为不透明。

### 验证
- 三 Tab 截图烟测全部通过（tests/shot-tab1/2/3.png），视觉对齐设计目标。

### 迭代：自绘沉浸式标题栏
- 用户反馈系统标题栏与界面风格割裂。改为无边框窗口 + 自绘标题栏：
  - `FormBorderStyle.None`，`CreateParams` 加 `CS_DROPSHADOW` 保留系统窗口阴影；
  - 窗体 Region 圆角裁剪（半径 12）；
  - 标题栏高 40：左侧强调色小方块 + 软件名，右侧自绘最小化/关闭按钮
    （悬停灰底 / 关闭悬停 #E81123 红底白 X，Win11 风格）；
  - `WndProc` 处理 WM_NCHITTEST：标题栏空白区返回 HTCAPTION 支持拖动，
    按钮区除外；
  - 标题栏底部极浅分隔线；窗口高度 660 → 700，内容区整体下移；
  - 截图辅助脚本同步适配（无边框窗口客户区原点即窗口原点）。

### 迭代：边框细节与悬浮感修复（据用户截图反馈）
- **卡片圆角缺口（"月牙"）根因**：CardPanel 是矩形控件，圆角只是画上去的，
  下方 ShadowPanel 绘制的投影从方形四角露出形成缺口。
  修复：CardPanel 增加 Region 圆角裁剪（与 RoundedButton 同手法），
  投影沿真正的圆角边缘自然过渡。
- **投影算法优化**：DrawShadow 改为 5 层、外层最淡逐层加深（alpha 4→8）、
  向下偏移 2px，边缘过渡更柔和自然。
- **窗口悬浮感不足**：CS_DROPSHADOW 阴影较弱且原窗口描边 alpha 仅 24。
  修复：窗体 OnPaint 末尾绘制 1px 清晰描边（alpha 64），
  窗口圆角半径 12 → 8（Win11 标准，同时减轻 Region 硬边锯齿感）。
- 截图验证：卡片角落与窗口角落放大检查，缺口消除、边缘清晰。

---

## 2026-08-12（收尾：目录规范化 + README 重构）

按 GitHub 项目惯例整理目录（不实际上传）：

```
new-hire-tools/
├── README.md               项目文档（简介/功能/截图/构建/结构/安全说明）
├── build.bat               一键编译脚本（src/ -> dist/）
├── .gitignore              bin/obj、IDE、测试输出截图等
├── dist/NewHireToolbox.exe 编译产物（主交付物）
├── src/                    NewHireToolbox.cs + PasswordGenerator.cs
├── assets/screenshots/     tab1-nas-mapping / tab2-password-query / tab3-custom-mapping.png
├── tests/                  smoke-shot.ps1（UI 截图回归）、parse-test.ps1（解析样例）
└── docs/                   dev-log.md（本文件）、feedback-border-issue.png（反馈截图存档）
```

- 文件改名：tests/_smoke-shot.ps1 → smoke-shot.ps1、_parse-test.ps1 → parse-test.ps1、
  开发日志.md → docs/dev-log.md；截图改为语义化命名；
  smoke-shot.ps1 中 exe 路径同步改为 dist/。
- 新增 build.bat（纯 ASCII——中文 .bat 会被 cmd 按 GBK 误读导致解析失败，
  已踩坑修复），并实测通过。
- src/NewHireToolbox.cs 头部编译命令同步为 src/ -> dist/ 新路径。
- README 重构为 GitHub 风格：功能特性、界面预览（引用 assets 截图）、
  快速开始、从源码构建、项目结构、算法说明、安全说明、日志链接。
- 验证：build.bat 编译通过；smoke-shot / parse-test 回归通过。

### 迭代：项目开发提示词沉淀
- 新增 `docs/development-prompt.md`：把本次开发沉淀为可直接复用的提示词——
  功能需求（三个功能页完整流程）、算法规范（含小写替换位的扩展约定）、
  共享解析的正确做法（虚线定位、不依赖类型列）、界面文案约束、安全要求、
  工程要求（零依赖/C# 5 语法/模块化/验证/交付结构/bat 纯 ASCII 等踩坑经验）
  全部写具体；前端界面仅给出设计目标与原则，实现细节留给未来模型自由发挥。
- README 项目结构一节同步补充该文件。

---

## 2026-08-25（V1.0 放弃，DWM 官方方案回移 beta）

### 背景
- V1.0 分支（左侧导航大改版）经评审后放弃，文件夹已删除；
  其中验证成功的两项技术方案回移到 beta 主版本（本目录）。

### 回移内容
1. **DWM 官方窗口装饰**（替代 Region 裁剪 + CS_DROPSHADOW 土办法）：
   - `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE=33, DWMWCP_ROUND)`
     —— Win11 原生抗锯齿圆角（Win10 自动忽略回退直角）；
   - `DwmExtendFrameIntoClientArea(1px)` —— DWM 官方阴影与窗口边框；
   - 删除手绘窗口描边，避免与官方边框叠影。
2. **去 Region 化（消除控件锯齿）**：GDI Region 是像素蒙版不抗锯齿，
   是按钮/卡片锯齿的根因。RoundedButton 与 CardPanel 全部移除 Region，
   圆角外区域填充父容器背景色，纯抗锯齿绘制；阴影收进 CardPanel 自绘，
   ShadowPanel 中间层删除（页面卡片直接挂窗体）；标签背景改为显式卡片白。
3. **DPI 感知**：`SetProcessDPIAware` + `AutoScaleMode.None`，
   高缩放显示器原生清晰渲染。
4. **截图脚本重写**：PowerShell 进程开头声明 DPI 感知 +
   SetWindowPos 强制移到主屏，坐标与像素统一为物理坐标；
   解决多显示器/DPI 下截黑屏、截局部、点击错位问题。

### 验证
- build.bat 编译通过；三 Tab 截图回归正常；
- 放大检查：窗口四角（DWM 原生圆角）、按钮、输入框、Tab 胶囊均无锯齿；
- 解析回归测试通过。

### 备注
- beta.zip 为本次改造前的快照存档，未随之更新；如需当前状态的压缩包可重新打包。
- V1.0 中的程序化图标生成器（IconGen）与左侧导航未回移（随 V1.0 一并放弃）。

---

## 2026-08-25（taste-skill 审计 + 全量优化）

### 技能安装
- 全局安装 taste-skill（design-taste-frontend v2）与 redesign-skill 到
  `~/.kimi-code/skills/`（用户级，新会话生效）。
- 范围说明：taste-skill 自我声明不适用于产品型 UI 并指向官方设计系统，
  与本项目的 Fluent 路线一致；审计采用 redesign-skill 清单 +
  taste-skill 可迁移原则（色彩锁定、形状一致性、状态完整性、字体层级）。

### 审计后实施（P0+P1+P2 全量）
- **P0-1 字体栈**：首选 Segoe UI Variable（标题 Display / 正文 Text /
  辅助 Small），等宽首选 Cascadia Mono，均带回退（雅黑/Consolas）。
- **P0-2 加载状态**：新增 ProgressRing 控件（Fluent 风格旋转弧），
  探测/映射/清除期间在"运行日志"标题旁旋转。
- **P0-3 空状态**：共享列表为空时显示居中的引导文案
  （原生 CheckedListBox 不支持自绘覆盖，改为空时隐藏列表、
  由外层 RoundedFrame 容器绘制文案）。
- **P0-4 按钮按压微交互**（按下内容内缩）+ 键盘焦点框
  （实心按钮内白框 / 描边按钮内强调色框），GotFocus/LostFocus 触发重绘。
- **P1 形状收敛**：日志框与共享列表由直角改为 6px 圆角
  （新增 RoundedFrame 圆角边框容器包裹原生控件，子控件内缩避免方角露出）。
- **P1 阴影着色**：纯黑低透明度 → 蓝灰调（27,43,66），与浅色环境融合。
- **P1 Tab2 布局重心**：内容块整体下移，消除头重脚轻。
- **P2 Tab 图标**：Tab 栏新增细线几何图标（共享节点/钥匙/地球），
  图标+文字整体居中。

### 踩坑
- CheckedListBox 等原生控件忽略 OnPaint 自绘覆盖 → 空状态文案改由外层容器绘制。
- ShareListBox 出现重复 SetEntries（编辑叠加）→ 编译错误，已清理。

### 验证
- build.bat 编译通过；三 Tab 截图回归正常；空状态文案、圆角列表、
  Tab 图标、Tab2 布局均经截图确认；解析回归通过。

---

## 2026-08-25（换装 emilkowalski/skills + 问题修复）

### 技能更换
- 卸载 taste-skill / redesign-skill（用户反馈效果不佳）；
- 全局安装 emilkowalski/skills 全套 12 个技能到 `~/.kimi-code/skills/`
  （emil-design-eng / animate / review-animations / apple-design 等）。

### 按 emil-design-eng 修正的动效细节
- 缓动曲线：ease-out cubic → ease-out quart（更强出场手感，贴近 cubic-bezier(0.23,1,0.32,1)）；
- 密码淡入 350ms → 250ms（UI 动效 < 300ms 原则）；
- 进度环提速 9°→12°/tick（更快的 spinner 让加载感觉更快，感知性能原则）。

### 截图反馈问题修复
- **Tab3 表单标签被输入框裁切**：标签按固定 x=110 布局，实测字宽超出即被压。
  新增 MakeFormRow 辅助：输入框按标签实测宽度（TextRenderer.MeasureText）
  动态定位，多行取最长标签对齐；标签垂直居中于输入框；
- **Tab 文字未上下居中**：TextRenderer.VerticalCenter 受字体度量影响偏移，
  改为按实测文字高度手动计算 Y 坐标绘制；
- **共享列表提示文字超出卡片右缘**：文案缩短为
  "勾选需要映射的项，拖动调整顺序，首项为 Z:"。

### 验证
- 编译通过；三 Tab 截图回归：标签无重叠、Tab 文字居中、提示完整显示；
- 解析回归通过。

### 迭代：修复"凭据正确却连接失败"（探测机制根因修复）
- **根因**：ProbeNas 用 `net use \IP\IPC$` 做连接验证，但目标 NAS 是 Samba 设备，
  不暴露可用的 IPC$ 命名共享，恒返回系统错误 67（找不到网络名），
  凭据正确也必然"连接失败"。实测该 NAS 的映射驱动器工作正常可佐证。
- **修复**：探测流程改为与经过现场检验的 map-nas.ps1 完全一致的顺序——
  先 cmdkey 存凭据 → 清理旧连接（避免 1219）→ 枚举共享验证；
  失败时回滚刚保存的凭据，不留错误凭据。
- **枚举改用官方 API**：放弃 net view 文本解析，改用 netapi32 的
  NetShareEnum（net view 的内部实现同款）。实测本机 net view 对两台 NAS
  及 127.0.0.1 均报 1702（绑定句柄无效，客户端故障），而 NetShareEnum
  直接成功返回 4+2 个共享（含带空格的 "HR_ Admin"）。
  顺带消除了文本解析、编码、net view 客户端故障三类问题。
- **诊断信息**：连接失败时日志附带可读错误原因
  （5=凭据错误 / 53=服务器不可达 / 1219=凭据冲突 / 其他错误码）。
- 测试更新：删除 parse-test.ps1（net view 解析已不复存在），
  新增 share-enum-test.ps1（NetShareEnum 连通性测试，需在内网运行）。
- 注意：未用真实凭据在真实 NAS 上做端到端 UI 测试——探测流程会覆盖/回滚
  已保存的 NAS 凭据，存在破坏用户现有映射的风险；机制已通过
  share-enum-test.ps1 验证。

### 迭代：development-prompt.md 整体重写为 v2
- 按用户要求整体重写（非补丁），并保留用户在文件上的手动修改
  （"供 IT 在新员工入职时使用"、员工号示例保留、"Win11 原生设计/禁止割裂"等措辞）。
- 新增/更新的强约束：
  - **Seed 逐字符写死在提示词中**（任何重写必须逐字符一致，
    否则存量员工密码全部对不上）；
  - NAS 环境约束：Samba 不暴露 IPC$（错误 67）、net view 客户端故障（1702）；
    正确探测流程 = cmdkey 存凭据 → 清旧连接 → NetShareEnum 枚举 → 失败回滚；
  - 界面硬性经验：窗口装饰用 DWM 官方 API、禁止 Region 裁剪圆角、
    DPI 感知、表单按实测字宽对齐、原生控件自绘限制由外层容器承担；
  - 动效原则：强 ease-out、<300ms、按压反馈、spinner 感知性能。

---

## 2026-08-25（布局放宽 + 自绘共享列表 + 密码动画）

### 1. 窗口与布局
- 窗口 560x700 → 640x780，各页行距与列表/日志区域同步放宽，消除拥挤感。

### 2. 字体固定为微软雅黑
- Theme 字阶全部固定为 Microsoft YaHei UI（标题 13 Bold / 正文 9.5 /
  辅助 8.5 / 日志 9 / 密码 20 Bold），删除 Segoe UI/Cascadia 回退逻辑。

### 3. 共享列表重写为完全自绘的 ShareListView
- 原生 CheckedListBox 无法实现目标交互，重写为自绘控件：
  - **盘符徽章**：每项直接显示即将映射的盘符（Z:/Y:/X: 胶囊徽章），
    只分配给勾选项，取消勾选后自动重新分配；
  - **自绘勾选框**：圆角、勾选 = 强调蓝底白勾；
  - **拖拽悬浮**：被拖行脱离队列，白底 + 投影 + 左侧强调条；
  - **退避动画**：其它行以指数趋近（ease-out 手感）动画让位，
    松手后从当前视觉位置平滑归位（提交重排时保持视觉连续）；
  - 单击切换勾选；API 与原控件一致（SetEntries / GetSelectedUncs）。
- 验证：新增 tests/ListDemo.cs 演示宿主 + _list-demo-shot.ps1 自动化脚本
  （点击勾选/拖拽模拟）：初始徽章分配、取消勾选后徽章重排、
  拖拽悬浮与退避、松手归位全部截图确认。
  （脚本拖拽选中的是 HR Admin 而非预期的 SG-NAS——演示窗体含系统标题栏，
  坐标换算有偏差，仅影响演示脚本不影响功能。）

### 4. 初始密码查询页美化
- 新增 ScrambleText 控件：**1 秒乱码解码动画**——字符从
  字母数字符号池随机滚动，从左到右依次定格为真实密码，
  底部强调线同步扫过；动画完成才启用"复制到剪贴板"。
- 输入行（标签+输入框+按钮）整体水平居中，密码区大字号展示。

### 验证
- build.bat 编译通过；三 Tab 截图回归正常（新窗口尺寸）；
  截图脚本坐标同步更新（Tab 中心 x = 184/320/456）。

### 迭代：自适应窗口 + 拖拽卡死修复 + 忽略 homes 选项
- **拖拽卡死修复**：退避动画 Timer 收敛后自动停止，但按住拖动时目标值更新
  不会重启 Timer，导致"拖久了卡住"。修复：UpdateRetreatTargets 末尾
  确保 Timer 运行。
- **忽略 homes 选项**：新增自研 ToggleSwitch（圆角滑轨 + 动画旋钮，
  默认勾选），置于共享列表标题行右侧；两个映射页的开关状态同步，
  切换后即时按原始探测结果重新过滤并重排窗口（SetEntries 会重置勾选状态）。
- **窗口随共享数量加长**：RelayoutNasPage / RelayoutCustomPage 按共享行数
  加高列表框（默认 4 行，最多 10 行），下方按钮与日志区整体下移，
  窗体与卡片同步加高；超出 10 行时列表支持滚轮滚动
  （ShareListView 新增滚动偏移，行定位/插入位计算均按可视坐标系）。
- 提示文案配合开关缩短（去掉"首项为 Z:"，盘符徽章已直接显示）。
- 验证：编译通过；Tab1/Tab3 截图确认开关渲染正确无遮挡；
  ListDemo（6 项）徽章 Z:~U: 分配、拖拽悬浮与退避让位均正常。

### 迭代：预设共享过滤 + 版本信息
- "忽略 homes 共享" 改为 **"忽略NAS预设共享"**，默认开启，
  预设清单：homes / docker / music / video（PresetShares 数组，大小写不敏感）。
- 共享列表提示文案精简为"可访问的共享（可拖动调整）"
  （盘符徽章已直接呈现分配信息，文字不再重复）。
- **版本信息**：新增 src/AssemblyInfo.cs——产品名称"新员工入职工具箱"、
  产品/文件版本 1.0.0.0、公司 CoolingRabbit、
  版权 Copyright © 2026 CoolingRabbit（Windows 文件属性可见，已验证）；
  build.bat 编译时一并包含。

### 迭代：development-prompt.md 整体重写为 v3
- 功能页一/三补充：忽略NAS预设共享开关（默认开启，homes/docker/music/video）、
  盘符徽章与拖拽交互规格、窗口随共享数量自适应加长（最多 10 行 + 滚动）；
- 功能页二补充：1 秒乱码解码动画、完成后才启用复制；
- 界面章节：字体固定微软雅黑；新增"动画 Timer 收敛后需重启"的坑；
  阴影蓝灰调；程序集信息（AssemblyInfo.cs 产品名/版本/版权）纳入工程要求；
- 交付结构补充 AssemblyInfo.cs 与自绘控件演示宿主。
