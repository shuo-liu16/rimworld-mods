# 工坊物品被移除：申诉材料

2026-10-29 夜间发现：两个已上架的 Mod 在工坊页都挂着

> This item has been removed from the community because it violates Steam Community &
> Content Guidelines. It is only visible to you.

页面**不给具体原因**（Steam 的惯例），只给一个 Steam Support 入口。工单入口：

```
https://help.steampowered.com/en/wizard/HelpWithSteamIssue/?issueid=415
```

（中文站点把 `/en/` 换成 `/zh-cn/` 即可。`issueid=415` 就是「我的创意工坊物品被移除」。）

上传账号：Dream16 (`76561199519977813`)。

---

## 排查结论（先排除自己的问题）

| 检查项 | 结果 |
| --- | --- |
| 版本号写错？ | 否。本地游戏 `1.7.4990 rev1069`，`About.xml` 声明 `1.7` |
| 真的「不兼容」？ | 否。页面里 `incompatibleNotification` 是 `display:none` 的隐藏模板，不显示 |
| 描述里有违规内容？ | 未见。纯功能文档，无广告、无捐赠、无站外下载、无联系方式 |
| 上传的是脏目录？ | 否。`deploy.bat` 会剥掉 `Source/` 与 `obj/`，上传的只有 About / Defs / Patches / Assemblies / Languages |
| 上传本身失败？ | 不是。两条都 `Upload finished ... : OK`，且已有订阅者（6 / 8） |

剩下的可能：含 `.dll` 的物品被自动审核误判（RimWorld mod 带程序集是常态，但二进制会被扫描），
或账号信誉相关的自动限制。两种都只能靠人工复核。

---

## 申诉正文（英文，直接粘贴）

标题：`Workshop items removed by mistake - RimWorld mods (3828945363 / 3828947061)`

```
Hello Steam Support,

Two of my RimWorld Workshop items were removed for violating the Steam Community &
Content Guidelines, but I cannot find anything in them that violates the guidelines.
I believe this is an automated false positive and would like a manual review.

Items:
- Auto Clean: https://steamcommunity.com/sharedfiles/filedetails/?id=3828945363
- Grid & Thermal Console: https://steamcommunity.com/sharedfiles/filedetails/?id=3828947061

About the items:
- Free RimWorld mods (App ID 294100). No ads, no external downloads, no monetization,
  no donation or contact links, no off-site redirects.
- Full source code is public under the MIT license:
  https://github.com/shuo-liu16/rimworld-mods
- The .dll files are compiled from that source with the official .NET SDK against the
  game's own assembly. They contain Harmony patches for UI and gameplay behaviour only -
  no anti-cheat bypass, no DRM, no network code, no file system access outside the game's
  own save/config directories.
- Descriptions are plain-text documentation in Chinese: what the mod does, how to use it,
  known limitations, and a disclaimer.

Could you please review them manually and restore visibility if they do not actually
violate the guidelines? If a specific part of the description or the uploaded files needs
to be changed, please tell me what it is and I will fix it right away.

Thank you.
```

> 要点：不要写「我不知道为什么」，要写「我检查过这些项，都不违规，请人工复核」，
> 并主动给出可自查的凭据（开源仓库、无外链、无网络代码）。客服看到可核查的事实才会转人工。

---

## 待办

- [ ] 查绑定邮箱（含垃圾邮件），Steam 移除物品通常发过通知，邮件里有时带具体原因
- [ ] 提交申诉，等回复（一般 1-3 个工作日）
- [ ] 删除空条目 `3829566544`：我的创意工坊物品 → 该条目 → 删除
- [ ] 申诉通过后，用 `deploy.bat` + 游戏内「更新到创意工坊」重新推一次内容
- [ ] Work Sorter 暂缓上传，等这两条的处理结果明确，避免同样的判定再吃一次
