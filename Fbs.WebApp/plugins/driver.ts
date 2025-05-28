import { driver } from 'driver.js'

export default defineNuxtPlugin(() => {
  return {
    provide: {
      driver: driver(),
    },
  }
})
