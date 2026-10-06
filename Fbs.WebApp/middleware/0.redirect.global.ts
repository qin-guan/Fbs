// The old domains still point at this app. Send those visitors to bookaspace.app, on the same page.
const legacyHosts = new Set(['fbs.temasek3.cc', '3sib-fbs.from.sg'])

export default defineNuxtRouteMiddleware((to) => {
  if (!legacyHosts.has(useRequestURL().hostname.toLowerCase())) {
    return
  }

  return navigateTo(`https://bookaspace.app${to.fullPath}`, { external: true, redirectCode: 301, replace: true })
})
