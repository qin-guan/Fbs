import { safeReturnTo } from '~/lib/return-to'

// Tells the app what WorkOS (AuthKit) says about the session, in a form that doesn't need WorkOS to use it, as
// plugins/clerk-session.client.ts does for Clerk. AuthKit is only loaded by a WorkOS build, so nobody else downloads it.
//
// Signing in happens on WorkOS's own pages, which come back to /callback, where AuthKit takes the code it is given for a
// session before anything else is done, and then goes on to where somebody was going.
export default defineNuxtPlugin((nuxtApp) => {
  if (useAuthMode() !== 'workos') {
    return
  }

  const config = useRuntimeConfig().public
  const state = useState<SessionState>('account-session', () => ({ isLoaded: false, isSignedIn: undefined }))

  const signedOut = () => {
    state.value = { isLoaded: true, isSignedIn: false, profile: null }
  }

  const ready = import('@workos-inc/authkit-js').then(async ({ createClient, LoginRequiredError }) => {
    const authkit = await createClient(config.workosClientId, {
      // A custom authentication domain keeps the session in a cookie of this site's; without one it is in the browser's storage
      apiHostname: config.workosApiHostname || undefined,
      redirectUri: `${window.location.origin}/callback`,
      onRedirectCallback: ({ state: returned }) => {
        nuxtApp.runWithContext(() => navigateTo(safeReturnTo(returned?.returnTo), { replace: true }))
      },
      // The session ended, so the next page that needs one asks them to sign in again
      onRefreshFailure: signedOut,
    })
    return { authkit, LoginRequiredError }
  })

  ready
    .then(({ authkit }) => {
      const user = authkit.getUser()
      state.value = {
        isLoaded: true,
        isSignedIn: user !== null,
        profile: user && {
          name: [user.firstName, user.lastName].filter(Boolean).join(' ') || null,
          email: user.email,
          pictureUrl: user.profilePictureUrl,
        },
      }
    })
    .catch((error) => {
      console.error('Couldn\'t load WorkOS', error)
      signedOut()
    })

  sessionActions.getToken = async () => {
    const { authkit, LoginRequiredError } = await ready
    try {
      return await authkit.getAccessToken()
    }
    catch (error) {
      if (error instanceof LoginRequiredError) {
        return null
      }

      throw error
    }
  }

  sessionActions.signIn = async (returnTo, screen) => {
    const { authkit } = await ready
    const options = { state: { returnTo: safeReturnTo(returnTo) } }
    await (screen === 'sign-up' ? authkit.signUp(options) : authkit.signIn(options))
  }

  sessionActions.signOut = async () => {
    const { authkit } = await ready
    try {
      // Ends the session with WorkOS too, which comes back to the home page (allowed in WorkOS as where to go after signing out)
      authkit.signOut({ returnTo: window.location.origin })
    }
    catch {
      // The session had already ended, so there is nothing to end with WorkOS
      signedOut()
      await nuxtApp.runWithContext(() => navigateTo('/'))
    }
  }
})
