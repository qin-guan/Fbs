import type { Ref } from 'vue'

export type AuthMode = 'clerk' | 'legacy' | 'test'

/** How this build signs people in, which is decided when it is built, see nuxt.config.ts. */
export function useAuthMode(): AuthMode {
  return useRuntimeConfig().public.authMode as AuthMode
}

/** Whether people have accounts and belong to organizations, rather than the old phone number pages. */
export function usesAccounts() {
  return useAuthMode() !== 'legacy'
}

/**
 * The session token to send to the API, or null when nobody is signed in. With Clerk it waits for Clerk to load,
 * which is why requests made before that don't go out without one.
 */
export async function getSessionToken(mode: AuthMode): Promise<string | null> {
  if (mode === 'test') {
    return 'test-token'
  }

  if (mode === 'clerk') {
    // Only loaded when it is used, so the old sign in doesn't carry it
    const { getToken } = await import('@clerk/vue')
    return await getToken()
  }

  return null
}

export interface SessionState {
  isLoaded: boolean
  isSignedIn: boolean | undefined
}

/** How to sign out, once Clerk has said. It isn't state, as it is only asked for by somebody who clicks. */
export const sessionActions: { signOut?: () => Promise<void> } = {}

export interface AccountSession {
  isLoaded: Readonly<Ref<boolean>>
  isSignedIn: Readonly<Ref<boolean | undefined>>
  signOut: () => Promise<void>
}

/**
 * Whether somebody is signed in with an account, and how they sign out. Only for builds with accounts. With Clerk it
 * follows what Clerk says (see plugins/clerk-session.client.ts), and is not loaded until Clerk is.
 */
export function useAccountSession(): AccountSession {
  const mode = useAuthMode()
  const state = useState<SessionState>('account-session', () => ({
    isLoaded: mode !== 'clerk',
    isSignedIn: mode === 'test' ? true : undefined,
  }))

  return {
    isLoaded: computed(() => state.value.isLoaded),
    isSignedIn: computed(() => state.value.isSignedIn),
    signOut: async () => {
      await sessionActions.signOut?.()
    },
  }
}
