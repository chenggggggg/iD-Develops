import { pathToFileURL } from 'node:url';
import { origin } from './production-release.mjs';

class SmokeFailure extends Error { }

async function request(fetchImpl, url, status = 200) {
  const response = await fetchImpl(url, { redirect: 'manual', signal: AbortSignal.timeout(15_000) });
  if (response.status !== status) throw new SmokeFailure(`Marketing smoke check failed: ${new URL(url).pathname} returned HTTP ${response.status}.`);
  return response;
}

// GET-only checks. Do not submit forms, start exam attempts, or exercise staff writes here.
export async function verifyMarketing({ publicUrl, portalUrl, commit }, fetchImpl = fetch) {
  if (!/^[a-f0-9]{40}$/.test(commit || '')) throw new SmokeFailure('Expected release commit is required.');
  const version = await (await request(fetchImpl, `${publicUrl}/health/version`)).json();
  if (version.version !== commit || version.siteMode !== 'Marketing') throw new SmokeFailure('Production is not serving the selected commit in Marketing mode.');
  await request(fetchImpl, `${publicUrl}/health/db`);
  const images = new Set();
  for (const culture of ['en-us', 'nl-nl']) {
    for (const path of ['', '/enterprise', '/nt2-integration', '/personal-development', '/language-and-communication', '/about', '/pricing', '/contact', '/level-test',
      '/terms-conditions', '/privacy', '/cookie-policy', '/disclaimer']) {
      const html = await (await request(fetchImpl, `${publicUrl}/${culture}${path}`)).text();
      if (!/<h1\b/i.test(html) || /<a\b[^>]*class=["'][^"']*\bpublic-nav-login\b/i.test(html))
        throw new SmokeFailure(`Public content or hidden-account navigation check failed: /${culture}${path}.`);
      if (/Catalog (?:Credits|Standard) [a-f0-9]{20,}/i.test(html)) throw new SmokeFailure('Public catalog contains known E2E fixture titles.');
      for (const match of html.matchAll(/<img\b[^>]*\bsrc=["']([^"']+)["']/gi)) {
        const url = new URL(match[1].replaceAll('&amp;', '&'), publicUrl);
        if (url.origin === publicUrl && url.pathname.startsWith('/images/')) images.add(url.href);
      }
    }
  }
  for (const image of images) {
    const response = await request(fetchImpl, image);
    if (!response.headers.get('content-type')?.startsWith('image/')) throw new SmokeFailure('A public image URL did not return image content.');
    await response.body?.cancel();
  }
  const login = await (await request(fetchImpl, `${portalUrl}/login`)).text();
  if (!login.includes('Staff sign in')) throw new SmokeFailure('Production portal is not showing the staff sign-in page.');
  await request(fetchImpl, `${portalUrl}/register`, 404);
  return { pages: 26, images: images.size };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    const result = await verifyMarketing({ publicUrl: origin(process.env.PRODUCTION_URL),
      portalUrl: origin(process.env.PRODUCTION_PORTAL_URL), commit: process.env.RELEASE_COMMIT });
    console.log(`Marketing smoke checks passed: ${result.pages} localized pages, ${result.images} images, health, and staff-only account entry.`);
  } catch (error) {
    // Avoid echoing provider response bodies or accidental user data from a live page.
    console.error(error instanceof SmokeFailure ? error.message : 'Marketing production smoke checks failed: a page could not be read. Check reachability and runtime logs.');
    process.exitCode = 1;
  }
}
