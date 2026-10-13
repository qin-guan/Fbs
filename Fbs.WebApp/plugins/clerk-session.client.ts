import { useQueryClient } from '@tanstack/vue-query'

// Tells the app what Clerk says about the session, in a form that doesn't need Clerk to use it. This is what keeps Clerk
// out of the pages and code the old sign in uses, and out of the download for anybody who isn't using it.
export default defineNuxtPlugin(async (nuxtApp) => {
  if (useAuthMode() !== 'clerk') {
    return
  }

  const state = useState<SessionState>('account-session', () => ({ isLoaded: false, isSignedIn: undefined }))
  const { useAuth, useClerk, useUser } = await import('@clerk/vue')

  // Clerk's own plugin, which comes before this one, has to have been installed for this to find it
  nuxtApp.vueApp.runWithContext(() => {
    const auth = useAuth()
    const clerk = useClerk()
    const { user } = useUser()
    watchEffect(() => {
      state.value = { isLoaded: auth.isLoaded.value, isSignedIn: auth.isSignedIn.value }
    })
    sessionActions.signOut = async () => {
      await auth.signOut.value()
    }
    sessionActions.manageAccount = () => {
      clerk.value?.openUserProfile()
    }

    // The API hears of a name (or email) changed with Clerk from the next session token, which can be a minute away, and changes it
    // in the organizations that call them by it. So a token is asked for at once, and what was read with the old name is read again.
    const said = computed(() => user.value ? `${user.value.fullName ?? ''}\n${user.value.primaryEmailAddress?.emailAddress ?? ''}` : undefined)
    watch(said, async (now, before) => {
      if (now === undefined || before === undefined) {
        return
      }

      await auth.getToken.value({ skipCache: true })
      await nuxtApp.vueApp.runWithContext(() => useQueryClient()).invalidateQueries()
    })
  })
})
