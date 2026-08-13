---
kind: dependency_management
name: Wiki 站点依赖管理（npm + VitePress）
category: dependency_management
scope:
    - '**'
source_files:
    - wiki-site/package.json
    - wiki-site/package-lock.json
    - .github/workflows/deploy.yml
    - wiki-site/.github/workflows/deploy.yml
    - wiki-site/scripts/sync-content.mjs
---

## 1. 使用的系统/工具

本仓库的依赖管理集中在 `wiki-site/` 子目录，采用 **npm** 作为包管理器，构建文档站点使用 **VitePress**。C# / Unity 主工程本身不通过 npm 管理运行时依赖（Unity 模组以 C# 源码与资源文件形式发布），因此“依赖管理”在本仓库中仅适用于 wiki 站点这一独立 Node.js 子项目。

- 包声明：`wiki-site/package.json`
- 锁定文件：`wiki-site/package-lock.json`（lockfileVersion 3）
- 运行环境：Node.js ≥ 14（由 vitepress 等包的 engines 字段约束）
- 构建脚本：`npm run build` → `npm run sync && vitepress build docs`
- 开发脚本：`npm run dev` → `npm run sync && vitepress dev docs`
- 内容同步脚本：`scripts/sync-content.mjs`（在构建前从外部源同步 WikiContent）

## 2. 关键文件

- `wiki-site/package.json`：声明唯一的生产/开发依赖 `vitepress ^1.6.0`，定义 `sync`、`dev`、`build`、`preview` 四个脚本。
- `wiki-site/package-lock.json`：完整锁定所有直接/间接依赖的版本、resolved URL 与 integrity hash，确保可重现安装。
- `wiki-site/node_modules/`：已安装的依赖树（包含 vitepress 及其全部传递依赖如 @algolia/*、@vue/*、esbuild、rollup、shiki 等）。
- `.github/workflows/deploy.yml`（根级与 wiki-site 下各一份）：CI 中通过 `cache-dependency-path: wiki-site/package-lock.json` 缓存 node_modules，加速构建。

## 3. 架构与约定

- **单一依赖入口**：整个 wiki 站点只依赖一个顶层包 `vitepress`，其余均为其传递依赖；没有自定义 registry、没有私有包、没有 monorepo workspace。
- **版本策略**：`package.json` 中对 vitepress 使用 `^1.6.0`（caret 范围），允许次级/补丁更新；具体解析后的精确版本由 `package-lock.json` 固化。
- **锁文件优先**：CI 和开发者均应基于 `package-lock.json` 安装，保证多环境一致。
- **构建前同步**：`build` 与 `dev` 都先执行 `npm run sync`，即先运行 `scripts/sync-content.mjs` 把 `WikiContent/` 下的多语言文档同步到 `docs/`，再交给 VitePress 构建。这意味着 wiki 站点的“内容依赖”来源于仓库内的 `WikiContent/` 目录，而非远程源。
- **无 vendoring**：未使用 `pnpm` 的 store-only 或 `yarn` 的 `yarn.lock` 风格，而是标准 npm lockfile + `node_modules` 就地安装。

## 4. 约定与约束

- **新增依赖必须更新 package-lock.json**：CI 通过 `cache-dependency-path: wiki-site/package-lock.json` 缓存依赖，若修改 `package.json` 后未提交更新的 `package-lock.json`，CI 缓存键会变化导致缓存失效，且本地与 CI 可能产生不一致的依赖树。
- **Node 版本要求**：vitepress 及其依赖要求 Node.js ≥ 14，开发者需满足该最低版本。
- **禁止引入额外运行时依赖**：当前 wiki 站点仅将 `vitepress` 声明为 `devDependencies`，构建产物不包含业务代码依赖；任何新依赖应评估是否确属构建期所需。
- **内容来源受控**：`scripts/sync-content.mjs` 是内容同步的唯一入口，不应绕过它直接编辑 `docs/` 下的生成文件（否则可能被下次 sync 覆盖）。
- **无私有 registry / .npmrc**：仓库内未发现 `.npmrc`、`.npmrc.local` 或 `registry=` 配置，所有包均从默认 npm 官方源拉取。

总结：本仓库的依赖管理仅作用于 `wiki-site/` 子项目，采用“npm + caret 版本范围 + package-lock.json 锁定 + GitHub Actions 缓存”的标准模式，核心依赖仅为 VitePress，并通过预构建脚本将 `WikiContent/` 中的游戏百科内容同步进站点。