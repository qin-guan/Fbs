import { defineRouteMiddleware, navigateTo } from '#app'

export default defineRouteMiddleware(async (to, from) => {
  const { data: me } = await useMe()

  if (!me.value?.isAdmin) {
    return navigateTo('/booking')
  }
})
