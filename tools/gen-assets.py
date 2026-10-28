#!/usr/bin/env python3
"""为无 NuGet 依赖的 SDK 风格项目生成 project.assets.json。

这个环境里 NuGet 解析用户目录会返回空路径，导致 dotnet build 的 restore 步骤
必定抛 "Value cannot be null. (Parameter 'path1')"。对于不引用任何 NuGet 包的
项目（我们的 Mod 和工具就是），生成一份空的 assets 文件即可让 --no-restore 成功编译。

用法: python gen-assets.py <项目目录> [目标框架]
"""
import json
import os
import sys


def main() -> int:
    proj_dir = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else ".")
    tfm = sys.argv[2] if len(sys.argv) > 2 else "net10.0"
    obj = os.path.join(proj_dir, "obj")
    os.makedirs(obj, exist_ok=True)

    csprojs = [f for f in os.listdir(proj_dir) if f.endswith(".csproj")]
    if not csprojs:
        print("找不到 .csproj")
        return 1
    name = csprojs[0][: -len(".csproj")]
    proj_path = os.path.join(proj_dir, csprojs[0])

    assets = {
        "version": 3,
        "targets": {tfm: {}},
        "libraries": {},
        "projectFileDependencyGroups": {tfm: []},
        "packageFolders": {},
        "project": {
            "version": "1.0.0",
            "restore": {
                "projectUniqueName": proj_path,
                "projectName": name,
                "projectPath": proj_path,
                "packagesPath": os.path.expanduser("~/.nuget/packages"),
                "outputPath": obj + os.sep,
                "projectStyle": "PackageReference",
                "configFilePaths": [],
                "originalTargetFrameworks": [tfm],
                "sources": {},
                "frameworks": {
                    tfm: {"targetAlias": tfm, "projectReferences": {}}
                },
            },
            "frameworks": {tfm: {"targetAlias": tfm, "projectReferences": {}}},
        },
    }
    with open(os.path.join(obj, "project.assets.json"), "w", encoding="utf-8") as f:
        json.dump(assets, f, indent=2)

    with open(os.path.join(obj, f"{name}.csproj.nuget.g.props"), "w", encoding="utf-8") as f:
        f.write('<?xml version="1.0" encoding="utf-8"?>\n<Project>\n  <PropertyGroup />\n</Project>\n')
    with open(os.path.join(obj, f"{name}.csproj.nuget.g.targets"), "w", encoding="utf-8") as f:
        f.write('<?xml version="1.0" encoding="utf-8"?>\n<Project>\n  <PropertyGroup />\n</Project>\n')

    print(f"assets 已生成: {obj}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
