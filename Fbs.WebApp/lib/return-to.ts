/**
 * Where to go back to after signing in, if it is a page of this app, and otherwise the home page. What comes back from signing
 * in went through the address bar, so anybody could have made it up: only a path on this site is taken, never another site, or
 * `//another.site`, which a browser takes as one.
 */
export function safeReturnTo(value: unknown): string {
  if (typeof value !== 'string' || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\')) {
    return '/'
  }

  try {
    const base = 'https://app.invalid'
    const url = new URL(value, base)
    return url.origin === base ? `${url.pathname}${url.search}${url.hash}` : '/'
  }
  catch {
    return '/'
  }
}
