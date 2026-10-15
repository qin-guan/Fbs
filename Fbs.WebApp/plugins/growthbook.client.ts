import { GrowthBook } from '@growthbook/growthbook'
import { autoAttributesPlugin } from '@growthbook/growthbook/plugins'

export default defineNuxtPlugin(async () => {
  const growthbook = new GrowthBook({
    apiHost: 'https://cdn.growthbook.io',
    clientKey: 'sdk-rkCEZsxMyyCxCli',
    enableDevMode: true,
    plugins: [autoAttributesPlugin()],
  })

  await growthbook.init({ streaming: true })

  return {
    provide: {
      growthbook,
    },
  }
})
