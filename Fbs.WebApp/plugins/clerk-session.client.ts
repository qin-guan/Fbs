// Tells the app what Clerk says about the session, in a form that doesn't need Clerk to use it. This is what keeps Clerk
// out of the pages and code the old sign in uses, and out of the download for anybody who isn't using it.
export default defineNuxtPlugin(async (nuxtApp) => {
  if (useAuthMode() !== 'clerk') {
    return
  }

  const state = useState<SessionState>('account-session', () => ({ isLoaded: false, isSignedIn: undefined }))
  const { useAuth } = await import('@clerk/vue')

  // Clerk's own plugin, which comes before this one, has to have been installed for this to find it
  nuxtApp.vueApp.runWithContext(() => {
    const auth = useAuth()
    watchEffect(() => {
      state.value = { isLoaded: auth.isLoaded.value, isSignedIn: auth.isSignedIn.value }
    })
    sessionActions.signOut = async () => {
      await auth.signOut.value()
    }
  })
})
