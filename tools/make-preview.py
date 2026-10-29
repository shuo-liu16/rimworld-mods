#!/usr/bin/env python3
"""生成两个 Mod 的 About/Preview.png（创意工坊封面）。

RimWorld 的预览图推荐比例是 2.468:1（见游戏目录 ModUpdating.txt），
这里用 1234x500，正好接近该比例。

用法: python tools/make-preview.py
"""
import os

from PIL import Image, ImageDraw, ImageFont

W, H = 1234, 500
FONT_BOLD = r"C:\Windows\Fonts\msyhbd.ttc"
FONT_REG = r"C:\Windows\Fonts\msyh.ttc"

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def font(path, size):
    return ImageFont.truetype(path, size)


def make(title, subtitle, bullets, accent, out_path):
    img = Image.new("RGB", (W, H), (26, 30, 38))
    d = ImageDraw.Draw(img)

    # 左侧强调色竖条
    d.rectangle([0, 0, 14, H], fill=accent)
    # 右下角淡淡的对角块，避免整张图太平
    d.polygon([(W, H), (W - 420, H), (W, H - 220)], fill=(32, 37, 47))

    d.text((64, 84), title, font=font(FONT_BOLD, 76), fill=(240, 242, 246))
    d.text((66, 190), subtitle, font=font(FONT_REG, 30), fill=accent)

    y = 268
    for b in bullets:
        d.ellipse([66, y + 12, 78, y + 24], fill=accent)
        d.text((96, y), b, font=font(FONT_REG, 28), fill=(196, 202, 214))
        y += 52

    d.text((W - 300, H - 56), "RimWorld 1.7", font=font(FONT_REG, 24), fill=(120, 128, 142))

    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    img.save(out_path, "PNG")
    print("written:", out_path)


def main():
    make(
        "电网与温控",
        "Grid & Thermal Console",
        [
            "全图每张电网的发电 / 耗电 / 储能 / 状态",
            "加热器与制冷器分组，统一设定目标温度",
            "季节模式：热季只开制冷、冷季只开制热",
        ],
        (90, 160, 230),
        os.path.join(ROOT, "mods", "GridThermal", "About", "Preview.png"),
    )

    make(
        "自动清扫",
        "Auto Clean",
        [
            "每天固定时刻自动清除居住区污渍",
            "不需要小人动手，也不消耗资源",
            "时刻、范围、提示均可在 Mod 设置调整",
        ],
        (90, 200, 150),
        os.path.join(ROOT, "mods", "AutoClean", "About", "Preview.png"),
    )

    make(
        "工作优先级",
        "Work Sorter",
        [
            "技能 / 热情 / 工作速度 / 身体能力综合打分",
            "叠加地图待办量：待搬运、蓝图、污渍、伤员",
            "表上预览再一键应用，逐格锁定不被覆盖",
        ],
        (226, 156, 92),
        os.path.join(ROOT, "mods", "WorkSorter", "About", "Preview.png"),
    )


if __name__ == "__main__":
    main()
