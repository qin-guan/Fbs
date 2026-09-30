import type {
  DehydratedState,
  VueQueryPluginOptions,
} from '@tanstack/vue-query'
import {
  VueQueryPlugin,
  QueryClient,
  QueryCache,
  MutationCache,
  hydrate,
  dehydrate,
} from '@tanstack/vue-query'
import { ResponseError } from '~/api'

// The API answers a missing, expired or unreadable session cookie with a bare 401 (or 403).
// Endpoints that refuse a specific action, like cancelling another unit's booking, send a 403
// with problem details instead, which the page reports itself.
function isAuthError(error: unknown) {
  if (!(error instanceof ResponseError)) {
    return false
  }

  if (error.status === 401) {
    return true
  }

  return error.status === 403 && !error.contentType?.includes('problem+json')
}

export default defineNuxtPlugin((nuxt) => {
  const vueQueryState = useState<DehydratedState | null>('vue-query')
  const router = useRouter()

  const queryClient = new QueryClient({
    queryCache: new QueryCache({ onError: redirectToLogin }),
    mutationCache: new MutationCache({ onError: redirectToLogin }),
    defaultOptions: {
      queries: {
        staleTime: 5000,
        // Retrying won't bring the session back and would hold off the redirect for several seconds
        retry: (failureCount, error) => !isAuthError(error) && failureCount < 3,
      },
    },
  })
  const options: VueQueryPluginOptions = { queryClient }

  // Pages that need a session are on these layouts, so leave as soon as any request is rejected
  const layoutsWithSession = new Set(['app', 'tenant', 'account'])
  const mode = useAuthMode()

  async function redirectToLogin(error: unknown) {
    const route = router.currentRoute.value
    if (!isAuthError(error) || !layoutsWithSession.has(String(route.meta.layout))) {
      return
    }

    // Drop data cached for the old session so the login page doesn't show the user as still logged in
    queryClient.clear()
    await router.push(mode === 'legacy' ? '/auth/login' : { path: '/sign-in', query: { redirect_url: route.fullPath } })
  }

  nuxt.vueApp.use(VueQueryPlugin, options)

  if (import.meta.server) {
    nuxt.hooks.hook('app:rendered', () => {
      vueQueryState.value = dehydrate(queryClient)
    })
  }

  if (import.meta.client) {
    hydrate(queryClient, vueQueryState.value)
  }
})
