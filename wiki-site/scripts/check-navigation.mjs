/** Execute the real useWiki composable and VitePress withBase against page URLs. */
import assert from 'node:assert/strict'
import { fileURLToPath } from 'node:url'
import { build } from 'esbuild'

const root = fileURLToPath(new URL('../', import.meta.url))
const composable = fileURLToPath(new URL('../docs/.vitepress/theme/composables/useWiki.ts', import.meta.url))
const utils = fileURLToPath(new URL('../node_modules/vitepress/dist/client/app/utils.js', import.meta.url))
const bundled = await build({
  stdin: {
    contents: `
      import { useWiki } from ${JSON.stringify(composable)};
      import { siteDataRef, state } from 'review-navigation-state';
      export function navigate(base, language, current, target) {
        siteDataRef.value.base = base;
        state.lang.value = language;
        state.route.path = current;
        return useWiki().href(target);
      }`,
    resolveDir: root,
  },
  bundle: true,
  write: false,
  platform: 'node',
  format: 'esm',
  logLevel: 'silent',
  plugins: [{
    name: 'navigation-host-boundaries',
    setup(plugin) {
      plugin.onResolve({ filter: /^(vitepress|review-navigation-state)$/ }, ({ path }) =>
        ({ path, namespace: 'review-navigation' }))
      // Only stub the page context; withBase itself executes the installed VitePress code.
      plugin.onResolve({ filter: /^\.\/data$/ }, ({ importer }) =>
        importer.replaceAll('\\', '/').endsWith('/client/app/utils.js')
          ? { path: 'review-navigation-state', namespace: 'review-navigation' }
          : undefined)
      plugin.onResolve({ filter: /changelog\.data\.mts$/ }, () =>
        ({ path: 'releases', namespace: 'review-navigation' }))
      plugin.onLoad({ filter: /.*/, namespace: 'review-navigation' }, ({ path }) => ({
        contents: path === 'vitepress'
          ? `export { withBase } from ${JSON.stringify(utils)};
             import { siteDataRef, state } from 'review-navigation-state';
             export const useData = () => ({ lang: state.lang, site: siteDataRef });
             export const useRoute = () => state.route;`
          : path === 'releases'
            ? 'export const data = [];'
            : `export const siteDataRef = { value: { base: '/' } };
               export const state = { lang: { value: 'zh-CN' }, route: { path: '/' } };`,
        resolveDir: root,
      }))
    },
  }],
})
const { navigate } = await import(`data:text/javascript;base64,${Buffer.from(bundled.outputFiles[0].text).toString('base64')}`)
let checks = 0
for (const base of ['/', '/BossRushMod/']) {
  for (const language of ['zh-CN', 'en']) {
    const localePrefix = language === 'en' ? 'en/' : ''
    for (const current of ['', 'bosses/dragon-king', 'changelog/v2.1.7.html', 'systems/']) {
      const page = `https://example.invalid${base}${localePrefix}${current}`
      for (const target of ['/', '/bosses/', '/bosses/dragon-king', '/equipment/frost-set', '/changelog/v2.1.7#changes']) {
        const actual = new URL(navigate(base, language, new URL(page).pathname, target), page)
        const expected = `https://example.invalid${base}${localePrefix}${target.slice(1)}`
        assert.equal(actual.href, expected, `Navigation from ${page} to ${target}`)
        checks++
      }
    }
  }
}
console.log(`Wiki navigation: ${checks} URL checks passed (real useWiki and VitePress withBase).`)
