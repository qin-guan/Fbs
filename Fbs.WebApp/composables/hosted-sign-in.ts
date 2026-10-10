import type { SignInScreen } from './session'
import { safeReturnTo } from '~/lib/return-to'

/**
 * With WorkOS, signing in and up happens on its pages (AuthKit), so the pages for them here only send people there, coming back
 * to where they were going (the `redirect_url` the access middleware adds). Somebody already signed in goes straight there.
 */
export function useHostedSignIn(screen: SignInScreen) {
  const route = useRoute()
  const { isLoaded, isSignedIn } = useAccountSession()
  const goingTo = ref(false)
  const failed = ref(false)
  const returnTo = computed(() => typeof route.query.redirect_url === 'string' ? route.query.redirect_url : '/')

  async function go() {
    failed.value = false
    goingTo.value = true
    try {
      await until(isLoaded).toBe(true)
      if (isSignedIn.value) {
        await navigateTo(safeReturnTo(returnTo.value), { replace: true })
        return
      }

      await sessionActions.signIn?.(returnTo.value, screen)
    }
    catch (error) {
      console.error(error)
      failed.value = true
    }
  }

  onMounted(() => {
    if (useAuthMode() === 'workos') {
      go()
    }
  })

  return { goingTo, failed, retry: go }
}
