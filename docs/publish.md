# 发布步骤

这份文档记录把三个 Mod 推到 GitHub 与 Steam 创意工坊的流程。命令按 Windows + Git Bash 写。

---

## 一、推送到 GitHub

### 1. 建仓库

在 GitHub 上新建一个空仓库（不要勾选 README / .gitignore / License，本地已有），记下地址，例如：

```
https://github.com/shuo-liu16/rimworld-mods.git
```

### 2. 本地初始化并推送

```bash
cd /e/Desktop-files/py/riamworldmod
git init
git add .
git commit -m "feat: 电网与温控 + 自动清扫（RimWorld 1.7）"
git branch -M main
git remote add origin https://github.com/shuo-liu16/rimworld-mods.git
git push -u origin main
```

首次 push 需要凭据：用 GitHub 的 Personal Access Token 当密码，或提前配好 SSH。

> 本机注意：这台机器的 TLS 走中间代理，`git push` 会报
> `schannel: CRYPT_E_NO_REVOCATION_CHECK` 或 `unable to get local issuer certificate`。
> 本仓库已写入 `http.sslVerify=false` 绕过；换到正常网络后可以删掉：
> `git config --unset http.sslVerify`。

### 3. 之后每次改动

```bash
build.bat                     # 先确认 0 错误
git add -A
git commit -m "fix: 修正温控表列宽"
git push
```

### 关于 Assemblies 里的 DLL

`mods/*/Assemblies/*.dll` **是提交进仓库的**——玩家 clone 下来就能直接用，不必自己装 .NET 编译。这跟普通软件项目「不提交构建产物」的惯例不同，是 RimWorld 模组仓库的常见做法。

代价是每次改代码都要把新 DLL 一起提交，别忘了先跑 `build.bat`。

---

## 二、上传到 Steam 创意工坊

RimWorld 的上传入口在游戏里，**不是**网页。

### 前置检查

- [ ] `About/About.xml` 里 `name` / `author` / `packageId` / `supportedVersions` / `description` 都填了
- [ ] `About/Preview.png` 存在（本仓库已生成，2.468:1，游戏推荐的封面比例）
- [ ] Mod 文件夹整个复制到了 `RimWorld\Mods\`（工坊只上传本地 Mod，不会上传创意工坊目录里的）
- [ ] 游戏内启用该 Mod，跑一遍确认没有红字

### 上传

1. 启动游戏 → 主菜单 → **选项 → Mod 设置**（Mods）
2. 在列表里选中要上传的 Mod
3. 右侧详情面板底部有 **「上传到 Steam 创意工坊」**（`Upload to Steam Workshop`）按钮
   - 第一次：上传
   - 之后：同一个 Mod 会变成「更新到创意工坊」
4. 填标题、描述、标签，确认

### 上传之后

游戏会在 `About/` 目录下生成 **`PublishedFileId.txt`**，里面是这条工坊条目的 ID。

**这个文件必须提交进仓库**：

```bash
git add mods/*/About/PublishedFileId.txt
git commit -m "chore: 记录创意工坊 ID"
```

没有它，下次上传会被当成新 Mod，产生两条重复条目；有了它，游戏才知道「更新」的是同一条。

> 这个文件只长在游戏目录里。`deploy.bat` 用的是 `robocopy /MIR`，
> 源目录没有它就当多余文件删掉——所以脚本现在会先把它回收进仓库，
> 并在同步时用 `/XF` 排除，两边都不会丢。

已记录的工坊 ID：

| Mod | PublishedFileId |
| --- | --- |
| Grid & Thermal Console | 3828947061 |
| Auto Clean | 3828945363 |
| Work Sorter | 未上传 |

> 权威来源是 Steam 的上传日志 `D:\steam\logs\workshop_log.txt`：
> `Create new workshop item ... : <id> (OK)` 后紧跟 `Upload finished ... : OK`
> 才算真正发布成功。丢了 ID 文件就到这里捞，不要凭印象填。

**废弃条目 `3829566544`**：2026-10-29 23:41 因 ID 文件丢失，游戏把 Grid & Thermal
Console 当成新 Mod 又建了一条，且上传在提交 manifest 阶段超时失败，Steam 上留下一个
空条目。需要在工坊页手动删除，否则玩家会搜到一个打不开的重复项。

### 更新已发布的 Mod

1. 改代码 → `build.bat`
2. `deploy.bat` 同步到游戏目录
3. 游戏内进 Mod 列表 → 选中 → **「更新到创意工坊」**
4. 版本号变化时，记得同步改 `About.xml` 的 `supportedVersions` 和描述里的版本说明

---

## 三、工坊描述文本

`docs/workshop-text.md` 里是可直接粘贴到工坊页面的文案（标题、简介、功能列表、常见问题）。改完记得两边同步。
