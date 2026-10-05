// What can be reached depends on how the build signs people in (see nuxt.config.ts): the old phone number pages until the
// switch to Clerk, and only the pages for accounts and organisations after it.
const accountsOnly = ['/sign-in', '/sign-up', '/orgs', '/onboarding', '/account', '/join', '/claim', '/t']
const legacyOnly = ['/auth', '/booking', '/profile', '/faqs', '/changelog']
const withoutSignIn = ['/sign-in', '/sign-up']

const isUnder = (path: string, prefixes: string[]) => prefixes.some(prefix => path === prefix || path.startsWith(`${prefix}/`))

export default defineNuxtRouteMiddleware(async (to) => {
  const mode = useAuthMode()

  if (mode === 'legacy') {
    return isUnder(to.path, accountsOnly) ? navigateTo('/', { replace: true }) : undefined
  }

  if (isUnder(to.path, legacyOnly)) {
    return navigateTo('/', { replace: true })
  }

  // The home page says whether somebody is signed in itself, as it is what they see before they are
  if (to.path === '/' || isUnder(to.path, withoutSignIn)) {
    return
  }

  const { isLoaded, isSignedIn } = useAccountSession()
  await until(isLoaded).toBe(true)
  if (!isSignedIn.value) {
    return navigateTo({ path: '/sign-in', query: { redirect_url: to.fullPath } })
  }
})
